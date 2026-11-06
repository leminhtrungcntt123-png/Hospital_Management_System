using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using HIS.Web.Models;
using HIS.Web.Workers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HIS.Web.Controllers;

public class AccountController : Controller
{
    private const string RegistrationMessage = "Yêu cầu đăng ký đã được xử lý. Vui lòng đăng nhập hoặc khôi phục mật khẩu nếu bạn đã có tài khoản.";

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
        if (loggedOut != 1 && User.Identity?.IsAuthenticated == true && IsAllowedReturnUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    // Đăng nhập nhanh cho Dev / Test (Bỏ qua mật khẩu)
    // Ví dụ truy cập: /Account/DevLogin?role=ADMIN hoặc /Account/DevLogin?role=DOCTOR
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> DevLogin([FromServices] AppDbContext db, string role = "ADMIN")
    {
        var targetRole = role.ToUpperInvariant() switch
        {
            "DOCTOR" or "DOC" => "DOCTOR",
            "NURSE" => "NURSE",
            "DEPT_HEAD" or "DEPARTMENT_HEAD" => "DEPARTMENT_HEAD",
            "PATIENT" => "PATIENT",
            _ => "ADMIN"
        };

        int accountId = 1;
        string username = targetRole.ToLower() + "_dev";
        string fullName = "Test " + targetRole;
        string? doctorId = null;

        if (targetRole == "ADMIN")
        {
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "admin");
            if (acc != null)
            {
                accountId = acc.Id;
                username = acc.Username;
                fullName = "Quản trị viên";
            }
        }
        else if (targetRole == "DEPARTMENT_HEAD")
        {
            var head = await db.DepartmentHeads
                .Include(dh => dh.Account)
                .Include(dh => dh.Department)
                .FirstOrDefaultAsync();

            if (head != null)
            {
                accountId = head.AccountId;
                username = head.Account?.Username ?? "head_noi";
                fullName = head.FullName;
            }
            else
            {
                var acc = await db.Accounts.FirstOrDefaultAsync(a => a.RoleId == (int)RoleId.DepartmentHead);
                if (acc != null)
                {
                    accountId = acc.Id;
                    username = acc.Username;
                    fullName = "Trưởng khoa";
                }
            }
        }
        else if (targetRole == "DOCTOR")
        {
            var doc = await db.Doctors
                .Include(d => d.Account)
                .Include(d => d.Department)
                .FirstOrDefaultAsync(d => d.AccountId > 0 && d.Account.RoleId == (int)RoleId.Doctor)
                ?? await db.Doctors.Include(d => d.Account).FirstOrDefaultAsync();

            if (doc != null)
            {
                accountId = doc.AccountId;
                username = doc.Account?.Username ?? "bs_nam";
                fullName = doc.FullName;
                doctorId = doc.Id.ToString();
            }
            else
            {
                var acc = await db.Accounts.FirstOrDefaultAsync(a => a.RoleId == (int)RoleId.Doctor);
                if (acc != null)
                {
                    accountId = acc.Id;
                    username = acc.Username;
                    fullName = "Bác sĩ";
                }
            }
        }
        else if (targetRole == "NURSE")
        {
            var nurse = await db.Nurses
                .Include(n => n.Account)
                .FirstOrDefaultAsync();

            if (nurse != null)
            {
                accountId = nurse.AccountId;
                username = nurse.Account?.Username ?? "nurse_lan";
                fullName = nurse.FullName;
            }
            else
            {
                var acc = await db.Accounts.FirstOrDefaultAsync(a => a.RoleId == (int)RoleId.Nurse);
                if (acc != null)
                {
                    accountId = acc.Id;
                    username = acc.Username;
                    fullName = "Điều dưỡng";
                }
            }
        }
        else if (targetRole == "PATIENT")
        {
            var patientAcc = await db.Accounts.FirstOrDefaultAsync(a => a.RoleId == (int)RoleId.Patient);
            if (patientAcc != null)
            {
                accountId = patientAcc.Id;
                username = patientAcc.Username;
                fullName = "Người bệnh";
            }
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accountId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, targetRole),
            new("FullName", fullName)
        };

        if (!string.IsNullOrEmpty(doctorId))
        {
            claims.Add(new Claim("DoctorId", doctorId));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

        return RedirectToDashboard(targetRole);
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

        if (IsAllowedReturnUrl(model.ReturnUrl))
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
    [EnableRateLimiting("register")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RegisterApi([FromBody] RegisterPatientRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null)
        {
            return Json(new { success = false, message = "Dữ liệu đăng ký không hợp lệ." });
        }

        var result = await _authService.RegisterPatientAsync(request, ct);
        if (!result.IsSuccess && result.Error.Code != "AUTH_REGISTRATION_CONFLICT")
        {
            return Json(new { success = false, message = result.Error.Message });
        }

        return Json(new
        {
            success = true,
            message = RegistrationMessage
        });
    }

    [HttpPost]
    [EnableRateLimiting("register")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
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
        ), ct);

        if (!result.IsSuccess && result.Error.Code != "AUTH_REGISTRATION_CONFLICT")
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = RegistrationMessage;
        return RedirectToAction("Login", "Account", new { mode = "login" });
    }

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

    [HttpGet]
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

    private bool IsAllowedReturnUrl([NotNullWhen(true)] string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl)) return false;
        var path = Url.Content(returnUrl);
        return path != "/"
            && !path.StartsWith(Url.Content("~/Account/Login"), StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith(Url.Content("~/Account/Logout"), StringComparison.OrdinalIgnoreCase);
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
