using HIS.BLL.Common;

namespace HIS.BLL.Services;

/// <summary>
/// Bốc số thứ tự và gọi khám:
/// - Số cấp từ queue_counters (khóa dòng theo ngày, phòng, ca, kênh: O.. = WEB, W.. = ON_SITE).
/// - Hàng đợi sắp theo priority_level giảm dần rồi tới giờ đặt/check-in.
/// </summary>
public interface IQueueService
{
    Task<Result<string>> CheckInAndIssueQueueNumberAsync(int appointmentId, int staffAccountId, CancellationToken ct = default);
    Task<Result<int>> CallNextPatientAsync(int roomId, int doctorAccountId, CancellationToken ct = default);
}
