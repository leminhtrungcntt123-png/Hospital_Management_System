using HIS.BLL.DTOs;

namespace HIS.BLL.Interfaces;

/// <summary>
/// Nơi đang đăng nhập. Portal khác cổng quản trị chỉ ở chỗ vai trò nào được vào:
/// cổng WinForms/WebApi dành cho nhân sự, trang đặt lịch web dành cho bệnh nhân.
/// </summary>
public enum LoginSurface
{
    /// <summary>Ứng dụng quản trị (mặc định, giữ nguyên hành vi cũ: chặn tài khoản PATIENT).</summary>
    Staff,

    /// <summary>Trang đặt lịch của bệnh nhân: chỉ nhận tài khoản PATIENT.</summary>
    PatientPortal
}

/// <summary>
/// Cổng xác thực dùng chung cho WinForms/WebApi (nhân sự) và trang đặt lịch web (bệnh nhân).
/// Thêm tham số <see cref="LoginSurface"/> theo cách tùy chọn để mọi lời gọi cũ
/// LoginAsync(username, password) vẫn biên dịch và giữ nguyên hành vi chặn tài khoản PATIENT.
/// </summary>
public interface IAuthService
{
    Task<LoginResult> LoginAsync(
        string username,
        string password,
        LoginSurface surface = LoginSurface.Staff,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(int accountId, CancellationToken cancellationToken = default);
}
