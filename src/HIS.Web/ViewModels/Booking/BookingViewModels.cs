using HIS.BLL.DTOs;

namespace HIS.Web.ViewModels.Booking;

// ViewModel của từng màn hình đặt lịch (chỉ phục vụ hiển thị ở HIS.Web).

/// <summary>Bước "chọn ngày khám" của luồng theo ngày: lịch toàn viện trong cửa sổ đặt được.</summary>
public class ChonNgayViewModel
{
    public List<DayVm> Days { get; set; } = new();
    public DateOnly SelectedDate { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
}

/// <summary>Bước "chọn chuyên khoa". OnDate khác null khi đi theo luồng ngày (số chỗ chỉ tính trong ngày đó).</summary>
public class ChonKhoaViewModel
{
    public List<DepartmentVm> Departments { get; set; } = new();
    public int? SelectedDepartmentId { get; set; }
    public DateOnly? OnDate { get; set; }
}

/// <summary>
/// Màn "chọn bác sĩ". Khi Date khác null (luồng theo ngày) mỗi bác sĩ kèm trọn khung giờ
/// của người đó trong ngày nên bệnh nhân chốt luôn cả hai trong một màn.
/// </summary>
public class ChonBacsiViewModel
{
    public DepartmentVm? Department { get; set; }
    public DateOnly? Date { get; set; }

    /// <summary>Chỉ luồng theo chuyên khoa mới có thẻ "Bác sĩ bất kỳ".</summary>
    public bool ShowAnyDoctor { get; set; }

    public List<DoctorVm> Doctors { get; set; } = new();
    public List<DoctorSlotsVm> DoctorSlots { get; set; } = new();

    public int? SelectedDoctorId { get; set; }
    public int? SelectedSlotId { get; set; }
}

/// <summary>Bước "ngày & khung giờ" — gộp thành một màn nên bệnh nhân không phải chọn ca riêng.</summary>
public class ChonNgayGioViewModel
{
    public DepartmentVm? Department { get; set; }
    public DoctorVm? Doctor { get; set; }
    public List<DayVm> Days { get; set; } = new();
    public DateOnly SelectedDate { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
    public List<SlotVm> Slots { get; set; } = new();
    public int? SelectedSlotId { get; set; }

    /// <summary>Luồng hiện hành có bước chọn chuyên khoa / chọn bác sĩ phía trước không, để hiện nút "Đổi".</summary>
    public bool CanChangeDepartment { get; set; }
    public bool CanChangeDoctor { get; set; }
}

/// <summary>Model của partial _SlotChips / _SlotList (khung giờ của một ngày, booking.js tải lại bằng fetch).</summary>
public class SlotListViewModel
{
    public DateOnly Date { get; set; }
    public List<SlotVm> Slots { get; set; } = new();
    public int? SelectedSlotId { get; set; }

    /// <summary>Hiện tên bác sĩ trên chip khi bệnh nhân chọn "Bác sĩ bất kỳ" (chưa chốt người khám).</summary>
    public bool ShowDoctor { get; set; }
}

public class ChonNguoiKhamViewModel
{
    public List<PatientVm> Patients { get; set; } = new();
    public int? SelectedPatientId { get; set; }
}

public class XacNhanViewModel
{
    public PatientVm Patient { get; set; } = new();
    public DepartmentVm Department { get; set; } = new();
    public DateOnly Date { get; set; }
    public SlotVm Slot { get; set; } = new();

    /// <summary>Phí khám dự kiến của chuyên khoa; bệnh nhân đóng tại quầy khi đến khám.</summary>
    public decimal Fee { get; set; }

    /// <summary>Hai khai báo bảo hiểm lấy từ Session, bấm lại được ngay ở bước này.</summary>
    public bool HasHealthInsurance { get; set; }
    public bool HasPrivateInsurance { get; set; }
}

public class ThanhCongViewModel
{
    public string Code { get; set; } = string.Empty;

    // CONFIRMED (đã xác nhận) hoặc PENDING (mới tiếp nhận, chờ xác nhận).
    public string Status { get; set; } = "CONFIRMED";
    public PatientVm Patient { get; set; } = new();
    public string DepartmentName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string ShiftLabel { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;

    // Số thứ tự A## bệnh nhân sẽ thấy trên màn gọi ở quầy; rỗng nếu khung giờ chưa cấp số.
    public string QueueNumber { get; set; } = string.Empty;

    public DoctorVm? Doctor { get; set; }

    /// <summary>Tên bác sĩ của lượt hẹn; trang thành công và hộp thoại luôn hiện được tên này.</summary>
    public string DoctorName { get; set; } = string.Empty;

    public string Room { get; set; } = string.Empty;

    /// <summary>Quyết định hướng dẫn sau khi đặt: có BHYT thì ghé quầy, không thì lên thẳng phòng khám.</summary>
    public bool HasHealthInsurance { get; set; }

    /// <summary>Số tiền đã thu ở bước thanh toán; 0 khi chưa thanh toán.</summary>
    public decimal PaidAmount { get; set; }

    public DateTime? PaidAt { get; set; }
}

/// <summary>
/// Màn thanh toán phí khám: thông tin lượt hẹn, tài khoản nhận tiền và VietQR động.
/// </summary>
public class ThanhToanViewModel
{
    public AppointmentPaymentDto Appointment { get; set; } = new();
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string TransferContent => Appointment.Code;

    /// <summary>Đường dẫn ảnh VietQR đã sinh trong wwwroot/images/qrcode.</summary>
    public string QrImagePath { get; set; } = string.Empty;
}

// Màn "Lịch khám của tôi": các lượt hẹn web do tài khoản đang đăng nhập đặt.
public class MyAppointmentsViewModel
{
    public List<AppointmentVm> Appointments { get; set; } = new();
}

public class AppointmentVm
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public bool IsCancellable { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public string QueueNumber { get; set; } = string.Empty;

    /// <summary>Phí khám của lượt hẹn (hóa đơn), dùng cho dòng "Phí khám" và nút thanh toán.</summary>
    public decimal Amount { get; set; }

    /// <summary>Đang chờ chuyển khoản: hiện nút "Thanh toán ngay"; chưa trả thì chưa có số.</summary>
    public bool IsAwaitingPayment { get; set; }

    /// <summary>"BHYT · Bảo hiểm tư nhân"; rỗng khi người bệnh không khai báo gì.</summary>
    public string InsuranceSummary { get; set; } = string.Empty;
}
