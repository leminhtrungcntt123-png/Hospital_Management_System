using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class DepartmentHeadService : IDepartmentHeadService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DepartmentHeadService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    private static DateTime UtcUnspecified => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    public async Task<int> ResolveDepartmentIdByAccountIdAsync(int accountId, CancellationToken ct = default)
    {
        var head = await _context.DepartmentHeads
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.AccountId == accountId, ct);

        if (head != null)
        {
            return head.DepartmentId;
        }

        // Fallback: nếu tài khoản là bác sĩ thuộc khoa
        var doctor = await _context.Doctors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.AccountId == accountId, ct);

        if (doctor != null)
        {
            return doctor.DepartmentId;
        }

        // Fallback: nếu tài khoản là điều dưỡng thuộc khoa
        var nurse = await _context.Nurses
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.AccountId == accountId, ct);

        if (nurse != null && nurse.DepartmentId.HasValue && nurse.DepartmentId.Value > 0)
        {
            return nurse.DepartmentId.Value;
        }

        return 0;
    }

    // 1. Quản lý đề nghị ngoại lệ (Nghỉ phép / Đổi ca)
    public async Task<IReadOnlyList<DepartmentExceptionItemDto>> GetExceptionsAsync(int departmentId, ScheduleExceptionStatus? status = null, CancellationToken ct = default)
    {
        var query = _context.ScheduleExceptions
            .AsNoTracking()
            .Include(e => e.Doctor)
            .Include(e => e.ReplacementDoctor)
            .Include(e => e.ApprovedByAccount)
            .Where(e => e.Doctor.DepartmentId == departmentId);

        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }

        var exceptions = await query
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        var validStatusIds = new[] { 1, 2, 3, 4, 5 };
        var result = new List<DepartmentExceptionItemDto>(exceptions.Count);

        foreach (var e in exceptions)
        {
            var startOfDay = e.Date.ToDateTime(new TimeOnly(0, 0, 0));
            var endOfDay = e.Date.ToDateTime(new TimeOnly(23, 59, 59, 999));

            var appQuery = _context.Appointments
                .AsNoTracking()
                .Where(a => a.DoctorId == e.DoctorId && validStatusIds.Contains(a.StatusId));

            if (e.Shift == ExamShift.Morning)
            {
                appQuery = appQuery.Where(a =>
                    (a.Slot != null && a.Slot.StartTime >= startOfDay && a.Slot.StartTime <= endOfDay && a.Slot.StartTime.Hour < 12) ||
                    (a.SlotId == null && a.CreatedAt >= startOfDay && a.CreatedAt <= endOfDay && a.CreatedAt.Hour < 12));
            }
            else if (e.Shift == ExamShift.Afternoon)
            {
                appQuery = appQuery.Where(a =>
                    (a.Slot != null && a.Slot.StartTime >= startOfDay && a.Slot.StartTime <= endOfDay && a.Slot.StartTime.Hour >= 12) ||
                    (a.SlotId == null && a.CreatedAt >= startOfDay && a.CreatedAt <= endOfDay && a.CreatedAt.Hour >= 12));
            }
            else
            {
                appQuery = appQuery.Where(a =>
                    (a.Slot != null && a.Slot.StartTime >= startOfDay && a.Slot.StartTime <= endOfDay) ||
                    (a.SlotId == null && a.CreatedAt >= startOfDay && a.CreatedAt <= endOfDay));
            }

            var affectedCount = await appQuery.CountAsync(ct);

            result.Add(new DepartmentExceptionItemDto(
                e.Id,
                e.DoctorId,
                e.Doctor.FullName,
                e.Date,
                e.Shift,
                e.Type,
                e.Status,
                e.ReplacementDoctorId,
                e.ReplacementDoctor != null ? e.ReplacementDoctor.FullName : null,
                e.Reason ?? "Không ghi rõ",
                e.CreatedAt,
                e.ApprovedByAccount != null ? e.ApprovedByAccount.Username : null,
                e.ApprovedAt,
                affectedCount
            ));
        }

        return result;
    }

    public async Task<Result> ApproveExceptionAsync(ApproveExceptionRequest request, CancellationToken ct = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var ex = await _context.ScheduleExceptions
                .Include(e => e.Doctor)
                .FirstOrDefaultAsync(e => e.Id == request.ExceptionId, ct);

            if (ex == null)
                return Result.Failure("NOT_FOUND", "Không tìm thấy đề nghị ngoại lệ.");

            if (!request.DepartmentId.HasValue || request.DepartmentId.Value <= 0 || (ex.Doctor != null && ex.Doctor.DepartmentId != request.DepartmentId.Value))
                return Result.Failure("FORBIDDEN", "Bạn chỉ có thể phê duyệt đề nghị của bác sĩ thuộc khoa mình quản lý.");

            if (ex.Status != ScheduleExceptionStatus.Pending)
                return Result.Failure("INVALID_STATUS", "Chỉ có thể phê duyệt đề nghị đang ở trạng thái chờ duyệt.");

            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(11028, 1)", ct);

            var now = _timeProvider.GetLocalNow().DateTime;

            ex.Status = ScheduleExceptionStatus.Approved;
            ex.ApprovedBy = request.DepartmentHeadAccountId;
            ex.ApprovedAt = now;

            var startOfDay = ex.Date.ToDateTime(new TimeOnly(0, 0));
            var endOfDay = ex.Date.ToDateTime(new TimeOnly(23, 59, 59));

            var slotsQuery = _context.AppointmentSlots
                .Where(s => s.DoctorId == ex.DoctorId && s.StartTime >= startOfDay && s.StartTime <= endOfDay);

            if (ex.Shift == ExamShift.Morning)
                slotsQuery = slotsQuery.Where(s => s.StartTime.Hour < 12);
            else if (ex.Shift == ExamShift.Afternoon)
                slotsQuery = slotsQuery.Where(s => s.StartTime.Hour >= 12);

            var slots = await slotsQuery.ToListAsync(ct);

            var appointmentsQuery = _context.Appointments
                .Include(a => a.Patient)
                .Where(a => a.DoctorId == ex.DoctorId &&
                            a.Slot != null &&
                            a.Slot.StartTime >= startOfDay &&
                            a.Slot.StartTime <= endOfDay &&
                            (a.StatusId == (int)AppointmentStatusId.Confirmed || a.StatusId == (int)AppointmentStatusId.Pending));

            if (ex.Shift == ExamShift.Morning)
                appointmentsQuery = appointmentsQuery.Where(a => a.Slot!.StartTime.Hour < 12);
            else if (ex.Shift == ExamShift.Afternoon)
                appointmentsQuery = appointmentsQuery.Where(a => a.Slot!.StartTime.Hour >= 12);

            var affectedApps = await appointmentsQuery.ToListAsync(ct);

            // Trường hợp 1: Có bác sĩ thay thế (Đổi ca / Nghỉ có người trực thay)
            if (ex.ReplacementDoctorId.HasValue && ex.ReplacementDoctorId.Value > 0)
            {
                var repDoctorId = ex.ReplacementDoctorId.Value;
                var repDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == repDoctorId, ct);

                var repExistingSlots = await _context.AppointmentSlots
                    .Where(s => s.DoctorId == repDoctorId && s.StartTime >= startOfDay && s.StartTime <= endOfDay)
                    .ToListAsync(ct);

                var slotMapping = new Dictionary<int, int>();

                foreach (var slot in slots)
                {
                    var existingRepSlot = repExistingSlots.FirstOrDefault(s => s.StartTime == slot.StartTime);
                    if (existingRepSlot != null)
                    {
                        existingRepSlot.Capacity += slot.Capacity;
                        slot.Status = SlotStatus.Blocked;
                        slotMapping[slot.Id] = existingRepSlot.Id;
                    }
                    else
                    {
                        slot.DoctorId = repDoctorId;
                        slotMapping[slot.Id] = slot.Id;
                    }
                }

                foreach (var app in affectedApps)
                {
                    app.DoctorId = repDoctorId;
                    if (app.SlotId.HasValue && slotMapping.TryGetValue(app.SlotId.Value, out var newSlotId))
                    {
                        app.SlotId = newSlotId;
                    }
                    app.UpdatedAt = now;

                    if (app.BookedBy.HasValue)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            AccountId = app.BookedBy.Value,
                            AppointmentId = app.Id,
                            Title = "Thay đổi bác sĩ khám",
                            Content = $"Lịch khám của bệnh nhân {app.Patient.FullName} ngày {ex.Date:dd/MM/yyyy} đã được điều phối sang BS. {repDoctor?.FullName ?? "chuyên khoa"}.",
                            CreatedAt = now
                        });
                    }
                }
            }
            else
            {
                // Trường hợp 2: Không có bác sĩ thay thế (Nghỉ không người trực thay)
                // Đóng các slot khám của ca
                foreach (var slot in slots)
                {
                    slot.Status = SlotStatus.Blocked;
                }

                // Chuyển các lịch hẹn đã đặt sang trạng thái RescheduleRequired và thông báo in-app
                foreach (var app in affectedApps)
                {
                    var oldStatus = app.StatusId;
                    app.StatusId = (int)AppointmentStatusId.RescheduleRequired;
                    app.UpdatedAt = now;

                    _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
                    {
                        AppointmentId = app.Id,
                        OldStatusId = oldStatus,
                        NewStatusId = (int)AppointmentStatusId.RescheduleRequired,
                        PerformedBy = request.DepartmentHeadAccountId,
                        ChangedAt = now
                    });

                    if (app.BookedBy.HasValue)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            AccountId = app.BookedBy.Value,
                            AppointmentId = app.Id,
                            Title = "Lịch khám cần đặt lại",
                            Content = $"Bác sĩ phụ trách ngày {ex.Date:dd/MM/yyyy} của bệnh nhân {app.Patient.FullName} có lịch nghỉ đột xuất. Vui lòng chọn lại thời gian hoặc bác sĩ khám.",
                            CreatedAt = now
                        });
                    }
                }
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Result.Success();
        });
    }

    public async Task<Result> RejectExceptionAsync(RejectExceptionRequest request, CancellationToken ct = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var ex = await _context.ScheduleExceptions
                .Include(e => e.Doctor)
                .FirstOrDefaultAsync(e => e.Id == request.ExceptionId, ct);
            if (ex == null)
                return Result.Failure("NOT_FOUND", "Không tìm thấy đề nghị.");

            if (!request.DepartmentId.HasValue || request.DepartmentId.Value <= 0 || (ex.Doctor != null && ex.Doctor.DepartmentId != request.DepartmentId.Value))
                return Result.Failure("FORBIDDEN", "Bạn chỉ có thể từ chối đề nghị của bác sĩ thuộc khoa mình quản lý.");

            if (ex.Status != ScheduleExceptionStatus.Pending)
                return Result.Failure("INVALID_STATUS", "Chỉ có thể từ chối đề nghị đang ở trạng thái chờ duyệt.");

            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(11028, 1)", ct);

            ex.Status = ScheduleExceptionStatus.Rejected;
            ex.ApprovedBy = request.DepartmentHeadAccountId;
            ex.ApprovedAt = _timeProvider.GetLocalNow().DateTime;

            if (!string.IsNullOrWhiteSpace(request.RejectReason))
            {
                ex.Reason = (ex.Reason ?? "") + $" [Từ chối: {request.RejectReason.Trim()}]";
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Result.Success();
        });
    }

    // 2. Thiết lập lịch khám mẫu, Đăng ký ca (Shift Bidding) & Phê duyệt / Điều phối
    public async Task<IReadOnlyList<DepartmentScheduleItemDto>> GetSchedulesAsync(int departmentId, int? periodId = null, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
    {
        var query = _context.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Doctor)
            .Include(s => s.Room)
            .Include(s => s.ReviewedByAccount)
            .Where(s => s.DepartmentId == departmentId && s.Status != DoctorScheduleStatus.Cancelled);

        if (periodId.HasValue)
        {
            query = query.Where(s => s.PeriodId == periodId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(s => s.WorkDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(s => s.WorkDate <= toDate.Value);
        }

        return await query
            .OrderBy(s => s.WorkDate)
            .ThenBy(s => s.Shift)
            .ThenBy(s => s.Doctor.FullName)
            .Select(s => new DepartmentScheduleItemDto(
                s.Id,
                s.PeriodId,
                s.DoctorId,
                s.Doctor.FullName,
                s.RoomId,
                s.Room != null ? s.Room.RoomNumber : null,
                s.WorkDate,
                s.Shift,
                s.StartTime,
                s.EndTime,
                s.SlotDurationMinutes,
                s.MaxPatients,
                s.OnlineSlots,
                s.IsHotShift,
                s.Status,
                s.DoctorNote,
                s.ReallocatedFromId,
                s.ReviewedByAccount != null ? s.ReviewedByAccount.Username : null,
                s.ReviewedAt,
                s.AppointmentSlots.SelectMany(sl => sl.Appointments).Count(a => a.StatusId == 1 || a.StatusId == 2)
            ))
            .ToListAsync(ct);
    }

    public async Task<Result<int>> UpsertDoctorScheduleAsync(UpsertDoctorScheduleRequest request, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (request.WorkDate < today)
            return Result.Failure<int>("PAST_DATE", "Không thể thiết lập lịch khám cho ngày trong quá khứ. Chỉ được phép thiết lập từ ngày hiện tại hoặc tương lai.");

        if (request.OnlineSlots < 0 || request.OnlineSlots > request.MaxPatients)
            return Result.Failure<int>("INVALID_QUOTA", "Số lượng slot trực tuyến phải từ 0 đến tổng số bệnh nhân.");

        if (request.EndTime <= request.StartTime)
            return Result.Failure<int>("INVALID_TIME", "Giờ kết thúc ca khám phải sau giờ bắt đầu.");

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == request.DoctorId, ct);
        if (doctor == null)
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy bác sĩ.");

        if (request.RoomId.HasValue)
        {
            var room = await _context.ClinicRooms.FirstOrDefaultAsync(r => r.Id == request.RoomId.Value, ct);
            if (room == null || room.DepartmentId != doctor.DepartmentId)
                return Result.Failure<int>("INVALID_ROOM", "Phòng khám không thuộc khoa của bác sĩ.");
        }

        DoctorSchedule? schedule = null;

        if (request.ScheduleId.HasValue && request.ScheduleId.Value > 0)
        {
            schedule = await _context.DoctorSchedules
                .FirstOrDefaultAsync(s => s.Id == request.ScheduleId.Value, ct);

            if (schedule != null && schedule.DepartmentId != doctor.DepartmentId)
            {
                return Result.Failure<int>("FORBIDDEN", "Không thể chỉnh sửa lịch khám thuộc khoa khác.");
            }
        }

        if (schedule == null)
        {
            schedule = await _context.DoctorSchedules.FirstOrDefaultAsync(s =>
                s.DoctorId == request.DoctorId &&
                s.WorkDate == request.WorkDate &&
                s.Shift == request.Shift &&
                (s.Status == DoctorScheduleStatus.Bidding || s.Status == DoctorScheduleStatus.Approved), ct);
        }

        var currentScheduleId = schedule?.Id ?? (request.ScheduleId ?? 0);

        // 1. Kiểm tra trùng phòng khám (uq_room_collision_guard)
        if (request.RoomId.HasValue && request.Status == DoctorScheduleStatus.Approved)
        {
            var roomConflict = await _context.DoctorSchedules
                .Include(s => s.Doctor)
                .Include(s => s.Room)
                .FirstOrDefaultAsync(s => s.RoomId == request.RoomId.Value
                    && s.WorkDate == request.WorkDate
                    && s.Shift == request.Shift
                    && s.Status == DoctorScheduleStatus.Approved
                    && s.Id != currentScheduleId
                    && s.DoctorId != request.DoctorId, ct);

            if (roomConflict != null)
            {
                var shiftName = request.Shift == ExamShift.Morning ? "Ca Sáng (07:30 - 11:30)" : "Ca Chiều (13:00 - 16:30)";
                var roomDisplayName = roomConflict.Room != null
                    ? $"{roomConflict.Room.RoomName} ({roomConflict.Room.RoomNumber})"
                    : $"Phòng khám #{request.RoomId.Value}";
                return Result.Failure<int>("ROOM_COLLISION",
                    $"{roomDisplayName} trong {shiftName} ngày {request.WorkDate:dd/MM/yyyy} đã được xếp cho BS. {roomConflict.Doctor.FullName}. Vui lòng chọn buồng khám khác còn trống!");
            }
        }

        // 2. Kiểm tra trùng ca trực của bác sĩ (uq_doctor_active_shift)
        if (request.Status == DoctorScheduleStatus.Approved || request.Status == DoctorScheduleStatus.Bidding)
        {
            var doctorConflict = await _context.DoctorSchedules
                .FirstOrDefaultAsync(s => s.DoctorId == request.DoctorId
                    && s.WorkDate == request.WorkDate
                    && s.Shift == request.Shift
                    && (s.Status == DoctorScheduleStatus.Approved || s.Status == DoctorScheduleStatus.Bidding)
                    && s.Id != currentScheduleId, ct);

            if (doctorConflict != null)
            {
                var shiftName = request.Shift == ExamShift.Morning ? "Ca Sáng" : "Ca Chiều";
                return Result.Failure<int>("DOCTOR_SHIFT_COLLISION",
                    $"Bác sĩ {doctor.FullName} đã có lịch khám {shiftName} ngày {request.WorkDate:dd/MM/yyyy}.");
            }
        }

        var periodId = request.PeriodId;
        if (!periodId.HasValue || periodId.Value <= 0)
        {
            var matchingPeriod = await _context.SchedulingPeriods
                .FirstOrDefaultAsync(p => (p.DepartmentId == null || p.DepartmentId == doctor.DepartmentId)
                    && p.StartDate <= request.WorkDate && p.EndDate >= request.WorkDate, ct);
            if (matchingPeriod != null)
            {
                periodId = matchingPeriod.Id;
            }
        }

        if (schedule != null)
        {
            schedule.PeriodId = periodId ?? schedule.PeriodId;
            schedule.DoctorId = request.DoctorId;
            schedule.DepartmentId = request.DepartmentId > 0 ? request.DepartmentId : doctor.DepartmentId;
            schedule.RoomId = request.RoomId;
            schedule.WorkDate = request.WorkDate;
            schedule.Shift = request.Shift;
            schedule.StartTime = request.StartTime;
            schedule.EndTime = request.EndTime;
            schedule.SlotDurationMinutes = request.SlotDurationMinutes;
            schedule.MaxPatients = request.MaxPatients;
            schedule.OnlineSlots = request.OnlineSlots;
            schedule.Status = request.Status;
            schedule.DoctorNote = request.DoctorNote ?? schedule.DoctorNote;
        }
        else
        {
            schedule = new DoctorSchedule
            {
                PeriodId = periodId,
                DoctorId = request.DoctorId,
                DepartmentId = request.DepartmentId > 0 ? request.DepartmentId : doctor.DepartmentId,
                RoomId = request.RoomId,
                WorkDate = request.WorkDate,
                Shift = request.Shift,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                SlotDurationMinutes = request.SlotDurationMinutes,
                MaxPatients = request.MaxPatients,
                OnlineSlots = request.OnlineSlots,
                Status = request.Status,
                DoctorNote = request.DoctorNote,
                CreatedAt = UtcUnspecified
            };
            _context.DoctorSchedules.Add(schedule);
        }

        try
        {
            await _context.SaveChangesAsync(ct);
            return Result<int>.Success(schedule.Id);
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is Npgsql.PostgresException pgEx)
            {
                if (pgEx.SqlState == "23505" && pgEx.ConstraintName == "uq_room_collision_guard")
                {
                    return Result.Failure<int>("ROOM_COLLISION",
                        $"Buồng khám đã chọn trong ca khám ngày {request.WorkDate:dd/MM/yyyy} đã có bác sĩ khác làm việc. Vui lòng chọn buồng khám khác.");
                }
                if (pgEx.SqlState == "23505" && pgEx.ConstraintName == "uq_doctor_active_shift")
                {
                    return Result.Failure<int>("DOCTOR_SHIFT_COLLISION",
                        $"Bác sĩ {doctor.FullName} đã có ca làm việc trong ca khám ngày {request.WorkDate:dd/MM/yyyy}.");
                }
            }
            return Result.Failure<int>("DB_UPDATE_ERROR", "Lỗi lưu lịch khám: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    public async Task<ScheduleCancelImpactDto> GetScheduleCancelImpactAsync(int scheduleId, CancellationToken ct = default)
    {
        var slotIds = await _context.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.ScheduleId == scheduleId)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (slotIds.Count == 0)
        {
            return new ScheduleCancelImpactDto(true, 0, 0, null);
        }

        var ongoingStatuses = new[] { 3, 4, 5, 6 }; // CheckedIn, ReadyForDoctor, InProgress, Completed
        var ongoingCount = await _context.Appointments
            .AsNoTracking()
            .CountAsync(a => a.SlotId.HasValue && slotIds.Contains(a.SlotId.Value) && ongoingStatuses.Contains(a.StatusId), ct);

        if (ongoingCount > 0)
        {
            return new ScheduleCancelImpactDto(false, 0, ongoingCount,
                "Không thể hủy ca làm việc vì đã có bệnh nhân tiếp đón, đang khám hoặc đã hoàn tất khám trong ca này.");
        }

        var pendingOrConfirmedCount = await _context.Appointments
            .AsNoTracking()
            .CountAsync(a => a.SlotId.HasValue && slotIds.Contains(a.SlotId.Value) && (a.StatusId == 1 || a.StatusId == 2), ct);

        return new ScheduleCancelImpactDto(true, pendingOrConfirmedCount, 0, null);
    }

    public async Task<Result> DeleteDoctorScheduleAsync(int scheduleId, int departmentId, int? cancelledByAccountId = null, CancellationToken ct = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var schedule = await _context.DoctorSchedules
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == scheduleId, ct);

            if (schedule == null)
                return Result.Failure("NOT_FOUND", "Không tìm thấy lịch khám.");

            if (schedule.DepartmentId != departmentId)
                return Result.Failure("FORBIDDEN", "Bạn chỉ có thể hủy lịch khám thuộc khoa của mình.");

            if (schedule.Status == DoctorScheduleStatus.Cancelled)
                return Result.Failure("ALREADY_CANCELLED", "Ca khám này đã được hủy trước đó.");

            var slotIds = await _context.AppointmentSlots
                .Where(s => s.ScheduleId == scheduleId)
                .Select(s => s.Id)
                .ToListAsync(ct);

            // 1. Kiểm tra điều kiện an toàn:
            // Nếu ca khám đã có lịch hẹn ở trạng thái đang khám hoặc đã hoàn thành (status_id IN (3, 4, 5, 6))
            var ongoingStatuses = new[] { 3, 4, 5, 6 }; // CheckedIn, ReadyForDoctor, InProgress, Completed
            var hasOngoing = await _context.Appointments
                .AnyAsync(a => a.SlotId.HasValue && slotIds.Contains(a.SlotId.Value) && ongoingStatuses.Contains(a.StatusId), ct);

            if (hasOngoing)
            {
                return Result.Failure("CANNOT_CANCEL_ONGOING",
                    "Không thể hủy ca làm việc vì đã có bệnh nhân tiếp đón, đang khám hoặc đã hoàn tất khám trong ca này.");
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var now = _timeProvider.GetLocalNow().DateTime;

            // BƯỚC 1: Xử lý appointments bị ảnh hưởng thuộc các slot của schedule_id này
            var affectedAppointments = await _context.Appointments
                .Where(a => a.SlotId.HasValue && slotIds.Contains(a.SlotId.Value))
                .ToListAsync(ct);

            foreach (var app in affectedAppointments)
            {
                var oldStatusId = app.StatusId;

                // Cập nhật status_id = 10 (RESCHEDULE_REQUIRED), slot_id = null, room_id = null
                app.StatusId = (int)AppointmentStatusId.RescheduleRequired;
                app.SlotId = null;
                app.RoomId = null;
                app.UpdatedAt = now;

                // Lưu vết vào appointment_status_history
                _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
                {
                    AppointmentId = app.Id,
                    OldStatusId = oldStatusId,
                    NewStatusId = (int)AppointmentStatusId.RescheduleRequired,
                    PerformedBy = cancelledByAccountId,
                    ChangedAt = now
                });

                // Tạo thông báo trong notifications gửi cho booked_by
                if (app.BookedBy.HasValue && app.BookedBy.Value > 0)
                {
                    _context.Notifications.Add(new Notification
                    {
                        AccountId = app.BookedBy.Value,
                        AppointmentId = app.Id,
                        Title = "Lịch khám cần đặt lại",
                        Content = $"Lịch khám mã {app.AppointmentCode} ngày {schedule.WorkDate:dd/MM/yyyy} ({schedule.Doctor?.FullName ?? "Bác sĩ"}) đã bị hủy do thay đổi lịch làm việc. Vui lòng chọn lại thời gian khám mới.",
                        IsRead = false,
                        CreatedAt = now
                    });
                }
            }

            // Ghi nhận cập nhật Bước 1 để tháo toàn bộ slot_id khỏi bảng appointments
            await _context.SaveChangesAsync(ct);

            // BƯỚC 2: Dọn dẹp appointment_slots (DELETE toàn bộ các dòng có schedule_id tương ứng)
            var slotsToDelete = await _context.AppointmentSlots
                .Where(s => s.ScheduleId == scheduleId)
                .ToListAsync(ct);

            if (slotsToDelete.Count > 0)
            {
                _context.AppointmentSlots.RemoveRange(slotsToDelete);
                await _context.SaveChangesAsync(ct);
            }

            // BƯỚC 3: Cập nhật doctor_schedules (Không xóa vật lý, cập nhật status = 'CANCELLED')
            schedule.Status = DoctorScheduleStatus.Cancelled;
            await _context.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return Result.Success();
        });
    }

    public async Task<Result<int>> CreateSchedulingPeriodAsync(CreateSchedulingPeriodRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<int>("INVALID_NAME", "Tên đợt đăng ký lịch không được để trống.");

        if (request.EndDate < request.StartDate)
            return Result.Failure<int>("INVALID_DATES", "Ngày kết thúc đợt làm việc phải sau hoặc bằng ngày bắt đầu.");

        if (request.BiddingClosesAt <= request.BiddingOpensAt)
            return Result.Failure<int>("INVALID_BIDDING_WINDOW", "Thời điểm đóng cổng đăng ký phải sau thời điểm mở cổng.");

        var period = new SchedulingPeriod
        {
            DepartmentId = request.DepartmentId,
            Name = request.Name.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            BiddingOpensAt = DateTime.SpecifyKind(request.BiddingOpensAt, DateTimeKind.Unspecified),
            BiddingClosesAt = DateTime.SpecifyKind(request.BiddingClosesAt, DateTimeKind.Unspecified),
            Status = SchedulingPeriodStatus.Open,
            CreatedBy = request.CreatedByAccountId,
            CreatedAt = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified)
        };

        _context.SchedulingPeriods.Add(period);
        await _context.SaveChangesAsync(ct);

        return Result.Success(period.Id);
    }

    public async Task<SchedulingPeriodDto?> GetActiveSchedulingPeriodAsync(int? departmentId, CancellationToken ct = default)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        var period = await _context.SchedulingPeriods
            .AsNoTracking()
            .Include(p => p.Department)
            .Where(p => p.DepartmentId == null || departmentId == null || p.DepartmentId == departmentId)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(ct);

        if (period == null) return null;

        bool isOpen = period.Status == SchedulingPeriodStatus.Open && now >= period.BiddingOpensAt && now <= period.BiddingClosesAt;

        return new SchedulingPeriodDto(
            period.Id,
            period.DepartmentId,
            period.Department != null ? period.Department.Name : "Toàn viện",
            period.Name,
            period.StartDate,
            period.EndDate,
            period.BiddingOpensAt,
            period.BiddingClosesAt,
            period.Status,
            isOpen
        );
    }

    public async Task<DepartmentDemandGridDto> GetDepartmentDemandGridAsync(int departmentId, int periodId, CancellationToken ct = default)
    {
        var dept = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        int totalActiveRooms = await _context.ClinicRooms
            .CountAsync(r => r.DepartmentId == departmentId && r.IsActive, ct);

        var period = await _context.SchedulingPeriods
            .AsNoTracking()
            .Include(p => p.Department)
            .FirstOrDefaultAsync(p => p.Id == periodId, ct);

        var now = _timeProvider.GetLocalNow().DateTime;
        SchedulingPeriodDto? periodDto = null;
        if (period != null)
        {
            periodDto = new SchedulingPeriodDto(
                period.Id,
                period.DepartmentId,
                period.Department != null ? period.Department.Name : "Toàn viện",
                period.Name,
                period.StartDate,
                period.EndDate,
                period.BiddingOpensAt,
                period.BiddingClosesAt,
                period.Status,
                period.Status == SchedulingPeriodStatus.Open && now >= period.BiddingOpensAt && now <= period.BiddingClosesAt
            );
        }

        var startDate = period?.StartDate ?? DateOnly.FromDateTime(DateTime.Today);
        var endDate = period?.EndDate ?? startDate.AddDays(6);

        var schedules = await _context.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Doctor)
            .Include(s => s.Room)
            .Where(s => s.DepartmentId == departmentId && s.WorkDate >= startDate && s.WorkDate <= endDate && (s.Status == DoctorScheduleStatus.Bidding || s.Status == DoctorScheduleStatus.Approved))
            .ToListAsync(ct);

        var cells = new List<ShiftDemandCellDto>();
        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            foreach (ExamShift shift in Enum.GetValues<ExamShift>())
            {
                var shiftSchedules = schedules
                    .Where(s => s.WorkDate == d && s.Shift == shift)
                    .ToList();

                int currentBidders = shiftSchedules.Count;
                int approvedCount = shiftSchedules.Count(s => s.Status == DoctorScheduleStatus.Approved);

                DemandLevel level = DemandLevel.Empty;
                if (currentBidders > totalActiveRooms)
                {
                    level = DemandLevel.HighCompetition;
                }
                else if (currentBidders > 0)
                {
                    level = DemandLevel.Available;
                }

                bool isHot = shiftSchedules.Any(s => s.IsHotShift) || currentBidders > totalActiveRooms;

                var bidders = shiftSchedules
                    .Select(s => new DoctorBidSummaryDto(
                        s.Id,
                        s.DoctorId,
                        s.Doctor.FullName,
                        s.RoomId,
                        s.Room != null ? s.Room.RoomNumber : null,
                        s.Status,
                        s.DoctorNote
                    ))
                    .ToList();

                cells.Add(new ShiftDemandCellDto(
                    d,
                    shift,
                    currentBidders,
                    approvedCount,
                    totalActiveRooms,
                    level,
                    isHot,
                    bidders
                ));
            }
        }

        return new DepartmentDemandGridDto(
            departmentId,
            dept?.Name ?? "",
            totalActiveRooms,
            periodDto,
            cells
        );
    }

    public async Task<Result<int>> RegisterShiftBidAsync(RegisterShiftBidRequest request, CancellationToken ct = default)
    {
        var period = await _context.SchedulingPeriods.FirstOrDefaultAsync(p => p.Id == request.PeriodId, ct);
        if (period == null)
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy kỳ đăng ký lịch.");

        var now = _timeProvider.GetLocalNow().DateTime;
        if (period.Status != SchedulingPeriodStatus.Open || now < period.BiddingOpensAt || now > period.BiddingClosesAt)
            return Result.Failure<int>("PERIOD_CLOSED", "Cổng đăng ký ca khám của kỳ này hiện đang đóng.");

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == request.DoctorId, ct);
        if (doctor == null)
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy thông tin bác sĩ.");

        if (period.DepartmentId.HasValue && period.DepartmentId.Value != doctor.DepartmentId)
            return Result.Failure<int>("FORBIDDEN", "Kỳ đăng ký này không thuộc khoa của bác sĩ.");

        // 1. Hard Block: Fatigue Mitigation (An toàn y khoa)
        if (request.Shift == ExamShift.Morning)
        {
            var morningStart = request.WorkDate.ToDateTime(new TimeOnly(6, 0));
            var noon = request.WorkDate.ToDateTime(new TimeOnly(12, 0));
            bool hasNightDuty = await _context.DutyShifts
                .AnyAsync(d => d.AccountId == doctor.AccountId
                    && d.Status == DutyShiftStatus.Approved
                    && d.EndTime >= morningStart
                    && d.StartTime <= noon, ct);

            if (hasNightDuty)
            {
                return Result.Failure<int>("FATIGUE_CONFLICT",
                    "Bạn có ca trực đêm kéo dài sang sáng ngày đăng ký. Vì an toàn y tế và giảm thiểu mệt mỏi, hệ thống chặn đăng ký ca khám Sáng này!");
            }
        }

        // 2. Chặn đăng ký trùng ca trong cùng ngày
        bool alreadyRegistered = await _context.DoctorSchedules
            .AnyAsync(s => s.DoctorId == doctor.Id
                && s.WorkDate == request.WorkDate
                && s.Shift == request.Shift
                && (s.Status == DoctorScheduleStatus.Bidding || s.Status == DoctorScheduleStatus.Approved), ct);

        if (alreadyRegistered)
        {
            return Result.Failure<int>("DUPLICATE_BID", "Bạn đã đăng ký hoặc được duyệt ca khám này rồi!");
        }

        // Kiểm tra trần số ca tối đa/tuần (tối đa toàn bộ ca trong tuần)
        var approvedShiftsCount = await _context.DoctorSchedules
            .CountAsync(s => s.DoctorId == doctor.Id
                && s.PeriodId == period.Id
                && (s.Status == DoctorScheduleStatus.Bidding || s.Status == DoctorScheduleStatus.Approved), ct);

        int maxShifts = Math.Max(10, (int)doctor.MaxShiftsPerWeek);
        if (approvedShiftsCount >= maxShifts)
        {
            return Result.Failure<int>("QUOTA_EXCEEDED", $"Bạn đã đạt trần số ca tối đa cho phép trong tuần ({maxShifts} ca).");
        }

        // Kiểm tra số lượng buồng khám: Chặn đăng ký khi Trưởng khoa đã DUYỆT ĐỦ tất cả các phòng
        int totalActiveRooms = await _context.ClinicRooms
            .CountAsync(r => r.DepartmentId == doctor.DepartmentId && r.IsActive, ct);

        int approvedRoomsCount = await _context.DoctorSchedules
            .CountAsync(s => s.DepartmentId == doctor.DepartmentId
                && s.WorkDate == request.WorkDate
                && s.Shift == request.Shift
                && s.Status == DoctorScheduleStatus.Approved, ct);

        var shiftName = request.Shift == ExamShift.Morning ? "Ca Sáng (07:30 - 11:30)" : "Ca Chiều (13:00 - 16:30)";
        if (totalActiveRooms > 0 && approvedRoomsCount >= totalActiveRooms)
        {
            return Result.Failure<int>("ROOMS_FULL",
                $"Ca khám ngày {request.WorkDate:dd/MM/yyyy} ({shiftName}) đã được Trưởng khoa duyệt kín tất cả các buồng khám. Không thể đăng ký thêm vào ca này!");
        }

        int currentBidders = await _context.DoctorSchedules
            .CountAsync(s => s.DepartmentId == doctor.DepartmentId
                && s.WorkDate == request.WorkDate
                && s.Shift == request.Shift
                && (s.Status == DoctorScheduleStatus.Bidding || s.Status == DoctorScheduleStatus.Approved), ct);

        bool isHot = (currentBidders + 1) > totalActiveRooms;

        var schedule = new DoctorSchedule
        {
            PeriodId = period.Id,
            DoctorId = doctor.Id,
            DepartmentId = doctor.DepartmentId,
            RoomId = null,
            WorkDate = request.WorkDate,
            Shift = request.Shift,
            StartTime = request.Shift == ExamShift.Morning ? new TimeOnly(7, 30) : new TimeOnly(13, 0),
            EndTime = request.Shift == ExamShift.Morning ? new TimeOnly(11, 30) : new TimeOnly(16, 30),
            SlotDurationMinutes = 30,
            MaxPatients = 40,
            OnlineSlots = 28,
            IsHotShift = isHot,
            Status = DoctorScheduleStatus.Bidding,
            DoctorNote = request.DoctorNote,
            CreatedAt = UtcUnspecified
        };

        _context.DoctorSchedules.Add(schedule);

        // Ghi AccessLog
        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = doctor.AccountId,
            Action = "SHIFT_BID",
            EntityName = "doctor_schedules",
            EntityId = 0,
            IsSuccess = true,
            Severity = "INFO",
            OccurredAt = UtcUnspecified,
            Details = $"Đăng ký ca khám {request.WorkDate:dd/MM/yyyy} ({request.Shift})"
        });

        await _context.SaveChangesAsync(ct);
        return Result<int>.Success(schedule.Id);
    }

    public async Task<Result> CancelShiftBidAsync(int doctorId, int scheduleId, CancellationToken ct = default)
    {
        var schedule = await _context.DoctorSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId && s.DoctorId == doctorId, ct);
        if (schedule == null)
            return Result.Failure("NOT_FOUND", "Không tìm thấy yêu cầu đăng ký ca.");

        if (schedule.Status != DoctorScheduleStatus.Bidding)
            return Result.Failure("CANNOT_CANCEL", "Chỉ có thể hủy ca đang ở trạng thái Chờ duyệt (BIDDING).");

        _context.DoctorSchedules.Remove(schedule);
        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<DecisionMatrixResultDto> GetDecisionMatrixAsync(int departmentId, DateOnly workDate, ExamShift shift, CancellationToken ct = default)
    {
        int totalActiveRooms = await _context.ClinicRooms
            .CountAsync(r => r.DepartmentId == departmentId && r.IsActive, ct);

        var schedules = await _context.DoctorSchedules
            .Include(s => s.Doctor)
                .ThenInclude(d => d.Account)
            .Include(s => s.Room)
            .Where(s => s.DepartmentId == departmentId
                && s.WorkDate == workDate
                && s.Shift == shift
                && (s.Status == DoctorScheduleStatus.Bidding || s.Status == DoctorScheduleStatus.Approved))
            .ToListAsync(ct);

        var assignedRoomIds = schedules
            .Where(s => s.Status == DoctorScheduleStatus.Approved && s.RoomId.HasValue)
            .Select(s => s.RoomId!.Value)
            .ToHashSet();

        var availableRooms = await _context.ClinicRooms
            .AsNoTracking()
            .Where(r => r.DepartmentId == departmentId && r.IsActive && !assignedRoomIds.Contains(r.Id))
            .OrderBy(r => r.RoomNumber)
            .Select(r => new ClinicRoomDto(r.Id, r.DepartmentId, r.RoomName, r.RoomNumber))
            .ToListAsync(ct);

        bool isHot = schedules.Any(s => s.IsHotShift) || schedules.Count > totalActiveRooms;

        var morningStart = workDate.ToDateTime(new TimeOnly(6, 0));
        var noon = workDate.ToDateTime(new TimeOnly(12, 0));
        var prevWeekStart = workDate.AddDays(-7);
        var prevWeekEnd = workDate.AddDays(-1);

        var candidates = new List<DecisionMatrixCandidateDto>();

        foreach (var s in schedules)
        {
            var doc = s.Doctor;

            // 1. Duty Shift Conflict: Đêm trước có trực đêm kéo dài sáng nay không?
            bool hasNightDutyConflict = false;
            if (shift == ExamShift.Morning)
            {
                hasNightDutyConflict = await _context.DutyShifts
                    .AnyAsync(d => d.AccountId == doc.AccountId
                        && d.Status == DutyShiftStatus.Approved
                        && d.EndTime >= morningStart
                        && d.StartTime <= noon, ct);
            }

            // 2. Rotation Hot Shift: Tuần trước
            // Đếm cả các bản ghi tuần trước có is_hot_shift = TRUE và status IN ('REJECTED', 'REALLOCATED') -> hưởng ưu tiên đền bù cao nhất (⭐⭐)
            bool hadRejectedOrReallocatedHotShift = await _context.DoctorSchedules
                .AnyAsync(ds => ds.DoctorId == doc.Id
                    && ds.IsHotShift
                    && (ds.Status == DoctorScheduleStatus.Rejected || ds.Status == DoctorScheduleStatus.Reallocated)
                    && ((ds.WorkDate >= prevWeekStart && ds.WorkDate <= prevWeekEnd) || (ds.WorkDate < workDate && ds.WorkDate >= workDate.AddDays(-14))), ct);

            int rotationScore = 0;
            string rotationBadge = "Bình thường";

            if (hadRejectedOrReallocatedHotShift)
            {
                rotationScore = 2;
                rotationBadge = "Đền bù ca Hot (⭐⭐)";
            }
            else
            {
                bool hadApprovedHotShift = await _context.DoctorSchedules
                    .AnyAsync(ds => ds.DoctorId == doc.Id
                        && ds.IsHotShift
                        && ds.Status == DoctorScheduleStatus.Approved
                        && ds.WorkDate >= prevWeekStart && ds.WorkDate <= prevWeekEnd, ct);

                if (!hadApprovedHotShift)
                {
                    rotationScore = 1;
                    rotationBadge = "Ưu tiên luân phiên (⭐)";
                }
                else
                {
                    rotationScore = 0;
                    rotationBadge = "Đã duyệt ca Hot tuần trước";
                }
            }

            // 3. Quota Balance: Số ca đã approved trong tuần này
            var weekStart = workDate.AddDays(-((int)workDate.DayOfWeek == 0 ? 6 : (int)workDate.DayOfWeek - 1));
            var weekEnd = weekStart.AddDays(6);
            int approvedShiftsThisPeriod = await _context.DoctorSchedules
                .CountAsync(ds => ds.DoctorId == doc.Id
                    && ds.Status == DoctorScheduleStatus.Approved
                    && ds.WorkDate >= weekStart && ds.WorkDate <= weekEnd, ct);

            // 4. Continuity of Care: Bệnh nhân hẹn tái khám đúng ngày workDate
            int continuityOfCareCount = await _context.MedicalRecords
                .CountAsync(mr => mr.DoctorId == doc.Id && mr.FollowUpDate == workDate, ct);

            candidates.Add(new DecisionMatrixCandidateDto(
                s.Id,
                doc.Id,
                doc.FullName,
                doc.Specialty,
                doc.AcademicTitle,
                doc.EmploymentType,
                s.RoomId,
                s.Room?.RoomNumber,
                s.Status,
                s.DoctorNote,
                s.ReallocatedFromId,
                hasNightDutyConflict,
                rotationScore,
                rotationBadge,
                approvedShiftsThisPeriod,
                doc.MinShiftsPerWeek,
                doc.MaxShiftsPerWeek,
                continuityOfCareCount
            ));
        }

        // Sắp xếp ưu tiên 4 tiêu chí:
        var sortedCandidates = candidates
            .OrderBy(c => c.HasNightDutyConflict)
            .ThenByDescending(c => c.RotationPriorityScore)
            .ThenBy(c => c.ApprovedShiftsThisPeriod)
            .ThenByDescending(c => c.ContinuityOfCareFollowUpCount)
            .ToList();

        return new DecisionMatrixResultDto(
            departmentId,
            workDate,
            shift,
            totalActiveRooms,
            isHot,
            sortedCandidates,
            availableRooms
        );
    }

    public async Task<Result> ApproveShiftAsync(ApproveShiftRequest request, CancellationToken ct = default)
    {
        var schedule = await _context.DoctorSchedules
            .Include(s => s.Doctor)
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, ct);

        if (schedule == null)
            return Result.Failure("NOT_FOUND", "Không tìm thấy ca đăng ký.");

        var room = await _context.ClinicRooms.FirstOrDefaultAsync(r => r.Id == request.RoomId && r.DepartmentId == schedule.DepartmentId && r.IsActive, ct);
        if (room == null)
            return Result.Failure("INVALID_ROOM", "Phòng khám không hợp lệ hoặc đã ngưng hoạt động.");

        // Check room collision: uq_room_collision_guard
        bool roomTaken = await _context.DoctorSchedules
            .AnyAsync(s => s.RoomId == request.RoomId
                && s.WorkDate == schedule.WorkDate
                && s.Shift == schedule.Shift
                && s.Status == DoctorScheduleStatus.Approved
                && s.Id != schedule.Id, ct);

        if (roomTaken)
            return Result.Failure("ROOM_COLLISION", $"Buồng khám {room.RoomNumber} đã được phê duyệt cho bác sĩ khác trong ca này!");

        schedule.RoomId = request.RoomId;
        schedule.Status = DoctorScheduleStatus.Approved;
        schedule.ReviewedBy = request.ReviewedByAccountId;
        schedule.ReviewedAt = UtcUnspecified;

        // Kiểm tra số phòng tối đa của khoa: Nếu đã đủ phòng thì các BIDDING còn lại chuyển REJECTED
        int totalActiveRooms = await _context.ClinicRooms
            .CountAsync(r => r.DepartmentId == schedule.DepartmentId && r.IsActive, ct);

        int approvedCount = await _context.DoctorSchedules
            .CountAsync(s => s.DepartmentId == schedule.DepartmentId
                && s.WorkDate == schedule.WorkDate
                && s.Shift == schedule.Shift
                && s.Status == DoctorScheduleStatus.Approved
                && s.Id != schedule.Id, ct) + 1;

        if (approvedCount >= totalActiveRooms)
        {
            var remainingBids = await _context.DoctorSchedules
                .Include(s => s.Doctor)
                .Where(s => s.DepartmentId == schedule.DepartmentId
                    && s.WorkDate == schedule.WorkDate
                    && s.Shift == schedule.Shift
                    && s.Status == DoctorScheduleStatus.Bidding
                    && s.Id != schedule.Id)
                .ToListAsync(ct);

            foreach (var bid in remainingBids)
            {
                bid.Status = DoctorScheduleStatus.Rejected;
                bid.IsHotShift = true; // Đánh dấu là ca Hot bị trượt để hưởng ưu tiên đền bù luân phiên lần sau (⭐⭐)
                bid.ReviewedBy = request.ReviewedByAccountId;
                bid.ReviewedAt = UtcUnspecified;

                if (bid.Doctor != null && bid.Doctor.AccountId > 0)
                {
                    var shiftName = bid.Shift == ExamShift.Morning ? "Ca Sáng (07:30 - 11:30)" : "Ca Chiều (13:00 - 16:30)";
                    _context.Notifications.Add(new Notification
                    {
                        AccountId = bid.Doctor.AccountId,
                        Title = "Đăng ký ca khám không thành công",
                        Content = $"Ca khám ngày {bid.WorkDate:dd/MM/yyyy} ({shiftName}) của bạn không được duyệt do buồng khám đã đủ bác sĩ. Hệ thống đã tự động cộng điểm ưu tiên đền bù (⭐⭐) cho bạn khi đăng ký ca ở kỳ tiếp theo!",
                        CreatedAt = UtcUnspecified
                    });
                }
            }
        }

        // Nếu kỳ đã xuất bản (PUBLISHED), tự động sinh appointment_slots ngay
        if (schedule.PeriodId.HasValue)
        {
            var period = await _context.SchedulingPeriods.FirstOrDefaultAsync(p => p.Id == schedule.PeriodId.Value, ct);
            if (period != null && period.Status == SchedulingPeriodStatus.Published)
            {
                GenerateAppointmentSlotsForSchedule(schedule);
            }
        }

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = request.ReviewedByAccountId,
            Action = "SHIFT_APPROVE",
            EntityName = "doctor_schedules",
            EntityId = schedule.Id,
            IsSuccess = true,
            Severity = "INFO",
            OccurredAt = UtcUnspecified,
            Details = $"Duyệt ca khám cho BS {schedule.Doctor.FullName} tại phòng {room.RoomNumber} ngày {schedule.WorkDate:dd/MM/yyyy} ({schedule.Shift})"
        });

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<int>> ReallocateShiftAsync(ReallocateShiftRequest request, CancellationToken ct = default)
    {
        var oldRecord = await _context.DoctorSchedules
            .Include(s => s.Doctor)
            .FirstOrDefaultAsync(s => s.Id == request.OldScheduleId, ct);

        if (oldRecord == null)
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy bản ghi ca khám gốc.");

        var room = await _context.ClinicRooms.FirstOrDefaultAsync(r => r.Id == request.TargetRoomId && r.DepartmentId == oldRecord.DepartmentId && r.IsActive, ct);
        if (room == null)
            return Result.Failure<int>("INVALID_ROOM", "Phòng khám điều chuyển không hợp lệ hoặc đã đóng.");

        // Kiểm tra trùng phòng ở ca mới: uq_room_collision_guard
        bool roomTaken = await _context.DoctorSchedules
            .AnyAsync(s => s.RoomId == request.TargetRoomId
                && s.WorkDate == request.TargetDate
                && s.Shift == request.TargetShift
                && s.Status == DoctorScheduleStatus.Approved, ct);

        if (roomTaken)
            return Result.Failure<int>("ROOM_COLLISION", $"Buồng khám {room.RoomNumber} đã có bác sĩ trực trong ca điều chuyển!");

        // Cập nhật oldRecord sang REALLOCATED
        oldRecord.Status = DoctorScheduleStatus.Reallocated;
        oldRecord.ReviewedBy = request.ReviewedByAccountId;
        oldRecord.ReviewedAt = UtcUnspecified;

        // Kiểm tra xem bác sĩ đã có bản ghi BIDDING ở target ca chưa (để không vi phạm uq_doctor_active_shift)
        var existingTargetBid = await _context.DoctorSchedules
            .FirstOrDefaultAsync(s => s.DoctorId == oldRecord.DoctorId
                && s.WorkDate == request.TargetDate
                && s.Shift == request.TargetShift
                && s.Status == DoctorScheduleStatus.Bidding, ct);

        DoctorSchedule targetSchedule;
        if (existingTargetBid != null)
        {
            existingTargetBid.RoomId = request.TargetRoomId;
            existingTargetBid.Status = DoctorScheduleStatus.Approved;
            existingTargetBid.ReallocatedFromId = oldRecord.Id;
            existingTargetBid.ReviewedBy = request.ReviewedByAccountId;
            existingTargetBid.ReviewedAt = UtcUnspecified;
            targetSchedule = existingTargetBid;
        }
        else
        {
            targetSchedule = new DoctorSchedule
            {
                PeriodId = oldRecord.PeriodId,
                DoctorId = oldRecord.DoctorId,
                DepartmentId = oldRecord.DepartmentId,
                RoomId = request.TargetRoomId,
                WorkDate = request.TargetDate,
                Shift = request.TargetShift,
                StartTime = request.TargetShift == ExamShift.Morning ? new TimeOnly(7, 30) : new TimeOnly(13, 0),
                EndTime = request.TargetShift == ExamShift.Morning ? new TimeOnly(11, 30) : new TimeOnly(16, 30),
                SlotDurationMinutes = 30,
                MaxPatients = 40,
                OnlineSlots = 28,
                Status = DoctorScheduleStatus.Approved,
                ReallocatedFromId = oldRecord.Id,
                ReviewedBy = request.ReviewedByAccountId,
                ReviewedAt = UtcUnspecified,
                CreatedAt = UtcUnspecified
            };
            _context.DoctorSchedules.Add(targetSchedule);
        }

        // Sinh appointment_slots cho ca mới được điều chuyển
        GenerateAppointmentSlotsForSchedule(targetSchedule);

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = request.ReviewedByAccountId,
            Action = "SHIFT_REALLOCATE",
            EntityName = "doctor_schedules",
            EntityId = targetSchedule.Id,
            IsSuccess = true,
            Severity = "WARNING",
            OccurredAt = UtcUnspecified,
            Details = $"Điều chuyển ca khám trượt (ID {oldRecord.Id}) của BS {oldRecord.Doctor.FullName} sang {request.TargetDate:dd/MM/yyyy} ({request.TargetShift}) phòng {room.RoomNumber}"
        });

        await _context.SaveChangesAsync(ct);
        return Result<int>.Success(targetSchedule.Id);
    }

    public async Task<Result<int>> PublishSchedulingPeriodAsync(int periodId, int reviewedByAccountId, CancellationToken ct = default)
    {
        var period = await _context.SchedulingPeriods
            .Include(p => p.Department)
            .FirstOrDefaultAsync(p => p.Id == periodId, ct);

        if (period == null)
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy kỳ đăng ký.");

        period.Status = SchedulingPeriodStatus.Published;

        var approvedSchedules = await _context.DoctorSchedules
            .Where(s => s.PeriodId == periodId && s.Status == DoctorScheduleStatus.Approved && s.RoomId != null)
            .ToListAsync(ct);

        int totalSlotsCreated = 0;
        foreach (var s in approvedSchedules)
        {
            totalSlotsCreated += GenerateAppointmentSlotsForSchedule(s);
        }

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = reviewedByAccountId,
            Action = "PERIOD_PUBLISH",
            EntityName = "scheduling_periods",
            EntityId = period.Id,
            IsSuccess = true,
            Severity = "INFO",
            OccurredAt = UtcUnspecified,
            Details = $"Xuất bản kỳ lịch {period.Name}, đã tạo {totalSlotsCreated} slots khám."
        });

        await _context.SaveChangesAsync(ct);
        return Result<int>.Success(totalSlotsCreated);
    }

    private int GenerateAppointmentSlotsForSchedule(DoctorSchedule s)
    {
        if (!s.RoomId.HasValue) return 0;

        int duration = s.SlotDurationMinutes > 0 ? s.SlotDurationMinutes : 30;
        var startDateTime = s.WorkDate.ToDateTime(s.StartTime);
        var endDateTime = s.WorkDate.ToDateTime(s.EndTime);

        var existingStartTimes = _context.AppointmentSlots
            .Local
            .Where(sl => sl.DoctorId == s.DoctorId && sl.StartTime >= startDateTime && sl.StartTime < endDateTime)
            .Select(sl => sl.StartTime)
            .Concat(_context.AppointmentSlots
                .Where(sl => sl.DoctorId == s.DoctorId && sl.StartTime >= startDateTime && sl.StartTime < endDateTime)
                .Select(sl => sl.StartTime))
            .ToHashSet();

        int count = 0;
        for (var current = startDateTime; current.AddMinutes(duration) <= endDateTime; current = current.AddMinutes(duration))
        {
            var next = current.AddMinutes(duration);
            if (!existingStartTimes.Contains(current))
            {
                _context.AppointmentSlots.Add(new AppointmentSlot
                {
                    ScheduleId = s.Id > 0 ? s.Id : null,
                    DoctorId = s.DoctorId,
                    RoomId = s.RoomId.Value,
                    StartTime = current,
                    EndTime = next,
                    Capacity = 1,
                    BookedCount = 0,
                    Status = SlotStatus.Available,
                    Version = 0
                });
                existingStartTimes.Add(current);
                count++;
            }
        }
        return count;
    }

    // 3. Phân công ca trực đêm nhân sự
    public async Task<IReadOnlyList<DepartmentDutyShiftDto>> GetDutyShiftsAsync(int departmentId, CancellationToken ct = default)
    {
        return await _context.DutyShifts
            .AsNoTracking()
            .Include(d => d.Account)
                .ThenInclude(a => a.Role)
            .Include(d => d.Account.Doctor)
            .Include(d => d.Account.Nurse)
            .Include(d => d.AssignedByAccount)
                .ThenInclude(a => a!.DepartmentHead)
            .Where(d => d.DepartmentId == departmentId)
            .OrderByDescending(d => d.StartTime)
            .Select(d => new DepartmentDutyShiftDto(
                d.Id,
                d.AccountId,
                d.Account.Doctor != null ? d.Account.Doctor.FullName : (d.Account.Nurse != null ? d.Account.Nurse.FullName : d.Account.Username),
                d.Account.Role.Name,
                d.StartTime,
                d.EndTime,
                d.Note,
                d.AssignedByAccount != null ? (d.AssignedByAccount.DepartmentHead != null ? d.AssignedByAccount.DepartmentHead.FullName : d.AssignedByAccount.Username) : null
            ))
            .ToListAsync(ct);
    }

    public async Task<Result<int>> AssignDutyShiftAsync(AssignDutyShiftRequest request, CancellationToken ct = default)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        if (request.StartTime < now)
            return Result.Failure<int>("PAST_TIME", "Không thể phân công ca trực trong quá khứ. Vui lòng chọn thời gian từ hiện tại hoặc tương lai.");

        if (request.EndTime <= request.StartTime)
            return Result.Failure<int>("INVALID_TIME", "Giờ kết thúc ca trực phải sau giờ bắt đầu.");

        // Kiểm tra nhân sự đã có ca trực trong đêm/khoảng thời gian này chưa
        bool hasConflict = await _context.DutyShifts
            .AnyAsync(d => d.AccountId == request.AccountId &&
                           ((d.StartTime < request.EndTime && d.EndTime > request.StartTime) ||
                            (d.StartTime.Date == request.StartTime.Date)), ct);

        if (hasConflict)
        {
            return Result.Failure<int>("DUPLICATE_DUTY", "Nhân sự này đã có ca trực trong đêm/ngày này rồi. Không được phép phân công trùng ca.");
        }

        var duty = new DutyShift
        {
            AccountId = request.AccountId,
            DepartmentId = request.DepartmentId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Note = string.IsNullOrWhiteSpace(request.Note) ? "Trực đêm tại khoa" : request.Note.Trim(),
            AssignedBy = request.AssignedByAccountId,
            CreatedAt = now
        };

        _context.DutyShifts.Add(duty);
        await _context.SaveChangesAsync(ct);
        return Result<int>.Success(duty.Id);
    }

    public async Task<Result> DeleteDutyShiftAsync(int dutyShiftId, int departmentId, CancellationToken ct = default)
    {
        var duty = await _context.DutyShifts.FirstOrDefaultAsync(d => d.Id == dutyShiftId, ct);
        if (duty == null)
            return Result.Failure("NOT_FOUND", "Không tìm thấy ca trực.");

        if (duty.DepartmentId != departmentId)
            return Result.Failure("FORBIDDEN", "Bạn chỉ có thể xóa ca trực thuộc khoa của mình.");

        _context.DutyShifts.Remove(duty);
        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    // 4. Giám sát bệnh án trong khoa
    public async Task<IReadOnlyList<DepartmentMedicalRecordDto>> GetMedicalRecordsAsync(int departmentId, int viewerAccountId, string? ipAddress = null, CancellationToken ct = default)
    {
        var records = await _context.MedicalRecords
            .AsNoTracking()
            .Include(r => r.Doctor)
            .Include(r => r.Appointment)
                .ThenInclude(a => a.Patient)
            .Where(r => r.Doctor.DepartmentId == departmentId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new DepartmentMedicalRecordDto(
                r.Id,
                r.AppointmentId,
                r.Appointment.AppointmentCode,
                r.Appointment.Patient.FullName,
                DateTime.Today.Year - r.Appointment.Patient.DateOfBirth.Year,
                r.Appointment.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
                r.Doctor.FullName,
                r.CreatedAt,
                r.PrimaryDiagnosisCode ?? "—",
                r.PrimaryDiagnosisDesc ?? "—",
                r.Conclusion ?? "Đã hoàn thành",
                r.ClinicalExamination,
                r.Symptoms
            ))
            .ToListAsync(ct);

        // Ghi AccessLog cho hành vi Trưởng khoa xem bệnh án khoa
        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = viewerAccountId,
            Action = "VIEW_RECORD",
            OccurredAt = _timeProvider.GetLocalNow().DateTime,
            IpAddress = ipAddress
        });
        await _context.SaveChangesAsync(ct);

        return records;
    }

    // 5. Tổng quan vận hành khoa
    public async Task<DepartmentOverviewDto> GetDepartmentOverviewAsync(int departmentId, CancellationToken ct = default)
    {
        var dept = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        var deptName = dept?.Name ?? "Khoa chuyên môn";

        var totalDoctors = await _context.Doctors.CountAsync(d => d.DepartmentId == departmentId, ct);
        var totalRooms = await _context.ClinicRooms.CountAsync(r => r.DepartmentId == departmentId, ct);
        var pendingExceptions = await _context.ScheduleExceptions
            .CountAsync(e => e.Doctor.DepartmentId == departmentId && e.Status == ScheduleExceptionStatus.Pending, ct);

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var startOfDay = today.ToDateTime(new TimeOnly(0, 0));
        var endOfDay = today.ToDateTime(new TimeOnly(23, 59, 59));

        var todayApps = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Room)
            .Include(a => a.VitalSigns)
            .Include(a => a.Slot)
            .Where(a => (a.DepartmentId == departmentId || (a.Doctor != null && a.Doctor.DepartmentId == departmentId))
                        && ((a.Slot != null && a.Slot.StartTime >= startOfDay && a.Slot.StartTime <= endOfDay)
                            || (a.CheckedInAt.HasValue && a.CheckedInAt.Value >= startOfDay && a.CheckedInAt.Value <= endOfDay)
                            || (a.CreatedAt >= startOfDay && a.CreatedAt <= endOfDay)))
            .ToListAsync(ct);

        var totalToday = todayApps.Count;
        var completed = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.Completed);
        var inProgress = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.InProgress || a.StatusId == (int)AppointmentStatusId.ReadyForDoctor);
        var waitingReception = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.Confirmed || a.StatusId == (int)AppointmentStatusId.Pending);
        var needVitalSigns = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.CheckedIn && (a.VitalSigns == null || !a.VitalSigns.SystolicBp.HasValue));

        var todayAppIds = todayApps.Select(a => a.Id).ToList();
        var unpaidInvoices = await _context.Invoices
            .AsNoTracking()
            .Where(i => todayAppIds.Contains(i.AppointmentId) && i.Status != InvoiceStatus.Paid)
            .ToListAsync(ct);
        var unpaidCount = unpaidInvoices.Count;
        var unpaidAmount = unpaidInvoices.Sum(i => i.PatientAmount);

        var activeQueue = todayApps
            .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor ||
                        a.StatusId == (int)AppointmentStatusId.CheckedIn ||
                        a.StatusId == (int)AppointmentStatusId.InProgress ||
                        a.StatusId == (int)AppointmentStatusId.Confirmed)
            .OrderBy(a => a.PriorityLevel)
            .ThenBy(a => a.QueueNumber)
            .Select(a => new DepartmentQueueItemDto(
                a.Id,
                a.AppointmentCode,
                a.Patient.FullName,
                DateTime.Today.Year - a.Patient.DateOfBirth.Year,
                a.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
                a.QueueNumber ?? "—",
                a.StatusId,
                a.StatusId == (int)AppointmentStatusId.ReadyForDoctor ? "Chờ bác sĩ" :
                a.StatusId == (int)AppointmentStatusId.InProgress ? "Đang khám" :
                a.StatusId == (int)AppointmentStatusId.CheckedIn ? "Đã tiếp nhận" :
                a.StatusId == (int)AppointmentStatusId.Confirmed ? "Đã đặt hẹn" : "Chờ xử lý",
                a.Room != null ? a.Room.RoomNumber : (a.Slot != null && a.Slot.Room != null ? a.Slot.Room.RoomNumber : "P.201"),
                a.Doctor != null ? a.Doctor.FullName : "BS. Chuyên khoa",
                a.Slot != null ? a.Slot.StartTime : (a.CheckedInAt ?? a.CreatedAt),
                a.Reason ?? "Khám chuyên khoa theo hẹn"
            ))
            .ToList();

        return new DepartmentOverviewDto(
            departmentId,
            deptName,
            totalDoctors,
            totalRooms,
            pendingExceptions,
            totalToday,
            completed,
            inProgress,
            waitingReception,
            needVitalSigns,
            unpaidCount,
            unpaidAmount,
            activeQueue
        );
    }

    // 6. Danh mục phụ trợ
    public async Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsInDepartmentAsync(int departmentId, CancellationToken ct = default)
    {
        var query = _context.Doctors
            .AsNoTracking();

        if (departmentId > 0)
        {
            query = query.Where(d => d.DepartmentId == departmentId);
        }

        return await query
            .OrderBy(d => d.FullName)
            .Select(d => new DoctorListItemDto(d.Id, d.FullName, d.Specialty ?? "Chuyên khoa", d.Department.Name, d.LicenseNumber ?? "", d.AccountId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StaffListItemDto>> GetStaffInDepartmentAsync(int departmentId, CancellationToken ct = default)
    {
        // Lấy danh sách account là trưởng khoa, bác sĩ hoặc y tá thuộc khoa
        var headAccountIds = await _context.DepartmentHeads
            .Where(h => h.DepartmentId == departmentId)
            .Select(h => h.AccountId)
            .ToListAsync(ct);

        var doctorAccountIds = await _context.Doctors
            .Where(d => d.DepartmentId == departmentId)
            .Select(d => d.AccountId)
            .ToListAsync(ct);

        var nurseAccountIds = await _context.Nurses
            .Where(n => n.DepartmentId == departmentId)
            .Select(n => n.AccountId)
            .ToListAsync(ct);

        var allAccountIds = headAccountIds.Concat(doctorAccountIds).Concat(nurseAccountIds).Distinct().ToList();

        return await _context.Accounts
            .AsNoTracking()
            .Include(a => a.Role)
            .Include(a => a.Doctor)
            .Include(a => a.DepartmentHead)
            .Include(a => a.Nurse)
            .Where(a => allAccountIds.Contains(a.Id))
            .OrderBy(a => a.Username)
            .Select(a => new StaffListItemDto(
                a.Id,
                a.Username,
                a.Doctor != null ? a.Doctor.FullName : (a.DepartmentHead != null ? a.DepartmentHead.FullName : (a.Nurse != null ? a.Nurse.FullName : a.Username)),
                a.Role.Name,
                a.Role.Code,
                a.IsActive
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ClinicRoomDto>> GetRoomsInDepartmentAsync(int departmentId, CancellationToken ct = default)
    {
        return await _context.ClinicRooms
            .AsNoTracking()
            .Where(r => r.DepartmentId == departmentId && r.IsActive)
            .OrderBy(r => r.RoomNumber)
            .Select(r => new ClinicRoomDto(r.Id, r.DepartmentId, r.RoomName, r.RoomNumber))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DepartmentPatientListItemDto>> GetPatientsAsync(int? departmentId = null, CancellationToken ct = default)
    {
        var query = _context.Patients.AsNoTracking();
        if (departmentId.HasValue && departmentId.Value > 0)
        {
            var deptPatientIds = await _context.Appointments
                .Where(a => a.DepartmentId == departmentId.Value)
                .Select(a => a.PatientId)
                .Distinct()
                .ToListAsync(ct);

            if (deptPatientIds.Count > 0)
            {
                query = query.Where(p => deptPatientIds.Contains(p.Id));
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var list = await query
            .OrderBy(p => p.FullName)
            .Select(p => new DepartmentPatientListItemDto(
                p.Id,
                $"BN{p.Id:D4}",
                p.FullName,
                p.Gender == Gender.Male ? "Nam" : p.Gender == Gender.Female ? "Nữ" : "Khác",
                p.DateOfBirth,
                today.Year - p.DateOfBirth.Year - (today < new DateOnly(today.Year, p.DateOfBirth.Month, p.DateOfBirth.Day) ? 1 : 0),
                p.PhoneNumber,
                p.Address
            ))
            .ToListAsync(ct);

        if (list.Count == 0)
        {
            list = await _context.Patients
                .AsNoTracking()
                .OrderBy(p => p.FullName)
                .Select(p => new DepartmentPatientListItemDto(
                    p.Id,
                    $"BN{p.Id:D4}",
                    p.FullName,
                    p.Gender == Gender.Male ? "Nam" : p.Gender == Gender.Female ? "Nữ" : "Khác",
                    p.DateOfBirth,
                    today.Year - p.DateOfBirth.Year - (today < new DateOnly(today.Year, p.DateOfBirth.Month, p.DateOfBirth.Day) ? 1 : 0),
                    p.PhoneNumber,
                    p.Address
                ))
                .ToListAsync(ct);
        }

        return list;
    }
}
