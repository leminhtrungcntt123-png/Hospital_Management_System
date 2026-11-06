using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HIS.Web.Controllers;

// Trang đăng nhập của cổng đặt lịch: xác thực qua IAuthService rồi phát cookie Claims
// (Id, tên đăng nhập, vai trò, tên hiển thị) để các trang hồ sơ/đặt lịch biết đang làm việc với ai.
// Cùng màn này có đăng ký tài khoản người khám và khôi phục mật khẩu bằng OTP qua email.
public class AccountController : Controller
{
    private const string FullNameClaim = "FullName";

    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null, int? loggedOut = null)
    {
        if (loggedOut != 1 && User.Identity?.IsAuthenticated == true)
        {
            return IsSafeReturnUrl(returnUrl)
                ? Redirect(returnUrl!)
                : RedirectToAction("Index", "Booking");
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

        // Đăng nhập ở cổng PatientPortal: AuthService tự chặn vai trò không phải bệnh nhân ở cổng này.
        LoginResult result = await _authService.LoginAsync(
            model.Username!, model.Password!, LoginSurface.PatientPortal);

        if (!result.Succeeded || result.User is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Đăng nhập không thành công.");
            return View(model);
        }

        AuthUserDto user = result.User;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.AccountId.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.RoleCode),
            new(FullNameClaim, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName)
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        // Chỉ quay về đường dẫn nội bộ để không biến trang đăng nhập thành cổng chuyển hướng ra ngoài.
        return IsSafeReturnUrl(model.ReturnUrl)
            ? Redirect(model.ReturnUrl!)
            : RedirectToAction("Index", "Booking");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int accountId) && accountId > 0)
        {
            await _authService.LogoutAsync(accountId);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Xóa cả cookie cũ còn sót của phiên trước để lần sau mở web không bị đăng nhập nhầm tài khoản.
        foreach (string cookie in Request.Cookies.Keys)
        {
            if (cookie.StartsWith(".AspNetCore", StringComparison.OrdinalIgnoreCase))
            {
                Response.Cookies.Delete(cookie);
                Response.Cookies.Delete(cookie, new CookieOptions { Path = "/" });
            }
        }

        return RedirectToAction(nameof(Login), new { loggedOut = 1 });
    }

    [HttpGet]
    public IActionResult Register() => RedirectToAction(nameof(Login), new { mode = "register" });

    [HttpGet]
    public IActionResult ForgotPassword() => RedirectToAction(nameof(Login), new { mode = "forgot" });

    [HttpGet]
    public IActionResult ResetPassword() => RedirectToAction(nameof(Login), new { mode = "forgot" });

    // API cho tab Đăng ký trên màn hình Login (fetch JSON).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterApi([FromBody] RegisterPatientRequest request)
    {
        if (request == null)
        {
            return Json(new { success = false, message = "Dữ liệu đăng ký không hợp lệ." });
        }

        RegisterResult result = await _authService.RegisterPatientAsync(request);
        if (!result.Succeeded)
        {
            return Json(new { success = false, message = result.ErrorMessage });
        }

        return Json(new
        {
            success = true,
            username = result.Username,
            message = "Đăng ký tài khoản thành công! Vui lòng đăng nhập và tạo hồ sơ khám bệnh."
        });
    }

    // Bước 1 của Quên mật khẩu: gửi OTP tới email đã đăng ký.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPasswordReset([FromBody] ForgotPasswordRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EmailOrUsername))
        {
            return Json(new { success = false, message = "Vui lòng nhập email hoặc tên đăng nhập." });
        }

        ForgotPasswordResult result = await _authService.SendPasswordResetOtpAsync(request.EmailOrUsername);
        if (!result.Succeeded)
        {
            return Json(new { success = false, message = result.ErrorMessage });
        }

        return Json(new
        {
            success = true,
            message = "Mã xác thực OTP đã được gửi đến email của bạn.",
            maskedEmail = result.MaskedEmail,
            demoOtp = result.DemoOtp
        });
    }

    // Bước 2: đối chiếu OTP và đặt mật khẩu mới.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] ResetPasswordRequest request)
    {
        if (request == null
            || string.IsNullOrWhiteSpace(request.EmailOrUsername)
            || string.IsNullOrWhiteSpace(request.Otp)
            || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin yêu cầu." });
        }

        PasswordResetResult result = await _authService.ResetPasswordWithOtpAsync(
            request.EmailOrUsername, request.Otp, request.NewPassword);

        if (!result.Succeeded)
        {
            return Json(new { success = false, message = result.ErrorMessage });
        }

        return Json(new
        {
            success = true,
            message = "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập ngay với mật khẩu mới."
        });
    }

    private bool IsSafeReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl)
           && Url.IsLocalUrl(returnUrl)
           && returnUrl != "/"
           && !returnUrl.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase);
}
