using System.ComponentModel.DataAnnotations;

namespace HIS.Web.ViewModels.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    [StringLength(100, ErrorMessage = "Tên đăng nhập hoặc email tối đa 100 ký tự.")]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(72, ErrorMessage = "Mật khẩu tối đa 72 byte.")]
    public string? Password { get; set; }

    // Giữ cookie đăng nhập 7 ngày trên máy này.
    public bool RememberMe { get; set; }

    // Nơi cần quay về sau khi đăng nhập; chỉ nhận đường dẫn nội bộ của web.
    public string? ReturnUrl { get; set; }
}
