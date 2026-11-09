namespace HIS.Web.Options;

/// <summary>
/// Cấu hình SMTP để gửi mã OTP thật (Gmail: Host smtp.gmail.com, Port 587, User = địa chỉ Gmail,
/// Password = App Password 16 ký tự). Giá trị thật đặt trong appsettings.Otp.local.json
/// (đã .gitignore) hoặc biến môi trường, không bao giờ commit vào appsettings.json.
/// </summary>
public class OtpSmtpOptions
{
    public const string SectionName = "Otp:Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string SenderName { get; set; } = "HIS Demo";

    /// <summary>Còn placeholder CHANGE_ME thì coi như chưa cấu hình để portal chạy kênh demo.</summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(User)
           && !string.IsNullOrWhiteSpace(Password)
           && !Password!.Contains("CHANGE_ME");
}
