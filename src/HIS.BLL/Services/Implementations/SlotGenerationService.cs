using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class SlotGenerationService : ISlotGenerationService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public SlotGenerationService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<Result<SlotGenerationResultDto>> GenerateSlotsAsync(
        DateOnly startDate,
        DateOnly endDate,
        int? departmentId = null,
        CancellationToken ct = default)
    {
        if (endDate < startDate)
        {
            return Result.Failure<SlotGenerationResultDto>("INVALID_DATE_RANGE", "Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu.");
        }

        var query = _context.DoctorSchedules
            .Include(s => s.Doctor)
            .Include(s => s.Room)
            .Where(s => s.WorkDate >= startDate && s.WorkDate <= endDate 
                     && s.Status == DoctorScheduleStatus.Approved 
                     && s.RoomId.HasValue);

        if (departmentId.HasValue && departmentId.Value > 0)
        {
            query = query.Where(s => s.DepartmentId == departmentId.Value);
        }

        var schedules = await query.ToListAsync(ct);
        if (schedules.Count == 0)
        {
            return Result<SlotGenerationResultDto>.Success(new SlotGenerationResultDto(0, 0, 0));
        }

        var doctorIds = schedules.Select(s => s.DoctorId).Distinct().ToList();
        var approvedExceptions = await _context.ScheduleExceptions
            .AsNoTracking()
            .Where(e => doctorIds.Contains(e.DoctorId) 
                     && e.Date >= startDate && e.Date <= endDate 
                     && e.Status == ScheduleExceptionStatus.Approved)
            .ToListAsync(ct);

        var minDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var maxDateTime = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var existingSlots = await _context.AppointmentSlots
            .AsNoTracking()
            .Where(sl => doctorIds.Contains(sl.DoctorId) && sl.StartTime >= minDateTime && sl.StartTime < maxDateTime)
            .Select(sl => new { sl.DoctorId, sl.StartTime })
            .ToListAsync(ct);

        var existingSet = existingSlots.Select(x => (x.DoctorId, x.StartTime)).ToHashSet();

        int createdCount = 0;
        int existingCount = 0;
        int exceptionSkippedCount = 0;

        foreach (var s in schedules)
        {
            bool isSkipped = approvedExceptions.Any(e => e.DoctorId == s.DoctorId 
                                                     && e.Date == s.WorkDate 
                                                     && (e.Shift == null || e.Shift == s.Shift));
            if (isSkipped)
            {
                exceptionSkippedCount++;
                continue;
            }

            int duration = s.SlotDurationMinutes > 0 ? s.SlotDurationMinutes : 30;
            var startDateTime = s.WorkDate.ToDateTime(s.StartTime);
            var endDateTime = s.WorkDate.ToDateTime(s.EndTime);

            for (var cur = startDateTime; cur.AddMinutes(duration) <= endDateTime; cur = cur.AddMinutes(duration))
            {
                if (existingSet.Contains((s.DoctorId, cur)))
                {
                    existingCount++;
                }
                else
                {
                    _context.AppointmentSlots.Add(new AppointmentSlot
                    {
                        ScheduleId = s.Id,
                        DoctorId = s.DoctorId,
                        RoomId = s.RoomId!.Value,
                        StartTime = cur,
                        EndTime = cur.AddMinutes(duration),
                        Capacity = 1,
                        BookedCount = 0,
                        Status = SlotStatus.Available,
                        Version = 0
                    });
                    existingSet.Add((s.DoctorId, cur));
                    createdCount++;
                }
            }
        }

        if (createdCount > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return Result<SlotGenerationResultDto>.Success(new SlotGenerationResultDto(createdCount, existingCount, exceptionSkippedCount));
    }

    public async Task<Result<int>> GenerateSlotsForDateRangeAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var res = await GenerateSlotsAsync(fromDate, toDate, null, ct);
        if (!res.IsSuccess)
        {
            return Result.Failure<int>(res.Error);
        }
        return Result<int>.Success(res.Value?.CreatedCount ?? 0);
    }

    public async Task<IReadOnlyList<AppointmentSlotItemDto>> SearchSlotsAsync(SlotSearchRequest request, CancellationToken ct = default)
    {
        var minDateTime = request.FilterStartDate.ToDateTime(TimeOnly.MinValue);
        var maxDateTime = request.FilterEndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var query = _context.AppointmentSlots
            .AsNoTracking()
            .Include(s => s.Doctor).ThenInclude(d => d.Department)
            .Include(s => s.Room).ThenInclude(r => r.Department)
            .Include(s => s.Schedule)
            .Where(s => s.StartTime >= minDateTime && s.StartTime < maxDateTime);

        var shifts = request.Shifts ?? [];
        bool hasMorning = shifts.Any(s => s.Equals("MORNING", StringComparison.OrdinalIgnoreCase));
        bool hasAfternoon = shifts.Any(s => s.Equals("AFTERNOON", StringComparison.OrdinalIgnoreCase));

        if (hasMorning && !hasAfternoon)
        {
            query = query.Where(s => s.StartTime.Hour < 12);
        }
        else if (!hasMorning && hasAfternoon)
        {
            query = query.Where(s => s.StartTime.Hour >= 12);
        }
        else if (!hasMorning && !hasAfternoon)
        {
            return [];
        }

        if (request.DepartmentId.HasValue && request.DepartmentId.Value > 0)
        {
            var deptId = request.DepartmentId.Value;
            query = query.Where(s => s.Doctor.DepartmentId == deptId || s.Room.DepartmentId == deptId);
        }

        if (request.RoomId.HasValue && request.RoomId.Value > 0)
        {
            query = query.Where(s => s.RoomId == request.RoomId.Value);
        }

        if (request.DoctorId.HasValue && request.DoctorId.Value > 0)
        {
            query = query.Where(s => s.DoctorId == request.DoctorId.Value);
        }

        query = query.OrderBy(s => s.StartTime).ThenBy(s => s.RoomId).ThenBy(s => s.DoctorId);

        var slots = await query.ToListAsync(ct);

        return slots.Select(s =>
        {
            var doctor = s.Doctor;
            var room = s.Room;
            var deptName = room?.Department?.Name ?? doctor?.Department?.Name ?? "";
            var rawName = doctor?.FullName ?? "";
            var academic = string.IsNullOrWhiteSpace(doctor?.AcademicTitle) ? "BS." : doctor.AcademicTitle.Trim();
            var cleanName = System.Text.RegularExpressions.Regex.Replace(rawName, @"^(PGS\.TS|PGS|TS|ThS|BS|BS\.CKI|BS\.CKII)\.?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            var docName = doctor != null ? $"{academic} {cleanName}" : "Chưa phân bổ";
            var workDate = DateOnly.FromDateTime(s.StartTime);
            var startTime = TimeOnly.FromDateTime(s.StartTime);
            var endTime = TimeOnly.FromDateTime(s.EndTime);
            var remaining = Math.Max(0, s.Capacity - s.BookedCount);
            var statusStr = s.Status switch
            {
                SlotStatus.Full => "FULL",
                SlotStatus.Blocked => "BLOCKED",
                _ => "AVAILABLE"
            };

            return new AppointmentSlotItemDto(
                s.Id,
                docName,
                academic,
                deptName,
                room?.RoomName ?? "",
                room?.RoomNumber ?? "",
                workDate,
                startTime,
                endTime,
                s.Capacity,
                s.BookedCount,
                remaining,
                statusStr
            );
        }).ToList();
    }

    public async Task<Result<int>> ExpirePendingHoldsAsync(CancellationToken ct = default)
    {
        var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
        var expiredAppointments = await _context.Appointments
            .Include(a => a.Slot)
            .Where(a => a.StatusId == (int)AppointmentStatusId.Pending && a.HoldUntil.HasValue && a.HoldUntil.Value < now)
            .ToListAsync(ct);

        if (expiredAppointments.Count == 0) return Result<int>.Success(0);

        foreach (var app in expiredAppointments)
        {
            app.StatusId = (int)AppointmentStatusId.Expired;
            app.UpdatedAt = now;
            if (app.Slot != null && app.Slot.BookedCount > 0)
            {
                app.Slot.BookedCount--;
                if (app.Slot.Status == SlotStatus.Full)
                {
                    app.Slot.Status = SlotStatus.Available;
                }
            }

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = app.Id,
                OldStatusId = (int)AppointmentStatusId.Pending,
                NewStatusId = (int)AppointmentStatusId.Expired,
                ChangedAt = now
            });
        }

        await _context.SaveChangesAsync(ct);
        return Result<int>.Success(expiredAppointments.Count);
    }
}
