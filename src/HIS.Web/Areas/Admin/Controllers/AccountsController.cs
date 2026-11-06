using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public class AccountsController : Controller
{
    // Màn hình quản lý tài khoản / nhân sự (Staff)
    [HttpGet]
    public IActionResult Index() => View();
}
