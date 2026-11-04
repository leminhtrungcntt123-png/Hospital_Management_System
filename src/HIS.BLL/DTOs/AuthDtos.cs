namespace HIS.BLL.DTOs;

public record RegisterPatientRequest(
    string Username,
    string Password,
    string FullName,
    DateOnly DateOfBirth,
    string PhoneNumber,
    string? Email = null,
    string? NationalId = null,
    string? Address = null
);

public record LoginRequest(string Username, string Password);

public record AuthResponse(int AccountId, string Username, string Role);

