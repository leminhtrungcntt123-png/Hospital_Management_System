using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

public class HomeController : Controller
{
    // Mặc định chuyển hướng thẳng vào màn hình Admin Accounts (Staff/Department/Doctor)
    public IActionResult Index() => RedirectToAction("Index", "Accounts", new { area = "Admin" });
}
