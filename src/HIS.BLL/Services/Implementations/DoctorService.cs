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

        var now = _timeProvider.GetLocalNow().DateTime;

        var record = new MedicalRecord
        {
            AppointmentId = request.AppointmentId,
            DoctorId = request.DoctorId,
            Symptoms = request.Symptoms,
            ClinicalExamination = request.ClinicalExamination,
            PrimaryDiagnosisCode = request.PrimaryDiagnosisCode,
            PrimaryDiagnosisDesc = request.PrimaryDiagnosisDesc,
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
        if (request.LabOrders != null && request.LabOrders.Count > 0)
        {
            foreach (var lab in request.LabOrders.Where(l => l.ServiceId > 0))
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

        // Tạo đơn thuốc và các chi tiết thuốc nếu có
        if (request.PrescriptionItems != null && request.PrescriptionItems.Count > 0)
        {
            var validMeds = request.PrescriptionItems.Where(p => p.MedicationId > 0 && p.Quantity > 0).ToList();
            if (validMeds.Count > 0)
            {
                var prescription = new Prescription
                {
                    MedicalRecord = record,
                    PrescribedDate = DateOnly.FromDateTime(now),
                    Note = string.IsNullOrWhiteSpace(request.PrescriptionNote) ? null : request.PrescriptionNote.Trim()
                };
                _context.Prescriptions.Add(prescription);

                foreach (var med in validMeds)
                {
                    _context.PrescriptionItems.Add(new PrescriptionItem
                    {
                        Prescription = prescription,
                        MedicationId = med.MedicationId,
                        Quantity = med.Quantity,
                        Dosage = string.IsNullOrWhiteSpace(med.Dosage) ? null : med.Dosage.Trim(),
                        DurationDays = med.DurationDays,
                        UsageInstructions = string.IsNullOrWhiteSpace(med.UsageInstructions) ? null : med.UsageInstructions.Trim()
                    });
                }
            }
        }

        // Chuyển trạng thái lượt khám sang Completed và ghi nhận lịch sử trạng thái
        var oldStatus = (AppointmentStatusId)appointment.StatusId;
        if (oldStatus != AppointmentStatusId.Completed && AppointmentStateMachine.CanTransition(oldStatus, AppointmentStatusId.Completed))
        {
            appointment.StatusId = (int)AppointmentStatusId.Completed;
            appointment.UpdatedAt = now;

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
}
