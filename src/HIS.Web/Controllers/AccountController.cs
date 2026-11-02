using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

public class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login() => View();

    [HttpPost]
    public IActionResult Login(string username, string password) => View();

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    public IActionResult Register(string username, string password) => View();

    public IActionResult Logout() => RedirectToAction("Index", "Home");

    public IActionResult AccessDenied() => View();

    [HttpGet]
    public IActionResult ChangePassword() => View();

    [HttpPost]
    public IActionResult ChangePassword(string oldPassword, string newPassword) => View();
}
