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
