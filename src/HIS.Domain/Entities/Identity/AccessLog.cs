namespace HIS.Domain;

public class AccessLog
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    // Ghi vết khi truy cập hồ sơ của một người, kể cả hành động không gắn bệnh án cụ thể (LINK_PATIENT, REVOKE_LINK)
    public int? PatientId { get; set; }
    public Patient? Patient { get; set; }
    public int? MedicalRecordId { get; set; }
    public MedicalRecord? MedicalRecord { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? IpAddress { get; set; }
}
