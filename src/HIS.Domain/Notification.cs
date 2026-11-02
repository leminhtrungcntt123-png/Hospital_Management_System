namespace HIS.Domain;

public class Notification
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int? AppointmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Appointment? Appointment { get; set; }
}
