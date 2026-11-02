namespace HIS.Domain;

public class AppointmentStatus
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsTerminal { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<AppointmentStatusHistory> OldStatusHistories { get; set; } = [];
    public ICollection<AppointmentStatusHistory> NewStatusHistories { get; set; } = [];
}
