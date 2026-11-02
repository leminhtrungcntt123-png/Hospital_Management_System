using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "NurseOnly")]
public class ReceptionController : Controller
{
    public IActionResult Index() => View();
}
