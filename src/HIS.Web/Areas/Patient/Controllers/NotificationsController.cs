using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = "PatientOnly")]
public class NotificationsController(IPatientPortalService portalService) : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) || accountId <= 0)
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập lại." });
        }

        var result = await portalService.GetNotificationsAsync(accountId, ct);
        return result.IsSuccess
            ? Json(new { unreadCount = result.Value!.UnreadCount, items = result.Value.Items })
            : BadRequest(new { message = result.Error.Message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] MarkNotificationReadRequest? request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) || accountId <= 0)
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập lại." });
        }

        var result = await portalService.MarkNotificationsReadAsync(accountId, request?.NotificationId, ct);
        return result.IsSuccess
            ? Ok(new { success = true })
            : BadRequest(new { message = result.Error.Message });
    }
}
