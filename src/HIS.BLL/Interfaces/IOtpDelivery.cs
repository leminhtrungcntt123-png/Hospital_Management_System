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

    void Send(OtpRecipient to, string code, DateTimeOffset expiresAt);
}
