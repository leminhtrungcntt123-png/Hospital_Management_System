using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = "PatientOnly")]
public class MyAppointmentsController(IPatientPortalService portalService) : Controller
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

        var result = await portalService.GetAppointmentsAsync(accountId, ct);
        return result.IsSuccess
            ? Json(new { items = result.Value })
            : BadRequest(new { message = result.Error.Message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromBody] CancelAppointmentRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) || accountId <= 0)
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập lại." });
        }

        var result = await portalService.CancelAppointmentAsync(accountId, request.AppointmentId, ct);
        return result.IsSuccess
            ? Ok(new { success = true, message = "Đã hủy lịch hẹn thành công." })
            : BadRequest(new { message = result.Error.Message });
    }
}
