using HIS.BLL.Common;

namespace HIS.BLL.Services;

public class SlotGenerationService : ISlotGenerationService
{
    public Task<Result<int>> GenerateSlotsForDateRangeAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result<int>> ExpirePendingHoldsAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}
