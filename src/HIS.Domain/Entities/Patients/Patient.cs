namespace HIS.Domain;

/// <summary>
/// Hồ sơ người khám, tồn tại độc lập với tài khoản đăng nhập.
/// Quan hệ với tài khoản nằm ở AccountPatientProfile; bệnh nhân vãng lai
/// có thể không có dòng liên kết nào.
/// </summary>
public class Patient
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender? Gender { get; set; }

    /// <summary>CCCD; NULL với trẻ chưa có căn cước.</summary>
    public string? NationalId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ICollection<AccountPatientProfile> AccountLinks { get; set; } = [];
    public ICollection<InsuranceCard> InsuranceCards { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<AccessLog> AccessLogs { get; set; } = [];
}
