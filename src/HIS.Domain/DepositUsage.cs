namespace HIS.Domain;

/// <summary>
/// Nhật ký cấn trừ tiền tạm ứng vào hóa đơn viện phí.
/// </summary>
public class DepositUsage
{
    public int Id { get; set; }
    public int DepositId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime UsedAt { get; set; }
    public int? CashierId { get; set; }

    public Deposit Deposit { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
    public Account? Cashier { get; set; }
}

