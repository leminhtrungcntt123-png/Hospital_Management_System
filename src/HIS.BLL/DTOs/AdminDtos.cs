using System.ComponentModel.DataAnnotations;

namespace HIS.BLL.DTOs;

public sealed class CreateAdminAccountRequest
{
    [Required, RegularExpression(@"^[a-zA-Z0-9_.]{3,50}$")]
    public string Username { get; set; } = "";

    [Required, StringLength(72, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [StringLength(100)]
    public string? FullName { get; set; }

    private string? _email;

    [EmailAddress, StringLength(100)]
    public string? Email
    {
        get => _email;
        set => _email = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    [Range(1, 5)]
    public int RoleId { get; set; }

    public int? DepartmentId { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    [StringLength(15)]
    public string? PhoneNumber { get; set; }
}

public sealed class DepartmentCreateRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [StringLength(255)]
    public string? Description { get; set; }
}

public record AccountActivationRequest(
    int AccountId,
    bool IsActive);

public record ScheduleDecisionRequest(
    int ExceptionId,
    bool Approve,
    int? ReplacementDoctorId);

public record AdminAccountRow(
    int Id,
    string Username,
    string Name,
    string Role,
    string RoleName,
    string? Department,
    bool IsActive,
    bool CanChangeStatus);

public record RevenueDay(
    DateOnly Date,
    decimal Deposits,
    decimal Payments,
    decimal Refunds,
    decimal Revenue,
    int Visits);

public record RevenueReport(
    DateOnly From,
    DateOnly To,
    decimal Deposits,
    decimal Payments,
    decimal Refunds,
    decimal Revenue,
    int Visits,
    IReadOnlyList<RevenueDay> Days);

public record AdminOption(
    int Id,
    string Name);

public record AdminRoleOption(
    int Id,
    string Code,
    string Name,
    bool RequiresName,
    bool RequiresDepartment,
    bool RequiresPatient);

public record ScheduleRequestRow(
    int Id,
    string Doctor,
    string Department,
    DateOnly Date,
    string Shift,
    string Type,
    string? Reason,
    string Status,
    string? Replacement,
    bool CanApprove,
    bool CanReject,
    IReadOnlyList<AdminOption> Replacements);

public record ScheduleDecisionResult(
    int Transferred,
    int NeedsRebooking,
    int ExpiredHolds);
