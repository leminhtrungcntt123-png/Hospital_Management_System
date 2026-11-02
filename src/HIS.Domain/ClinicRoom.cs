namespace HIS.Domain;

public class ClinicRoom
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;

    public Department Department { get; set; } = null!;
    public ICollection<DoctorSchedule> Schedules { get; set; } = [];
    public ICollection<AppointmentSlot> AppointmentSlots { get; set; } = [];
    public ICollection<QueueCounter> QueueCounters { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
}
