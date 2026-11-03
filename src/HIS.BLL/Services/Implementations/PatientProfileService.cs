using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public class PatientProfileService : IPatientProfileService
{
    public Task<Result<int>> CreateProfileAsync(int accountId, CreateProfileRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> RequestLinkAsync(int accountId, RequestLinkRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> VerifyLinkAsync(int linkId, bool grantRecordAccess, int verifiedByAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> RevokeLinkAsync(int linkId, int revokedByAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
