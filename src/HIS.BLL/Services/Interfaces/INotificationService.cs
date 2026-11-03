using HIS.BLL.Common;

namespace HIS.BLL.Services;

/// <summary>
/// Gửi thông báo trong hệ thống và qua email:
/// - Thông báo trong hệ thống lưu bảng notifications gửi về booked_by, nội dung ghi rõ tên người khám.
/// </summary>
public interface INotificationService
{
    Task<Result> SendInAppNotificationAsync(int accountId, int? appointmentId, string title, string content, CancellationToken ct = default);
    Task<Result> SendEmailNotificationAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}
