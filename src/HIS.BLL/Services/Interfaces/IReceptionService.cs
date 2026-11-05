using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IReceptionService
{
    Task<Result<ReceptionDesk>> GetDeskAsync(int staffAccountId, string? search, CancellationToken ct = default);
    Task<Result<string>> CheckInAsync(int staffAccountId, int appointmentId, bool identityVerified, CancellationToken ct = default);
    Task<Result<string>> RegisterWalkInAsync(int staffAccountId, WalkInReceptionRequest request, CancellationToken ct = default);
}
