using System.Collections.Concurrent;
using System.Security.Cryptography;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class AuthService : IAuthService
{
    private static readonly ConcurrentDictionary<string, (string Otp, DateTime ExpiresAt, int Attempts)> _resetOtps = new();

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IEmailSender? _emailSender;

    public AuthService(AppDbContext db, TimeProvider timeProvider, IEmailSender? emailSender = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _emailSender = emailSender;
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_INPUT", "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.");
        }

        var normalizedUsername = request.Username.Trim();
        var now = _timeProvider.GetLocalNow().DateTime;

        var account = await _db.Accounts
            .Include(a => a.Role)
            .Include(a => a.Doctor)
            .Include(a => a.Nurse)
            .Include(a => a.DepartmentHead)
            .Include(a => a.PatientLinks)
                .ThenInclude(l => l.Patient)
            .FirstOrDefaultAsync(a => a.Username == normalizedUsername || (a.Email != null && a.Email == normalizedUsername), ct);

        if (account == null)
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_CREDENTIALS", "Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        if (!account.IsActive)
        {
            return Result.Failure<AuthResponse>("AUTH_ACCOUNT_INACTIVE", "Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");
        }

        if (account.LockedUntil.HasValue && account.LockedUntil.Value > now)
        {
            var remainingMinutes = (int)Math.Ceiling((account.LockedUntil.Value - now).TotalMinutes);
            return Result.Failure<AuthResponse>(
                "AUTH_ACCOUNT_LOCKED",
                $"Tài khoản đang bị tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút (lúc {account.LockedUntil.Value:HH:mm}).");
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
                await _db.SaveChangesAsync(ct);
                return Result.Failure<AuthResponse>(
                    "AUTH_ACCOUNT_LOCKED",
                    "Đăng nhập sai quá 5 lần. Tài khoản của bạn đã bị tạm khóa 15 phút để đảm bảo an toàn.");
            }

            await _db.SaveChangesAsync(ct);
            int remainingAttempts = 5 - account.FailedLoginCount;
            return Result.Failure<AuthResponse>(
                "AUTH_INVALID_CREDENTIALS",
                $"Tên đăng nhập hoặc mật khẩu không chính xác. Bạn còn {remainingAttempts} lần thử trước khi tài khoản bị khóa.");
        }

        // Đăng nhập thành công -> Reset số lần sai và thời gian khóa nếu có
        if (account.FailedLoginCount > 0 || account.LockedUntil != null)
        {
            account.FailedLoginCount = 0;
            account.LockedUntil = null;
            await _db.SaveChangesAsync(ct);
        }

        string fullName = account.Doctor?.FullName
            ?? account.Nurse?.FullName
            ?? account.DepartmentHead?.FullName
            ?? account.PatientLinks.FirstOrDefault(l => l.Relationship == PatientRelationship.Self)?.Patient.FullName
            ?? account.Username;

        return Result.Success(new AuthResponse(account.Id, account.Username, account.Role.Code, fullName));
    }

    public async Task<Result<AuthResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_INPUT", "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result.Failure<AuthResponse>("AUTH_INVALID_INPUT", "Vui lòng nhập họ và tên bệnh nhân.");
        }

        var normalizedUsername = request.Username.Trim();
        var normalizedEmail = request.Email?.Trim();

        // 1. Kiểm tra Username đã tồn tại chưa
        var isUsernameTaken = await _db.Accounts
            .AnyAsync(a => a.Username.ToLower() == normalizedUsername.ToLower(), ct);
        if (isUsernameTaken)
        {
            return Result.Failure<AuthResponse>("AUTH_USERNAME_EXISTS", $"Tên đăng nhập '{normalizedUsername}' đã được sử dụng. Vui lòng chọn tên khác.");
        }

        // 2. Kiểm tra Email đã tồn tại chưa (nếu người dùng có nhập email)
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var isEmailTaken = await _db.Accounts
                .AnyAsync(a => a.Email != null && a.Email.ToLower() == normalizedEmail.ToLower(), ct);
            if (isEmailTaken)
            {
                return Result.Failure<AuthResponse>("AUTH_EMAIL_EXISTS", $"Địa chỉ email '{normalizedEmail}' đã được đăng ký cho tài khoản khác.");
            }
        }

        // 3. Tìm Role Bệnh nhân (PATIENT)
        var patientRole = await _db.Roles.FirstOrDefaultAsync(r => r.Code == "PATIENT", ct);
        if (patientRole == null)
        {
            return Result.Failure<AuthResponse>("AUTH_ROLE_NOT_FOUND", "Không tìm thấy vai trò Bệnh nhân (PATIENT) trong hệ thống.");
        }

        var now = _timeProvider.GetLocalNow().DateTime;
        var dob = request.DateOfBirth == default 
            ? DateOnly.FromDateTime(DateTime.Today.AddYears(-20)) 
            : request.DateOfBirth;

        // 4. Tạo Account
        var account = new Account
        {
            Username = normalizedUsername,
            Email = string.IsNullOrWhiteSpace(normalizedEmail) ? null : normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
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

        await _db.SaveChangesAsync(ct);

        return Result.Success(new AuthResponse(account.Id, account.Username, patientRole.Code, patient.FullName));
    }

    public async Task<Result> ChangePasswordAsync(int accountId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FindAsync([accountId], ct);
        if (account == null)
        {
            return Result.Failure("AUTH_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, account.PasswordHash))
        {
            return Result.Failure("AUTH_INVALID_OLD_PASSWORD", "Mật khẩu hiện tại không chính xác.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return Result.Failure("AUTH_INVALID_NEW_PASSWORD", "Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        account.UpdatedAt = _timeProvider.GetLocalNow().DateTime;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public Task<Result> ResetPasswordByAdminAsync(int accountId, string newPassword, int adminAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public async Task<Result<ForgotPasswordResponse>> SendPasswordResetOtpAsync(string emailOrUsername, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailOrUsername))
        {
            return Result.Failure<ForgotPasswordResponse>("AUTH_INVALID_INPUT", "Vui lòng nhập email hoặc tên đăng nhập.");
        }

        var normalized = emailOrUsername.Trim().ToLowerInvariant();
        var now = _timeProvider.GetLocalNow().DateTime;

        var account = await _db.Accounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Username.ToLower() == normalized || (a.Email != null && a.Email.ToLower() == normalized), ct);

        if (account == null)
        {
            return Result.Failure<ForgotPasswordResponse>("AUTH_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản với email hoặc tên đăng nhập này.");
        }

        if (!account.IsActive)
        {
            return Result.Failure<ForgotPasswordResponse>("AUTH_ACCOUNT_INACTIVE", "Tài khoản của bạn đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");
        }

        if (string.IsNullOrWhiteSpace(account.Email))
        {
            return Result.Failure<ForgotPasswordResponse>("AUTH_NO_EMAIL", "Tài khoản này chưa cập nhật email liên hệ. Vui lòng liên hệ Ban Quản trị hệ thống (admin@his.local).");
        }

        // Tạo mã OTP 6 chữ số ngẫu nhiên
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAt = now.AddMinutes(5);

        var entry = (otp, expiresAt, Attempts: 0);
        _resetOtps[account.Username.ToLowerInvariant()] = entry;
        _resetOtps[account.Email.ToLowerInvariant()] = entry;

        var maskedEmail = MaskEmail(account.Email);
        var subject = "[MediCare HIS] Mã xác thực đặt lại mật khẩu";
        var body = $"""
            Xin chào {account.Username},

            Hệ thống nhận được yêu cầu đặt lại mật khẩu cho tài khoản MediCare của bạn.
            Mã xác thực (OTP) của bạn là: {otp}
            Mã này có hiệu lực trong 5 phút (đến {expiresAt:HH:mm}).

            Nếu bạn không gửi yêu cầu này, vui lòng bỏ qua email hoặc đổi mật khẩu để bảo vệ tài khoản.

            Trân trọng,
            Hệ thống Quản lý Bệnh viện MediCare
            """;

        if (_emailSender != null)
        {
            await _emailSender.SendEmailAsync(account.Email, subject, body, ct);
        }

        return Result.Success(new ForgotPasswordResponse(maskedEmail, null));
    }

    public async Task<Result> ResetPasswordWithOtpAsync(string emailOrUsername, string otp, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailOrUsername))
        {
            return Result.Failure("AUTH_INVALID_INPUT", "Vui lòng nhập email hoặc tên đăng nhập.");
        }

        if (string.IsNullOrWhiteSpace(otp) || otp.Trim().Length != 6)
        {
            return Result.Failure("AUTH_INVALID_OTP", "Vui lòng nhập mã xác thực OTP gồm 6 chữ số.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return Result.Failure("AUTH_INVALID_PASSWORD", "Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        var normalized = emailOrUsername.Trim().ToLowerInvariant();
        var cleanOtp = otp.Trim();
        var now = _timeProvider.GetLocalNow().DateTime;

        if (!_resetOtps.TryGetValue(normalized, out var entry))
        {
            return Result.Failure("AUTH_OTP_NOT_FOUND", "Không tìm thấy yêu cầu đặt lại mật khẩu hoặc mã OTP đã hết hạn. Vui lòng gửi lại mã.");
        }

        if (now > entry.ExpiresAt)
        {
            _resetOtps.TryRemove(normalized, out _);
            return Result.Failure("AUTH_OTP_EXPIRED", "Mã xác thực OTP đã hết hạn (quá 5 phút). Vui lòng yêu cầu mã mới.");
        }

        if (entry.Attempts >= 5)
        {
            _resetOtps.TryRemove(normalized, out _);
            return Result.Failure("AUTH_OTP_MAX_ATTEMPTS", "Bạn đã nhập sai OTP quá 5 lần. Vui lòng yêu cầu mã mới.");
        }

        if (entry.Otp != cleanOtp)
        {
            _resetOtps[normalized] = (entry.Otp, entry.ExpiresAt, entry.Attempts + 1);
            int remaining = 5 - (entry.Attempts + 1);
            return Result.Failure("AUTH_OTP_MISMATCH", $"Mã xác thực OTP không chính xác. Bạn còn {remaining} lần thử.");
        }

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Username.ToLower() == normalized || (a.Email != null && a.Email.ToLower() == normalized), ct);

        if (account == null)
        {
            return Result.Failure("AUTH_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản.");
        }

        // Băm mật khẩu mới bằng BCrypt và cập nhật
        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        account.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        // Xóa OTP sau khi đổi mật khẩu thành công
        _resetOtps.TryRemove(account.Username.ToLowerInvariant(), out _);
        if (!string.IsNullOrEmpty(account.Email))
        {
            _resetOtps.TryRemove(account.Email.ToLowerInvariant(), out _);
        }

        // Gửi email thông báo
        if (_emailSender != null && !string.IsNullOrEmpty(account.Email))
        {
            await _emailSender.SendEmailAsync(
                account.Email,
                "[MediCare HIS] Mật khẩu đã được thay đổi thành công",
                $"Xin chào {account.Username},\n\nMật khẩu tài khoản MediCare của bạn vừa được đặt lại thành công vào lúc {now:HH:mm dd/MM/yyyy}.\nNếu bạn không thực hiện việc này, vui lòng liên hệ ngay với Ban Quản trị.",
                ct);
        }

        return Result.Success();
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;
        var name = email[..atIndex];
        var domain = email[atIndex..];
        if (name.Length <= 2) return $"{name[0]}***{domain}";
        return $"{name[..2]}***{name[^1]}{domain}";
    }
}
