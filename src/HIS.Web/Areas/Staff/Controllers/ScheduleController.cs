using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.Domain;
using HIS.Web.Areas.Staff.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "StaffAny")]
public class ScheduleController : Controller
{
    private readonly IDepartmentHeadService _departmentHeadService;
    private readonly ISlotGenerationService _slotGenerationService;
    private readonly IDepartmentService _departmentService;
    private readonly TimeProvider _timeProvider;

    public ScheduleController(
        IDepartmentHeadService departmentHeadService,
        ISlotGenerationService slotGenerationService,
        IDepartmentService departmentService,
        TimeProvider timeProvider)
    {
        _departmentHeadService = departmentHeadService;
        _slotGenerationService = slotGenerationService;
        _departmentService = departmentService;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? tab, int weekOffset = 0, CancellationToken ct = default)
    {
        var accountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(accountId, ct);

        var activeTab = string.IsNullOrWhiteSpace(tab) ? "schedule" : tab.ToLowerInvariant();

        var today = DateOnly.FromDateTime(DateTime.Today);
        int daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var mondayThisWeek = today.AddDays(-daysSinceMonday);
        var mondayTargetWeek = mondayThisWeek.AddDays(weekOffset * 7);

        var weekDates = new List<DateOnly>
        {
            mondayTargetWeek,
            mondayTargetWeek.AddDays(1),
            mondayTargetWeek.AddDays(2),
            mondayTargetWeek.AddDays(3),
            mondayTargetWeek.AddDays(4)
        };

        var allDepartments = await _departmentService.GetDepartmentsAsync(ct);
        var allRooms = await _departmentService.GetRoomsByDepartmentAsync(departmentId > 0 ? departmentId : 0, ct);

        IReadOnlyList<AppointmentSlotItemDto> slots = [];
        var slotFromDate = today;
        var slotToDate = today.AddDays(7);

        if (activeTab == "slots")
        {
            slots = await _slotGenerationService.SearchSlotsAsync(new SlotSearchRequest(
                slotFromDate,
                slotToDate,
                ["MORNING", "AFTERNOON"],
                departmentId > 0 ? departmentId : null,
                null
            ), ct);
        }

        if (departmentId <= 0 && activeTab != "slots")
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào. Vui lòng liên hệ Quản trị viên hệ thống.";
            return View(new ScheduleViewModel
            {
                ActiveTab = activeTab,
                Today = today,
                WeekStart = mondayTargetWeek,
                WeekDates = weekDates,
                AllDepartments = allDepartments,
                AvailableRooms = allRooms,
                Slots = slots,
                SlotFilterStartDate = slotFromDate,
                SlotFilterEndDate = slotToDate
            });
        }

        var overview = departmentId > 0
            ? await _departmentHeadService.GetDepartmentOverviewAsync(departmentId, ct)
            : new DepartmentOverviewDto(0, "Tất cả chuyên khoa", 0, 0, 0, 0, 0, 0);

        var exceptions = departmentId > 0
            ? await _departmentHeadService.GetExceptionsAsync(departmentId, null, ct)
            : (IReadOnlyList<DepartmentExceptionItemDto>)[];

        var activePeriod = departmentId > 0
            ? await _departmentHeadService.GetActiveSchedulingPeriodAsync(departmentId, ct)
            : null;

        DepartmentDemandGridDto? demandGrid = null;
        if (departmentId > 0 && activePeriod != null)
        {
            demandGrid = await _departmentHeadService.GetDepartmentDemandGridAsync(departmentId, activePeriod.Id, ct);
        }

        var schedules = departmentId > 0
            ? await _departmentHeadService.GetSchedulesAsync(departmentId, null, mondayTargetWeek, mondayTargetWeek.AddDays(6), ct)
            : (IReadOnlyList<DepartmentScheduleItemDto>)[];

        var dutyShifts = departmentId > 0
            ? await _departmentHeadService.GetDutyShiftsAsync(departmentId, ct)
            : (IReadOnlyList<DepartmentDutyShiftDto>)[];

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var isHead = User.IsInRole("DEPARTMENT_HEAD");
        var medicalRecords = (isHead && departmentId > 0)
            ? await _departmentHeadService.GetMedicalRecordsAsync(departmentId, accountId, ipAddress, ct)
            : (IReadOnlyList<DepartmentMedicalRecordDto>)[];

        var doctors = departmentId > 0
            ? await _departmentHeadService.GetDoctorsInDepartmentAsync(departmentId, ct)
            : await _departmentHeadService.GetDoctorsInDepartmentAsync(0, ct);

        var staff = departmentId > 0
            ? await _departmentHeadService.GetStaffInDepartmentAsync(departmentId, ct)
            : (IReadOnlyList<StaffListItemDto>)[];

