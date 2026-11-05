using HIS.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

/// <summary>
/// Lớp chung cho các trang web cần biết đang làm việc với tài khoản bệnh nhân nào
/// (đặt lịch, xem lịch hẹn, thêm hồ sơ). Chưa đăng nhập thì đưa về trang demo rồi quay lại
/// đúng trang đang làm; trang giới thiệu (Booking/Index) không cần đăng nhập.
/// </summary>
public abstract class PatientPageController : Controller
{
    protected int AccountId => HttpContext.Account()?.Id ?? 0;

    /// <summary>
    /// Tài khoản chưa nằm trong Session -> về trang đăng nhập kèm đường dẫn quay lại.
    /// Trả về null khi đã đăng nhập để controller tiếp tục.
    /// </summary>
    protected IActionResult? RequireAccount(string? returnUrl = null)
        => AccountId == 0
            ? RedirectToAction("Login", "Account", new { returnUrl = returnUrl ?? Request.Path.Value })
            : null;
}
