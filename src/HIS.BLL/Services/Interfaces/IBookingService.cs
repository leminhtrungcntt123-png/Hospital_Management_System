using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

/// <summary>
/// Quy trình đặt lịch khám:
/// - Đặt online 2 bước: hold slot -> PENDING (hold_until) -> confirm -> CONFIRMED.
/// - Giữ chỗ bằng UPDATE nguyên tử booked_count &lt; capacity.
/// - Không cho hủy trong vòng CancelCutoffMinutes trước giờ khám.
/// - Chống gom slot: &lt;= MaxPatientProfilesPerAccount link, &lt;= MaxActiveAppointmentsPerAccount lịch hiệu lực.
/// - Sinh appointment_code ngẫu nhiên HIS-{yyyyMMdd}-{6 ký tự base32}.
/// </summary>
public interface IBookingService
{
    Task<Result<int>> HoldSlotAsync(int accountId, HoldSlotRequest request, CancellationToken ct = default);
    Task<Result<string>> ConfirmAppointmentAsync(int accountId, ConfirmAppointmentRequest request, CancellationToken ct = default);
    Task<Result> CancelAppointmentAsync(int accountId, int appointmentId, string reason, CancellationToken ct = default);
    Task<Result<int>> CreateWalkInAppointmentAsync(int staffAccountId, WalkInAppointmentRequest request, CancellationToken ct = default);
}
