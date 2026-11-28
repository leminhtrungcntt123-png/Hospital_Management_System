using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class DoctorService : IDoctorService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DoctorService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    // Lấy danh sách bác sĩ kèm chuyên khoa và khoa phòng
    public async Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Doctors
            .AsNoTracking()
            .OrderBy(d => d.FullName)
            .Select(d => new DoctorListItemDto(
                d.Id,
                d.FullName,
                d.Specialty ?? "",
                d.Department.Name,
                d.LicenseNumber ?? ""))
            .ToListAsync(cancellationToken);
    }

    // Lấy danh sách bác sĩ hiển thị trang chủ (Public Showcase)
    public async Task<IReadOnlyList<DoctorPublicDto>> GetPublicFeaturedDoctorsAsync(int count = 8, CancellationToken cancellationToken = default)
    {
        return await _context.Doctors
            .AsNoTracking()
            .Where(d => d.IsPublic)
            .OrderByDescending(d => d.IsFeatured)
            .ThenBy(d => d.DisplayOrder)
            .Take(count)
            .Select(d => new DoctorPublicDto(
                d.Id,
                d.FullName,
                d.Specialty,
                d.AcademicTitle,
                d.AvatarUrl,
                d.ShortBio,
                d.Biography,
                d.YearsOfExperience,
                d.DisplayOrder,
                d.Department.Name))
            .ToListAsync(cancellationToken);
    }

    // Lấy danh sách bệnh nhân chờ khám theo bác sĩ và ca khám
    public async Task<IReadOnlyList<QueuePatientDto>> GetQueuePatientsAsync(int doctorId, DateOnly date, ExamShift shift, CancellationToken cancellationToken = default)
    {
        var startDateTime = shift == ExamShift.Morning
            ? date.ToDateTime(new TimeOnly(7, 0))
            : date.ToDateTime(new TimeOnly(13, 0));

        var endDateTime = shift == ExamShift.Morning
            ? date.ToDateTime(new TimeOnly(12, 0))
            : date.ToDateTime(new TimeOnly(18, 0));

        return await _context.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId == doctorId &&
                        a.Slot != null &&
                        a.Slot.StartTime >= startDateTime &&
                        a.Slot.StartTime <= endDateTime &&
                        (a.StatusId == (int)AppointmentStatusId.CheckedIn ||
                         a.StatusId == (int)AppointmentStatusId.ReadyForDoctor ||
                         a.StatusId == (int)AppointmentStatusId.InProgress))
            .OrderByDescending(a => a.PriorityLevel)
            .ThenBy(a => a.QueueNumber)
            .Select(a => new QueuePatientDto(
                a.Id,
                a.AppointmentCode,
                a.Patient.FullName,
                a.QueueNumber,
                a.PriorityLevel,
                a.CheckedInAt))
            .ToListAsync(cancellationToken);
    }

    // Bắt đầu khám bệnh cho lượt khám (ReadyForDoctor -> InProgress)
    public async Task<Result> StartConsultationAsync(int doctorAccountId, int appointmentId, CancellationToken cancellationToken = default)
    {
        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.AccountId == doctorAccountId, cancellationToken);
        var deptHead = doctor == null
            ? await _context.DepartmentHeads.FirstOrDefaultAsync(dh => dh.AccountId == doctorAccountId, cancellationToken)
            : null;

        if (doctor == null && deptHead == null)
        {
            return Result.Failure("Forbidden", "Tài khoản không có quyền bác sĩ hoặc trưởng khoa.");
        }

        var departmentId = doctor?.DepartmentId ?? deptHead!.DepartmentId;
        var currentDoctorId = doctor?.Id;

        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({appointmentId})", cancellationToken);

            var appointment = await _context.Appointments
                .Include(a => a.VitalSigns)
                .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DepartmentId == departmentId, cancellationToken);

            if (appointment == null)
            {
                return Result.Failure("NotFound", "Không tìm thấy lượt khám trong khoa phụ trách.");
            }

            if (doctor != null && appointment.DoctorId.HasValue && appointment.DoctorId.Value != currentDoctorId)
            {
                return Result.Failure("Forbidden", "Lượt khám này thuộc về bác sĩ khác.");
            }

            if (appointment.StatusId == (int)AppointmentStatusId.InProgress)
            {
                // Đã bắt đầu khám rồi
                return Result.Success();
            }

            if (appointment.StatusId != (int)AppointmentStatusId.ReadyForDoctor)
            {
                return Result.Failure("InvalidState",
                    appointment.StatusId == (int)AppointmentStatusId.CheckedIn
                        ? "Bệnh nhân chưa được đo sinh hiệu. Vui lòng hướng dẫn người bệnh đo sinh hiệu trước khi khám."
                        : $"Lượt khám đang ở trạng thái {appointment.StatusId}, không thể bắt đầu khám.");
            }

            if (appointment.VitalSigns == null)
            {
                return Result.Failure("MissingVitals",
                    "Lượt khám chưa có dữ liệu sinh hiệu trong hệ thống. Vui lòng chuyển điều dưỡng đo sinh hiệu.");
            }

            if (!AppointmentStateMachine.CanTransition(AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.InProgress))
            {
                return Result.Failure("InvalidTransition", "Không thể chuyển trạng thái sang InProgress.");
            }

            var now = _timeProvider.GetLocalNow().DateTime;
            appointment.StatusId = (int)AppointmentStatusId.InProgress;
            appointment.UpdatedAt = now;

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = appointment.Id,
                OldStatusId = (int)AppointmentStatusId.ReadyForDoctor,
                NewStatusId = (int)AppointmentStatusId.InProgress,
                PerformedBy = doctorAccountId,
                ChangedAt = now
            });

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Result.Success();
        });
    }

    // Tạo hồ sơ bệnh án cho lượt khám
    public async Task<int> CreateMedicalRecordAsync(CreateMedicalRecordRequest request, CancellationToken cancellationToken = default)
    {
        var appointment = await _context.Appointments
            .Include(a => a.MedicalRecord)
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Không tìm thấy lượt hẹn có mã {request.AppointmentId}.");

        if (appointment.MedicalRecord != null)
            throw new InvalidOperationException("Lượt khám này đã có hồ sơ bệnh án.");

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == request.DoctorId, cancellationToken);
        if (doctor == null)
            throw new InvalidOperationException("Không tìm thấy thông tin bác sĩ tạo bệnh án.");

        if (appointment.DepartmentId != doctor.DepartmentId)
        {
            throw new InvalidOperationException("Bạn không có quyền tạo hồ sơ bệnh án cho lượt khám thuộc khoa khác.");
        }

        if (appointment.DoctorId.HasValue && appointment.DoctorId.Value != doctor.Id)
        {
            throw new InvalidOperationException("Bạn không có quyền tạo hồ sơ bệnh án cho lượt khám của bác sĩ khác.");
        }

        if (appointment.StatusId != (int)AppointmentStatusId.InProgress)
        {
            throw new InvalidOperationException(
                $"Chỉ có thể tạo bệnh án khi ca khám đang diễn ra (InProgress). Trạng thái hiện tại: {appointment.StatusId}.");
        }

        var oldStatus = (AppointmentStatusId)appointment.StatusId;
        if (oldStatus != AppointmentStatusId.Completed && !AppointmentStateMachine.CanTransition(oldStatus, AppointmentStatusId.Completed))
        {
            throw new InvalidOperationException("Trạng thái lượt khám hiện tại không hợp lệ để hoàn tất và tạo hồ sơ bệnh án.");
        }

        var now = _timeProvider.GetLocalNow().DateTime;

        var record = new MedicalRecord
        {
            AppointmentId = request.AppointmentId,
            DoctorId = request.DoctorId,
            Symptoms = request.Symptoms,
            ClinicalExamination = request.ClinicalExamination,
            PrimaryDiagnosisCode = request.PrimaryDiagnosisCode,
            PrimaryDiagnosisDesc = request.PrimaryDiagnosisDesc,
            SecondaryDiagnosis = string.IsNullOrWhiteSpace(request.SecondaryDiagnosis) ? null : request.SecondaryDiagnosis.Trim(),
            Conclusion = request.Conclusion,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.MedicalRecords.Add(record);

        // Ghi nhận nhật ký truy vết tạo hồ sơ bệnh án
        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = doctor.AccountId,
            PatientId = appointment.PatientId,
            MedicalRecord = record,
            Action = "CREATE_RECORD",
            OccurredAt = now,
            IpAddress = request.IpAddress
        });

        // Cập nhật thông tin Dị ứng của bệnh nhân nếu bác sĩ có nhập
        if (!string.IsNullOrWhiteSpace(request.Allergies))
        {
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == appointment.PatientId, cancellationToken);
            if (patient != null)
            {
                patient.Allergies = request.Allergies.Trim();
            }
        }

        // Tạo các chỉ định cận lâm sàng nếu có
        var validLabs = (request.LabOrders ?? []).Where(l => l.ServiceId > 0).ToList();
        bool waitingForLabs = validLabs.Count > 0;

        if (waitingForLabs)
        {
            foreach (var lab in validLabs)
            {
                _context.LabOrders.Add(new LabOrder
                {
                    MedicalRecord = record,
                    ServiceId = lab.ServiceId,
                    Note = string.IsNullOrWhiteSpace(lab.Note) ? null : lab.Note.Trim(),
                    Status = LabOrderStatus.Pending,
                    CreatedAt = now
                });
            }
        }

        // Lưu đơn thuốc nếu bác sĩ đã kê (bao gồm cả thuốc kê sơ bộ trước khi chuyển chờ kết quả cận lâm sàng)
        var initialPrescription = BuildInitialPrescription(record, request.PrescriptionItems, request.PrescriptionNote, DateOnly.FromDateTime(now));
        if (initialPrescription != null)
        {
            _context.Prescriptions.Add(initialPrescription);
            foreach (var item in initialPrescription.PrescriptionItems)
            {
                _context.PrescriptionItems.Add(item);
            }
        }

        // Chỉ chuyển sang Completed khi không có chỉ định chờ kết quả; nếu có chỉ định giữ InProgress để bác sĩ chẩn đoán tiếp ở tab Chỉ định & kết quả
        appointment.UpdatedAt = now;
        if (!waitingForLabs && oldStatus != AppointmentStatusId.Completed)
        {
            appointment.StatusId = (int)AppointmentStatusId.Completed;

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = appointment.Id,
                OldStatusId = (int)oldStatus,
                NewStatusId = (int)AppointmentStatusId.Completed,
                PerformedBy = doctor.AccountId,
                ChangedAt = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return record.Id;
    }

    public async Task<Result> SaveLabResultAsync(int doctorAccountId, int labOrderId, string resultText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return Result.Failure("Validation", "Vui lòng nhập nội dung kết quả cận lâm sàng.");
        }

        var doctor = await _context.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.AccountId == doctorAccountId, cancellationToken);
        var deptHead = doctor == null
            ? await _context.DepartmentHeads.AsNoTracking().FirstOrDefaultAsync(dh => dh.AccountId == doctorAccountId, cancellationToken)
            : null;
        if (doctor == null && deptHead == null)
        {
            return Result.Failure("Forbidden", "Tài khoản không có quyền cập nhật kết quả chỉ định.");
        }

        var departmentId = doctor?.DepartmentId ?? deptHead!.DepartmentId;
        var labOrder = await _context.LabOrders
            .Include(l => l.LabResult)
            .Include(l => l.MedicalRecord).ThenInclude(r => r.Doctor)
            .FirstOrDefaultAsync(l => l.Id == labOrderId, cancellationToken);

        if (labOrder == null)
        {
            return Result.Failure("NotFound", "Không tìm thấy phiếu chỉ định cận lâm sàng.");
        }

        if (labOrder.MedicalRecord.Doctor.DepartmentId != departmentId)
        {
            return Result.Failure("Forbidden", "Chỉ định này thuộc khoa khác.");
        }

        var now = _timeProvider.GetLocalNow().DateTime;
        if (labOrder.LabResult == null)
        {
            _context.LabResults.Add(new LabResult
            {
                OrderId = labOrder.Id,
                Result = resultText.Trim(),
                PerformedBy = doctorAccountId,
                RecordedAt = now
            });
        }
        else
        {
            labOrder.LabResult.Result = resultText.Trim();
            labOrder.LabResult.PerformedBy = doctorAccountId;
            labOrder.LabResult.RecordedAt = now;
        }

        labOrder.Status = LabOrderStatus.Completed;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CompleteRecordAfterLabsAsync(CompleteRecordAfterLabsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PrimaryDiagnosisCode) || string.IsNullOrWhiteSpace(request.PrimaryDiagnosisDesc))
        {
            return Result.Failure("Validation", "Vui lòng nhập đầy đủ mã ICD-10 và chẩn đoán chính.");
        }

        var doctor = await _context.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DoctorId, cancellationToken);
        if (doctor == null)
        {
            return Result.Failure("NotFound", "Không tìm thấy thông tin bác sĩ.");
        }

        var record = await _context.MedicalRecords
            .Include(r => r.Appointment)
            .Include(r => r.Prescriptions)
                .ThenInclude(p => p.PrescriptionItems)
            .FirstOrDefaultAsync(r => r.Id == request.MedicalRecordId, cancellationToken);

        if (record == null)
        {
            return Result.Failure("NotFound", "Không tìm thấy hồ sơ bệnh án.");
        }

        if (record.Appointment.DepartmentId != doctor.DepartmentId || record.DoctorId != doctor.Id)
        {
            return Result.Failure("Forbidden", "Bạn không có quyền hoàn tất bệnh án này.");
        }

        if (record.Appointment.StatusId != (int)AppointmentStatusId.InProgress
            || !AppointmentStateMachine.CanTransition(AppointmentStatusId.InProgress, AppointmentStatusId.Completed))
        {
            return Result.Failure("InvalidState", "Ca khám này đã được hoàn tất hoặc không ở trạng thái chờ kết quả.");
        }

        var now = _timeProvider.GetLocalNow().DateTime;
        record.PrimaryDiagnosisCode = request.PrimaryDiagnosisCode.Trim();
        record.PrimaryDiagnosisDesc = request.PrimaryDiagnosisDesc.Trim();
        record.SecondaryDiagnosis = string.IsNullOrWhiteSpace(request.SecondaryDiagnosis) ? null : request.SecondaryDiagnosis.Trim();
        record.Conclusion = string.IsNullOrWhiteSpace(request.Conclusion) ? "Đã hoàn tất khám sau cận lâm sàng" : request.Conclusion.Trim();
        record.UpdatedAt = now;

        var mutation = SyncPostLabPrescriptions(
            record,
            request.PrescriptionItems,
            request.PrescriptionNote,
            DateOnly.FromDateTime(now),
            request.ReplaceExistingPrescriptions);

        if (mutation.RemovedItems.Count > 0)
        {
            _context.PrescriptionItems.RemoveRange(mutation.RemovedItems);
        }

        if (mutation.RemovedPrescriptions.Count > 0)
        {
            _context.Prescriptions.RemoveRange(mutation.RemovedPrescriptions);
        }

        foreach (var p in mutation.AddedPrescriptions)
        {
            _context.Prescriptions.Add(p);
        }

        foreach (var item in mutation.AddedItems)
        {
            _context.PrescriptionItems.Add(item);
        }

        record.Appointment.StatusId = (int)AppointmentStatusId.Completed;
        record.Appointment.UpdatedAt = now;

        _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = record.AppointmentId,
            OldStatusId = (int)AppointmentStatusId.InProgress,
            NewStatusId = (int)AppointmentStatusId.Completed,
            PerformedBy = doctor.AccountId,
            ChangedAt = now
        });

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = doctor.AccountId,
            PatientId = record.Appointment.PatientId,
            MedicalRecordId = record.Id,
            Action = "UPDATE_RECORD",
            OccurredAt = now,
            IpAddress = request.IpAddress
        });

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public sealed record PrescriptionSyncMutation(
        IReadOnlyList<Prescription> AddedPrescriptions,
        IReadOnlyList<Prescription> RemovedPrescriptions,
        IReadOnlyList<PrescriptionItem> AddedItems,
        IReadOnlyList<PrescriptionItem> RemovedItems);

    public static Prescription? BuildInitialPrescription(
        MedicalRecord record,
        IEnumerable<CreatePrescriptionItemDto>? items,
        string? note,
        DateOnly prescribedDate)
    {
        var normalized = NormalizePrescriptionItems(items);
        if (normalized.Count == 0)
        {
            return null;
        }

        var prescription = new Prescription
        {
            MedicalRecord = record,
            MedicalRecordId = record.Id,
            PrescribedDate = prescribedDate,
            Note = TruncateText(note, 255)
        };

        foreach (var med in normalized)
        {
            prescription.PrescriptionItems.Add(new PrescriptionItem
            {
                Prescription = prescription,
                MedicationId = med.MedicationId,
                Quantity = med.Quantity,
                Dosage = med.Dosage,
                DurationDays = med.DurationDays,
                UsageInstructions = med.UsageInstructions
            });
        }

        record.Prescriptions.Add(prescription);
        return prescription;
    }

    public static PrescriptionSyncMutation SyncPostLabPrescriptions(
        MedicalRecord record,
        IEnumerable<CreatePrescriptionItemDto>? submittedItems,
        string? note,
        DateOnly prescribedDate,
        bool replaceExisting)
    {
        var normalized = NormalizePrescriptionItems(submittedItems);
        var addedPrescriptions = new List<Prescription>();
        var removedPrescriptions = new List<Prescription>();
        var addedItems = new List<PrescriptionItem>();
        var removedItems = new List<PrescriptionItem>();

        var existingPrescriptions = record.Prescriptions.OrderBy(p => p.Id).ToList();

        if (replaceExisting && normalized.Count == 0)
        {
            foreach (var p in existingPrescriptions)
            {
                removedItems.AddRange(p.PrescriptionItems);
                p.PrescriptionItems.Clear();
                removedPrescriptions.Add(p);
                record.Prescriptions.Remove(p);
            }

            return new PrescriptionSyncMutation(addedPrescriptions, removedPrescriptions, addedItems, removedItems);
        }

        if (!replaceExisting && normalized.Count == 0)
        {
            return new PrescriptionSyncMutation(addedPrescriptions, removedPrescriptions, addedItems, removedItems);
        }

        var primary = existingPrescriptions.FirstOrDefault();
        if (primary == null)
        {
            primary = new Prescription
            {
                MedicalRecord = record,
                MedicalRecordId = record.Id,
                PrescribedDate = prescribedDate,
                Note = TruncateText(note, 255)
            };
            record.Prescriptions.Add(primary);
            addedPrescriptions.Add(primary);
        }
        else
        {
            primary.PrescribedDate = prescribedDate;
            var cleanNote = TruncateText(note, 255);
            if (cleanNote != null)
            {
                primary.Note = cleanNote;
            }

            foreach (var extra in existingPrescriptions.Skip(1))
            {
                foreach (var extraItem in extra.PrescriptionItems.ToList())
                {
                    if (!replaceExisting)
                    {
                        var targetExisting = primary.PrescriptionItems.FirstOrDefault(x => x.MedicationId == extraItem.MedicationId);
                        if (targetExisting != null)
                        {
                            targetExisting.Quantity += extraItem.Quantity;
                            removedItems.Add(extraItem);
                        }
                        else
                        {
                            extraItem.Prescription = primary;
                            extraItem.PrescriptionId = primary.Id;
                            primary.PrescriptionItems.Add(extraItem);
                        }
                    }
                    else
                    {
                        removedItems.Add(extraItem);
                    }
                }

                extra.PrescriptionItems.Clear();
                removedPrescriptions.Add(extra);
                record.Prescriptions.Remove(extra);
            }
        }

        if (replaceExisting)
        {
            var requestedIds = normalized.Select(m => m.MedicationId).ToHashSet();
            var seenIds = new HashSet<int>();
            foreach (var existingItem in primary.PrescriptionItems.ToList())
            {
                if (!requestedIds.Contains(existingItem.MedicationId) || !seenIds.Add(existingItem.MedicationId))
                {
                    primary.PrescriptionItems.Remove(existingItem);
                    removedItems.Add(existingItem);
                }
            }

            foreach (var med in normalized)
            {
                var existing = primary.PrescriptionItems.FirstOrDefault(x => x.MedicationId == med.MedicationId);
                if (existing != null)
                {
                    existing.Quantity = med.Quantity;
                    existing.Dosage = med.Dosage;
                    existing.DurationDays = med.DurationDays;
                    existing.UsageInstructions = med.UsageInstructions;
                }
                else
                {
                    var newItem = new PrescriptionItem
                    {
                        Prescription = primary,
                        PrescriptionId = primary.Id,
                        MedicationId = med.MedicationId,
                        Quantity = med.Quantity,
                        Dosage = med.Dosage,
                        DurationDays = med.DurationDays,
                        UsageInstructions = med.UsageInstructions
                    };
                    primary.PrescriptionItems.Add(newItem);
                    addedItems.Add(newItem);
                }
            }
        }
        else
        {
            foreach (var med in normalized)
            {
                var existing = primary.PrescriptionItems.FirstOrDefault(x => x.MedicationId == med.MedicationId);
                if (existing != null)
                {
                    existing.Quantity += med.Quantity;
                    if (!string.IsNullOrWhiteSpace(med.Dosage))
                    {
                        existing.Dosage = med.Dosage;
                    }

                    if (med.DurationDays.HasValue)
                    {
                        existing.DurationDays = med.DurationDays;
                    }

                    if (!string.IsNullOrWhiteSpace(med.UsageInstructions))
                    {
                        existing.UsageInstructions = med.UsageInstructions;
                    }
                }
                else
                {
                    var newItem = new PrescriptionItem
                    {
                        Prescription = primary,
                        PrescriptionId = primary.Id,
                        MedicationId = med.MedicationId,
                        Quantity = med.Quantity,
                        Dosage = med.Dosage,
                        DurationDays = med.DurationDays,
                        UsageInstructions = med.UsageInstructions
                    };
                    primary.PrescriptionItems.Add(newItem);
                    addedItems.Add(newItem);
                }
            }
        }

        return new PrescriptionSyncMutation(addedPrescriptions, removedPrescriptions, addedItems, removedItems);
    }

    private static List<CreatePrescriptionItemDto> NormalizePrescriptionItems(IEnumerable<CreatePrescriptionItemDto>? items)
    {
        if (items == null)
        {
            return [];
        }

        return items
            .Where(p => p.MedicationId > 0 && p.Quantity > 0)
            .GroupBy(p => p.MedicationId)
            .Select(g =>
            {
                var lastWithDosage = g.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.Dosage));
                var lastWithDuration = g.LastOrDefault(x => x.DurationDays.HasValue);
                var lastWithInstructions = g.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.UsageInstructions));
                return new CreatePrescriptionItemDto
                {
                    MedicationId = g.Key,
                    Quantity = g.Sum(x => x.Quantity),
                    Dosage = TruncateText(lastWithDosage?.Dosage, 100),
                    DurationDays = lastWithDuration?.DurationDays,
                    UsageInstructions = TruncateText(lastWithInstructions?.UsageInstructions, 255)
                };
            })
            .ToList();
    }

    private static string? TruncateText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
