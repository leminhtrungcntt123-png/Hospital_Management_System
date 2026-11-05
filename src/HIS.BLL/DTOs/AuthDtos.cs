namespace HIS.BLL.DTOs;

public record RegisterPatientRequest(
    string Username,
    string Password,
    string FullName,
    DateOnly DateOfBirth = default,
    string PhoneNumber = "",
    string? Email = null,
    string? NationalId = null,
    string? Address = null
);

public record LoginRequest(string Username, string Password);

public record AuthResponse(int AccountId, string Username, string Role, string? FullName = null);

public record ForgotPasswordRequest(string EmailOrUsername);

public record ForgotPasswordResponse(string MaskedEmail, string? DemoOtp = null);

public record ResetPasswordRequest(string EmailOrUsername, string Otp, string NewPassword);
