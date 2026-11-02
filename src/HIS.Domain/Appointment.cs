namespace HIS.Domain;

public class Appointment
{
    public int Id { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public int? BookedBy { get; set; }
    public int? SlotId { get; set; }
    public int? DoctorId { get; set; }
    public int? DepartmentId { get; set; }
    public int? RoomId { get; set; }
    public int StatusId { get; set; }
    public BookingChannel BookingChannel { get; set; } = BookingChannel.Web;
    public VisitType VisitType { get; set; } = VisitType.Scheduled;
    public int PriorityLevel { get; set; }
    public string? QueueNumber { get; set; }
    public string? Reason { get; set; }
    public DateTime? HoldUntil { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CalledAt { get; set; }
    public int? ParentAppointmentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Patient Patient { get; set; } = null!;
    public Account? BookedByAccount { get; set; }
    public AppointmentSlot? Slot { get; set; }
    public Doctor? Doctor { get; set; }
    public Department? Department { get; set; }
    public ClinicRoom? Room { get; set; }
    public AppointmentStatus Status { get; set; } = null!;
    public Appointment? ParentAppointment { get; set; }

    public ICollection<Appointment> ChildAppointments { get; set; } = [];
    public ICollection<AppointmentStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
    public VitalSigns? VitalSigns { get; set; }
    public MedicalRecord? MedicalRecord { get; set; }
    public Invoice? Invoice { get; set; }
}
