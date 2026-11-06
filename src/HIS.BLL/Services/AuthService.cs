using System.Collections.Concurrent;
using System.Security.Cryptography;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Security;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public class AuthService : IAuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Mã OTP đặt lại mật khẩu đang chờ nhập. Không lưu DB: mã chỉ sống vài phút và mỗi lần
    /// gửi lại đều thay thế mã cũ, nên giữ trong bộ nhớ tiến trình là đủ cho một máy chủ web.
    /// </summary>
    private static readonly ConcurrentDictionary<string, ResetOtp> ResetOtps = new();

    private readonly AppDbContext _context;
    private readonly IReadOnlyList<IOtpDelivery> _deliveries;
    private readonly OtpOptions _otpOptions;

    public AuthService(
        AppDbContext context,
        IEnumerable<IOtpDelivery>? deliveries = null,
        IOptions<OtpOptions>? otpOptions = null)
    {
        _context = context;
        _deliveries = deliveries?.ToList() ?? [];
        _otpOptions = otpOptions?.Value ?? new OtpOptions();
    }

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        LoginSurface surface = LoginSurface.Staff,
        CancellationToken cancellationToken = default)
    {
        username = (username ?? string.Empty).Trim();

        if (username.Length == 0 || string.IsNullOrEmpty(password))
        {
            return LoginResult.Fail("Vui lòng nhập tên đăng nhập và mật khẩu.");
        }

        // Chỉ nạp Account và Role để xác thực, tuyệt đối không .Include các bảng nghiệp vụ khác (Doctor, Nurse, Patient...)
        // Tên đăng nhập hoặc email đều dùng được; email lưu chữ thường nên so ILIKE để không phân biệt hoa thường.
        Account? account = await _context.Accounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(
                a => EF.Functions.ILike(a.Username, username)
                     || (a.Email != null && EF.Functions.ILike(a.Email, username)),
                cancellationToken);

        if (account == null)
        {
            return LoginResult.Fail("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        DateTime now = DateTime.Now;

        // Kiểm tra thời gian khóa tạm thời
        if (account.LockedUntil is DateTime lockedUntil && lockedUntil > now)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalMinutes));
            return LoginResult.Fail(
                $"Tài khoản đang bị tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau {minutes} phút (lúc {lockedUntil:HH:mm}).");
        }

        // Kiểm tra trạng thái hoạt động của tài khoản
        if (!account.IsActive)
        {
            return LoginResult.Fail("Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");
        }

        // Kiểm tra định dạng hash BCrypt
        if (!IsBcryptHash(account.PasswordHash))
        {
            return LoginResult.Fail("Tài khoản chưa được thiết lập mật khẩu hợp lệ.");
        }

        // Xác thực mật khẩu
        if (!VerifyPassword(password, account.PasswordHash))
        {
            account.FailedLoginCount++;
            account.UpdatedAt = now;

            _context.AccessLogs.Add(new AccessLog
            {
                AccountId = account.Id,
                Action = "LOGIN_FAILED",
                OccurredAt = now
            });

            if (account.FailedLoginCount >= MaxFailedAttempts)
            {
                account.LockedUntil = now.Add(LockDuration);

                await _context.SaveChangesAsync(cancellationToken);

                return LoginResult.Fail(
                    $"Đăng nhập sai quá {MaxFailedAttempts} lần. Tài khoản của bạn đã bị tạm khóa " +
                    $"{LockDuration.TotalMinutes:0} phút để đảm bảo an toàn.");
            }

            await _context.SaveChangesAsync(cancellationToken);

            int remaining = MaxFailedAttempts - account.FailedLoginCount;
            return LoginResult.Fail(
                $"Tên đăng nhập hoặc mật khẩu không chính xác. Bạn còn {remaining} lần thử trước khi tài khoản bị khóa.");
        }

        // Phân quyền theo nơi đăng nhập: WinForms/WebApi chỉ nhân sự, trang đặt lịch web chỉ bệnh nhân.
        bool isPatient = string.Equals(account.Role?.Code, "PATIENT", StringComparison.OrdinalIgnoreCase);
        if (surface == LoginSurface.Staff && isPatient)
        {
            return LoginResult.Fail("Tài khoản bệnh nhân không dùng cho ứng dụng quản trị. Vui lòng đăng nhập tại cổng web.");
        }
        if (surface == LoginSurface.PatientPortal && !isPatient)
        {
            return LoginResult.Fail("Trang đặt lịch chỉ dành cho tài khoản bệnh nhân. Nhân viên vui lòng dùng phần mềm tiếp nhận.");
        }

        // Đăng nhập thành công -> Reset số lần đăng nhập sai
        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        account.UpdatedAt = now;

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = account.Id,
            Action = "LOGIN_SUCCESS",
            OccurredAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        string roleCode = account.Role?.Code ?? string.Empty;
        string roleName = account.Role?.Name ?? string.Empty;
        string displayName = await ResolveDisplayNameAsync(account, cancellationToken);
        IReadOnlyList<string> permissions = RolePermissions.For(roleCode);

        return LoginResult.Success(new AuthUserDto(
            account.Id,
            account.Username,
            roleCode,
            roleName,
            displayName,
            permissions));
    }

    public async Task<RegisterResult> RegisterPatientAsync(
        RegisterPatientRequest request,
        CancellationToken cancellationToken = default)
    {
        string username = (request.Username ?? string.Empty).Trim();
        string password = request.Password ?? string.Empty;
        string? email = ProfileEmail.NormalizeUsable(request.Email);

        if (username.Length == 0 || password.Length == 0)
        {
            return RegisterResult.Fail("Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.");
        }

        if (username.Length > 50)
        {
            return RegisterResult.Fail("Tên đăng nhập tối đa 50 ký tự.");
        }

        if (password.Length < 6)
        {
            return RegisterResult.Fail("Mật khẩu phải có tối thiểu 6 ký tự.");
        }

        if (email is null)
        {
            return RegisterResult.Fail(string.IsNullOrWhiteSpace(request.Email)
                ? "Vui lòng nhập email liên hệ để nhận mã xác thực khi cần khôi phục mật khẩu."
                : $"Email \"{request.Email.Trim()}\" không hợp lệ. Vui lòng kiểm tra lại.");
        }

        if (email.Length > 100)
        {
            return RegisterResult.Fail("Email tối đa 100 ký tự.");
        }

        bool usernameTaken = await _context.Accounts
            .AnyAsync(a => EF.Functions.ILike(a.Username, username), cancellationToken);
        if (usernameTaken)
        {
            return RegisterResult.Fail($"Tên đăng nhập '{username}' đã được sử dụng. Vui lòng chọn tên khác.");
        }

        bool emailTaken = await _context.Accounts
            .AnyAsync(a => a.Email != null && EF.Functions.ILike(a.Email, email), cancellationToken);
        if (emailTaken)
        {
            return RegisterResult.Fail($"Địa chỉ email '{email}' đã được đăng ký cho tài khoản khác.");
        }

        Role? patientRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Code == "PATIENT", cancellationToken);
        if (patientRole == null)
        {
            return RegisterResult.Fail("Không tìm thấy vai trò Bệnh nhân (PATIENT) trong hệ thống.");
        }

        DateTime now = DateTime.Now;

        var account = new Account
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = patientRole.Id,
            IsActive = true,
            FailedLoginCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Accounts.Add(account);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            return RegisterResult.Fail(
                "Không tạo được tài khoản vì tên đăng nhập hoặc email vừa được sử dụng. " +
                "Vui lòng kiểm tra lại hoặc đăng nhập bằng tài khoản đã có.");
        }

        return RegisterResult.Success(account.Id, account.Username);
    }

    public async Task<ForgotPasswordResult> SendPasswordResetOtpAsync(
        string emailOrUsername,
        CancellationToken cancellationToken = default)
    {
        string key = (emailOrUsername ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0)
        {
            return ForgotPasswordResult.Fail("Vui lòng nhập email hoặc tên đăng nhập.");
        }

        Account? account = await _context.Accounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(
                a => EF.Functions.ILike(a.Username, key)
                     || (a.Email != null && EF.Functions.ILike(a.Email, key)),
                cancellationToken);

        if (account == null)
        {
            return ForgotPasswordResult.Fail("Không tìm thấy tài khoản với email hoặc tên đăng nhập này.");
        }

        if (!account.IsActive)
        {
            return ForgotPasswordResult.Fail("Tài khoản của bạn đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");
        }

        string? recipient = ProfileEmail.NormalizeUsable(account.Email);
        if (recipient is null)
        {
            return ForgotPasswordResult.Fail(
                "Tài khoản này chưa cập nhật email liên hệ. Vui lòng liên hệ Ban Quản trị hệ thống (hotline 0808 0808).");
        }

        if (_deliveries.Count == 0)
        {
            return ForgotPasswordResult.Fail("Chưa cấu hình kênh gửi mã OTP.");
        }

        DateTime now = DateTime.Now;
        string code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        DateTime expiresAt = now.AddMinutes(_otpOptions.LifetimeMinutes);
        var entry = new ResetOtp(code, expiresAt, 0);

        string usernameKey = account.Username.ToLowerInvariant();
        ResetOtps[usernameKey] = entry;
        ResetOtps[recipient] = entry;

        var failures = new List<string>();
        foreach (IOtpDelivery delivery in _deliveries)
        {
            try
            {
                delivery.Send(
                    new OtpRecipient(account.Username, recipient),
                    code,
                    new DateTimeOffset(expiresAt),
                    "Mã xác thực đặt lại mật khẩu");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{delivery.Name}: {ex.Message}");
            }
        }

        if (failures.Count == _deliveries.Count)
        {
            ResetOtps.TryRemove(usernameKey, out _);
            ResetOtps.TryRemove(recipient, out _);
            return ForgotPasswordResult.Fail($"Chưa gửi được mã xác thực. {string.Join(" | ", failures)}");
        }

        return ForgotPasswordResult.Success(
            ProfileEmail.Mask(recipient),
            _otpOptions.RevealCodeOnScreen ? code : null);
    }

    public async Task<PasswordResetResult> ResetPasswordWithOtpAsync(
        string emailOrUsername,
        string otp,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        string key = (emailOrUsername ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0)
        {
            return PasswordResetResult.Fail("Vui lòng nhập email hoặc tên đăng nhập.");
        }

        string cleanOtp = (otp ?? string.Empty).Trim();
        if (cleanOtp.Length != 6)
        {
            return PasswordResetResult.Fail("Vui lòng nhập mã xác thực OTP gồm 6 chữ số.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return PasswordResetResult.Fail("Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        DateTime now = DateTime.Now;

        if (!ResetOtps.TryGetValue(key, out ResetOtp? entry))
        {
            return PasswordResetResult.Fail(
                "Không tìm thấy yêu cầu đặt lại mật khẩu hoặc mã OTP đã hết hạn. Vui lòng gửi lại mã.");
        }

        if (now > entry.ExpiresAt)
        {
            ResetOtps.TryRemove(key, out _);
            return PasswordResetResult.Fail("Mã xác thực OTP đã hết hạn. Vui lòng yêu cầu mã mới.");
        }

        if (entry.Attempts >= _otpOptions.MaxAttempts)
        {
            ResetOtps.TryRemove(key, out _);
            return PasswordResetResult.Fail(
                $"Bạn đã nhập sai OTP quá {_otpOptions.MaxAttempts} lần. Vui lòng yêu cầu mã mới.");
        }

        if (!string.Equals(entry.Code, cleanOtp, StringComparison.Ordinal))
        {
            int attempts = entry.Attempts + 1;
            ResetOtps[key] = entry with { Attempts = attempts };
            return PasswordResetResult.Fail(
                $"Mã xác thực OTP không chính xác. Bạn còn {_otpOptions.MaxAttempts - attempts} lần thử.");
        }

        Account? account = await _context.Accounts
            .FirstOrDefaultAsync(
                a => EF.Functions.ILike(a.Username, key)
                     || (a.Email != null && EF.Functions.ILike(a.Email, key)),
                cancellationToken);

        if (account == null)
        {
            return PasswordResetResult.Fail("Không tìm thấy tài khoản.");
        }

        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        account.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        ResetOtps.TryRemove(account.Username.ToLowerInvariant(), out _);
        if (account.Email is not null)
        {
            ResetOtps.TryRemove(account.Email.ToLowerInvariant(), out _);
        }

        return PasswordResetResult.Success();
    }

    public async Task LogoutAsync(int accountId, CancellationToken cancellationToken = default)
    {
        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = accountId,
            Action = "LOGOUT",
            OccurredAt = DateTime.Now
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private sealed record ResetOtp(string Code, DateTime ExpiresAt, int Attempts);

    private static bool IsBcryptHash(string? hash) =>
        hash is { Length: 60 } && hash.StartsWith("$2", StringComparison.Ordinal);

    private static bool VerifyPassword(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ResolveDisplayNameAsync(Account account, CancellationToken cancellationToken)
    {
        // So theo id role (7 vai trò trong DB) thay vì chuỗi code: code trong DB là "DEPT_HEAD",
        // không phải "DEPARTMENT_HEAD" nên so chuỗi dễ bỏ sót vai trò.
        return (RoleId)account.RoleId switch
        {
            RoleId.Admin => "Quản trị viên",
            RoleId.Doctor => await _context.Doctors
                .Where(d => d.AccountId == account.Id)
                .Select(d => d.FullName)
                .FirstOrDefaultAsync(cancellationToken) ?? account.Username,
            RoleId.Nurse => await _context.Nurses
                .Where(n => n.AccountId == account.Id)
                .Select(n => n.FullName)
                .FirstOrDefaultAsync(cancellationToken) ?? account.Username,
            RoleId.DepartmentHead => await _context.DepartmentHeads
                .Where(dh => dh.AccountId == account.Id)
                .Select(dh => dh.FullName)
                .FirstOrDefaultAsync(cancellationToken) ?? account.Username,
            RoleId.Patient => await _context.AccountPatientProfiles
                .Where(l => l.AccountId == account.Id
                            && l.Relationship == PatientRelationship.Self
                            && l.Status != LinkStatus.Revoked)
                .Select(l => l.Patient.FullName)
                .FirstOrDefaultAsync(cancellationToken) ?? account.Username,
            _ => account.Username
        };
    }
}
