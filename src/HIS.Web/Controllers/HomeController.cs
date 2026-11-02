using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
