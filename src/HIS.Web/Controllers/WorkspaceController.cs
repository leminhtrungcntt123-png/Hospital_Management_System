using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

[Authorize]
[Route("Workspace/staff/{page}")]
public sealed class WorkspaceController : Controller
{
    [HttpGet]
    public IActionResult Index(string page)
    {
        string[] allowedRoles = page switch
        {
            "dashboard" => ["NURSE", "DEPARTMENT_HEAD", "DEPT_HEAD", "ADMIN"],
            "vitals" => ["NURSE", "DOCTOR"],
            "patients" => ["NURSE", "DOCTOR", "DEPARTMENT_HEAD", "DEPT_HEAD", "ADMIN"],
            "labs" => ["DOCTOR", "DEPARTMENT_HEAD", "DEPT_HEAD"],
            "records" => ["DOCTOR", "DEPARTMENT_HEAD", "DEPT_HEAD"],
            "exceptions" => ["DOCTOR", "DEPARTMENT_HEAD", "DEPT_HEAD"],
            _ => []
        };

        if (allowedRoles.Length == 0)
        {
            return NotFound();
        }

        if (!allowedRoles.Any(User.IsInRole))
        {
            return Forbid();
        }

        ViewData["MediCareRoute"] = "staff/" + page;
        return View("~/Views/Shared/MediCarePage.cshtml");
    }
}

