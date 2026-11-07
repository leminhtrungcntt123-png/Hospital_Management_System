using System.ComponentModel.DataAnnotations;

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

public record AuthResponse(int AccountId, string Username, string Role, string? FullName = null, string? DepartmentName = null);

public record ForgotPasswordRequest([Required, StringLength(100)] string EmailOrUsername);

public record ResetPasswordRequest(
    [Required, StringLength(100)] string EmailOrUsername,
    [Required, RegularExpression(@"^[0-9]{6}$")] string Otp,
    [Required, StringLength(72, MinimumLength = 6)] string NewPassword);
