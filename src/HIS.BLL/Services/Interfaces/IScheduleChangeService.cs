using HIS.BLL.Common;

namespace HIS.BLL.Services;

/// <summary>
/// Hủy ca hoặc đổi lịch bác sĩ:
/// - Có bác sĩ thay: UPDATE slot.doctor_id, giữ nguyên lịch hẹn.
/// - Không ai thay: trải đều lịch sang bác sĩ cùng khoa/ngày/ca còn slot. Hết chỗ -> slot_id=NULL, status RESCHEDULE_REQUIRED.
/// - Chuyển lịch TRƯỚC, rồi mới DELETE slot rỗng trong 1 transaction. Ghi status_history và gửi thông báo.
/// </summary>
public interface IScheduleChangeService
{
    Task<Result> ProcessDoctorLeaveAsync(int doctorId, DateOnly date, string? shift, int? replacementDoctorId, CancellationToken ct = default);
}
