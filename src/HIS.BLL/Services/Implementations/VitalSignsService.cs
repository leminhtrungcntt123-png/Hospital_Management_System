using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public sealed class VitalSignsService(
    AppDbContext db,
    TimeProvider clock) : IVitalSignsService
{
    private DateTime Now => DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);
    private DateOnly Today => DateOnly.FromDateTime(Now);

    private async Task<Nurse?> NurseAsync(int accountId, CancellationToken ct) =>
        await db.Nurses
            .Include(n => n.Department)
            .Include(n => n.Account)
            .SingleOrDefaultAsync(
                n => n.AccountId == accountId
                    && n.Account.IsActive
                    && n.Account.Role.Code == "NURSE"
                    && n.DepartmentId != null,
                ct);

    public async Task<Result<IReadOnlyList<VitalSignsQueueItemDto>>> GetQueueAsync(
        int staffAccountId,
        bool includeRecorded = true,
        CancellationToken ct = default)
    {
        var nurse = await NurseAsync(staffAccountId, ct);
        if (nurse == null || nurse.DepartmentId == null)
        {
            return Result.Failure<IReadOnlyList<VitalSignsQueueItemDto>>(
                "Forbidden", "Tài khoản không có quyền điều dưỡng trong khoa.");
        }

        var start = Today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var query = db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Room)
            .Include(a => a.Slot)
            .Include(a => a.VitalSigns).ThenInclude(v => v!.RecordedByNurse)
            .Where(a => a.DepartmentId == nurse.DepartmentId
                && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                    || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end)));

        if (includeRecorded)
        {
            query = query.Where(a => a.StatusId == (int)AppointmentStatusId.CheckedIn
                || a.StatusId == (int)AppointmentStatusId.ReadyForDoctor);
        }
        else
        {
            query = query.Where(a => a.StatusId == (int)AppointmentStatusId.CheckedIn && a.VitalSigns == null);
        }

        var list = await query
            .OrderByDescending(a => a.PriorityLevel)
            .ThenBy(a => a.StatusId == (int)AppointmentStatusId.CheckedIn ? 0 : 1)
            .ThenBy(a => a.CheckedInAt ?? a.CreatedAt)
            .ThenBy(a => a.Slot != null ? a.Slot.StartTime : a.CreatedAt)
            .Select(a => new VitalSignsQueueItemDto(
                a.Id,
                a.AppointmentCode,
                a.QueueNumber,
                a.PatientId,
                a.Patient.FullName,
                a.Patient.DateOfBirth,
                a.Patient.Gender == Gender.Male ? "Nam" : (a.Patient.Gender == Gender.Female ? "Nữ" : "Khác"),
                a.Patient.PhoneNumber,
                a.Doctor != null ? a.Doctor.FullName : "Chưa phân công",
                a.Room != null ? a.Room.RoomNumber : (a.Slot != null && a.Slot.Room != null ? a.Slot.Room.RoomNumber : ""),
                a.Slot != null ? a.Slot.StartTime : a.CreatedAt,
                a.StatusId,
                a.VitalSigns != null,
                a.VitalSigns != null
                    ? new VitalSignsDetailDto(
                        a.VitalSigns.Id,
                        a.VitalSigns.AppointmentId,
                        a.VitalSigns.Pulse,
                        a.VitalSigns.SystolicBp,
                        a.VitalSigns.DiastolicBp,
                        a.VitalSigns.Temperature,
                        a.VitalSigns.RespiratoryRate,
                        a.VitalSigns.Spo2,
                        a.VitalSigns.Weight,
                        a.VitalSigns.Height,
                        a.VitalSigns.RecordedBy,
                        a.VitalSigns.RecordedByNurse.FullName,
                        a.VitalSigns.RecordedAt)
                    : null
            ))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<VitalSignsQueueItemDto>>(list);
    }

    public async Task<Result<VitalSignsDetailDto>> GetDetailAsync(
        int staffAccountId,
        int appointmentId,
        CancellationToken ct = default)
    {
        var nurse = await NurseAsync(staffAccountId, ct);
        if (nurse == null || nurse.DepartmentId == null)
        {
            return Result.Failure<VitalSignsDetailDto>(
                "Forbidden", "Tài khoản không có quyền điều dưỡng trong khoa.");
        }

        var vitals = await db.VitalSigns
            .AsNoTracking()
            .Include(v => v.RecordedByNurse)
            .Include(v => v.Appointment)
            .SingleOrDefaultAsync(v => v.AppointmentId == appointmentId
                && v.Appointment.DepartmentId == nurse.DepartmentId, ct);

        if (vitals == null)
        {
            return Result.Failure<VitalSignsDetailDto>(
                "NotFound", "Không tìm thấy dữ liệu sinh hiệu cho lượt khám này trong khoa.");
        }

        return Result.Success(new VitalSignsDetailDto(
            vitals.Id,
            vitals.AppointmentId,
            vitals.Pulse,
            vitals.SystolicBp,
            vitals.DiastolicBp,
            vitals.Temperature,
            vitals.RespiratoryRate,
            vitals.Spo2,
            vitals.Weight,
            vitals.Height,
            vitals.RecordedBy,
            vitals.RecordedByNurse.FullName,
            vitals.RecordedAt));
    }

    public async Task<Result<VitalSignsDetailDto>> RecordOrUpdateAsync(
        int staffAccountId,
        RecordVitalSignsRequest request,
        CancellationToken ct = default)
    {
        var nurse = await NurseAsync(staffAccountId, ct);
        if (nurse == null || nurse.DepartmentId == null)
        {
            return Result.Failure<VitalSignsDetailDto>(
                "Forbidden", "Tài khoản không có quyền điều dưỡng trong khoa.");
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var dayStart = Today.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Row-level advisory lock on appointmentId
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({request.AppointmentId})", ct);

            var appointment = await db.Appointments
                .Include(a => a.VitalSigns)
                .SingleOrDefaultAsync(a => a.Id == request.AppointmentId
                    && a.DepartmentId == nurse.DepartmentId
                    // chỉ lượt khám của hôm nay: ca tồn từ ngày trước không được đẩy sang "chờ bác sĩ"
                    && ((a.Slot != null && a.Slot.StartTime >= dayStart && a.Slot.StartTime < dayEnd)
                        || (a.Slot == null && a.CreatedAt >= dayStart && a.CreatedAt < dayEnd)), ct);

            if (appointment == null)
            {
                return Result.Failure<VitalSignsDetailDto>(
                    "NotFound", "Không tìm thấy lượt khám hôm nay trong khoa phụ trách.");
            }

            var now = Now;

            if (appointment.VitalSigns == null)
            {
                // Lần đầu ghi sinh hiệu: Bắt buộc ở CheckedIn (3)
                if (appointment.StatusId != (int)AppointmentStatusId.CheckedIn)
                {
                    return Result.Failure<VitalSignsDetailDto>(
                        "InvalidState",
                        $"Chỉ có thể ghi sinh hiệu cho lượt khám đã tiếp nhận (CheckedIn). Trạng thái hiện tại: {appointment.StatusId}.");
                }

                if (!AppointmentStateMachine.CanTransition(AppointmentStatusId.CheckedIn, AppointmentStatusId.ReadyForDoctor))
                {
                    return Result.Failure<VitalSignsDetailDto>(
                        "InvalidTransition", "Chuyển trạng thái sang ReadyForDoctor không hợp lệ theo quy trình.");
                }

                var vitals = new VitalSigns
                {
                    AppointmentId = appointment.Id,
                    Pulse = request.Pulse,
                    SystolicBp = request.SystolicBp,
                    DiastolicBp = request.DiastolicBp,
                    Temperature = request.Temperature,
                    RespiratoryRate = request.RespiratoryRate,
                    Spo2 = request.Spo2,
                    Weight = request.Weight,
                    Height = request.Height,
                    RecordedBy = nurse.Id,
                    RecordedAt = now
                };
                db.VitalSigns.Add(vitals);

                appointment.StatusId = (int)AppointmentStatusId.ReadyForDoctor;
                appointment.UpdatedAt = now;

                db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
                {
                    AppointmentId = appointment.Id,
                    OldStatusId = (int)AppointmentStatusId.CheckedIn,
                    NewStatusId = (int)AppointmentStatusId.ReadyForDoctor,
                    PerformedBy = staffAccountId,
                    ChangedAt = now
                });

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Result.Success(new VitalSignsDetailDto(
                    vitals.Id,
                    vitals.AppointmentId,
                    vitals.Pulse,
                    vitals.SystolicBp,
                    vitals.DiastolicBp,
                    vitals.Temperature,
                    vitals.RespiratoryRate,
                    vitals.Spo2,
                    vitals.Weight,
                    vitals.Height,
                    vitals.RecordedBy,
                    nurse.FullName,
                    vitals.RecordedAt));
            }
            else
            {
                // Cập nhật sinh hiệu: Chỉ cho phép khi ca khám vẫn ở ReadyForDoctor (4)
                if (appointment.StatusId != (int)AppointmentStatusId.ReadyForDoctor)
                {
                    return Result.Failure<VitalSignsDetailDto>(
                        "InvalidState",
                        "Bác sĩ đã bắt đầu khám hoặc ca khám đã kết thúc, không thể chỉnh sửa sinh hiệu.");
                }

                var vitals = appointment.VitalSigns;
                vitals.Pulse = request.Pulse;
                vitals.SystolicBp = request.SystolicBp;
                vitals.DiastolicBp = request.DiastolicBp;
                vitals.Temperature = request.Temperature;
                vitals.RespiratoryRate = request.RespiratoryRate;
                vitals.Spo2 = request.Spo2;
                vitals.Weight = request.Weight;
                vitals.Height = request.Height;
                vitals.RecordedBy = nurse.Id;
                vitals.RecordedAt = now;

                appointment.UpdatedAt = now;

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Result.Success(new VitalSignsDetailDto(
                    vitals.Id,
                    vitals.AppointmentId,
                    vitals.Pulse,
                    vitals.SystolicBp,
                    vitals.DiastolicBp,
                    vitals.Temperature,
                    vitals.RespiratoryRate,
                    vitals.Spo2,
                    vitals.Weight,
                    vitals.Height,
                    vitals.RecordedBy,
                    nurse.FullName,
                    vitals.RecordedAt));
            }
        });
    }
}
