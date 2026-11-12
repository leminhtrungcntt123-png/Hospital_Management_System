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

public record AdminRoomRow(
    int Id,
    int DepartmentId,
    string DepartmentName,
    string RoomName,
    string RoomNumber,
    int ActiveSchedulesCount);

public sealed class CreateRoomRequest
{
    [Required, Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn phòng ban/khoa hợp lệ.")]
    public int DepartmentId { get; set; }

    [Required(ErrorMessage = "Tên phòng khám không được để trống."), StringLength(100, MinimumLength = 2, ErrorMessage = "Tên phòng từ 2 đến 100 ký tự.")]
    public string RoomName { get; set; } = "";

    [Required(ErrorMessage = "Số phòng không được để trống."), StringLength(20, MinimumLength = 1, ErrorMessage = "Số phòng từ 1 đến 20 ký tự.")]
    public string RoomNumber { get; set; } = "";
}

public record DeleteRoomRequest(int RoomId);

public record AdminServiceRow(
    int Id,
    int ServiceTypeId,
    string ServiceTypeName,
    string Name,
    decimal Price,
    bool IsActive);

public sealed class UpsertServiceRequest
{
    public int? Id { get; set; }

    [Required, Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn loại dịch vụ hợp lệ.")]
    public int ServiceTypeId { get; set; }

    [Required(ErrorMessage = "Tên dịch vụ không được để trống."), StringLength(150, MinimumLength = 2, ErrorMessage = "Tên dịch vụ từ 2 đến 150 ký tự.")]
    public string Name { get; set; } = "";

    [Range(0, 1000000000, ErrorMessage = "Đơn giá dịch vụ phải từ 0 đến 1.000.000.000 VNĐ.")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}

public record AdminMedicationRow(
    int Id,
    string Name,
    string? ActiveIngredient,
    string? Strength,
    string Unit,
    decimal Price,
    bool IsActive);

public sealed class UpsertMedicationRequest
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Tên thuốc không được để trống."), StringLength(150, MinimumLength = 2, ErrorMessage = "Tên thuốc từ 2 đến 150 ký tự.")]
    public string Name { get; set; } = "";

    [StringLength(150, ErrorMessage = "Hoạt chất tối đa 150 ký tự.")]
    public string? ActiveIngredient { get; set; }

    [StringLength(50, ErrorMessage = "Hàm lượng tối đa 50 ký tự.")]
    public string? Strength { get; set; }

    [Required(ErrorMessage = "Đơn vị tính không được để trống."), StringLength(30, MinimumLength = 1, ErrorMessage = "Đơn vị tính tối đa 30 ký tự.")]
    public string Unit { get; set; } = "";

    [Range(0, 1000000000, ErrorMessage = "Đơn giá thuốc phải từ 0 đến 1.000.000.000 VNĐ.")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}

public record AdminAuditLogRow(
    long Id,
    int? AccountId,
    string Username,
    string FullName,
    string Action,
    string? PatientName,
    int? MedicalRecordId,
    DateTime OccurredAt,
    string? IpAddress);

public record DepartmentVisitStat(
    int DepartmentId,
    string DepartmentName,
    int Visits,
    decimal Revenue);

