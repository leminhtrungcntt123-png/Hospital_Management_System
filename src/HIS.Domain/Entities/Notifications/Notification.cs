namespace HIS.Domain;

/// <summary>
/// Thông báo gửi về TÀI KHOẢN ĐẶT LỊCH (booked_by), không gửi về hồ sơ người khám,
/// vì người đặt có thể là người nhà. Nội dung phải ghi rõ tên người khám.
/// </summary>
public class Notification
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
