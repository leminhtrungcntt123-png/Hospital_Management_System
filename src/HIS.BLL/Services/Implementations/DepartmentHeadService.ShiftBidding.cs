using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public partial class DepartmentHeadService
{
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
            CreatedAt = LocalNow
        };

        _context.SchedulingPeriods.Add(period);
        await _context.SaveChangesAsync(ct);

        return Result.Success(period.Id);
    }

    public async Task<SchedulingPeriodDto?> GetActiveSchedulingPeriodAsync(int? departmentId, CancellationToken ct = default)
    {
        var now = LocalNow;
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

        var now = LocalNow;
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

        var startDate = period?.StartDate ?? DateOnly.FromDateTime(now);
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

        var now = LocalNow;
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

        // Kiểm tra trần số ca tối đa/tuần
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
            CreatedAt = now
        };

        _context.DoctorSchedules.Add(schedule);

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = doctor.AccountId,
            Action = "SHIFT_BID",
            EntityName = "doctor_schedules",
            EntityId = 0,
            IsSuccess = true,
            Severity = "INFO",
            OccurredAt = now,
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
        var lookbackStart = workDate.AddDays(-14);
        var weekStart = workDate.AddDays(-((int)workDate.DayOfWeek == 0 ? 6 : (int)workDate.DayOfWeek - 1));
        var weekEnd = weekStart.AddDays(6);

        var doctorIds = schedules.Select(s => s.DoctorId).Distinct().ToList();
        var accountIds = schedules.Select(s => s.Doctor.AccountId).Distinct().ToList();

        var nightDutyAccountIds = shift == ExamShift.Morning && accountIds.Count > 0
            ? (await _context.DutyShifts
                .AsNoTracking()
                .Where(d => accountIds.Contains(d.AccountId)
                    && d.Status == DutyShiftStatus.Approved
                    && d.EndTime >= morningStart
                    && d.StartTime <= noon)
                .Select(d => d.AccountId)
                .Distinct()
                .ToListAsync(ct)).ToHashSet()
            : new HashSet<int>();

        var recentHotSchedules = doctorIds.Count > 0
            ? await _context.DoctorSchedules
                .AsNoTracking()
                .Where(ds => doctorIds.Contains(ds.DoctorId)
                    && ds.IsHotShift
                    && ds.WorkDate >= lookbackStart && ds.WorkDate < workDate
                    && (ds.Status == DoctorScheduleStatus.Rejected
                        || ds.Status == DoctorScheduleStatus.Reallocated
                        || (ds.Status == DoctorScheduleStatus.Approved && ds.WorkDate >= prevWeekStart && ds.WorkDate <= prevWeekEnd)))
                .Select(ds => new { ds.DoctorId, ds.Status })
                .ToListAsync(ct)
            : [];

        var rejectedHotDoctorIds = recentHotSchedules
            .Where(x => x.Status == DoctorScheduleStatus.Rejected || x.Status == DoctorScheduleStatus.Reallocated)
            .Select(x => x.DoctorId)
            .ToHashSet();

        var approvedHotDoctorIds = recentHotSchedules
            .Where(x => x.Status == DoctorScheduleStatus.Approved)
            .Select(x => x.DoctorId)
            .ToHashSet();

        var weeklyApprovedCounts = doctorIds.Count > 0
            ? await _context.DoctorSchedules
                .AsNoTracking()
                .Where(ds => doctorIds.Contains(ds.DoctorId)
                    && ds.Status == DoctorScheduleStatus.Approved
                    && ds.WorkDate >= weekStart && ds.WorkDate <= weekEnd)
                .GroupBy(ds => ds.DoctorId)
                .Select(g => new { DoctorId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DoctorId, x => x.Count, ct)
            : new Dictionary<int, int>();

        var followUpCounts = doctorIds.Count > 0
            ? await _context.MedicalRecords
                .AsNoTracking()
                .Where(mr => doctorIds.Contains(mr.DoctorId) && mr.FollowUpDate == workDate)
                .GroupBy(mr => mr.DoctorId)
                .Select(g => new { DoctorId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DoctorId, x => x.Count, ct)
            : new Dictionary<int, int>();

        var candidates = new List<DecisionMatrixCandidateDto>();

        foreach (var s in schedules)
        {
            var doc = s.Doctor;
            bool hasNightDutyConflict = nightDutyAccountIds.Contains(doc.AccountId);

            int rotationScore;
            string rotationBadge;
            if (rejectedHotDoctorIds.Contains(doc.Id))
            {
                rotationScore = 2;
                rotationBadge = "Đền bù ca Hot (⭐⭐)";
            }
            else if (!approvedHotDoctorIds.Contains(doc.Id))
            {
                rotationScore = 1;
                rotationBadge = "Ưu tiên luân phiên (⭐)";
            }
            else
            {
                rotationScore = 0;
                rotationBadge = "Đã duyệt ca Hot tuần trước";
            }

            int approvedShiftsThisPeriod = weeklyApprovedCounts.GetValueOrDefault(doc.Id);
            int continuityOfCareCount = followUpCounts.GetValueOrDefault(doc.Id);

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

        if (!await IsActiveHospitalAdminAsync(request.ReviewedByAccountId, ct))
        {
            var callerDeptId = await ResolveDepartmentIdByAccountIdAsync(request.ReviewedByAccountId, ct);
            if (callerDeptId <= 0 || schedule.DepartmentId != callerDeptId)
                return Result.Failure("FORBIDDEN", "Bạn không có quyền duyệt ca khám của khoa khác.");
        }

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

        var now = LocalNow;
        schedule.RoomId = request.RoomId;
        schedule.Status = DoctorScheduleStatus.Approved;
        schedule.ReviewedBy = request.ReviewedByAccountId;
        schedule.ReviewedAt = now;

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
                bid.IsHotShift = true;
                bid.ReviewedBy = request.ReviewedByAccountId;
                bid.ReviewedAt = now;

                if (bid.Doctor != null && bid.Doctor.AccountId > 0)
                {
                    var shiftName = bid.Shift == ExamShift.Morning ? "Ca Sáng (07:30 - 11:30)" : "Ca Chiều (13:00 - 16:30)";
                    _context.Notifications.Add(new Notification
                    {
                        AccountId = bid.Doctor.AccountId,
                        Title = "Đăng ký ca khám không thành công",
                        Content = $"Ca khám ngày {bid.WorkDate:dd/MM/yyyy} ({shiftName}) của bạn không được duyệt do buồng khám đã đủ bác sĩ. Hệ thống đã tự động cộng điểm ưu tiên đền bù (⭐⭐) cho bạn khi đăng ký ca ở kỳ tiếp theo!",
                        CreatedAt = now
                    });
                }
            }
        }

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
            OccurredAt = now,
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

        if (!await IsActiveHospitalAdminAsync(request.ReviewedByAccountId, ct))
        {
            var callerDeptId = await ResolveDepartmentIdByAccountIdAsync(request.ReviewedByAccountId, ct);
            if (callerDeptId <= 0 || oldRecord.DepartmentId != callerDeptId)
                return Result.Failure<int>("FORBIDDEN", "Bạn không có quyền điều chuyển ca khám của khoa khác.");
        }

        if (oldRecord.Status == DoctorScheduleStatus.Approved)
            return Result.Failure<int>("CANNOT_REALLOCATE_ACTIVE", "Không thể điều chuyển ca khám đã được duyệt. Vui lòng hủy ca theo quy trình hủy lịch trước khi sắp xếp lại.");

        if (oldRecord.Status != DoctorScheduleStatus.Bidding && oldRecord.Status != DoctorScheduleStatus.Rejected)
            return Result.Failure<int>("INVALID_STATUS", "Chỉ có thể điều chuyển ca đang đăng ký hoặc đã bị từ chối.");

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

        // Kiểm tra bác sĩ đã có ca Approved tại ca mới chưa để tránh vi phạm uq_doctor_active_shift
        bool doctorBusy = await _context.DoctorSchedules
            .AnyAsync(s => s.DoctorId == oldRecord.DoctorId
                && s.WorkDate == request.TargetDate
                && s.Shift == request.TargetShift
                && s.Status == DoctorScheduleStatus.Approved, ct);

        if (doctorBusy)
            return Result.Failure<int>("DOCTOR_BUSY", $"Bác sĩ {oldRecord.Doctor.FullName} đã có ca khám được duyệt vào thời điểm điều chuyển.");

        var now = LocalNow;
        oldRecord.Status = DoctorScheduleStatus.Reallocated;
        oldRecord.ReviewedBy = request.ReviewedByAccountId;
        oldRecord.ReviewedAt = now;

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
            existingTargetBid.ReviewedAt = now;
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
                ReviewedAt = now,
                CreatedAt = now
            };
            _context.DoctorSchedules.Add(targetSchedule);
        }

        GenerateAppointmentSlotsForSchedule(targetSchedule);

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = request.ReviewedByAccountId,
            Action = "SHIFT_REALLOCATE",
            EntityName = "doctor_schedules",
            EntityId = targetSchedule.Id,
            IsSuccess = true,
            Severity = "WARNING",
            OccurredAt = now,
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

        bool isHospitalAdmin = await IsActiveHospitalAdminAsync(reviewedByAccountId, ct);

        if (!period.DepartmentId.HasValue)
        {
            if (!isHospitalAdmin)
                return Result.Failure<int>("FORBIDDEN", "Chỉ quản trị viên toàn viện mới có quyền xuất bản kỳ đăng ký dùng chung toàn viện.");
        }
        else if (!isHospitalAdmin)
        {
            var callerDeptId = await ResolveDepartmentIdByAccountIdAsync(reviewedByAccountId, ct);
            if (callerDeptId <= 0 || period.DepartmentId.Value != callerDeptId)
                return Result.Failure<int>("FORBIDDEN", "Bạn không có quyền xuất bản kỳ đăng ký của khoa khác.");
        }

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
            OccurredAt = LocalNow,
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
                    Schedule = s,
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

    private Task<bool> IsActiveHospitalAdminAsync(int accountId, CancellationToken ct) =>
        _context.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.IsActive && a.RoleId == (int)RoleId.Admin, ct);
}

