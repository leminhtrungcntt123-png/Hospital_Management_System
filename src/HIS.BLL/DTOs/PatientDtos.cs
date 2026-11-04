namespace HIS.BLL.DTOs;

public record CreateProfileRequest(
    string FullName,
    DateOnly DateOfBirth,
    string PhoneNumber,
    string Relationship,
    string? NationalId = null,
    string? Address = null
);

public record RequestLinkRequest(
    int PatientId,
    string Relationship,
    string FullName,
    DateOnly DateOfBirth,
    string? NationalId = null,
    string? InsuranceCardNumber = null,
    string? PhoneNumber = null
);

