using HIS.BLL.Common;

namespace HIS.BLL.Services;

public class NotificationService : INotificationService
{
    public Task<Result> SendInAppNotificationAsync(int accountId, int? appointmentId, string title, string content, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> SendEmailNotificationAsync(string toEmail, string subject, string body, CancellationToken ct = default)
        => throw new NotImplementedException();
}
