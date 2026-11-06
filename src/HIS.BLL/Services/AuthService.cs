using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Mã OTP đặt lại mật khẩu đang chờ nhập. Quản lý bằng PasswordResetChallenge hỗ trợ khóa đa luồng
    /// và kiểm tra constant-time để chống race condition và brute-force.
    /// </summary>
    private static readonly ConcurrentDictionary<string, PasswordResetChallenge> ResetOtps = new();

    private readonly AppDbContext _context;
    private readonly IReadOnlyList<IOtpDelivery> _deliveries;
    private readonly OtpOptions _otpOptions;
    private readonly TimeProvider _timeProvider;

    public AuthService(
        AppDbContext context,
        IEnumerable<IOtpDelivery>? deliveries = null,
        IOptions<OtpOptions>? otpOptions = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _deliveries = deliveries?.ToList() ?? [];
        _otpOptions = otpOptions?.Value ?? new OtpOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        LoginSurface surface = LoginSurface.Staff,
        CancellationToken cancellationToken = default)
    {
        username = (username ?? string.Empty).Trim();
        password = password ?? string.Empty;

        if (username.Length == 0 || password.Length == 0)
        {
            return LoginResult.Fail("Vui lòng nhập tên đăng nhập và mật khẩu.");
        }

        if (Encoding.UTF8.GetByteCount(password) > 72)
        {
            VerifyPassword(password, DummyPasswordHash);
            return LoginResult.Fail("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        // Chỉ nạp Account và Role để xác thực, tuyệt đối không .Include các bảng nghiệp vụ khác (Doctor, Nurse, Patient...)
        // Tên đăng nhập hoặc email đều dùng được; so chữ thường để không phân biệt hoa thường.
        string lowerUsername = username.ToLowerInvariant();
        Account? account = await _context.Accounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(
                a => a.Username.ToLower() == lowerUsername
                     || (a.Email != null && a.Email.ToLower() == lowerUsername),
                cancellationToken);

        DateTime now = _timeProvider.GetLocalNow().DateTime;

        if (account == null)
        {
            VerifyPassword(password, DummyPasswordHash);
            return LoginResult.Fail("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        // Kiểm tra thời gian khóa tạm thời
        if (account.LockedUntil is DateTime lockedUntil && lockedUntil > now)
        {
            VerifyPassword(password, DummyPasswordHash);
            int minutes = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalMinutes));
            return LoginResult.Fail(
                $"Tài khoản đang bị tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau {minutes} phút (lúc {lockedUntil:HH:mm}).");
        }

        // Kiểm tra trạng thái hoạt động của tài khoản
        if (!account.IsActive)
        {
            VerifyPassword(password, DummyPasswordHash);
            return LoginResult.Fail("Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");
        }

        // Kiểm tra định dạng hash BCrypt
        if (!IsBcryptHash(account.PasswordHash))
        {
            VerifyPassword(password, DummyPasswordHash);
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
        // Cổng Web chung chấp nhận cả bệnh nhân và nhân sự.
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
        var (displayName, departmentName, doctorId) = await ResolveDetailsAsync(account, cancellationToken);
        IReadOnlyList<string> permissions = RolePermissions.For(roleCode);

        return LoginResult.Success(new AuthUserDto(
            account.Id,
            account.Username,
            roleCode,
            roleName,
            displayName,
            permissions,
            departmentName,
            doctorId));
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

        if (Encoding.UTF8.GetByteCount(password) > 72)
        {
            return RegisterResult.Fail("Mật khẩu tối đa 72 byte UTF-8.");
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

        string lowerRegUsername = username.ToLowerInvariant();
        bool usernameTaken = await _context.Accounts
            .AnyAsync(a => a.Username.ToLower() == lowerRegUsername, cancellationToken);
        if (usernameTaken)
        {
            return RegisterResult.Fail($"Tên đăng nhập '{username}' đã được sử dụng. Vui lòng chọn tên khác.");
        }

        string lowerRegEmail = email.ToLowerInvariant();
        bool emailTaken = await _context.Accounts
            .AnyAsync(a => a.Email != null && a.Email.ToLower() == lowerRegEmail, cancellationToken);
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

        DateTime now = _timeProvider.GetLocalNow().DateTime;

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
                a => a.Username.ToLower() == key
                     || (a.Email != null && a.Email.ToLower() == key),
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

        DateTime now = _timeProvider.GetLocalNow().DateTime;
        string code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        DateTime expiresAt = now.AddMinutes(_otpOptions.LifetimeMinutes);
        string maskedEmail = ProfileEmail.Mask(recipient);
        string? demoOtp = _otpOptions.RevealCodeOnScreen ? code : null;

        var challenge = new PasswordResetChallenge(code, expiresAt, maskedEmail, demoOtp);

        string usernameKey = account.Username.ToLowerInvariant();
        ResetOtps[usernameKey] = challenge;
        ResetOtps[recipient] = challenge;

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
            ResetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(usernameKey, challenge));
            ResetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(recipient, challenge));
            return ForgotPasswordResult.Fail($"Chưa gửi được mã xác thực. {string.Join(" | ", failures)}");
        }

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = account.Id,
            Action = "PASSWORD_RESET_OTP_SENT",
            OccurredAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);

        return ForgotPasswordResult.Success(maskedEmail, demoOtp);
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

        if (Encoding.UTF8.GetByteCount(newPassword) > 72)
        {
            return PasswordResetResult.Fail("Mật khẩu mới tối đa 72 byte UTF-8.");
        }

        DateTime now = _timeProvider.GetLocalNow().DateTime;

        if (!ResetOtps.TryGetValue(key, out PasswordResetChallenge? challenge))
        {
            return PasswordResetResult.Fail(
                "Không tìm thấy yêu cầu đặt lại mật khẩu hoặc mã OTP đã hết hạn. Vui lòng gửi lại mã.");
        }

        var outcome = challenge.TryVerify(cleanOtp, now, _otpOptions.MaxAttempts, out int remainingAttempts);
        if (outcome == PasswordResetChallenge.VerifyOutcome.AlreadyConsumed || outcome == PasswordResetChallenge.VerifyOutcome.Expired)
        {
            ResetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(key, challenge));
            return PasswordResetResult.Fail("Mã xác thực OTP đã hết hạn. Vui lòng yêu cầu mã mới.");
        }

        if (outcome == PasswordResetChallenge.VerifyOutcome.MaxAttemptsExceeded)
        {
            ResetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(key, challenge));
            return PasswordResetResult.Fail(
                $"Bạn đã nhập sai OTP quá {_otpOptions.MaxAttempts} lần. Vui lòng yêu cầu mã mới.");
        }

        if (outcome == PasswordResetChallenge.VerifyOutcome.Mismatch)
        {
            return PasswordResetResult.Fail(
                $"Mã xác thực OTP không chính xác. Bạn còn {remainingAttempts} lần thử.");
        }

        Account? account = await _context.Accounts
            .FirstOrDefaultAsync(
                a => a.Username.ToLower() == key
                     || (a.Email != null && a.Email.ToLower() == key),
                cancellationToken);

        if (account == null)
        {
            return PasswordResetResult.Fail("Không tìm thấy tài khoản.");
        }

        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        account.UpdatedAt = now;

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = account.Id,
            Action = "PASSWORD_RESET_SUCCESS",
            OccurredAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        ResetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(account.Username.ToLowerInvariant(), challenge));
        if (account.Email is not null)
        {
            ResetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(account.Email.ToLowerInvariant(), challenge));
        }

        return PasswordResetResult.Success();
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(
        int accountId,
        string oldPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        oldPassword = oldPassword ?? string.Empty;
        newPassword = newPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(oldPassword))
        {
            return ChangePasswordResult.Fail("Vui lòng nhập mật khẩu hiện tại.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return ChangePasswordResult.Fail("Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        if (Encoding.UTF8.GetByteCount(newPassword) > 72)
        {
            return ChangePasswordResult.Fail("Mật khẩu mới tối đa 72 byte UTF-8.");
        }

        Account? account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account == null)
        {
            return ChangePasswordResult.Fail("Không tìm thấy tài khoản.");
        }

        if (!account.IsActive)
        {
            return ChangePasswordResult.Fail("Tài khoản đã bị vô hiệu hóa.");
        }

        if (!VerifyPassword(oldPassword, account.PasswordHash))
        {
            return ChangePasswordResult.Fail("Mật khẩu hiện tại không chính xác.");
        }

        if (VerifyPassword(newPassword, account.PasswordHash))
        {
            return ChangePasswordResult.Fail("Mật khẩu mới không được trùng với mật khẩu hiện tại.");
        }

        DateTime now = _timeProvider.GetLocalNow().DateTime;
        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        account.UpdatedAt = now;

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = account.Id,
            Action = "CHANGE_PASSWORD",
            OccurredAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
        return ChangePasswordResult.Success();
    }

    public async Task<bool> IsSessionActiveAsync(
        int accountId,
        string? roleCode = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0) return false;

        DateTime now = _timeProvider.GetLocalNow().DateTime;
        var account = await _context.Accounts
            .AsNoTracking()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account == null || !account.IsActive)
        {
            return false;
        }

        if (account.LockedUntil is DateTime lockedUntil && lockedUntil > now)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(roleCode)
            && !string.Equals(account.Role?.Code, roleCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public async Task LogoutAsync(int accountId, CancellationToken cancellationToken = default)
    {
        DateTime now = _timeProvider.GetLocalNow().DateTime;
        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = accountId,
            Action = "LOGOUT",
            OccurredAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

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

    private async Task<(string DisplayName, string? DepartmentName, string? DoctorId)> ResolveDetailsAsync(Account account, CancellationToken cancellationToken)
    {
        return (RoleId)account.RoleId switch
        {
            RoleId.Admin => ("Quản trị viên", null, null),
            RoleId.Doctor => await _context.Doctors
                .Where(d => d.AccountId == account.Id)
                .Select(d => new { d.FullName, DepartmentName = d.Department != null ? d.Department.Name : null, DoctorId = d.Id.ToString() })
                .FirstOrDefaultAsync(cancellationToken) is { } doc
                    ? (doc.FullName, doc.DepartmentName, doc.DoctorId)
                    : (account.Username, null, null),
            RoleId.Nurse => await _context.Nurses
                .Where(n => n.AccountId == account.Id)
                .Select(n => new { n.FullName, DepartmentName = n.Department != null ? n.Department.Name : null })
                .FirstOrDefaultAsync(cancellationToken) is { } nurse
                    ? (nurse.FullName, nurse.DepartmentName, null)
                    : (account.Username, null, null),
            RoleId.DepartmentHead => await _context.DepartmentHeads
                .Where(dh => dh.AccountId == account.Id)
                .Select(dh => new { dh.FullName, DepartmentName = dh.Department != null ? dh.Department.Name : null })
                .FirstOrDefaultAsync(cancellationToken) is { } dh
                    ? (dh.FullName, dh.DepartmentName, null)
                    : (account.Username, null, null),
            RoleId.Patient => (await _context.AccountPatientProfiles
                .Where(l => l.AccountId == account.Id
                            && l.Relationship == PatientRelationship.Self
                            && l.Status != LinkStatus.Revoked)
                .Select(l => l.Patient.FullName)
                .FirstOrDefaultAsync(cancellationToken) ?? account.Username, null, null),
            _ => (account.Username, null, null)
        };
    }
}
