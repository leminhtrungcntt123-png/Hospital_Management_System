namespace HIS.Domain;

public class Nurse
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int? DepartmentId { get; set; }
    public string FullName { get; set; } = string.Empty;

    public Account Account { get; set; } = null!;
    public Department? Department { get; set; }
    public ICollection<VitalSigns> RecordedVitalSigns { get; set; } = [];
}
