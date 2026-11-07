using HIS.Domain;

namespace HIS.BLL.Services;

/// <summary>
/// Hợp đồng chuyển trạng thái lượt khám theo init.md mục 7 (hàm thuần, không chạm DB).
/// Dùng mã trạng thái (appointment_statuses.code) làm khóa vì id chỉ cố định để làm
/// điều kiện lọc cho các partial index, còn nghiệp vụ đọc mã.
///
/// Khởi tạo: lịch đặt web bắt đầu ở PENDING; lịch walk-in (ON_SITE) tạo thẳng ở CHECKED_IN,
/// không đi qua PENDING/CONFIRMED. Lịch PENDING bị hủy ca thì chuyển thẳng CANCELLED
/// (không qua RESCHEDULE_REQUIRED); lịch CONFIRMED mới đi qua RESCHEDULE_REQUIRED rồi CANCELLED
/// khi chuyển sang lượt mới (lượt mới có parent_appointment_id trỏ về lượt cũ).
///
/// Hiện BookingService/ReceptionService vẫn đặt StatusId trực tiếp sau khi kiểm tra điều kiện
/// riêng của từng luồng; lớp này là bảng chuẩn để các luồng mới (màn hình bác sĩ:
/// CHECKED_IN -> READY_FOR_DOCTOR -> IN_PROGRESS -> COMPLETED) đi theo và để test ràng buộc.
/// </summary>
public static class AppointmentStateMachine
{
    private static readonly IReadOnlyDictionary<string, string[]> Transitions =
        new Dictionary<string, string[]>
        {
            [AppointmentStatus.Codes.Pending] =
            [
                AppointmentStatus.Codes.Confirmed,
                AppointmentStatus.Codes.Expired,
                AppointmentStatus.Codes.Cancelled
            ],
            [AppointmentStatus.Codes.Confirmed] =
            [
                AppointmentStatus.Codes.CheckedIn,
                AppointmentStatus.Codes.Cancelled,
                AppointmentStatus.Codes.NoShow,
                AppointmentStatus.Codes.RescheduleRequired
            ],
            [AppointmentStatus.Codes.CheckedIn] =
            [
                AppointmentStatus.Codes.ReadyForDoctor,
                AppointmentStatus.Codes.Cancelled
            ],
            [AppointmentStatus.Codes.ReadyForDoctor] = [AppointmentStatus.Codes.InProgress],
            [AppointmentStatus.Codes.InProgress] = [AppointmentStatus.Codes.Completed],
            [AppointmentStatus.Codes.RescheduleRequired] = [AppointmentStatus.Codes.Cancelled],
            // COMPLETED, CANCELLED, NO_SHOW, EXPIRED là trạng thái kết thúc: không đi tiếp.
            [AppointmentStatus.Codes.Completed] = [],
            [AppointmentStatus.Codes.Cancelled] = [],
            [AppointmentStatus.Codes.NoShow] = [],
            [AppointmentStatus.Codes.Expired] = []
        };

    public static IReadOnlyCollection<string> Codes { get; } = [.. Transitions.Keys];

    /// <summary>Các trạng thái kế tiếp hợp lệ từ <paramref name="from"/> (rỗng nếu là trạng thái kết thúc).</summary>
    public static IReadOnlyList<string> AllowedNext(string from) => Next(from);

    public static bool IsTerminal(string code) => Next(code).Length == 0;

    public static bool CanTransition(string from, string to) => Next(from).Contains(Status(to));

    private static readonly Dictionary<AppointmentStatusId, HashSet<AppointmentStatusId>> AllowedTransitions = new()
    {
        [AppointmentStatusId.Pending] = [AppointmentStatusId.Confirmed, AppointmentStatusId.Expired, AppointmentStatusId.Cancelled, AppointmentStatusId.RescheduleRequired],
        [AppointmentStatusId.Confirmed] = [AppointmentStatusId.CheckedIn, AppointmentStatusId.Cancelled, AppointmentStatusId.NoShow, AppointmentStatusId.RescheduleRequired],
        [AppointmentStatusId.CheckedIn] = [AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.Cancelled],
        [AppointmentStatusId.ReadyForDoctor] = [AppointmentStatusId.InProgress],
        [AppointmentStatusId.InProgress] = [AppointmentStatusId.Completed],
        [AppointmentStatusId.RescheduleRequired] = [AppointmentStatusId.Cancelled],
        [AppointmentStatusId.Completed] = [],
        [AppointmentStatusId.Cancelled] = [],
        [AppointmentStatusId.NoShow] = [],
        [AppointmentStatusId.Expired] = []
    };

    public static bool CanTransition(AppointmentStatusId current, AppointmentStatusId next) =>
        AllowedTransitions.TryGetValue(current, out var targets) && targets.Contains(next);

    public static bool IsTerminal(AppointmentStatusId status) =>
        status is AppointmentStatusId.Completed
            or AppointmentStatusId.Cancelled
            or AppointmentStatusId.NoShow
            or AppointmentStatusId.Expired;

    private static string[] Next(string from) =>
        Transitions.TryGetValue(Status(from), out string[]? next) ? next : [];

    private static string Status(string code) =>
        Codes.Contains(code)
            ? code
            : throw new ArgumentException($"Trạng thái lượt khám không hợp lệ: '{code}'.", nameof(code));
}
