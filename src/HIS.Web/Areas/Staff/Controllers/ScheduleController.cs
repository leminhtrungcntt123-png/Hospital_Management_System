using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "DepartmentHeadOnly")]
public class ScheduleController : Controller
{
    public IActionResult Index() => View();
}
