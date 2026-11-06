namespace HIS.BLL.DTOs;

// DTO danh sách Khoa phục vụ màn hình Đặt lịch
public class BookingDepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Số suất đặt online còn lại trong phạm vi đang xét: cả cửa sổ đặt được khi liệt kê khoa,
    /// hoặc riêng một ngày khi gọi GetDepartmentsOnDateAsync. 0 = khoa đó không còn lịch đặt được.
    /// </summary>
    public int SlotsAvailable { get; set; }
}

public class BookingDayDto
{
    public DateOnly Date { get; set; }

    /// <summary>"off" (nghỉ/không trực), "full" (kín suất online), "few", "available", "closed" (ngoài cửa sổ/quá cutoff).</summary>
    public string State { get; set; } = "available";
}

/// <summary>
/// Bác sĩ ở màn "chọn bác sĩ". SlotsAvailable là suất A (đặt online) còn lại, không phải tổng ghế,
/// vì phần còn lại của mỗi khung dành cho bệnh nhân đến quầy lấy số T.
/// </summary>
public class BookingDoctorDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>Chuyên môn sâu trong doctors.specialty (VD: "Nội tiêu hóa").</summary>
    public string Specialty { get; set; } = string.Empty;

    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    /// <summary>Các ngày trong tuần bác sĩ có lịch trực đang hiệu lực, dạng "T2 · T5 · T7".</summary>
    public string DutyDays { get; set; } = string.Empty;

    public int SlotsAvailable { get; set; }

    /// <summary>Ngày gần nhất còn suất đặt online; null khi không còn lịch đặt được trong cửa sổ.</summary>
    public DateOnly? NextAvailableDate { get; set; }
}

/// <summary>Một bác sĩ kèm các khung giờ của họ trong một ngày — màn "Bác sĩ & khung giờ".</summary>
public class BookingDoctorSlotsDto
{
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public List<BookingSlotDto> Slots { get; set; } = new();

    public int SlotsLeft => Slots.Sum(s => s.SlotsLeft);
}

/// <summary>Khung giờ khám ở màn "chọn ngày & giờ". SlotsLeft là số suất A còn lại của khung.</summary>
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
    public int DepartmentId { get; set; }
    public int DoctorId { get; set; }
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

    /// <summary>Hai khai báo bảo hiểm người bệnh tick ở bước xác nhận; rỗng/false thì UI không hiển thị gì thêm.</summary>
    public bool HasHealthInsurance { get; set; }
    public bool HasPrivateInsurance { get; set; }
    public DateTime? SlotStartTime { get; set; }
    public DateTime? SlotEndTime { get; set; }

    /// <summary>Phí khám phải trả của lượt hẹn; 0 khi chưa có hóa đơn.</summary>
    public decimal Amount { get; set; }

    /// <summary>Đang chờ chuyển khoản; chưa trả thì chưa có số thứ tự và có thể hủy để trả suất.</summary>
    public bool IsAwaitingPayment { get; set; }

    /// <summary>Chỉ PENDING/CONFIRMED và chưa tới giờ khám mới tự hủy được trên web.</summary>
    public bool IsCancellable { get; set; }
}

/// <summary>Kết quả hủy lịch hẹn trên web: thành công hay nêu lý do bị từ chối (quá hạn hủy).</summary>
public sealed record CancelAppointmentResult(bool Success, string Message);

/// <summary>
/// Thông tin một lượt hẹn ở bước thanh toán: phí khám và dữ liệu để dựng VietQR.
/// Status là mã trạng thái hiện tại để màn hình biết đang chờ trả tiền hay đã thanh toán.
/// </summary>
public class AppointmentPaymentDto
{
    public int AppointmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public bool HasHealthInsurance { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string ShiftLabel { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string QueueNumber { get; set; } = string.Empty;
}

/// <summary>Kết quả xác nhận chuyển khoản: số thứ tự chỉ được cấp khi thanh toán đã được ghi nhận.</summary>
public sealed record PaymentConfirmationResult(bool Success, string Message, string QueueNumber = "", decimal Amount = 0m);
