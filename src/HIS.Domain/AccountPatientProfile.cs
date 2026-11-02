namespace HIS.Domain;

public class AccountPatientProfile
{
    public int AccountId { get; set; }
    public int PatientId { get; set; }
    public PatientRelationship Relationship { get; set; }
    public LinkStatus Status { get; set; } = LinkStatus.Active;
    public bool CanViewRecords { get; set; }
    public int? CreatedBy { get; set; }
    public int? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
    public Account? CreatedByAccount { get; set; }
    public Account? VerifiedByAccount { get; set; }
}
