using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Interfaces;

public interface IBookingService
{
    // Lấy danh sách chuyên khoa đang hoạt động kèm số chỗ còn trong tuần
    Task<List<BookingDepartmentDto>> GetActiveDepartmentsAsync();

    // Lấy thông tin chi tiết chuyên khoa theo mã định danh (Id)
    Task<BookingDepartmentDto?> GetDepartmentByIdAsync(int id);

    // Lấy trạng thái khả dụng của các ngày trong tuần
    Task<List<BookingDayDto>> GetDaysAsync(int departmentId, DateOnly from, int count);

    // Lấy danh sách ca khám và số chỗ trống theo chuyên khoa và ngày
    Task<List<BookingShiftDto>> GetShiftsAsync(int departmentId, DateOnly date);

    // Lấy danh sách khung giờ còn suất đặt online (A) trong một ca của chuyên khoa
    Task<List<BookingSlotDto>> GetSlotsAsync(int departmentId, DateOnly date, string shiftKey, CancellationToken cancellationToken = default);

    // Lấy chi phí khám bệnh của chuyên khoa
    Task<decimal> GetExaminationFeeAsync(int departmentId);

    // Tạo mới lịch hẹn khám và giữ một suất A (đặt online) của khung giờ đã chọn
    Task<Appointment> CreateAppointmentAsync(
        int accountId,
        int patientId,
        int slotId,
        CancellationToken cancellationToken = default);

    // Bệnh nhân tự hủy lịch hẹn web của mình, có xét hạn mức hủy muộn trong tháng
    Task<CancelAppointmentResult> CancelAppointmentAsync(
        int appointmentId,
        int accountId,
        CancellationToken cancellationToken = default);

    // Lịch hẹn web do tài khoản này đặt, mới nhất trước, dùng cho màn "Lịch khám của tôi"
    Task<List<BookingAppointmentDto>> GetAppointmentsByAccountAsync(int accountId, CancellationToken cancellationToken = default);
}
