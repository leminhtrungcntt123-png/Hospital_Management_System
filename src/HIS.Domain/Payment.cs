namespace HIS.Domain;

public class Payment
{
    public int Id { get; set; }
    public int? InvoiceId { get; set; }
    public int? DepositId { get; set; }
    public int? OriginalPaymentId { get; set; }
    public decimal Amount { get; set; }
    public PaymentType PaymentType { get; set; } = PaymentType.Payment;
    public PaymentMethod PaymentMethod { get; set; }
    public int? CashierId { get; set; }
    public string? ReceiptEmail { get; set; }
    public string? Note { get; set; }
    public string? RefundReason { get; set; }
    /// <summary>Mã giao dịch nội bộ dạng PT-00000001, sinh bằng sequence DB.</summary>
    public string TransactionCode { get; set; } = null!;
    /// <summary>Mã giao dịch ngân hàng (bắt buộc khi chuyển khoản).</summary>
    public string? ReferenceCode { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Confirmed;
    /// <summary>Khóa chống ghi trùng khi bấm đúp / mạng chậm.</summary>
    public string? IdempotencyKey { get; set; }
    public int? ShiftId { get; set; }
    public DateTime PaidAt { get; set; }

    public Invoice? Invoice { get; set; }
    public Deposit? Deposit { get; set; }
    public Payment? OriginalPayment { get; set; }
    public Account? Cashier { get; set; }
    public CashierShift? Shift { get; set; }
}
