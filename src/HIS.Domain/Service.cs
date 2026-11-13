namespace HIS.Domain;

public class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ServiceTypeId { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Dịch vụ nằm trong danh mục BHYT chi trả. false = người bệnh trả 100%.</summary>
    public bool IsInsuranceCovered { get; set; } = true;

    public ServiceType ServiceType { get; set; } = null!;
    public ICollection<LabOrder> LabOrders { get; set; } = [];
}
