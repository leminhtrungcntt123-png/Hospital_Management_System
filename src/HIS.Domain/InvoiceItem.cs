namespace HIS.Domain;

public class InvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public InvoiceCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public Invoice Invoice { get; set; } = null!;
}
