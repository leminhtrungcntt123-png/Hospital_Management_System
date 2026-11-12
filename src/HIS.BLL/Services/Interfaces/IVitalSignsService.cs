using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IVitalSignsService
{
    Task<Result<IReadOnlyList<VitalSignsQueueItemDto>>> GetQueueAsync(
        int staffAccountId,
        bool includeRecorded = true,
        CancellationToken ct = default);

    Task<Result<VitalSignsDetailDto>> GetDetailAsync(
        int staffAccountId,
        int appointmentId,
        CancellationToken ct = default);

    Task<Result<VitalSignsDetailDto>> RecordOrUpdateAsync(
        int staffAccountId,
        RecordVitalSignsRequest request,
        CancellationToken ct = default);
}
