using Microsoft.Extensions.Logging;

namespace HIS.BLL.Services;

public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("[EmailSender Stub] Gửi email tới {ToEmail} | Tiêu đề: {Subject}", toEmail, subject);
        return Task.CompletedTask;
    }
}

