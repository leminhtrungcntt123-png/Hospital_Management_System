namespace HIS.Domain;

public class InsuranceCard
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public int InsurancePlanId { get; set; }
    public string? RegisteredFacility { get; set; }
    public bool IsRegisteredHere { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Patient Patient { get; set; } = null!;
    public InsurancePlan InsurancePlan { get; set; } = null!;
    public ICollection<Invoice> Invoices { get; set; } = [];
}
