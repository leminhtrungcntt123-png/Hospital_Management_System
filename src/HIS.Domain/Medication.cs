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

    public ICollection<PrescriptionItem> PrescriptionItems { get; set; } = [];
}
