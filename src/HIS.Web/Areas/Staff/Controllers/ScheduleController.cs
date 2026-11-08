using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
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
    private readonly TimeProvider _timeProvider;

    public ScheduleController(IDepartmentHeadService departmentHeadService, TimeProvider? timeProvider = null)
    {
        _departmentHeadService = departmentHeadService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Hiển thị giao diện điều hành khoa phòng dành cho Trưởng khoa (lịch tuần, ca trực, đề xuất đổi ca, bệnh án).
    /// </summary>
    /// <param name="tab">Tab chức năng đang chọn.</param>
    /// <param name="weekOffset">Độ lệch tuần so với tuần hiện tại.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Index(string? tab, int weekOffset = 0, CancellationToken ct = default)
    {
        var accountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(accountId, ct);

        var activeTab = string.IsNullOrWhiteSpace(tab) ? "schedule" : tab.ToLowerInvariant();

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
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

        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào. Vui lòng liên hệ Quản trị viên hệ thống.";
            return View(new ScheduleViewModel
            {
                ActiveTab = activeTab,
                Today = today,
                WeekStart = mondayTargetWeek,
                WeekDates = weekDates
            });
        }

        var overview = await _departmentHeadService.GetDepartmentOverviewAsync(departmentId, ct);
        var exceptions = await _departmentHeadService.GetExceptionsAsync(departmentId, null, ct);
        var schedules = await _departmentHeadService.GetSchedulesAsync(departmentId, ct);
        var dutyShifts = await _departmentHeadService.GetDutyShiftsAsync(departmentId, ct);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var isHead = User.IsInRole("DEPARTMENT_HEAD") || User.IsInRole("DEPT_HEAD");
        var medicalRecords = isHead
            ? await _departmentHeadService.GetMedicalRecordsAsync(departmentId, accountId, ipAddress, ct)
            : (IReadOnlyList<DepartmentMedicalRecordDto>)[];

        var doctors = await _departmentHeadService.GetDoctorsInDepartmentAsync(departmentId, ct);
        var staff = await _departmentHeadService.GetStaffInDepartmentAsync(departmentId, ct);
        var rooms = await _departmentHeadService.GetRoomsInDepartmentAsync(departmentId, ct);

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
            AvailableDoctors = doctors,
            AvailableStaff = staff,
            AvailableRooms = rooms
        };

        return View(vm);
    }

    /// <summary>
    /// Phê duyệt đề nghị nghỉ phép hoặc đổi ca trực của bác sĩ thuộc khoa phòng.
    /// </summary>
    /// <param name="id">Mã ngoại lệ.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
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

    /// <summary>
    /// Từ chối đề nghị nghỉ phép hoặc đổi ca trực của bác sĩ kèm lý do.
    /// </summary>
    /// <param name="id">Mã ngoại lệ.</param>
    /// <param name="reason">Lý do từ chối.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
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

    /// <summary>
    /// Phân công ca trực đêm cho nhân sự y tế trong khoa.
    /// </summary>
    /// <param name="accountId">Mã tài khoản nhân sự.</param>
    /// <param name="startTime">Thời gian bắt đầu ca trực.</param>
    /// <param name="endTime">Thời gian kết thúc ca trực.</param>
    /// <param name="note">Ghi chú phân công.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ActionName("AssignDuty")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignDuty(int accountId, DateTime startTime, DateTime endTime, string? note, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        // Kiểm tra nhân sự có thuộc khoa quản lý không
        var allowedStaff = await _departmentHeadService.GetStaffInDepartmentAsync(departmentId, ct);
        if (!allowedStaff.Any(s => s.Id == accountId))
        {
            TempData["ErrorMessage"] = "Bạn chỉ có thể phân công ca trực cho nhân sự thuộc khoa của mình.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        var now = _timeProvider.GetLocalNow().DateTime;
        if (startTime < now)
        {
            TempData["ErrorMessage"] = "Không thể phân công ca trực trong quá khứ. Vui lòng chọn thời gian từ hiện tại hoặc tương lai.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
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

        return RedirectToAction(nameof(Index), new { tab = "schedule" });
    }

    /// <summary>
    /// Thiết lập hoặc cập nhật lịch khám định kỳ hàng tuần cho bác sĩ và cấu hình hạn mức online.
    /// </summary>
    /// <param name="scheduleId">Mã lịch khám nếu cập nhật.</param>
    /// <param name="doctorId">Mã bác sĩ.</param>
    /// <param name="roomId">Mã phòng khám.</param>
    /// <param name="dayOfWeek">Thứ trong tuần.</param>
    /// <param name="shift">Ca khám (Sáng/Chiều).</param>
    /// <param name="startTime">Giờ bắt đầu.</param>
    /// <param name="endTime">Giờ kết thúc.</param>
    /// <param name="slotDurationMinutes">Thời lượng mỗi slot.</param>
    /// <param name="maxPatients">Tổng công suất bệnh nhân.</param>
    /// <param name="onlineSlots">Số lượng suất phân bổ online.</param>
    /// <param name="effectiveFrom">Ngày bắt đầu áp dụng.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ActionName("UpsertSchedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpsertSchedule(
        int? scheduleId,
        int doctorId,
        int roomId,
        DayOfWeek dayOfWeek,
        ExamShift shift,
        TimeOnly startTime,
        TimeOnly endTime,
        int slotDurationMinutes,
        int maxPatients,
        int onlineSlots,
        DateOnly? effectiveFrom,
        CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var effectiveDate = effectiveFrom ?? today;
        if (effectiveDate < today)
        {
            TempData["ErrorMessage"] = "Không thể thiết lập lịch khám cho ngày trong quá khứ. Chỉ được phép thiết lập từ ngày hiện tại hoặc tương lai.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        // Kiểm tra bác sĩ có thuộc khoa quản lý không
        var allowedDoctors = await _departmentHeadService.GetDoctorsInDepartmentAsync(departmentId, ct);
        if (!allowedDoctors.Any(d => d.Id == doctorId))
        {
            TempData["ErrorMessage"] = "Bạn chỉ có thể thiết lập lịch cho bác sĩ thuộc khoa của mình.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        // Kiểm tra phòng khám có thuộc khoa quản lý không
        var allowedRooms = await _departmentHeadService.GetRoomsInDepartmentAsync(departmentId, ct);
        if (!allowedRooms.Any(r => r.Id == roomId))
        {
            TempData["ErrorMessage"] = "Bạn chỉ có thể phân công phòng khám thuộc khoa của mình.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        var request = new UpsertDoctorScheduleRequest(
            scheduleId,
            doctorId,
            roomId,
            dayOfWeek,
            shift,
            startTime,
            endTime,
            slotDurationMinutes <= 0 ? 15 : slotDurationMinutes,
            maxPatients <= 0 ? 40 : maxPatients,
            onlineSlots <= 0 ? 28 : onlineSlots,
            effectiveDate,
            null
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

        return RedirectToAction(nameof(Index), new { tab = "schedule" });
    }

    /// <summary>
    /// Xóa cấu hình lịch khám định kỳ của bác sĩ.
    /// </summary>
    /// <param name="scheduleId">Mã lịch khám.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSchedule(int scheduleId, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        var result = await _departmentHeadService.DeleteDoctorScheduleAsync(scheduleId, departmentId, ct);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã xóa lịch khám thành công.";
        }

        return RedirectToAction(nameof(Index), new { tab = "schedule" });
    }

    /// <summary>
    /// Xóa ca trực đêm đã phân công cho nhân sự.
    /// </summary>
    /// <param name="id">Mã ca trực.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [Authorize(Policy = "DepartmentHeadOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDutyShift(int id, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);
        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
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

        return RedirectToAction(nameof(Index), new { tab = "schedule" });
    }

    private int GetCurrentAccountId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}

