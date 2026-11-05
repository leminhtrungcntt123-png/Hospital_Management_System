namespace HIS.BLL.DTOs;

// DTO danh sách Khoa phục vụ màn hình Đặt lịch (có thêm SlotsThisWeek)
public class BookingDepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SlotsThisWeek { get; set; }
}

public class BookingDayDto
{
    public DateOnly Date { get; set; }
    public string State { get; set; } = "available"; // "off", "full", "few", "available"
}

/// <summary>Khung giờ khám ở bước "chọn giờ" của web. SlotsLeft là số suất A (đặt online) còn lại của khung.</summary>
public class BookingSlotDto
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;          // "07:30 – 08:30"
    public string ShiftLabel { get; set; } = string.Empty;     // "Ca sáng", "Ca chiều"
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Capacity { get; set; }
    public int OnlineQuota { get; set; }
    public int SlotsLeft { get; set; }
    public string Status { get; set; } = "AVAILABLE";          // "AVAILABLE", "FULL"
    public bool IsSelectable => SlotsLeft > 0;
    public int DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string? DoctorTitle { get; set; }
    public string? RoomName { get; set; }
}

public class BookingShiftDto
{
    public string Key { get; set; } = string.Empty;     // "MORNING", "AFTERNOON"
    public string Label { get; set; } = string.Empty;   // "Ca sáng", "Ca chiều"
    public string TimeRange { get; set; } = string.Empty; // "07:30 – 11:30"
    public int SlotsLeft { get; set; }                    // tổng suất A còn lại của các khung giờ trong ca
    public string Status { get; set; } = "AVAILABLE";   // "AVAILABLE", "FULL", "NO_SCHEDULE"
    public bool IsSelectable => Status == "AVAILABLE" && SlotsLeft > 0;
    public int? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string? DoctorTitle { get; set; }
    public string? RoomName { get; set; }
}

/// <summary>Một lịch hẹn bệnh nhân đặt trên web, dùng cho màn "Lịch khám của tôi".</summary>
public class BookingAppointmentDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;          // PENDING, CONFIRMED, ...
    public string StatusLabel { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string QueueNumber { get; set; } = string.Empty;      // "A06"; rỗng khi suất A đã bị giải phóng
    public DateTime? SlotStartTime { get; set; }
    public DateTime? SlotEndTime { get; set; }

    /// <summary>Chỉ PENDING/CONFIRMED và chưa tới giờ khám mới tự hủy được trên web.</summary>
    public bool IsCancellable { get; set; }
}

/// <summary>Kết quả hủy lịch hẹn trên web: báo cho bệnh nhân biết lần hủy có tính vào hạn mức tháng hay không.</summary>
public sealed record CancelAppointmentResult(
    bool Success,
    string Message,
    bool IsLateCancel = false,
    int LateCancelsUsedThisMonth = 0);
