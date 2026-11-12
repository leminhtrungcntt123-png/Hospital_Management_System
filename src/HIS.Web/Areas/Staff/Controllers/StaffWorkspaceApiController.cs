using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "NurseOnly")]
[Route("api/staff/workspace/[action]")]
[ApiController]
public class StaffWorkspaceApiController(
    IVitalSignsService vitalSigns,
    INurseDashboardService dashboard,
    ILogger<StaffWorkspaceApiController> logger) : ControllerBase
{
    private int AccountId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public async Task<IActionResult> VitalsQueue([FromQuery] bool includeRecorded = true, CancellationToken ct = default)
    {
        var result = await vitalSigns.GetQueueAsync(AccountId, includeRecorded, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { message = result.Error.Message });
        }
        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> VitalsDetail([FromQuery] int appointmentId, CancellationToken ct = default)
    {
        var result = await vitalSigns.GetDetailAsync(AccountId, appointmentId, ct);
        if (!result.IsSuccess)
        {
            return NotFound(new { message = result.Error.Message });
        }
        return Ok(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVitals([FromBody] RecordVitalSignsRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await vitalSigns.RecordOrUpdateAsync(AccountId, request, ct);
        if (!result.IsSuccess)
        {
            logger.LogWarning("SaveVitals failed for account {AccountId}, appointment {AppId}: {Err}",
                AccountId, request.AppointmentId, result.Error.Message);
            return BadRequest(new { message = result.Error.Message });
        }

        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard(CancellationToken ct = default)
    {
        var result = await dashboard.GetDashboardSummaryAsync(AccountId, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { message = result.Error.Message });
        }
        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> Patients([FromQuery] string? keyword, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var result = await dashboard.GetPatientsAsync(AccountId, keyword, page, 50, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { message = result.Error.Message });
        }
        return Ok(result.Value);
    }
}
