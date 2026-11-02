using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using Microsoft.EntityFrameworkCore;

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
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AuthService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_INPUT", "Tên đăng nhập và mật khẩu không được để trống.");
        }

        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        var account = await _db.Accounts
            .Include(a => a.Role)
            .Include(a => a.Doctor)
            .Include(a => a.Nurse)
            .Include(a => a.DepartmentHead)
            .Include(a => a.PatientLinks)
                .ThenInclude(l => l.Patient)
            .FirstOrDefaultAsync(a => a.Username.ToLower() == normalizedUsername, ct);

        if (account is null)
        {
            return Result.Failure<AuthResponse>("AUTH_FAILED", "Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        if (!account.IsActive)
        {
            return Result.Failure<AuthResponse>("ACCOUNT_INACTIVE", "Tài khoản này đã bị khóa hoặc ngừng hoạt động.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (account.LockedUntil.HasValue && account.LockedUntil.Value > now)
        {
            var remainingMinutes = Math.Ceiling((account.LockedUntil.Value - now).TotalMinutes);
            return Result.Failure<AuthResponse>("ACCOUNT_LOCKED", $"Tài khoản tạm thời bị khóa do nhập sai nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút.");
        }

        bool isPasswordValid = false;
        try
        {
            isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash);
        }
        catch
        {
            isPasswordValid = false;
        }

        if (!isPasswordValid)
        {
            account.FailedLoginCount++;
            if (account.FailedLoginCount >= 5)
            {
                account.LockedUntil = now.AddMinutes(15);
                account.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync(ct);
            return Result.Failure<AuthResponse>("AUTH_FAILED", "Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        if (account.FailedLoginCount > 0 || account.LockedUntil.HasValue)
        {
            account.FailedLoginCount = 0;
            account.LockedUntil = null;
            await _db.SaveChangesAsync(ct);
        }

        var fullName = account.Doctor?.FullName
            ?? account.Nurse?.FullName
            ?? account.DepartmentHead?.FullName
            ?? account.PatientLinks.FirstOrDefault()?.Patient?.FullName
            ?? account.Username;

        return Result.Success(new AuthResponse(account.Id, account.Username, account.Role.Code, fullName));
    }

    public Task<Result<AuthResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result> ChangePasswordAsync(int accountId, string oldPassword, string newPassword, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result> ResetPasswordByAdminAsync(int accountId, string newPassword, int adminAccountId, CancellationToken ct = default) => throw new NotImplementedException();
}
