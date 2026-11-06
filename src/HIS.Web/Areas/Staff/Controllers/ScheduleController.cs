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

    public ScheduleController(IDepartmentHeadService departmentHeadService)
    {
        _departmentHeadService = departmentHeadService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? tab, int weekOffset = 0, CancellationToken ct = default)
    {
        var accountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(accountId, ct);

        if (departmentId <= 0)
        {
            TempData["ErrorMessage"] = "Tài khoản của bạn chưa được liên kết với khoa chuyên môn nào. Vui lòng liên hệ Quản trị viên hệ thống.";
            return RedirectToAction("Index", "Consultation");
        }

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

        var overview = await _departmentHeadService.GetDepartmentOverviewAsync(departmentId, ct);
        var exceptions = await _departmentHeadService.GetExceptionsAsync(departmentId, null, ct);
        var schedules = await _departmentHeadService.GetSchedulesAsync(departmentId, ct);
        var dutyShifts = await _departmentHeadService.GetDutyShiftsAsync(departmentId, ct);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var medicalRecords = await _departmentHeadService.GetMedicalRecordsAsync(departmentId, accountId, ipAddress, ct);

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

    // Duyệt đề nghị nghỉ / đổi ca
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveException(int id, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(accountId, ct);
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectException(int id, string? reason, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(accountId, ct);
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
    [ActionName("AssignDuty")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignDuty(int accountId, DateTime startTime, DateTime endTime, string? note, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);

        // Kiểm tra nhân sự có thuộc khoa quản lý không
        var allowedStaff = await _departmentHeadService.GetStaffInDepartmentAsync(departmentId, ct);
        if (!allowedStaff.Any(s => s.Id == accountId))
        {
            TempData["ErrorMessage"] = "Bạn chỉ có thể phân công ca trực cho nhân sự thuộc khoa của mình.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        if (startTime < DateTime.Now)
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

    // Thiết lập / cập nhật lịch khám tuần & hạn mức
    [HttpPost]
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

        var today = DateOnly.FromDateTime(DateTime.Today);
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

    // Xóa / hủy cấu hình lịch khám
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSchedule(int scheduleId, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);

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

    // Xóa / hủy ca trực đêm
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDutyShift(int id, CancellationToken ct)
    {
        var currentAccountId = GetCurrentAccountId();
        var departmentId = await _departmentHeadService.ResolveDepartmentIdByAccountIdAsync(currentAccountId, ct);

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
