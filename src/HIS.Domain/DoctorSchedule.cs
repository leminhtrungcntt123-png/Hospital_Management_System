namespace HIS.Domain;

public class DoctorSchedule
{
    public int Id { get; set; }
    public int? PeriodId { get; set; }
    public int DoctorId { get; set; }
    public int DepartmentId { get; set; }
    public int? RoomId { get; set; }
    public DateOnly WorkDate { get; set; }
    public ExamShift Shift { get; set; }
    public TimeOnly StartTime { get; set; } = new TimeOnly(7, 30);
    public TimeOnly EndTime { get; set; } = new TimeOnly(11, 30);
    public int SlotDurationMinutes { get; set; } = 30;
    public int MaxPatients { get; set; } = 40;
    public int OnlineSlots { get; set; } = 28;
    public bool IsHotShift { get; set; } = false;
    public DoctorScheduleStatus Status { get; set; } = DoctorScheduleStatus.Bidding;
    public string? DoctorNote { get; set; }
    public int? ReallocatedFromId { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public SchedulingPeriod? Period { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public ClinicRoom? Room { get; set; }
    public DoctorSchedule? ReallocatedFrom { get; set; }
    public Account? ReviewedByAccount { get; set; }

    public ICollection<DoctorSchedule> ReallocatedSchedules { get; set; } = [];
    public ICollection<AppointmentSlot> AppointmentSlots { get; set; } = [];
    public ICollection<ScheduleException> ScheduleExceptions { get; set; } = [];
}
