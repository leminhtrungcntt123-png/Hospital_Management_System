using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "NurseOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReceptionController : Controller
{
    private readonly IReceptionService _reception;
    private readonly ILogger<ReceptionController> _logger;

    public ReceptionController(IReceptionService reception, ILogger<ReceptionController> logger)
    {
        _reception = reception;
        _logger = logger;
    }

    private int AccountId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    /// <summary>
    /// Hiển thị màn hình bàn tiếp đón bệnh nhân và danh sách lượt khám trong ngày.
    /// </summary>
    /// <param name="search">Từ khóa tìm kiếm.</param>
    /// <param name="tab">Tab đang hoạt động (web-visits, walk-in, reception-queue).</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? tab, CancellationToken ct = default) 
        => await DeskAsync(search, new(), tab ?? "web-visits", ct);

    /// <summary>
    /// Tra cứu hồ sơ bệnh nhân theo số CCCD (AJAX).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> FindPatientByCccd(string? cccd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cccd))
        {
            return Json(new { success = false, message = "Vui lòng nhập số CCCD cần tìm." });
        }

        var result = await _reception.FindPatientByNationalIdAsync(cccd.Trim(), ct);
        if (!result.IsSuccess)
        {
            return Json(new { success = false, message = result.Error.Message });
        }

        return Json(new
        {
            success = true,
            found = result.Value != null,
            patient = result.Value,
            message = result.Value != null ? "Tìm thấy hồ sơ" : "Số CCCD này hiện chưa có hồ sơ trong hệ thống"
        });
    }

    /// <summary>
    /// Tìm kiếm bệnh nhân theo từ khóa tên, SĐT hoặc CCCD (AJAX).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchPatients(string? q, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Json(new { success = true, items = Array.Empty<object>() });
        }

        var result = await _reception.SearchPatientsAsync(q.Trim(), ct);
        return Json(new { success = result.IsSuccess, items = result.Value ?? [] });
    }

    /// <summary>
    /// Lấy danh sách bác sĩ trực ca hôm nay thuộc chuyên khoa kèm số lượng chờ khám (AJAX).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDepartmentDoctors(int departmentId, CancellationToken ct = default)
    {
        if (departmentId <= 0)
        {
            return Json(new { success = false, message = "Chuyên khoa không hợp lệ." });
        }

        var result = await _reception.GetDepartmentSchedulesWithLoadAsync(departmentId, null, ct);
        if (!result.IsSuccess)
        {
            return Json(new { success = false, message = result.Error.Message });
        }

        return Json(new { success = true, doctors = result.Value ?? [] });
    }

    /// <summary>
    /// Lấy dữ liệu phiếu khám theo STT hoặc mã lịch hẹn (AJAX).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTicket(string? queueNumber, int? appointmentId, CancellationToken ct = default)
    {
        var result = await _reception.GetWalkInTicketAsync(queueNumber ?? "", appointmentId, ct);
        if (!result.IsSuccess || result.Value == null)
        {
            return Json(new { success = false, message = "Không tìm thấy thông tin phiếu khám." });
        }

        return Json(new { success = true, ticket = result.Value });
    }

    /// <summary>
    /// Tiếp nhận (check-in) cho lượt khám đặt trước sau khi đối chiếu danh tính.
    /// </summary>
    /// <param name="appointmentId">Mã lượt khám.</param>
    /// <param name="identityVerified">Cờ xác nhận danh tính.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(
        int appointmentId,
        bool identityVerified,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _reception.CheckInAsync(AccountId, appointmentId, identityVerified, ct);
            TempData[result.IsSuccess ? "ReceptionSuccess" : "ReceptionError"] = result.IsSuccess
                ? $"Đã tiếp nhận. Số thứ tự: {result.Value}."
                : result.Error.Message;
        }
        catch (Exception e) when (e is DbUpdateException or PostgresException)
        {
            _logger.LogWarning("Reception write rejected: {Type}", e.GetType().Name);
            TempData["ReceptionError"] = "Chưa lưu được do dữ liệu vừa thay đổi. Hãy tải lại danh sách và kiểm tra trước khi thử lại.";
        }

        return RedirectToAction(nameof(Index), new { tab = "web-visits" });
    }

    /// <summary>
    /// Đăng ký và tiếp nhận người bệnh vãng lai trực tiếp tại quầy và cấp STT khám ngay.
    /// </summary>
    /// <param name="request">Thông tin đăng ký vãng lai.</param>
    /// <param name="search">Từ khóa tìm kiếm hiện tại.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WalkIn(
        [Bind(Prefix = "WalkIn")] WalkInReceptionRequest request,
        string? search,
        CancellationToken ct = default)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (Request.ContentType != null && Request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase));

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = string.Join(" ", errors) });
            }
            return await DeskAsync(search, request, "walk-in", ct);
        }

        try
        {
            var result = await _reception.RegisterWalkInAsync(AccountId, request, ct);

            if (!result.IsSuccess)
            {
                if (isAjax)
                {
                    return Json(new { success = false, message = result.Error.Message });
                }
                ModelState.AddModelError(string.Empty, result.Error.Message);
                return await DeskAsync(search, request, "walk-in", ct);
            }

            var ticketRes = await _reception.GetWalkInTicketAsync(result.Value!, null, ct);

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    queueNumber = result.Value,
                    ticket = ticketRes.Value,
                    message = $"Đã tiếp nhận. Số thứ tự: {result.Value}."
                });
            }

            TempData["ReceptionSuccess"] = $"Đã đăng ký và tiếp nhận khách vãng lai. Số thứ tự: {result.Value}.";
            if (ticketRes.Value != null)
            {
                TempData["WalkInTicketJson"] = System.Text.Json.JsonSerializer.Serialize(ticketRes.Value);
            }

            return RedirectToAction(nameof(Index), new { tab = "walk-in" });
        }
        catch (Exception e) when (e is DbUpdateException or PostgresException)
        {
            _logger.LogWarning("Walk-in write rejected: {Type}", e.GetType().Name);
            var err = "Chưa lưu được do dữ liệu vừa thay đổi. Hãy kiểm tra danh sách trước khi gửi lại.";
            if (isAjax)
            {
                return Json(new { success = false, message = err });
            }
            ModelState.AddModelError(string.Empty, err);
            return await DeskAsync(search, request, "walk-in", ct);
        }
    }

    private async Task<IActionResult> DeskAsync(
        string? search,
        WalkInReceptionRequest request,
        string activeTab = "web-visits",
        CancellationToken ct = default)
    {
        var result = await _reception.GetDeskAsync(AccountId, search, ct);

        if (!result.IsSuccess)
        {
            return Forbid();
        }

        WalkInTicketDto? ticket = null;
        if (TempData["WalkInTicketJson"] is string json && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                ticket = System.Text.Json.JsonSerializer.Deserialize<WalkInTicketDto>(json);
            }
            catch
            {
                // Ignore deserialize failure
            }
        }

        return View("Index", new ReceptionViewModel
        {
            Desk = result.Value!,
            WalkIn = request,
            Search = search,
            ActiveTab = activeTab,
            Ticket = ticket
        });
    }
}

