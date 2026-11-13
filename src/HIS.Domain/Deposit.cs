namespace HIS.Domain;

/// <summary>
/// Quản lý tiền tạm ứng viện phí (nhập viện, chỉ định xét nghiệm, phẫu thuật...).
/// Liên kết bắt buộc với bệnh nhân, lượt khám tùy chọn.
/// </summary>
public class Deposit
{
    public int Id { get; set; }
    /// <summary>Mã phiếu tạm ứng dạng TU-00000001, sinh từ sequence DB.</summary>
    public string DepositCode { get; set; } = null!;
    public int PatientId { get; set; }
    public int? AppointmentId { get; set; }
    public decimal Amount { get; set; }
    public decimal UsedAmount { get; set; } = 0m;
    public decimal RefundedAmount { get; set; } = 0m;
    public decimal Balance => Amount - UsedAmount - RefundedAmount;
    public DepositStatus Status { get; set; } = DepositStatus.Active;
    public PaymentMethod PaymentMethod { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Purpose { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Note { get; set; }
    public int? CashierId { get; set; }
    public int? ShiftId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public Account? Cashier { get; set; }
    public CashierShift? Shift { get; set; }
    public ICollection<DepositUsage> Usages { get; set; } = new List<DepositUsage>();
}

