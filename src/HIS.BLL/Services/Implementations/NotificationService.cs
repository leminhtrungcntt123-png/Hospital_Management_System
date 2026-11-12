using HIS.BLL.Common;
using HIS.DAL;
using HIS.Domain;

namespace HIS.BLL.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IEmailSender? _emailSender;

    public NotificationService(AppDbContext context, TimeProvider timeProvider, IEmailSender? emailSender = null)
    {
        _context = context;
        _timeProvider = timeProvider;
        _emailSender = emailSender;
    }

    public async Task<Result> SendInAppNotificationAsync(int accountId, int? appointmentId, string title, string content, CancellationToken ct = default)
    {
        var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
        _context.Notifications.Add(new Notification
        {
            AccountId = accountId,
            AppointmentId = appointmentId,
            Title = title,
            Content = content,
            CreatedAt = now
        });

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SendEmailNotificationAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        if (_emailSender != null && !string.IsNullOrWhiteSpace(toEmail))
        {
            try
            {
                await _emailSender.SendEmailAsync(toEmail, subject, body, ct);
            }
            catch
            {
                // Soft fail on external email network errors so patient flow is not blocked
            }
        }
        return Result.Success();
    }
}
