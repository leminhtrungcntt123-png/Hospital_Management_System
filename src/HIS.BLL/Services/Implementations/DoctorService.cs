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
            .OrderBy(a => a.PriorityLevel)
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

        if (appointment.DoctorId.HasValue && appointment.DoctorId.Value != doctor.Id && appointment.DepartmentId != doctor.DepartmentId)
        {
            throw new InvalidOperationException("Bạn không có quyền tạo hồ sơ bệnh án cho lượt khám thuộc khoa khác.");
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

        // Chuyển trạng thái lượt khám sang Completed nếu đang InProgress hoặc ReadyForDoctor
        appointment.StatusId = (int)AppointmentStatusId.Completed;
        appointment.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);
        return record.Id;
    }
}
