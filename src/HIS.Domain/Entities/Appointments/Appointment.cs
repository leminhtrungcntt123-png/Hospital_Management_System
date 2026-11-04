namespace HIS.Domain;

public class Appointment
{
    public int Id { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int? BookedBy { get; set; }
    public Account? BookedByAccount { get; set; }
    public int? SlotId { get; set; }
    public AppointmentSlot? Slot { get; set; }
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? RoomId { get; set; }
    public ClinicRoom? Room { get; set; }
    public int StatusId { get; set; }
    public AppointmentStatus Status { get; set; } = null!;
    public BookingChannel BookingChannel { get; set; } = BookingChannel.Web;
    public VisitType VisitType { get; set; } = VisitType.Scheduled;
    public int PriorityLevel { get; set; } = 0;

    // Vị trí cố định trong khung giờ: 1..online_quota là suất A (đặt online),
    // online_quota+1..capacity là suất T (đến trực tiếp). NULL khi bị giải phóng sau mốc cắt.
    public int? SlotPosition { get; set; }

    // Số lần đã bị bệnh nhân ưu tiên khẩn (mức 2) chen lên trước; chạm trần thì không bị chen thêm.
    public int OvertakenCount { get; set; } = 0;

    public string? QueueNumber { get; set; }
    public string? Reason { get; set; }
    public DateTime? HoldUntil { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CalledAt { get; set; }
    public int? ParentAppointmentId { get; set; }
    public Appointment? ParentAppointment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Appointment> ChildAppointments { get; set; } = [];
    public ICollection<AppointmentStatusHistory> StatusHistories { get; set; } = [];
    public VitalSigns? VitalSigns { get; set; }
    public MedicalRecord? MedicalRecord { get; set; }
    public Invoice? Invoice { get; set; }
}
