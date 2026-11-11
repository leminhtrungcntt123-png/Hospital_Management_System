using HIS.Domain;

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

public record PatientProfileItemDto(
    int PatientId,
    string FullName,
    DateOnly DateOfBirth,
    Gender? Gender,
    string PhoneNumber,
    string? Email,
    string? Address,
    string? MedicalCode,
    string Relationship,
    string RelationshipLabel
);

public record AccountProfilesOverviewDto(
    int AccountId,
    string Username,
    string? AccountEmail,
    PatientProfileItemDto? SelfProfile,
    IReadOnlyList<PatientProfileItemDto> Relatives
);

public sealed class SavePersonalProfileRequest
{
    public string FullName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string? Email { get; set; }
    public string? Address { get; set; }
}

public sealed class AddRelativeProfileRequest
{
    public string FullName { get; set; } = "";
    public Gender? Gender { get; set; }
    public PatientRelationship Relationship { get; set; } = PatientRelationship.Other;
    public DateOnly DateOfBirth { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string? Email { get; set; }
    public string? MedicalCode { get; set; }
}


