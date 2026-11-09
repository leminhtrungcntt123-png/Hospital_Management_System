namespace HIS.BLL.DTOs;

public sealed record AuthUserDto(
    int AccountId,
    string Username,
    string RoleCode,
    string RoleName,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    string? DepartmentName = null,
    string? DoctorId = null);

public sealed class LoginResult
{
    public bool Succeeded { get; private init; }

    public string? ErrorMessage { get; private init; }

    public AuthUserDto? User { get; private init; }

    public static LoginResult Success(AuthUserDto user) => new()
    {
        Succeeded = true,
        User = user
    };

    public static LoginResult Fail(string message) => new()
    {
        ErrorMessage = message
    };
}

/// <summary>
/// Phiếu đăng ký tài khoản người khám: chỉ tạo tài khoản đăng nhập.
/// Hồ sơ bệnh nhân (kể cả "Bản thân") do người dùng tự tạo ở trang Hồ sơ sau khi đăng nhập.
/// </summary>
public sealed record RegisterPatientRequest(
    string Username,
    string Password,
    string Email);

public sealed class RegisterResult
{
    public bool Succeeded { get; private init; }

    public string? ErrorMessage { get; private init; }

    public int AccountId { get; private init; }

    public string? Username { get; private init; }

    public static RegisterResult Success(int accountId, string username) => new()
    {
        Succeeded = true,
        AccountId = accountId,
        Username = username
    };

    public static RegisterResult Fail(string message) => new()
    {
        ErrorMessage = message
    };
}

public sealed record ForgotPasswordRequest(string EmailOrUsername);

public sealed record ResetPasswordRequest(string EmailOrUsername, string Otp, string NewPassword);

public sealed class ForgotPasswordResult
{
    public bool Succeeded { get; private init; }

    public string? ErrorMessage { get; private init; }

    public string? MaskedEmail { get; private init; }

    /// <summary>
    /// Chỉ khác rỗng khi Otp:RevealCodeOnScreen = true (môi trường Development) để dev test
    /// mà không cần mở hộp thư; production luôn null.
    /// </summary>
    public string? DemoOtp { get; private init; }

    public static ForgotPasswordResult Success(string maskedEmail, string? demoOtp) => new()
    {
        Succeeded = true,
        MaskedEmail = maskedEmail,
        DemoOtp = demoOtp
    };

    public static ForgotPasswordResult Fail(string message) => new()
    {
        ErrorMessage = message
    };
}

public sealed class PasswordResetResult
{
    public bool Succeeded { get; private init; }

    public string? ErrorMessage { get; private init; }

    public static PasswordResetResult Success() => new() { Succeeded = true };

    public static PasswordResetResult Fail(string message) => new()
    {
        ErrorMessage = message
    };
}

public sealed record ChangePasswordRequest(
    int AccountId,
    string OldPassword,
    string NewPassword);

public sealed class ChangePasswordResult
{
    public bool Succeeded { get; private init; }

    public string? ErrorMessage { get; private init; }

    public static ChangePasswordResult Success() => new() { Succeeded = true };

    public static ChangePasswordResult Fail(string message) => new()
    {
        ErrorMessage = message
    };
}