        var rooms = departmentId > 0
            ? await _departmentHeadService.GetRoomsInDepartmentAsync(departmentId, ct)
            : allRooms;

        var patients = departmentId > 0
            ? await _departmentHeadService.GetPatientsAsync(departmentId, ct)
            : (IReadOnlyList<DepartmentPatientListItemDto>)[];

        var headDoctor = doctors.FirstOrDefault(d => d.AccountId == accountId);
        if (headDoctor == null && activeTab == "my-schedule")
        {
            ViewBag.UnlinkedAccountMessage = "Tài khoản hiện tại chưa được liên kết với hồ sơ bác sĩ trong khoa. Vui lòng liên hệ Quản trị viên để gán tài khoản với hồ sơ Bác sĩ.";
        }

        var vm = new ScheduleViewModel
        {
            ActiveTab = activeTab,
            DepartmentId = departmentId,
            DepartmentName = overview.DepartmentName,
            WeekOffset = weekOffset,
            Today = today,
            WeekStart = mondayTargetWeek,
            WeekDates = weekDates,
            Overview = overview,
            Exceptions = exceptions,
            Schedules = schedules,
            DutyShifts = dutyShifts,
            MedicalRecords = medicalRecords,
            Patients = patients,
            AvailableDoctors = doctors,
            AvailableStaff = staff,
            AvailableRooms = rooms,
            CurrentHeadDoctorId = headDoctor?.Id,
            CurrentHeadDoctor = headDoctor,
            ActivePeriod = activePeriod,
            DemandGrid = demandGrid,
            AllDepartments = allDepartments,
            Slots = slots,
            SlotFilterStartDate = slotFromDate,
            SlotFilterEndDate = slotToDate,
            SlotFilterDepartmentId = departmentId > 0 ? departmentId : null
        };

