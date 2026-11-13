namespace HIS.Domain;

public class Patient
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? NationalId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Allergies { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<AccountPatientProfile> AccountLinks { get; set; } = [];
    public ICollection<InsuranceCard> InsuranceCards { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Deposit> Deposits { get; set; } = [];
    public ICollection<AccessLog> AccessLogs { get; set; } = [];
}
