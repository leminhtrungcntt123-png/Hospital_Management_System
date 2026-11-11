using System.Net;
using System.Net.Mail;
using HIS.BLL.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly IOptionsSnapshot<EmailOptions> _optionsSnapshot;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptionsSnapshot<EmailOptions> optionsSnapshot, ILogger<SmtpEmailSender> logger)
    {
        _optionsSnapshot = optionsSnapshot;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        var options = _optionsSnapshot.Value;

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.UserName) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("Chưa cấu hình dịch vụ email.");
        }

        try
        {
            using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
            {
                EnableSsl = options.EnableSsl,
                Credentials = new NetworkCredential(options.UserName, options.Password?.Replace(" ", "")),
                Timeout = 15000 // 15 giây
            };

            var fromAddress = new MailAddress(
                string.IsNullOrWhiteSpace(options.FromEmail) ? options.UserName : options.FromEmail,
                string.IsNullOrWhiteSpace(options.FromDisplayName) ? "Benh vien MediCare" : options.FromDisplayName
            );

            using var message = new MailMessage(fromAddress, new MailAddress(toEmail))
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("[SmtpEmailSender] Gửi email THẬT thành công tới {ToEmail} | Tiêu đề: {Subject}", toEmail, subject);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Email delivery failed: {Type}", ex.GetType().Name);
            throw;
        }
    }
}
