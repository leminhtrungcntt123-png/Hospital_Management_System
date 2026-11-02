namespace HIS.Domain;

public class DutyShift
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int DepartmentId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Note { get; set; }
    public int? AssignedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public Account? AssignedByAccount { get; set; }
}
