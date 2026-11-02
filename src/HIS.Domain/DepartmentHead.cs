namespace HIS.Domain;

public class DepartmentHead
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int DepartmentId { get; set; }
    public string FullName { get; set; } = string.Empty;

    public Account Account { get; set; } = null!;
    public Department Department { get; set; } = null!;
}
