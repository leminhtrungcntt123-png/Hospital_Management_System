using System.Security.Claims;
using HIS.BLL.Services;
using HIS.DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HIS.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = "PatientOnly")]
public class MedicalRecordsController : Controller
{
    private readonly IBillingService _billingService;
    private readonly IPatientAccessService _patientAccessService;
    private readonly AppDbContext _context;

    public MedicalRecordsController(
        IBillingService billingService,
        IPatientAccessService patientAccessService,
        AppDbContext context)
    {
        _billingService = billingService;
        _patientAccessService = patientAccessService;
        _context = context;
    }

    private int CurrentAccountId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> BillingHistory(int? patientId, CancellationToken ct)
    {
        int targetPatientId = 0;
        if (patientId.HasValue && patientId.Value > 0)
        {
            if (!await _patientAccessService.CanViewRecordsAsync(CurrentAccountId, patientId.Value, ct))
            {
                return Forbid();
            }

            targetPatientId = patientId.Value;
        }
        else
        {
            var primaryLink = await _context.AccountPatientProfiles
                .AsNoTracking()
                .Where(l => l.AccountId == CurrentAccountId && l.Status == Domain.LinkStatus.Active && l.CanViewRecords)
                .OrderByDescending(l => l.Relationship == Domain.PatientRelationship.Self)
                .ThenBy(l => l.PatientId)
                .FirstOrDefaultAsync(ct);
            if (primaryLink != null)
            {
                targetPatientId = primaryLink.PatientId;
            }
        }

        if (targetPatientId <= 0)
        {
            return Ok(new List<object>());
        }

        var history = await _billingService.GetPatientBillingHistoryAsync(targetPatientId, ct);
        return Ok(history);
    }
}
