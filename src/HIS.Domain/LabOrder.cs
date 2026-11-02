namespace HIS.Domain;

public class LabOrder
{
    public int Id { get; set; }
    public int MedicalRecordId { get; set; }
    public int ServiceId { get; set; }
    public string? Note { get; set; }
    public LabOrderStatus Status { get; set; } = LabOrderStatus.Pending;
    public DateTime CreatedAt { get; set; }

    public MedicalRecord MedicalRecord { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public LabResult? LabResult { get; set; }
}
