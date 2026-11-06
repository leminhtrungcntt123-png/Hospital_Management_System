using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Common;
using HIS.BLL.Services;
using HIS.Web.Models;
using HIS.Web.Workers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace HIS.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly ILogger<AccountController> _logger;
    private readonly PasswordResetQueue _passwordResets;

    public AccountController(IAuthService authService, ILogger<AccountController> logger, PasswordResetQueue passwordResets)
    {
        _authService = authService;
        _logger = logger;
        _passwordResets = passwordResets;
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

        Result<AuthResponse> result;
        try
        {
            result = await _authService.LoginAsync(new LoginRequest(model.Username, model.Password), HttpContext.RequestAborted);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogError("Login failed: {Type}", error.GetType().Name);
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError(string.Empty, "Chưa kết nối được hệ thống đăng nhập. Vui lòng thử lại sau.");
            return View(model);
        }
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

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Login", "Account", new { loggedOut = 1 });
    }

    [HttpGet]
    public IActionResult Register() => RedirectToAction("Login", "Account", new { mode = "register" });

    [HttpPost]
    public async Task<IActionResult> RegisterApi([FromBody] RegisterPatientRequest request)
    {
        if (!ModelState.IsValid || request == null)
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
    [EnableRateLimiting("password-reset-request")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult RequestPasswordReset([FromBody] ForgotPasswordRequest request)
    {
        if (request == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(request.EmailOrUsername)
            || request.EmailOrUsername.Length > 100)
        {
            return Json(new { success = false, message = "Vui lòng nhập email hoặc tên đăng nhập." });
        }

        // Queue every valid identifier so account lookup and SMTP timing cannot identify an account.
        _passwordResets.Enqueue(request.EmailOrUsername);

        return Json(new
        {
            success = true,
            message = "Nếu thông tin tài khoản phù hợp, mã xác thực sẽ được gửi đến email đã đăng ký. Vui lòng kiểm tra hộp thư."
        });
    }

    [HttpPost]
    [EnableRateLimiting("password-reset-confirm")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        if (request == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(request.EmailOrUsername) ||
            string.IsNullOrWhiteSpace(request.Otp) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin yêu cầu." });
        }

        var result = await _authService.ResetPasswordWithOtpAsync(request.EmailOrUsername, request.Otp, request.NewPassword, ct);
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
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        var accountId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.ChangePasswordAsync(accountId, model.OldPassword, model.NewPassword, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return View(model);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["SuccessMessage"] = "Đã đổi mật khẩu. Vui lòng đăng nhập lại.";
        return RedirectToAction(nameof(Login));
    }

    private IActionResult RedirectToDashboard(string? role)
    {
        return (role?.Trim().ToUpperInvariant()) switch
        {
            "ADMIN" => RedirectToAction("Index", "Management", new { area = "Admin" }),
            "DEPARTMENT_HEAD" or "DEPT_HEAD" => RedirectToAction("Index", "Schedule", new { area = "Staff" }),
            "DOCTOR" => RedirectToAction("Index", "Consultation", new { area = "Staff" }),
            "NURSE" => RedirectToAction("Index", "Reception", new { area = "Staff" }),
            "CASHIER" => RedirectToAction("Index", "Cashier", new { area = "Staff" }),
            "PATIENT" => RedirectToAction("Index", "Booking", new { area = "Patient" }),
            _ => RedirectToAction("Index", "Home")
        };
    }
}
