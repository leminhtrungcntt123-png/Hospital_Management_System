using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public partial class DepartmentHeadService : IDepartmentHeadService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DepartmentHeadService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    private DateTime LocalNow => DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);

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
                CreatedAt = LocalNow
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
            .OrderByDescending(a => a.PriorityLevel)
            .ThenBy(a => a.QueueNumber)
            .Select(a => new DepartmentQueueItemDto(
                a.Id,
                a.AppointmentCode,
                a.Patient.FullName,
                LocalNow.Year - a.Patient.DateOfBirth.Year,
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
