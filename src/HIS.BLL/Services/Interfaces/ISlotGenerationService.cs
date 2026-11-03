using HIS.BLL.Common;

namespace HIS.BLL.Services;

/// <summary>
/// Sinh slot online từ doctor_schedules trừ schedule_exceptions đã APPROVED (shift NULL = cả ngày).
/// Số slot online = online_slots; tại quầy = max_patients - online_slots.
/// </summary>
public interface ISlotGenerationService
{
    Task<Result<int>> GenerateSlotsForDateRangeAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
    Task<Result<int>> ExpirePendingHoldsAsync(CancellationToken ct = default);
}
