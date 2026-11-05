using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.Web.Helpers;
using HIS.Web.ViewModels.Account;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

// Trang đăng nhập demo cho riêng HIS.Web.
// Phần đăng nhập thật (đổi mật khẩu, phân quyền theo module) do một thành viên khác phụ trách,
// nên trang này không tự viết lại bước kiểm mật khẩu: luôn gọi IAuthService.LoginAsync
// (BCrypt, đếm lần sai, khóa tạm) rồi chỉ giữ Id tài khoản trong Session để luồng đặt lịch biết
// đang đặt cho ai. Khi module đăng nhập nối vào web, thay AccountController bằng trang thật.
public class AccountController : Controller
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Đăng nhập ở cổng PatientPortal: AuthService tự chặn vai trò không phải bệnh nhân ở cổng này.
        LoginResult result = await _authService.LoginAsync(model.Username!, model.Password!, LoginSurface.PatientPortal);

        if (!result.Succeeded || result.User is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Đăng nhập không thành công.");
            return View(model);
        }

        HttpContext.SignIn(new LoggedInAccount(
            result.User.AccountId,
            result.User.Username,
            result.User.DisplayName,
            result.User.RoleCode));

        // Chỉ quay về đường dẫn nội bộ để không biến trang đăng nhập thành cổng chuyển hướng ra ngoài.
        return Url.IsLocalUrl(model.ReturnUrl)
            ? Redirect(model.ReturnUrl!)
            : RedirectToAction("Index", "Booking");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.SignOut();
        return RedirectToAction(nameof(Login));
    }
}
