using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

/// <summary>
/// Lớp chung cho các trang web cần biết đang làm việc với tài khoản bệnh nhân nào
/// (đặt lịch, xem lịch hẹn, quản lý hồ sơ). Lấy Id từ cookie đăng nhập; chưa đăng nhập
/// thì đưa về trang đăng nhập rồi quay lại đúng trang đang làm; trang giới thiệu không cần đăng nhập.
/// </summary>
public abstract class PatientPageController : Controller
{
    protected int AccountId
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;

    /// <summary>
    /// Tài khoản chưa đăng nhập -> về trang đăng nhập kèm đường dẫn quay lại.
    /// Trả về null khi đã đăng nhập để controller tiếp tục.
    /// </summary>
    protected IActionResult? RequireAccount(string? returnUrl = null)
        => AccountId == 0
            ? RedirectToAction("Login", "Account", new { returnUrl = returnUrl ?? Request.Path.Value })
            : null;
}
