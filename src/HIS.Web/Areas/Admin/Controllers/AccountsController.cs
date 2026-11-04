using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountsController : Controller
{
    // Màn hình quản lý tài khoản / nhân sự (Staff)
    [HttpGet]
    public IActionResult Index() => View();
}
