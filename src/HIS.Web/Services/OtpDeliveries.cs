using System.Net;
using System.Net.Mail;
using HIS.BLL.Interfaces;
using HIS.Web.Options;
using Microsoft.Extensions.Options;

namespace HIS.Web.Services;

/// <summary>
/// Kênh OTP gửi mail thật qua SMTP (Gmail dùng App Password). Chỉ được đăng ký khi
/// Otp:Smtp có User/Password; lỗi SMTP do OtpService bắt và thông báo cho người dùng.
/// </summary>
public class SmtpOtpDelivery : IOtpDelivery
{
    private readonly OtpSmtpOptions _options;

    public SmtpOtpDelivery(IOptions<OtpSmtpOptions> options) => _options = options.Value;

    public string Name => $"email ({_options.Host})";

    public void Send(OtpRecipient to, string code, DateTimeOffset expiresAt)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_options.User!, _options.SenderName),
            Subject = "Mã xác thực hồ sơ của bạn"
        };
        message.To.Add(new MailAddress(to.Email));
        message.Body =
            $"Xin chào {to.DisplayName},\n\n" +
            $"Mã xác thực hồ sơ của bạn: {code}\n" +
            $"Mã có hiệu lực đến {expiresAt:HH:mm} ngày {expiresAt:dd/MM/yyyy}.\n\n" +
            "Nếu bạn không vừa thêm hồ sơ trên cổng đặt khám, hãy bỏ qua email này.\n";

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_options.User, _options.Password),
            Timeout = 15000
        };

        client.Send(message);
    }
}

/// <summary>
/// Kênh dự phòng khi chưa cấu hình SMTP: chỉ in mã ra log, người dùng đọc mã ngay trên màn hình
/// (Otp:RevealCodeOnScreen = true). Bản production phải thay bằng kênh thật và tắt ô hiện mã.
/// </summary>
public class DemoOtpDelivery : IOtpDelivery
{
    private readonly ILogger<DemoOtpDelivery> _logger;

    public DemoOtpDelivery(ILogger<DemoOtpDelivery> logger) => _logger = logger;

    public string Name => "demo trên màn hình";

    public void Send(OtpRecipient to, string code, DateTimeOffset expiresAt)
        => _logger.LogInformation("Mã OTP demo cho {Email} ({FullName}): {Code}, hết hạn {ExpiresAt:HH:mm}",
            to.Email, to.DisplayName, code, expiresAt);
}
