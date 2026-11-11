using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Patient.Controllers;

[Area("Patient")]
public class FamilyProfilesController(IPatientProfileService profileService) : Controller
{
    [Authorize(Policy = "PatientOnly")]
    public IActionResult Index() => View();

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Profiles(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Json(new { authenticated = false, selfProfile = (object?)null, relatives = Array.Empty<object>() });
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) || accountId <= 0)
        {
            return Json(new { authenticated = false, selfProfile = (object?)null, relatives = Array.Empty<object>() });
        }

        var result = await profileService.GetAccountProfilesAsync(accountId, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { message = result.Error.Message });
        }

        var data = result.Value!;
        return Json(new
        {
            authenticated = true,
            accountId = data.AccountId,
            username = data.Username,
            accountEmail = data.AccountEmail,
            selfProfile = data.SelfProfile,
            relatives = data.Relatives
        });
    }

    [HttpPost]
    [Authorize(Policy = "PatientOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePersonal([FromBody] SavePersonalProfileRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) || accountId <= 0)
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập lại." });
        }

        var result = await profileService.SavePersonalProfileAsync(accountId, request, ct);
        return result.IsSuccess
            ? Ok(new { success = true, message = "Đã cập nhật thông tin cá nhân thành công." })
            : BadRequest(new { message = result.Error.Message });
    }

    [HttpPost]
    [Authorize(Policy = "PatientOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRelative([FromBody] AddRelativeProfileRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) || accountId <= 0)
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập lại." });
        }

        var result = await profileService.AddRelativeProfileAsync(accountId, request, ct);
        return result.IsSuccess
            ? Ok(new { success = true, patientId = result.Value, message = "Đã thêm thông tin người thân thành công." })
            : BadRequest(new { message = result.Error.Message });
    }
}
