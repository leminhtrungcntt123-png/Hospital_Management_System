namespace HIS.Domain;

public class InsurancePlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal CoveragePct { get; set; }
    public ReferralType? ReferralType { get; set; }

    // Navigation properties
    public ICollection<InsuranceCard> InsuranceCards { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
}
