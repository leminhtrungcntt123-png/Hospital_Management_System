using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = "PatientOnly")]
public class MedicalRecordsController : Controller
{
    public IActionResult Index() => View();
}
