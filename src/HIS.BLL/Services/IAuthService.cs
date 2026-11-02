using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

/// <summary>
/// Quản lý đăng nhập, khóa tài khoản sau 5 lần sai, đăng ký tài khoản bệnh nhân kèm tạo Patient và link SELF trong 1 transaction.
/// </summary>
public interface IAuthService
{
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(int accountId, string oldPassword, string newPassword, CancellationToken ct = default);
    Task<Result> ResetPasswordByAdminAsync(int accountId, string newPassword, int adminAccountId, CancellationToken ct = default);
}

public class AuthService : IAuthService
{
    public Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result<AuthResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result> ChangePasswordAsync(int accountId, string oldPassword, string newPassword, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result> ResetPasswordByAdminAsync(int accountId, string newPassword, int adminAccountId, CancellationToken ct = default) => throw new NotImplementedException();
}
