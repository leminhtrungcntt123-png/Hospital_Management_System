namespace HIS.Domain;

public class AccessLog
{
    public long Id { get; set; }
    public int? AccountId { get; set; }
    public int? PatientId { get; set; }
    public int? MedicalRecordId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string Severity { get; set; } = "INFO";
    public DateTime OccurredAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }

    public Account? Account { get; set; }
    public Patient? Patient { get; set; }
    public MedicalRecord? MedicalRecord { get; set; }
}
