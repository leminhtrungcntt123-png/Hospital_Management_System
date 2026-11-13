using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface ICashierShiftService
{
    Task<CashierShiftDto?> GetCurrentShiftAsync(int cashierAccountId, CancellationToken ct = default);
    Task<Result<CashierShiftDto>> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default);
    Task<Result<CashierShiftDto>> CloseShiftAsync(CloseShiftRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CashierShiftDto>> GetShiftsAsync(DateOnly? date = null, int? cashierAccountId = null, CancellationToken ct = default);
}

