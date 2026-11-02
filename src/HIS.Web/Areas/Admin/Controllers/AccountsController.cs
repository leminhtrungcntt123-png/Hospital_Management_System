using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public class AccountsController : Controller
{
    public IActionResult Index() => View();

    [HttpPost]
    public IActionResult ResetPassword(int accountId, string newPassword) => View();
}