        return View(vm);
    }

    // Duyệt đề nghị nghỉ / đổi ca
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveException(int id, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(accountId, ct);
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = "exceptions" });
        }

        var result = await _departmentHeadService.ApproveExceptionAsync(new ApproveExceptionRequest(id, accountId, departmentId), ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã phê duyệt đề nghị và tự động điều phối slot / lịch hẹn liên quan.";
        }

        return RedirectToAction(nameof(Index), new { tab = "exceptions" });
    }

    // Từ chối đề nghị nghỉ / đổi ca
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectException(int id, string? reason, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(accountId, ct);
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = "exceptions" });
        }

        var result = await _departmentHeadService.RejectExceptionAsync(new RejectExceptionRequest(id, accountId, reason, departmentId), ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã từ chối đề nghị.";
        }

        return RedirectToAction(nameof(Index), new { tab = "exceptions" });
    }

    // Phân công ca trực đêm nhân sự
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ActionName("AssignDuty")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignDuty(int accountId, DateTime startTime, DateTime endTime, string? note, string? returnTab, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        var targetTab = !string.IsNullOrWhiteSpace(returnTab) ? returnTab : "schedule";
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        // Kiểm tra nhân sự có thuộc khoa quản lý không
        var allowedStaff = await _departmentHeadService.GetStaffInDepartmentAsync(departmentId, ct);
        if (!allowedStaff.Any(s => s.Id == accountId))
        {
            TempData["ErrorMessage"] = "Bạn chỉ có thể phân công ca trực cho nhân sự thuộc khoa của mình.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        if (startTime < _timeProvider.GetLocalNow().DateTime)
        {
            TempData["ErrorMessage"] = "Không thể phân công ca trực trong quá khứ. Vui lòng chọn thời gian từ hiện tại hoặc tương lai.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        var request = new AssignDutyShiftRequest(
            accountId,
            departmentId,
            DateTime.SpecifyKind(startTime, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(endTime, DateTimeKind.Unspecified),
            note,
            currentAccountId
        );

        var result = await _departmentHeadService.AssignDutyShiftAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã phân công ca trực nhân sự thành công.";
        }

        return RedirectToAction(nameof(Index), new { tab = targetTab });
    }

    // Trưởng khoa mở đợt đăng ký ca mới (Shift Bidding Period)
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePeriod(
        string name,
        DateOnly startDate,
        DateOnly endDate,
        DateTime biddingOpensAt,
        DateTime biddingClosesAt,
        CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        var request = new CreateSchedulingPeriodRequest(
            departmentId,
            name,
            startDate,
            endDate,
            biddingOpensAt,
            biddingClosesAt,
            currentAccountId
        );

        var result = await _departmentHeadService.CreateSchedulingPeriodAsync(request, ct);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = $"Đã mở đợt đăng ký lịch \"{name}\" thành công! Bác sĩ có thể bắt đầu đăng ký ca.";
        }

        return RedirectToAction(nameof(Index), new { tab = "schedule" });
    }

    // Thiết lập / cập nhật lịch khám theo ngày cụ thể & hạn mức
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ActionName("UpsertSchedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpsertSchedule(
        int? scheduleId,
        int? periodId,
        int doctorId,
        int roomId,
        DateOnly? workDate,
        DateOnly? effectiveFrom,
        ExamShift shift,
        TimeOnly startTime,
        TimeOnly endTime,
        int slotDurationMinutes,
        int maxPatients,
        int onlineSlots,
        string? returnTab,
        CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        var targetTab = !string.IsNullOrWhiteSpace(returnTab) ? returnTab : "schedule";
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        var targetDate = workDate ?? effectiveFrom ?? DateOnly.FromDateTime(DateTime.Today);
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (targetDate < today)
        {
            TempData["ErrorMessage"] = "Không thể thiết lập lịch khám cho ngày trong quá khứ. Chỉ được phép thiết lập từ ngày hiện tại hoặc tương lai.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        // Tự động gắn vào đợt đang mở nếu chưa truyền periodId
        if (!periodId.HasValue || periodId.Value <= 0)
        {
            var activePeriod = await _departmentHeadService.GetActiveSchedulingPeriodAsync(departmentId, ct);
            periodId = activePeriod?.Id;
        }

        // Kiểm tra bác sĩ có thuộc khoa quản lý không
        var allowedDoctors = await _departmentHeadService.GetDoctorsInDepartmentAsync(departmentId, ct);
        if (!allowedDoctors.Any(d => d.Id == doctorId))
        {
            TempData["ErrorMessage"] = "Bạn chỉ có thể thiết lập lịch cho bác sĩ thuộc khoa của mình.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        // Kiểm tra phòng khám có thuộc khoa quản lý không
        var allowedRooms = await _departmentHeadService.GetRoomsInDepartmentAsync(departmentId, ct);
        if (!allowedRooms.Any(r => r.Id == roomId))
        {
            TempData["ErrorMessage"] = "Bạn chỉ có thể phân công phòng khám thuộc khoa của mình.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        var request = new UpsertDoctorScheduleRequest(
            scheduleId,
            periodId,
            doctorId,
            departmentId,
            roomId,
            targetDate,
            shift,
            startTime,
            endTime,
            slotDurationMinutes <= 0 ? 30 : slotDurationMinutes,
            maxPatients <= 0 ? 40 : maxPatients,
            onlineSlots <= 0 ? 28 : onlineSlots,
            DoctorScheduleStatus.Approved
        );

        var result = await _departmentHeadService.UpsertDoctorScheduleAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã lưu thiết lập lịch khám thành công.";
        }

        return RedirectToAction(nameof(Index), new { tab = targetTab });
    }

    // Lấy bảng ma trận quyết định hỗ trợ Trưởng khoa ra quyết định (AJAX)
    [HttpGet]
    [Authorize(Policy = "DepartmentHeadOnly")]
    public async Task<IActionResult> GetDecisionMatrix(DateOnly workDate, ExamShift shift, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        if (departmentId <= 0)
        {
            return BadRequest(new { message = "Không xác định được khoa chuyên môn." });
        }

        var matrix = await _departmentHeadService.GetDecisionMatrixAsync(departmentId, workDate, shift, ct);
        return Ok(matrix);
    }

    // Trưởng khoa phê duyệt ca & gán phòng khám
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveShift(int scheduleId, int roomId, string? tab, DateOnly? date, string? shift, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var request = new ApproveShiftRequest(scheduleId, roomId, currentAccountId);
        var result = await _departmentHeadService.ApproveShiftAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã phê duyệt ca khám và phân công phòng thành công.";
        }

        return RedirectToAction(nameof(Index), new
        {
            tab = string.IsNullOrWhiteSpace(tab) ? "bidding" : tab,
            date = date?.ToString("yyyy-MM-dd"),
            shift = shift
        });
    }

    // Trưởng khoa điều chuyển ca trượt sang ca mới
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReallocateShift(int oldScheduleId, DateOnly targetDate, ExamShift targetShift, int targetRoomId, string? tab, DateOnly? date, string? shift, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var request = new ReallocateShiftRequest(oldScheduleId, targetDate, targetShift, targetRoomId, currentAccountId);
        var result = await _departmentHeadService.ReallocateShiftAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = $"Đã điều chuyển bác sĩ sang ca ngày {targetDate:dd/MM/yyyy} ({targetShift}) thành công!";
        }

        return RedirectToAction(nameof(Index), new
        {
            tab = string.IsNullOrWhiteSpace(tab) ? "bidding" : tab,
            date = date?.ToString("yyyy-MM-dd"),
            shift = shift
        });
    }

    // Trưởng khoa xuất bản toàn bộ lịch kỳ (PUBLISHED -> Sinh appointment_slots hàng loạt)
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishPeriod(int periodId, string? tab, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var result = await _departmentHeadService.PublishSchedulingPeriodAsync(periodId, currentAccountId, ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = $"Đã xuất bản lịch làm việc thành công! Hệ thống đã tạo {result.Value} slots khám trực tuyến.";
        }

        return RedirectToAction(nameof(Index), new { tab = string.IsNullOrWhiteSpace(tab) ? "bidding" : tab });
    }

    // Lấy thông tin cảnh báo ảnh hưởng lịch hẹn trước khi hủy ca
    [HttpGet]
    [Authorize(Policy = "DepartmentHeadOnly")]
    public async Task<IActionResult> GetScheduleAffectedCount(int scheduleId, CancellationToken ct)
    {
        var impact = await _departmentHeadService.GetScheduleCancelImpactAsync(scheduleId, ct);
        return Json(new
        {
            success = true,
            canCancel = impact.CanCancel,
            ongoingCount = impact.OngoingAppointmentsCount,
            affectedCount = impact.AffectedAppointmentsCount,
            message = impact.Message
        });
    }

    // Xóa / hủy cấu hình lịch khám
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSchedule(int scheduleId, string? returnTab, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        var targetTab = !string.IsNullOrWhiteSpace(returnTab) ? returnTab : "schedule";
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        var result = await _departmentHeadService.DeleteDoctorScheduleAsync(scheduleId, departmentId, currentAccountId, ct);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã hủy ca khám thành công. Các lịch hẹn bị ảnh hưởng đã được chuyển sang trạng thái cần đặt lại lịch.";
        }

        return RedirectToAction(nameof(Index), new { tab = targetTab });
    }

    // Xóa / hủy ca trực đêm
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDutyShift(int id, string? returnTab, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        var targetTab = !string.IsNullOrWhiteSpace(returnTab) ? returnTab : "schedule";
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = targetTab });
        }

        var result = await _departmentHeadService.DeleteDutyShiftAsync(id, departmentId, ct);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã xóa ca trực thành công.";
        }

        return RedirectToAction(nameof(Index), new { tab = targetTab });
    }

    // ========================================================
    // QUẢN LÝ LỊCH KHÁM & KHUNG GIỜ (SLOT)
    // ========================================================

    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    public async Task<IActionResult> GenerateSlots([FromBody] SlotGenerationRequest request, CancellationToken ct = default)
    {
        if (request == null)
        {
            return Json(new { success = false, message = "Dữ liệu yêu cầu không hợp lệ." });
        }

        if (request.EndDate < request.StartDate)
        {
            return Json(new { success = false, message = "Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu." });
        }

        var callerDepartmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(GetCurrentAccountId(), ct);
        if (callerDepartmentId <= 0)
        {
            return Json(new { success = false, message = "Không xác định được khoa quản lý của bạn." });
        }

        var result = await _slotGenerationService.GenerateSlotsAsync(request.StartDate, request.EndDate, callerDepartmentId, ct);
        if (!result.IsSuccess)
        {
            return Json(new { success = false, message = result.Error.Message });
        }

        var data = result.Value!;
        return Json(new
        {
            success = true,
            createdCount = data.CreatedCount,
            existingCount = data.ExistingCount,
            exceptionSkippedCount = data.ExceptionSkippedCount,
            message = $"Sinh thành công {data.CreatedCount} khung giờ khám mới ({data.ExistingCount} slot đã tồn tại, {data.ExceptionSkippedCount} ca nghỉ)."
        });
    }

    [HttpGet]
    public async Task<IActionResult> SearchSlots(
        DateOnly filterStartDate,
        DateOnly filterEndDate,
        [FromQuery] List<string>? shifts,
        int? departmentId,
        int? roomId,
        int? doctorId,
        CancellationToken ct = default)
    {
        var slotReq = new SlotSearchRequest(
            filterStartDate,
            filterEndDate,
            shifts ?? ["MORNING", "AFTERNOON"],
            departmentId,
            roomId,
            doctorId
        );

        var list = await _slotGenerationService.SearchSlotsAsync(slotReq, ct);

        int total = list.Count;
        int available = list.Sum(s => s.RemainingCount);
        int booked = list.Sum(s => s.BookedCount);

        return Json(new
        {
            success = true,
            data = list,
            total,
            available,
            booked
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetRoomsByDepartment(int? departmentId, CancellationToken ct = default)
    {
        var rooms = await _departmentService.GetRoomsByDepartmentAsync(departmentId ?? 0, ct);
        return Json(rooms);
    }

    [HttpGet]
    public async Task<IActionResult> GetDoctorsByDepartment(int? departmentId, CancellationToken ct = default)
    {
        var doctors = await _departmentHeadService.GetDoctorsInDepartmentAsync(departmentId ?? 0, ct);
        return Json(doctors);
    }

    private int GetCurrentAccountId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}
