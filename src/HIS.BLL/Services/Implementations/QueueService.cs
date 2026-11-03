using HIS.BLL.Common;

namespace HIS.BLL.Services;

public class QueueService : IQueueService
{
    public Task<Result<string>> CheckInAndIssueQueueNumberAsync(int appointmentId, int staffAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result<int>> CallNextPatientAsync(int roomId, int doctorAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
