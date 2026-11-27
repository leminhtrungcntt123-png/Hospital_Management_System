using HIS.BLL.DTOs;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public partial class DepartmentHeadService
{
    public async Task<SchedulingBoardDto> GetSchedulingBoardAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken ct = default)
    {
        var departments = await _context.Departments.AsNoTracking()
            .OrderBy(department => department.Name)
            .Select(department => new
            {
                department.Id,
                department.Name,
                HeadName = department.DepartmentHead != null ? department.DepartmentHead.FullName : null
            })
            .ToListAsync(ct);

        var doctorCounts = await _context.Doctors.AsNoTracking()
            .Where(doctor => doctor.Account.IsActive)
            .GroupBy(doctor => doctor.DepartmentId)
            .Select(group => new { DepartmentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.DepartmentId, row => row.Count, ct);

        var roomCounts = await _context.ClinicRooms.AsNoTracking()
            .Where(room => room.IsActive)
            .GroupBy(room => room.DepartmentId)
            .Select(group => new { DepartmentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.DepartmentId, row => row.Count, ct);

        var schedules = await _context.DoctorSchedules.AsNoTracking()
            .Where(schedule => schedule.WorkDate >= weekStart && schedule.WorkDate <= weekEnd
                && (schedule.Status == DoctorScheduleStatus.Approved || schedule.Status == DoctorScheduleStatus.Bidding)
                && schedule.Doctor.Account.IsActive)
            .Select(schedule => new
            {
                schedule.DepartmentId,
                schedule.DoctorId,
                schedule.WorkDate,
                schedule.Shift,
                schedule.Status
            })
            .ToListAsync(ct);

        var periods = await _context.SchedulingPeriods.AsNoTracking()
            .Where(period => period.Status == SchedulingPeriodStatus.Published || period.Status == SchedulingPeriodStatus.Open)
            .Select(period => new { period.DepartmentId, period.StartDate, period.EndDate, period.Status })
            .ToListAsync(ct);

        var rows = new List<SchedulingBoardDepartmentDto>();
        foreach (var department in departments)
        {
            var doctorCount = doctorCounts.GetValueOrDefault(department.Id);
            if (doctorCount == 0)
            {
                continue;
            }

            var departmentSchedules = schedules.Where(schedule => schedule.DepartmentId == department.Id).ToList();
            var approved = departmentSchedules.Count(schedule => schedule.Status == DoctorScheduleStatus.Approved);
            var pending = departmentSchedules.Count(schedule => schedule.Status == DoctorScheduleStatus.Bidding);
            var registered = departmentSchedules.Select(schedule => schedule.DoctorId).Distinct().Count();

            var rooms = roomCounts.GetValueOrDefault(department.Id);
            var understaffed = rooms == 0
                ? 0
                : departmentSchedules
                    .GroupBy(schedule => (schedule.WorkDate, schedule.Shift))
                    .Count(cell => cell.Count(schedule => schedule.Status == DoctorScheduleStatus.Approved) < rooms);

            var published = periods.Any(period =>
                period.DepartmentId == department.Id
                && period.Status == SchedulingPeriodStatus.Published
                && period.StartDate <= weekEnd && period.EndDate >= weekStart);
            var open = periods.Any(period =>
                period.DepartmentId == department.Id
                && period.Status == SchedulingPeriodStatus.Open
                && period.StartDate <= weekEnd && period.EndDate >= weekStart);

            rows.Add(new SchedulingBoardDepartmentDto(
                department.Id, department.Name, department.HeadName,
                doctorCount, registered, approved, pending, published, open, understaffed));
        }

        var totals = new SchedulingBoardTotalsDto(
            rows.Count(row => row.IsPublished),
            rows.Count,
            rows.Sum(row => row.RegisteredDoctorCount),
            rows.Sum(row => row.DoctorCount),
            rows.Sum(row => row.PendingShifts),
            rows.Sum(row => row.UnderstaffedShifts));

        return new SchedulingBoardDto(
            DateOnly.FromDateTime(LocalNow), weekStart, weekEnd, totals, rows);
    }
}
