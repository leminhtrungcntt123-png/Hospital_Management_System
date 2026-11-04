namespace HIS.Domain;

public class AppointmentSlot
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public int RoomId { get; set; }
    public ClinicRoom Room { get; set; } = null!;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Capacity { get; set; } = 1;
    public int BookedCount { get; set; } = 0;

    // Suất dành cho kênh A (đặt online) = round(capacity * online_ratio_pct / 100);
    // capacity - online_quota là suất T (đến trực tiếp). Suất A còn trống được mở cho T.
    public int OnlineQuota { get; set; } = 0;
    public int OnlineBooked { get; set; } = 0;

    public SlotStatus Status { get; set; } = SlotStatus.Available;

    // Optimistic locking: tăng mỗi lần booked_count/status thay đổi để chặn 2 người cùng giữ 1 chỗ
    public int Version { get; set; } = 0;

    // Navigation properties
    public ICollection<Appointment> Appointments { get; set; } = [];
}
