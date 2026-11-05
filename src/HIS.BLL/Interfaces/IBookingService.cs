using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Interfaces;

public interface IBookingService
{
    /// <summary>Ngày khám sớm nhất còn đặt được (ngày mai, hoặc ngày kia nếu đã qua cutoff hôm nay).</summary>
    DateOnly FirstBookableDate { get; }

    /// <summary>Ngày khám xa nhất còn đặt được: hôm nay + MaxAdvanceDays.</summary>
    DateOnly LastBookableDate { get; }

    /// <summary>Số ngày nguyên nằm trong cửa sổ đặt được, dùng để vẽ lịch.</summary>
    int BookableDayCount { get; }

    /// <summary>Ngày D còn đặt được khi chưa quá cutoff ngày liền trước và còn trong cửa sổ.</summary>
    bool IsBookableDate(DateOnly date);

    // ---------------- Ba đường vào đặt khám đều hội tụ về một slotId ----------------

    // Luồng "theo chuyên khoa": mọi khoa đang hoạt động kèm số chỗ còn trong cửa sổ đặt được
    Task<List<BookingDepartmentDto>> GetActiveDepartmentsAsync();

    // Luồng "theo ngày": chỉ những khoa còn mở đặt online trong đúng ngày đã chọn
    Task<List<BookingDepartmentDto>> GetDepartmentsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<BookingDepartmentDto?> GetDepartmentByIdAsync(int id);

    /// <summary>
    /// Bác sĩ cho màn "chọn bác sĩ": lọc theo khoa (luồng theo chuyên khoa/theo ngày) và
    /// chỉ giữ người còn khung đặt được trong ngày đã chọn (luồng theo ngày).
    /// </summary>
    Task<List<BookingDoctorDto>> GetDoctorsAsync(
        int? departmentId = null,
        DateOnly? date = null,
        CancellationToken cancellationToken = default);

    /// <summary>Bác sĩ của một khoa kèm mọi khung giờ của từng người trong một ngày — màn "Bác sĩ & khung giờ".</summary>
    Task<List<BookingDoctorSlotsDto>> GetDoctorSlotsAsync(
        int departmentId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trạng thái từng ngày trong cửa sổ đặt được: để trống bộ lọc là lịch của toàn viện,
    /// lọc theo bác sĩ thì ngày nghỉ của bác sĩ đó thành "off" (màn lịch của một bác sĩ).
    /// </summary>
    Task<List<BookingDayDto>> GetDaysAsync(
        DateOnly from,
        int count,
        int? departmentId = null,
        int? doctorId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khung giờ của một ngày, lọc theo khoa và/hoặc bác sĩ. Khung đã kín vẫn được trả về với
    /// Status = "FULL" để màn hình ghi nhãn "Đã đầy" thay vì ẩn đi.
    /// </summary>
    Task<List<BookingSlotDto>> GetSlotsAsync(
        DateOnly date,
        int? departmentId = null,
        int? doctorId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Một khung giờ theo Id để đối chiếu lại trước khi lưu (null nếu không tồn tại,
    /// đã Block, hoặc rơi vào ngày không còn đặt được).
    /// </summary>
    Task<BookingSlotDto?> GetSlotByIdAsync(int slotId, CancellationToken cancellationToken = default);

    // Lấy chi phí khám bệnh của chuyên khoa
    Task<decimal> GetExaminationFeeAsync(int departmentId);

    // Tạo mới lịch hẹn khám và giữ một suất A (đặt online) của khung giờ đã chọn.
    // Hai cờ bảo hiểm chỉ là khai báo của người bệnh để quầy phân luồng, không validate và không ảnh hưởng giá.
    Task<Appointment> CreateAppointmentAsync(
        int accountId,
        int patientId,
        int slotId,
        bool hasHealthInsurance = false,
        bool hasPrivateInsurance = false,
        CancellationToken cancellationToken = default);

    // Bệnh nhân tự hủy lịch hẹn web của mình; chỉ hủy được đến 23:59 ngày liền trước ngày khám
    Task<CancelAppointmentResult> CancelAppointmentAsync(
        int appointmentId,
        int accountId,
        CancellationToken cancellationToken = default);

    // Lịch hẹn web do tài khoản này đặt, mới nhất trước, dùng cho màn "Lịch khám của tôi"
    Task<List<BookingAppointmentDto>> GetAppointmentsByAccountAsync(int accountId, CancellationToken cancellationToken = default);
}
