namespace HIS.Domain;

public class DoctorSchedule
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public int RoomId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public ExamShift Shift { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int SlotDurationMinutes { get; set; } = 15;
    public int MaxPatients { get; set; } = 40;
    public int OnlineSlots { get; set; } = 28;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    public Doctor Doctor { get; set; } = null!;
    public ClinicRoom Room { get; set; } = null!;
}
