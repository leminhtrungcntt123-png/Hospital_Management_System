namespace HIS.BLL.Interfaces;

/// <summary>Người nhận một mã OTP. Current chỉ có email; kênh SMS sẽ thêm số điện thoại sau.</summary>
public sealed record OtpRecipient(string DisplayName, string Email);

/// <summary>
/// Một kênh chuyển mã OTP (email, SMS, hoặc bản demo ghi vào notifications).
/// BLL chỉ gọi kênh, không biết SMTP hay nhà mạng; host đăng ký implementation.
/// Ném exception khi gửi hỏng để <see cref="IOtpService"/> ghi lý do vào kết quả.
/// </summary>
public interface IOtpDelivery
{
    /// <summary>Tên kênh xuất hiện trong thông báo cho người dùng, ví dụ "email".</summary>
    string Name { get; }

    /// <summary>
    /// Chuyển một mã tới người nhận. <paramref name="purpose"/> là tiêu đề hiển thị (ví dụ
    /// "Mã xác thực đặt lại mật khẩu"); bỏ trống thì kênh dùng tiêu đề mặc định của OTP hồ sơ.
    /// </summary>
    void Send(OtpRecipient to, string code, DateTimeOffset expiresAt, string? purpose = null);
}
