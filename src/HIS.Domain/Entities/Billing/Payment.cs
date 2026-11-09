namespace HIS.Domain;

public class Payment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public decimal Amount { get; set; }
    public PaymentType PaymentType { get; set; } = PaymentType.Payment;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public int? CashierId { get; set; }
    public Account? Cashier { get; set; }
    public string? ReceiptEmail { get; set; }
    public string? Note { get; set; }
    public DateTime PaidAt { get; set; }
}
