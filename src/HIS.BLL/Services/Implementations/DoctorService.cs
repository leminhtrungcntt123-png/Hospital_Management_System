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

        // Chuyển trạng thái lượt khám sang Completed
        appointment.StatusId = (int)AppointmentStatusId.Completed;
        appointment.UpdatedAt = now;

        _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            OldStatusId = (int)AppointmentStatusId.InProgress,
            NewStatusId = (int)AppointmentStatusId.Completed,
            PerformedBy = doctor.AccountId,
            ChangedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
        return record.Id;
    }
}
