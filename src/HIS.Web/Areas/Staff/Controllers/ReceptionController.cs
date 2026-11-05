using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "NurseOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReceptionController(IReceptionService reception, ILogger<ReceptionController> logger) : Controller
{
    private int AccountId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken ct) => await DeskAsync(search, new(), ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(int appointmentId, bool identityVerified, CancellationToken ct)
    {
        try
        {
            var result = await reception.CheckInAsync(AccountId, appointmentId, identityVerified, ct);
            TempData[result.IsSuccess ? "ReceptionSuccess" : "ReceptionError"] = result.IsSuccess
                ? $"Đã tiếp nhận. Số thứ tự: {result.Value}." : result.Error.Message;
        }
        catch (Exception e) when (e is DbUpdateException or PostgresException)
        {
            logger.LogWarning("Reception write rejected: {Type}", e.GetType().Name);
            TempData["ReceptionError"] = "Chưa lưu được do dữ liệu vừa thay đổi. Hãy tải lại danh sách và kiểm tra trước khi thử lại.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WalkIn([Bind(Prefix = "WalkIn")] WalkInReceptionRequest request, string? search, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await DeskAsync(search, request, ct);
        try
        {
            var result = await reception.RegisterWalkInAsync(AccountId, request, ct);
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
            logger.LogWarning("Walk-in write rejected: {Type}", e.GetType().Name);
            ModelState.AddModelError(string.Empty, "Chưa lưu được do dữ liệu vừa thay đổi. Hãy kiểm tra danh sách trước khi gửi lại.");
            return await DeskAsync(search, request, ct);
        }
    }

    private async Task<IActionResult> DeskAsync(string? search, WalkInReceptionRequest request, CancellationToken ct)
    {
        var result = await reception.GetDeskAsync(AccountId, search, ct);
        if (!result.IsSuccess) return Forbid();
        return View("Index", new ReceptionViewModel { Desk = result.Value!, WalkIn = request, Search = search });
    }
}
