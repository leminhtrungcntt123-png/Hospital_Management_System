using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HIS.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public IActionResult Dashboard()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction("Login", "Account");
        }
        return RedirectToDashboard(User.FindFirst(ClaimTypes.Role)?.Value);
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null, int? loggedOut = null)
    {
        if (loggedOut != 1 && User.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl != "/" && !returnUrl.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(returnUrl);
            }
            return RedirectToDashboard(User.FindFirst(ClaimTypes.Role)?.Value);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [EnableRateLimiting("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authService.LoginAsync(new LoginRequest(model.Username, model.Password));
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return View(model);
        }

        var auth = result.Value!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, auth.AccountId.ToString()),
            new(ClaimTypes.Name, auth.Username),
            new(ClaimTypes.Role, auth.Role),
            new("FullName", string.IsNullOrWhiteSpace(auth.FullName) ? auth.Username : auth.FullName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) && model.ReturnUrl != "/" && !model.ReturnUrl.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToDashboard(auth.Role);
    }

    [HttpGet, HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        Response.Cookies.Delete(".AspNetCore.Cookies");
        Response.Cookies.Delete(".AspNetCore.Cookies", new CookieOptions { Path = "/" });
        foreach (var cookie in Request.Cookies.Keys)
        {
            if (cookie.StartsWith(".AspNetCore", StringComparison.OrdinalIgnoreCase))
            {
                Response.Cookies.Delete(cookie);
                Response.Cookies.Delete(cookie, new CookieOptions { Path = "/" });
            }
        }

        return RedirectToAction("Login", "Account", new { loggedOut = 1 });
    }

    [HttpGet]
    public IActionResult Register() => RedirectToAction("Login", "Account", new { mode = "register" });

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RegisterApi([FromBody] RegisterPatientRequest request)
    {
        if (request == null)
        {
            return Json(new { success = false, message = "Dữ liệu đăng ký không hợp lệ." });
        }

        var result = await _authService.RegisterPatientAsync(request);
        if (!result.IsSuccess)
        {
            return Json(new { success = false, message = result.Error.Message });
        }

        return Json(new
        {
            success = true,
            username = result.Value!.Username,
            fullName = result.Value.FullName,
            message = "Đăng ký tài khoản người khám thành công!"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authService.RegisterPatientAsync(new RegisterPatientRequest(
            model.Username,
            model.Password,
            model.FullName,
            model.DateOfBirth,
            model.PhoneNumber,
            model.Email
        ));

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = "Đăng ký tài khoản thành công! Vui lòng đăng nhập.";
        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => RedirectToAction("Login", "Account", new { mode = "forgot" });

    [HttpGet]
    public IActionResult ResetPassword() => RedirectToAction("Login", "Account", new { mode = "forgot" });

    // API endpoints cho Interactive Modal tại màn hình Login
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RequestPasswordReset([FromBody] ForgotPasswordRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EmailOrUsername))
        {
            return Json(new { success = false, message = "Vui lòng nhập email hoặc tên đăng nhập." });
        }

        var result = await _authService.SendPasswordResetOtpAsync(request.EmailOrUsername);
        if (!result.IsSuccess)
        {
            return Json(new { success = false, message = result.Error.Message });
        }

        return Json(new
        {
            success = true,
            message = "Mã xác thực OTP đã được gửi đến email của bạn.",
            maskedEmail = result.Value!.MaskedEmail
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] ResetPasswordRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EmailOrUsername) ||
            string.IsNullOrWhiteSpace(request.Otp) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin yêu cầu." });
        }

        var result = await _authService.ResetPasswordWithOtpAsync(request.EmailOrUsername, request.Otp, request.NewPassword);
        if (!result.IsSuccess)
        {
            return Json(new { success = false, message = result.Error.Message });
        }

        return Json(new
        {
            success = true,
            message = "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập ngay với mật khẩu mới."
        });
    }

    public IActionResult AccessDenied() => View();

    [HttpGet]
    public IActionResult ChangePassword() => View();

    [HttpPost]
    public IActionResult ChangePassword(string oldPassword, string newPassword) => View();

    private IActionResult RedirectToDashboard(string? role)
    {
        return (role?.Trim().ToUpperInvariant()) switch
        {
            "ADMIN" => RedirectToAction("Index", "Accounts", new { area = "Admin" }),
            "DEPARTMENT_HEAD" or "DEPT_HEAD" => RedirectToAction("Index", "Schedule", new { area = "Staff" }),
            "DOCTOR" => RedirectToAction("Index", "Consultation", new { area = "Staff" }),
            "NURSE" => RedirectToAction("Index", "Reception", new { area = "Staff" }),
            "CASHIER" => RedirectToAction("Index", "Cashier", new { area = "Staff" }),
            "PATIENT" => RedirectToAction("Index", "Booking", new { area = "Patient" }),
            _ => RedirectToAction("Index", "Home")
        };
    }
}
