using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

public class HomeController : Controller
{
    // Mặc định chuyển hướng thẳng vào màn hình Admin Accounts (Staff/Department/Doctor)
    public IActionResult Index() => RedirectToAction("Index", "Accounts", new { area = "Admin" });

    [HttpGet]
    [ActionName("NotFound")]
    [Route("404")]
    [Route("NotFound")]
    [Route("Home/NotFound")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult NotFoundPage(int? statusCode = null)
    {
        Response.StatusCode = statusCode ?? StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}
