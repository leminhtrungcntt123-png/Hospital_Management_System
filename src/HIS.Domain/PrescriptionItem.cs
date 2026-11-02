namespace HIS.Domain;

public class PrescriptionItem
{
    public int Id { get; set; }
    public int PrescriptionId { get; set; }
    public int MedicationId { get; set; }
    public string? Dosage { get; set; }
    public int Quantity { get; set; }
    public int? DurationDays { get; set; }
    public string? UsageInstructions { get; set; }

    public Prescription Prescription { get; set; } = null!;
    public Medication Medication { get; set; } = null!;
}
