namespace HIS.Domain;

public class Medication
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ActiveIngredient { get; set; }
    public string? Strength { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Thuốc nằm trong danh mục BHYT chi trả. false = người bệnh trả 100%.</summary>
    public bool IsInsuranceCovered { get; set; } = true;

    public ICollection<PrescriptionItem> PrescriptionItems { get; set; } = [];
}
