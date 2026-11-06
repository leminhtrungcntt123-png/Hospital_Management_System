using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using HIS.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HIS.Web.Controllers;

// Trang đăng nhập của cổng đặt lịch: xác thực qua IAuthService rồi phát cookie Claims
// (Id, tên đăng nhập, vai trò, tên hiển thị) để các trang hồ sơ/đặt lịch biết đang làm việc với ai.
// Cùng màn này có đăng ký tài khoản người khám, khôi phục mật khẩu bằng OTP và đổi mật khẩu.
public class AccountController : Controller
{
    private const string FullNameClaim = "FullName";

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
            return RedirectToAction(nameof(Login));
        }

        return RedirectToDashboard(User.FindFirstValue(ClaimTypes.Role));
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

    // Đăng nhập nhanh cho Dev / Test (Bỏ qua mật khẩu)
    // Ví dụ truy cập: /Account/DevLogin?role=ADMIN hoặc /Account/DevLogin?role=DOCTOR hoặc /Account/DevLogin?role=PATIENT
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> DevLogin(
        [FromServices] AppDbContext db,
        [FromServices] IWebHostEnvironment env,
        string role = "PATIENT")
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        var targetRole = role.ToUpperInvariant() switch
        {
            "DOCTOR" or "DOC" => "DOCTOR",
            "NURSE" => "NURSE",
            "DEPT_HEAD" or "DEPARTMENT_HEAD" => "DEPT_HEAD",
            "ADMIN" => "ADMIN",
            _ => "PATIENT"
        };

        int accountId = 1;
        string username = targetRole.ToLowerInvariant() + "_dev";
        string fullName = "Test " + targetRole;
        string? doctorId = null;
        string? departmentName = null;

        var acc = await db.Accounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Role.Code == targetRole && a.IsActive);

        if (acc != null)
        {
            accountId = acc.Id;
            username = acc.Username;

            if (targetRole == "DOCTOR")
            {
                var doc = await db.Doctors.Include(d => d.Department).FirstOrDefaultAsync(d => d.AccountId == acc.Id);
                if (doc != null)
                {
                    fullName = doc.FullName;
                    doctorId = doc.Id.ToString();
                    departmentName = doc.Department?.Name;
                }
            }
            else if (targetRole == "NURSE")
            {
                var nurse = await db.Nurses.Include(n => n.Department).FirstOrDefaultAsync(n => n.AccountId == acc.Id);
                if (nurse != null)
                {
                    fullName = nurse.FullName;
                    departmentName = nurse.Department?.Name;
                }
            }
            else if (targetRole == "DEPT_HEAD")
            {
                var head = await db.DepartmentHeads.Include(dh => dh.Department).FirstOrDefaultAsync(dh => dh.AccountId == acc.Id);
                if (head != null)
                {
                    fullName = head.FullName;
                    departmentName = head.Department?.Name;
                }
            }
            else if (targetRole == "ADMIN")
            {
                fullName = "Quản trị viên";
            }
            else
            {
                fullName = await db.AccountPatientProfiles
                    .Where(p => p.AccountId == acc.Id && p.Relationship == PatientRelationship.Self)
                    .Select(p => p.Patient.FullName)
                    .FirstOrDefaultAsync() ?? acc.Username;
            }
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accountId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, targetRole),
            new(FullNameClaim, fullName)
        };

        if (!string.IsNullOrEmpty(doctorId))
        {
            claims.Add(new Claim("DoctorId", doctorId));
        }

        if (!string.IsNullOrEmpty(departmentName))
        {
            claims.Add(new Claim("DepartmentName", departmentName));
        }

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

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

        if (!string.IsNullOrEmpty(user.DepartmentName))
        {
            claims.Add(new Claim("DepartmentName", user.DepartmentName));
        }

        if (!string.IsNullOrEmpty(user.DoctorId))
        {
            claims.Add(new Claim("DoctorId", user.DoctorId));
        }

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        // Chỉ quay về đường dẫn nội bộ an toàn để không biến trang đăng nhập thành cổng chuyển hướng ra ngoài.
        return IsSafeReturnUrl(model.ReturnUrl)
            ? Redirect(model.ReturnUrl!)
            : RedirectToDashboard(user.RoleCode);
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

    [HttpGet]
    public IActionResult AccessDenied() => View();

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int accountId) || accountId <= 0)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _authService.ChangePasswordAsync(accountId, model.OldPassword, model.NewPassword, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Đổi mật khẩu thất bại.");
            return View(model);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["SuccessMessage"] = "Đổi mật khẩu thành công! Vui lòng đăng nhập lại với mật khẩu mới.";
        return RedirectToAction(nameof(Login));
    }

    // API cho tab Đăng ký trên màn hình Login (fetch JSON).
    [HttpPost]
    [EnableRateLimiting("register")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
    [EnableRateLimiting("password-reset-request")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
    [EnableRateLimiting("password-reset-confirm")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
           && !returnUrl.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase)
           && !returnUrl.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase);

    private IActionResult RedirectToDashboard(string? role)
    {
        return (role?.Trim().ToUpperInvariant()) switch
        {
            "ADMIN" => RedirectToAction("Index", "Management", new { area = "Admin" }),
            "DOCTOR" => RedirectToAction("Index", "Consultation", new { area = "Staff" }),
            "DEPARTMENT_HEAD" or "DEPT_HEAD" => RedirectToAction("Index", "Schedule", new { area = "Staff" }),
            "NURSE" or "RECEPTIONIST" => RedirectToAction("Index", "Reception", new { area = "Staff" }),
            "CASHIER" => RedirectToAction("Index", "Cashier", new { area = "Staff" }),
            _ => RedirectToAction("Index", "Booking")
        };
    }
}
