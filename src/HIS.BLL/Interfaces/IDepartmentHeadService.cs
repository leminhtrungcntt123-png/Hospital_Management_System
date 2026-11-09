using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Interfaces;

/// <summary>
/// Dịch vụ nghiệp vụ dành riêng cho Trưởng khoa (DEPARTMENT_HEAD / DEPT_HEAD):
/// - Phê duyệt hoặc từ chối các đề nghị ngoại lệ (nghỉ phép, đổi ca) của bác sĩ trong khoa.
/// - Phân công lịch trực đêm (duty_shifts) cho nhân sự trong khoa.
/// - Thiết lập ma trận lịch khám mẫu (doctor_schedules).
/// - Giám sát toàn bộ hồ sơ bệnh án trong khoa (medical_records), ghi nhận access_logs.
/// - Xem tổng quan vận hành, tiến độ khám và điều phối của khoa.
/// </summary>
public interface IDepartmentHeadService
{
    // Xác định ID khoa từ tài khoản Trưởng khoa
    Task<int> ResolveDepartmentIdByAccountIdAsync(int accountId, CancellationToken ct = default);

    // 1. Quản lý đề nghị ngoại lệ (Nghỉ phép / Đổi ca)
    Task<IReadOnlyList<DepartmentExceptionItemDto>> GetExceptionsAsync(int departmentId, ScheduleExceptionStatus? status = null, CancellationToken ct = default);
    Task<Result> ApproveExceptionAsync(ApproveExceptionRequest request, CancellationToken ct = default);
    Task<Result> RejectExceptionAsync(RejectExceptionRequest request, CancellationToken ct = default);

    // 2. Thiết lập lịch khám mẫu & hạn mức
    Task<IReadOnlyList<DepartmentScheduleItemDto>> GetSchedulesAsync(int departmentId, CancellationToken ct = default);
    Task<Result<int>> UpsertDoctorScheduleAsync(UpsertDoctorScheduleRequest request, CancellationToken ct = default);
    Task<Result> DeleteDoctorScheduleAsync(int scheduleId, int departmentId, CancellationToken ct = default);

    // 3. Phân công ca trực đêm nhân sự
    Task<IReadOnlyList<DepartmentDutyShiftDto>> GetDutyShiftsAsync(int departmentId, CancellationToken ct = default);
    Task<Result<int>> AssignDutyShiftAsync(AssignDutyShiftRequest request, CancellationToken ct = default);
    Task<Result> DeleteDutyShiftAsync(int dutyShiftId, int departmentId, CancellationToken ct = default);

    // 4. Giám sát bệnh án trong khoa
    Task<IReadOnlyList<DepartmentMedicalRecordDto>> GetMedicalRecordsAsync(int departmentId, int viewerAccountId, string? ipAddress = null, CancellationToken ct = default);

    // 5. Tổng quan vận hành khoa
    Task<DepartmentOverviewDto> GetDepartmentOverviewAsync(int departmentId, CancellationToken ct = default);

    // 6. Danh sách nhân sự & phòng khám thuộc khoa để phục vụ điều phối
    Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsInDepartmentAsync(int departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<StaffListItemDto>> GetStaffInDepartmentAsync(int departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ClinicRoomDto>> GetRoomsInDepartmentAsync(int departmentId, CancellationToken ct = default);
}

