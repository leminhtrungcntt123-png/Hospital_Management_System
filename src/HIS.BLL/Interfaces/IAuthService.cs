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
    PatientPortal,

    /// <summary>Cổng Web chung (chấp nhận cả PATIENT và Nhân sự).</summary>
    Web
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

    /// <summary>
    /// Tạo tài khoản bệnh nhân. SĐT chủ tài khoản chỉ được thuộc một tài khoản (quy tắc hồ sơ của HIS),
    /// email bắt buộc để sau này nhận mã OTP đặt lại mật khẩu.
    /// </summary>
    Task<RegisterResult> RegisterPatientAsync(
        RegisterPatientRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sinh mã OTP 6 số, gửi tới email của tài khoản qua các kênh <see cref="IOtpDelivery"/> đã đăng ký.</summary>
    Task<ForgotPasswordResult> SendPasswordResetOtpAsync(
        string emailOrUsername,
        CancellationToken cancellationToken = default);

    /// <summary>Đối chiếu OTP còn hiệu lực rồi đặt mật khẩu mới; thành công thì mở khóa đăng nhập.</summary>
    Task<PasswordResetResult> ResetPasswordWithOtpAsync(
        string emailOrUsername,
        string otp,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>Đổi mật khẩu tài khoản đang đăng nhập.</summary>
    Task<ChangePasswordResult> ChangePasswordAsync(
        int accountId,
        string oldPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra tài khoản còn hoạt động hợp lệ trong phiên làm việc.</summary>
    Task<bool> IsSessionActiveAsync(
        int accountId,
        string? roleCode = null,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(int accountId, CancellationToken cancellationToken = default);
}
