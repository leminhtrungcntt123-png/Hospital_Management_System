using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface INurseDashboardService
{
    Task<Result<NurseDashboardSummaryDto>> GetDashboardSummaryAsync(
        int staffAccountId,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<NursePatientListItemDto>>> GetPatientsAsync(
        int staffAccountId,
        string? keyword,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);
}
