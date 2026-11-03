using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public class AuthService : IAuthService
{
    public Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result<AuthResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> ChangePasswordAsync(int accountId, string oldPassword, string newPassword, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> ResetPasswordByAdminAsync(int accountId, string newPassword, int adminAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();
}

