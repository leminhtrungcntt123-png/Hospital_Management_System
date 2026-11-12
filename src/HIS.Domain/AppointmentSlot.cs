namespace HIS.Domain;

public class AppointmentSlot
{
    public int Id { get; set; }
    public int? ScheduleId { get; set; }
    public int DoctorId { get; set; }
    public int RoomId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Capacity { get; set; } = 1;
    public int BookedCount { get; set; }
    public SlotStatus Status { get; set; } = SlotStatus.Available;
    public int Version { get; set; }

    public DoctorSchedule? Schedule { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public ClinicRoom Room { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = [];
}
