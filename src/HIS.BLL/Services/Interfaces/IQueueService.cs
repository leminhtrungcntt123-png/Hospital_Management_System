using HIS.BLL.Common;

namespace HIS.BLL.Services;

/// <summary>
/// Gọi khám:
/// - Hàng đợi sắp theo priority_level giảm dần (3 = khẩn cấp nhất) rồi tới số thứ tự / giờ check-in.
/// - Việc cấp số thứ tự (check-in) nằm ở <c>IReceptionService</c>, dùng queue_counters
///   (khóa dòng theo ngày, phòng, ca, kênh: O.. = WEB, W.. = ON_SITE).
/// </summary>
public interface IQueueService
{
    Task<Result<int>> CallNextPatientAsync(int roomId, int doctorAccountId, CancellationToken ct = default);
}
