using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "DoctorOnly")]
public class ConsultationController : Controller
{
    public IActionResult Index() => View();
}
