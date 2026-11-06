using System.ComponentModel.DataAnnotations;

namespace HIS.Web.ViewModels.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    public string? Password { get; set; }

    // Giữ cookie đăng nhập 7 ngày trên máy này.
    public bool RememberMe { get; set; }

    // Nơi cần quay về sau khi đăng nhập; chỉ nhận đường dẫn nội bộ của web.
    public string? ReturnUrl { get; set; }
}
