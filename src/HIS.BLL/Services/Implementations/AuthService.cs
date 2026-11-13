using System.Collections.Concurrent;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HIS.BLL.Services;

public class AuthService : IAuthService
{
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    // shortcut: OTP state is per process; use a shared store before running multiple app instances.
    private static readonly ConcurrentDictionary<string, PasswordResetChallenge> _resetOtps = new();

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IEmailSender? _emailSender;
    private readonly ILogger<AuthService>? _logger;
    private readonly Func<string, string, bool> _verifyPassword = (password, hash) => BCrypt.Net.BCrypt.Verify(password, hash);
    private readonly Func<string, string> _hashPassword = password => BCrypt.Net.BCrypt.HashPassword(password);

    public AuthService(AppDbContext db, TimeProvider timeProvider, IEmailSender? emailSender = null,
        ILogger<AuthService>? logger = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _emailSender = emailSender;
        _logger = logger;
    }

    internal AuthService(AppDbContext db, TimeProvider timeProvider,
        Func<string, string, bool> verifyPassword, Func<string, string> hashPassword) : this(db, timeProvider)
    {
        _verifyPassword = verifyPassword;
        _hashPassword = hashPassword;
    }

    private static Result<AuthResponse> InvalidLogin() =>
        Result.Failure<AuthResponse>("AUTH_INVALID", "Tên đăng nhập hoặc mật khẩu không đúng, hoặc tài khoản đang bị khóa.");

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100
            || string.IsNullOrEmpty(request.Password) || Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            return InvalidLogin();
        }

        var username = request.Username.Trim().ToLowerInvariant();

        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // Lock this account so simultaneous attempts cannot lose increments or bypass lockout.
            var matches = await _db.Accounts.FromSqlInterpolated($"""
                SELECT * FROM accounts WHERE lower(username) = {username} OR lower(email) = {username} FOR UPDATE
                """)
                .Include(account => account.Role)
                .Include(account => account.Doctor).ThenInclude(d => d!.Department)
                .Include(account => account.Nurse).ThenInclude(n => n!.Department)
                .Include(account => account.DepartmentHead).ThenInclude(dh => dh!.Department)
                .Include(account => account.PatientLinks).ThenInclude(link => link.Patient)
                .ToListAsync(ct);
            var account = matches.Count == 1 ? matches[0] : null;
            var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);

            if (account == null || !account.IsActive || account.LockedUntil > now)
            {
                // Use the same BCrypt cost as account creation instead of returning before password verification.
                _verifyPassword(request.Password, DummyPasswordHash);
                return InvalidLogin();
            }

            var passwordMatches = false;
            try
            {
                passwordMatches = _verifyPassword(request.Password, account.PasswordHash);
            }
            catch (Exception error) when (error is BCrypt.Net.SaltParseException or ArgumentException or FormatException)
            {
                // Invalid legacy hashes must not become successful logins or leak an exception to the page.
                _verifyPassword(request.Password, DummyPasswordHash);
            }

            if (account.LockedUntil.HasValue)
            {
                account.FailedLoginCount = 0;
                account.LockedUntil = null;
            }

            if (!passwordMatches)
            {
                account.FailedLoginCount++;
                if (account.FailedLoginCount >= 5)
                {
                    account.LockedUntil = now.AddMinutes(15);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return InvalidLogin();
            }

            if (account.Role.Code is not ("ADMIN" or "DEPARTMENT_HEAD" or "DOCTOR" or "NURSE" or "PATIENT" or "ACCOUNTANT"))
            {
                return InvalidLogin();
            }

            account.FailedLoginCount = 0;
            account.LockedUntil = null;
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var fullName = account.Doctor?.FullName
                ?? account.Nurse?.FullName
                ?? account.DepartmentHead?.FullName
                ?? account.PatientLinks.FirstOrDefault(link => link.Relationship == PatientRelationship.Self)?.Patient.FullName
                ?? account.Username;
            var departmentName = account.Doctor?.Department?.Name
                ?? account.Nurse?.Department?.Name
                ?? account.DepartmentHead?.Department?.Name;
            return Result.Success(new AuthResponse(account.Id, account.Username, account.Role.Code, fullName, departmentName));
        });
    }

    public async Task<Result<AuthResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_INPUT", "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100)
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_INPUT", "Vui lòng nhập họ và tên bệnh nhân.");
        }

        if (!Regex.IsMatch(request.Username.Trim(), @"^[a-zA-Z0-9_.]{3,50}$")
            || request.Password.Length < 6 || Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_INPUT", "Tên đăng nhập từ 3–50 ký tự chữ, số, dấu chấm/gạch dưới; mật khẩu từ 6 ký tự và tối đa 72 byte UTF-8.");
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (request.DateOfBirth == default || request.DateOfBirth > today || request.DateOfBirth < today.AddYears(-130)
            || !Regex.IsMatch(request.PhoneNumber ?? "", @"^[0-9+ ()-]{9,15}$"))
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_PROFILE", "Vui lòng nhập ngày sinh và số điện thoại hợp lệ của người khám.");
        }
        if (!string.IsNullOrWhiteSpace(request.Email)
            && (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email.Trim())
                || request.Email.Trim().Length > 100))
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_EMAIL", "Email không hợp lệ hoặc vượt quá 100 ký tự.");
        }
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        var normalizedEmail = request.Email?.Trim().ToLowerInvariant();
        // Conflicts must perform the same expensive password operation as a new registration.
        var passwordHash = _hashPassword(request.Password);

        // 1. Kiểm tra Username đã tồn tại chưa
        var isUsernameTaken = await _db.Accounts
            .AnyAsync(a => a.Username.ToLower() == normalizedUsername.ToLower(), ct);
        // 2. Kiểm tra Email đã tồn tại chưa (nếu người dùng có nhập email)
        var isEmailTaken = false;
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            isEmailTaken = await _db.Accounts
                .AnyAsync(a => a.Email != null && a.Email.ToLower() == normalizedEmail.ToLower(), ct);
        }
        if (isUsernameTaken || isEmailTaken)
        {
            return RegistrationConflict();
        }

        // 3. Tìm Role Bệnh nhân (PATIENT)
        var patientRole = await _db.Roles.FirstOrDefaultAsync(r => r.Code == "PATIENT", ct);
        if (patientRole == null)
        {
            return Result.Failure<AuthResponse>("AUTH_ROLE_NOT_FOUND", "Không tìm thấy vai trò Bệnh nhân (PATIENT) trong hệ thống.");
        }

        var now = _timeProvider.GetLocalNow().DateTime;
        var dob = request.DateOfBirth;

        // 4. Tạo Account
        var account = new Account
        {
            Username = normalizedUsername,
            Email = string.IsNullOrWhiteSpace(normalizedEmail) ? null : normalizedEmail,
            PasswordHash = passwordHash,
            RoleId = patientRole.Id,
            IsActive = true,
            FailedLoginCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        // 5. Tạo hồ sơ Patient
        var patient = new Patient
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = dob,
            PhoneNumber = request.PhoneNumber?.Trim() ?? string.Empty,
            NationalId = request.NationalId?.Trim(),
            Address = request.Address?.Trim(),
            CreatedAt = now
        };

        // 6. Tạo liên kết chính chủ (SELF)
        var link = new AccountPatientProfile
        {
            Account = account,
            Patient = patient,
            Relationship = PatientRelationship.Self,
            Status = LinkStatus.Active,
            CanViewRecords = true,
            CreatedAt = now
        };

        account.PatientLinks.Add(link);
        patient.AccountLinks.Add(link);

        _db.Accounts.Add(account);
        _db.Patients.Add(patient);
        _db.AccountPatientProfiles.Add(link);

        try
        {
            await _db.SaveChangesAsync(ct);
            return Result.Success(new AuthResponse(account.Id, account.Username, patientRole.Code, patient.FullName));
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_accounts_username" or "ix_accounts_email" or "accounts_username_key" or "accounts_email_key"
        })
        {
            _db.ChangeTracker.Clear();
            return RegistrationConflict();
        }
    }

    private static Result<AuthResponse> RegistrationConflict() =>
        Result.Failure<AuthResponse>("AUTH_REGISTRATION_CONFLICT", "Không thể tạo tài khoản với thông tin này.");

    public async Task<Result> ChangePasswordAsync(int accountId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FindAsync([accountId], ct);
        if (account == null)
        {
            return Result.Failure("AUTH_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        if (!_verifyPassword(oldPassword, account.PasswordHash))
        {
            return Result.Failure("AUTH_INVALID_OLD_PASSWORD", "Mật khẩu hiện tại không chính xác.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6 || Encoding.UTF8.GetByteCount(newPassword) > 72)
        {
            return Result.Failure("AUTH_INVALID_NEW_PASSWORD", "Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        account.PasswordHash = _hashPassword(newPassword);
        account.UpdatedAt = _timeProvider.GetLocalNow().DateTime;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public Task<Result> ResetPasswordByAdminAsync(int accountId, string newPassword, int adminAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public async Task<Result> SendPasswordResetOtpAsync(string emailOrUsername, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailOrUsername) || emailOrUsername.Length > 100)
        {
            return Result.Failure("AUTH_INVALID_INPUT", "Vui lòng nhập email hoặc tên đăng nhập hợp lệ.");
        }

        var normalized = emailOrUsername.Trim().ToLowerInvariant();
        var account = await _db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Username.ToLower() == normalized || (a.Email != null && a.Email.ToLower() == normalized), ct);
        if (account == null || !account.IsActive || string.IsNullOrWhiteSpace(account.Email))
        {
            return Result.Success();
        }

        var now = _timeProvider.GetLocalNow().DateTime;
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAt = now.AddMinutes(5);
        var key = account.Username.ToLowerInvariant();
        var entry = new PasswordResetChallenge(otp, expiresAt);
        _resetOtps[key] = entry;
        var body = $"""
            Xin chào {account.Username},

            Mã xác thực (OTP) đặt lại mật khẩu MediCare của bạn là: {otp}
            Mã này có hiệu lực trong 5 phút (đến {expiresAt:HH:mm}).
            Nếu bạn không gửi yêu cầu này, vui lòng bỏ qua email.
            """;
        try
        {
            if (_emailSender == null)
            {
                throw new InvalidOperationException("Chưa cấu hình dịch vụ email.");
            }
            await _emailSender.SendEmailAsync(account.Email, "[MediCare HIS] Mã xác thực đặt lại mật khẩu", body, ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A failed older send must not delete a newer challenge.
            _resetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(key, entry));
            _logger?.LogWarning("Password reset email delivery failed: {Type}", error.GetType().Name);
        }

        return Result.Success();
    }

    public async Task<Result> ResetPasswordWithOtpAsync(string emailOrUsername, string otp, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailOrUsername) || emailOrUsername.Length > 100
            || string.IsNullOrWhiteSpace(otp) || !Regex.IsMatch(otp.Trim(), @"^[0-9]{6}$"))
        {
            return InvalidReset();
        }
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6 || Encoding.UTF8.GetByteCount(newPassword) > 72)
        {
            return Result.Failure("AUTH_INVALID_PASSWORD", "Mật khẩu mới phải có tối thiểu 6 ký tự và không vượt quá 72 byte UTF-8.");
        }

        var identifier = emailOrUsername.Trim().ToLowerInvariant();
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Username.ToLower() == identifier
            || (a.Email != null && a.Email.ToLower() == identifier), ct);
        var now = _timeProvider.GetLocalNow().DateTime;
        if (account == null || !account.IsActive)
        {
            return InvalidReset();
        }

        var key = account.Username.ToLowerInvariant();
        if (!_resetOtps.TryGetValue(key, out var entry) || !entry.TryConsume(otp.Trim(), now)
            || !_resetOtps.TryRemove(new KeyValuePair<string, PasswordResetChallenge>(key, entry)))
        {
            return InvalidReset();
        }

        account.PasswordHash = _hashPassword(newPassword);
        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        account.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Result InvalidReset() => Result.Failure("AUTH_INVALID_RESET",
        "Thông tin xác thực không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu mã mới.");
}
