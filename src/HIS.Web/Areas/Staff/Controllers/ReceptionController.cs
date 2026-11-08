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
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken ct = default) => await DeskAsync(search, new(), ct);

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

        return RedirectToAction(nameof(Index));
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
        if (!ModelState.IsValid)
        {
            return await DeskAsync(search, request, ct);
        }

        try
        {
            var result = await _reception.RegisterWalkInAsync(AccountId, request, ct);

            if (!result.IsSuccess)
            {
                ModelState.AddModelError(string.Empty, result.Error.Message);
                return await DeskAsync(search, request, ct);
            }

            TempData["ReceptionSuccess"] = $"Đã đăng ký và tiếp nhận khách vãng lai. Số thứ tự: {result.Value}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception e) when (e is DbUpdateException or PostgresException)
        {
            _logger.LogWarning("Walk-in write rejected: {Type}", e.GetType().Name);
            ModelState.AddModelError(string.Empty, "Chưa lưu được do dữ liệu vừa thay đổi. Hãy kiểm tra danh sách trước khi gửi lại.");
            return await DeskAsync(search, request, ct);
        }
    }

    private async Task<IActionResult> DeskAsync(
        string? search,
        WalkInReceptionRequest request,
        CancellationToken ct = default)
    {
        var result = await _reception.GetDeskAsync(AccountId, search, ct);

        if (!result.IsSuccess)
        {
            return Forbid();
        }

        return View("Index", new ReceptionViewModel { Desk = result.Value!, WalkIn = request, Search = search });
    }
}

