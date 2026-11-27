using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Common;

internal static class ReplacementDoctorHelper
{
    public static (DateTime WindowStart, DateTime WindowEnd) GetShiftWindow(DateOnly date, ExamShift? shift) => shift switch
    {
        ExamShift.Morning => (date.ToDateTime(new TimeOnly(6, 0)), date.ToDateTime(new TimeOnly(12, 0))),
        ExamShift.Afternoon => (date.ToDateTime(new TimeOnly(12, 0)), date.ToDateTime(new TimeOnly(18, 0))),
        _ => (date.ToDateTime(TimeOnly.MinValue), date.ToDateTime(new TimeOnly(23, 59, 59)))
    };

    public static async Task<bool> IsDoctorBusyAsync(
        AppDbContext db,
        int doctorId,
        int accountId,
        DateOnly date,
        ExamShift? shift,
        CancellationToken ct)
    {
        bool hasSchedule = await db.DoctorSchedules.AnyAsync(s =>
            s.DoctorId == doctorId
            && s.WorkDate == date
            && (s.Status == DoctorScheduleStatus.Approved || s.Status == DoctorScheduleStatus.Bidding)
            && (shift == null || s.Shift == shift.Value), ct);
        if (hasSchedule) return true;

        var (windowStart, windowEnd) = GetShiftWindow(date, shift);
        bool hasDuty = await db.DutyShifts.AnyAsync(ds =>
            ds.AccountId == accountId
            && ds.Status == DutyShiftStatus.Approved
            && ds.StartTime < windowEnd
            && ds.EndTime > windowStart, ct);
        if (hasDuty) return true;

        bool hasLeave = await db.ScheduleExceptions.AnyAsync(ex =>
            ex.DoctorId == doctorId
            && ex.Date == date
            && ex.Status == ScheduleExceptionStatus.Approved
            && (shift == null || ex.Shift == null || ex.Shift == shift.Value), ct);
        return hasLeave;
    }

    public static async Task<IReadOnlyList<AdminOption>> GetAvailableReplacementsAsync(
        AppDbContext db,
        int departmentId,
        int excludeDoctorId,
        DateOnly date,
        ExamShift? shift,
        CancellationToken ct)
    {
        var candidates = await db.Doctors
            .AsNoTracking()
            .Where(d => d.DepartmentId == departmentId && d.Id != excludeDoctorId && d.Account.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName, d.AccountId })
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var candidateAccountIds = candidates.Select(c => c.AccountId).ToList();
        var (windowStart, windowEnd) = GetShiftWindow(date, shift);

        var busyBySchedule = await db.DoctorSchedules
            .AsNoTracking()
            .Where(s => candidateIds.Contains(s.DoctorId)
                && s.WorkDate == date
                && (s.Status == DoctorScheduleStatus.Approved || s.Status == DoctorScheduleStatus.Bidding)
                && (shift == null || s.Shift == shift.Value))
            .Select(s => s.DoctorId)
            .Distinct()
            .ToListAsync(ct);

        var busyByDutyAccounts = await db.DutyShifts
            .AsNoTracking()
            .Where(ds => candidateAccountIds.Contains(ds.AccountId)
                && ds.Status == DutyShiftStatus.Approved
                && ds.StartTime < windowEnd
                && ds.EndTime > windowStart)
            .Select(ds => ds.AccountId)
            .Distinct()
            .ToListAsync(ct);

        var busyByLeave = await db.ScheduleExceptions
            .AsNoTracking()
            .Where(ex => candidateIds.Contains(ex.DoctorId)
                && ex.Date == date
                && ex.Status == ScheduleExceptionStatus.Approved
                && (shift == null || ex.Shift == null || ex.Shift == shift.Value))
            .Select(ex => ex.DoctorId)
            .Distinct()
            .ToListAsync(ct);

        var busyIds = new HashSet<int>(busyBySchedule);
        busyIds.UnionWith(busyByLeave);
        var busyAccSet = new HashSet<int>(busyByDutyAccounts);

        return candidates
            .Where(c => !busyIds.Contains(c.Id) && !busyAccSet.Contains(c.AccountId))
            .Select(c => new AdminOption(c.Id, c.FullName))
            .ToList();
    }
}
