namespace HIS.Domain;

public class DoctorSchedule
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public int RoomId { get; set; }
    public ClinicRoom Room { get; set; } = null!;

    // 0 = Chủ nhật, 1 = Thứ hai ... 6 = Thứ bảy (khớp CHECK day_of_week BETWEEN 0 AND 6)
    public DayOfWeek DayOfWeek { get; set; }

    public ExamShift Shift { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int SlotDurationMinutes { get; set; } = 60;

    // Tỉ lệ suất dành cho kênh A (đặt online); suất còn lại cho T (đến trực tiếp).
    public int OnlineRatioPct { get; set; } = 60;

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}
