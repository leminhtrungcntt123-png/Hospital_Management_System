using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

public class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
