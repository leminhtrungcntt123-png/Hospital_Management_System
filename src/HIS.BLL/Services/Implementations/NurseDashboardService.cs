using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public sealed class NurseDashboardService(
    AppDbContext db,
    TimeProvider clock) : INurseDashboardService
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

    public async Task<Result<NurseDashboardSummaryDto>> GetDashboardSummaryAsync(
        int staffAccountId,
        CancellationToken ct = default)
    {
        var nurse = await NurseAsync(staffAccountId, ct);
        if (nurse == null || nurse.DepartmentId == null)
        {
            return Result.Failure<NurseDashboardSummaryDto>(
                "Forbidden", "Tài khoản không có quyền điều dưỡng trong khoa.");
        }

        var start = Today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Room)
            .Include(a => a.Slot)
            .Include(a => a.VitalSigns).ThenInclude(v => v!.RecordedByNurse)
            .Where(a => a.DepartmentId == nurse.DepartmentId
                && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                    || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end)))
            .ToListAsync(ct);

        var totalToday = appointments.Count(a => a.StatusId >= (int)AppointmentStatusId.Confirmed
            && a.StatusId <= (int)AppointmentStatusId.Completed);
        var waitingVitals = appointments.Count(a => a.StatusId == (int)AppointmentStatusId.CheckedIn);
        var readyForDoctor = appointments.Count(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor);
        var waitingProcess = waitingVitals + readyForDoctor;
        var inProgress = appointments.Count(a => a.StatusId == (int)AppointmentStatusId.InProgress);
        var completed = appointments.Count(a => a.StatusId == (int)AppointmentStatusId.Completed);

        var todayTasks = appointments
            .Where(a => a.StatusId is (int)AppointmentStatusId.Confirmed
                or (int)AppointmentStatusId.CheckedIn
                or (int)AppointmentStatusId.ReadyForDoctor
                or (int)AppointmentStatusId.InProgress)
            .OrderByDescending(a => a.PriorityLevel)
            .ThenBy(a => a.StatusId == (int)AppointmentStatusId.CheckedIn ? 0
                : (a.StatusId == (int)AppointmentStatusId.Confirmed ? 1 : 2))
            .ThenBy(a => a.QueueNumber ?? "ZZZ")
            .ThenBy(a => a.Slot != null ? a.Slot.StartTime : a.CreatedAt)
            .Take(50)
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
            .ToList();

        var summary = new NurseDashboardSummaryDto(
            nurse.Department!.Name,
            Today,
            totalToday,
            waitingProcess,
            waitingVitals,
            readyForDoctor,
            inProgress,
            completed,
            todayTasks);

        return Result.Success(summary);
    }

    public async Task<Result<IReadOnlyList<NursePatientListItemDto>>> GetPatientsAsync(
        int staffAccountId,
        string? keyword,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var nurse = await NurseAsync(staffAccountId, ct);
        if (nurse == null || nurse.DepartmentId == null)
        {
            return Result.Failure<IReadOnlyList<NursePatientListItemDto>>(
                "Forbidden", "Tài khoản không có quyền điều dưỡng trong khoa.");
        }

        var query = db.Patients
            .AsNoTracking()
            .Where(p => p.Appointments.Any(a => a.DepartmentId == nurse.DepartmentId));

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim().ToLower();
            query = query.Where(p => p.FullName.ToLower().Contains(keyword)
                || p.PhoneNumber.Contains(keyword)
                || (p.NationalId != null && p.NationalId.Contains(keyword)));
        }

        var patients = await query
            .OrderBy(p => p.FullName)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new NursePatientListItemDto(
                p.Id,
                p.FullName,
                p.DateOfBirth,
                p.Gender == Gender.Male ? "Nam" : (p.Gender == Gender.Female ? "Nữ" : "Khác"),
                p.PhoneNumber,
                p.NationalId,
                p.Appointments.Count(a => a.DepartmentId == nurse.DepartmentId),
                p.Appointments.Where(a => a.DepartmentId == nurse.DepartmentId)
                    .OrderByDescending(a => a.CreatedAt)
                    .Select(a => (DateTime?)a.CreatedAt)
                    .FirstOrDefault(),
                p.Appointments.Where(a => a.DepartmentId == nurse.DepartmentId)
                    .OrderByDescending(a => a.CreatedAt)
                    .Select(a => (int?)a.StatusId)
                    .FirstOrDefault()
            ))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<NursePatientListItemDto>>(patients);
    }
}
