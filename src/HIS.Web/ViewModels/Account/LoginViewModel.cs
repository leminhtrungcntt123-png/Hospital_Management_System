using System.ComponentModel.DataAnnotations;

namespace HIS.Web.ViewModels.Account;

// Tài khoản đang phiên đặt lịch (demo: lưu trong Session, xem HIS.Web/Helpers/DemoSession.cs).
public sealed record LoggedInAccount(int Id, string Username, string DisplayName, string RoleCode);

public class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    public string? Password { get; set; }

    // Nơi cần quay về sau khi đăng nhập; chỉ nhận đường dẫn nội bộ của web.
    public string? ReturnUrl { get; set; }
}
