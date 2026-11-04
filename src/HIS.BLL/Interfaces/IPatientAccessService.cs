namespace HIS.BLL.Interfaces;

/// <summary>
/// Cổng kiểm quyền truy cập hồ sơ theo bảng account_patient_profiles.
/// Tầng Web/WinForms không được tự suy ra quyền từ cột nào khác.
/// </summary>
public interface IPatientAccessService
{
    /// <summary>Đặt lịch được cho hồ sơ đang ACTIVE hoặc PENDING (PENDING vẫn khám được, quầy sẽ duyệt khi tới).</summary>
    Task<bool> CanBookAsync(int accountId, int patientId, CancellationToken cancellationToken = default);

    /// <summary>Chỉ ACTIVE và được cấp quyền xem hồ sơ.</summary>
    Task<bool> CanViewRecordsAsync(int accountId, int patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi vết một lần mở hồ sơ của người khác (kèm IP) vào access_logs.
    /// Trả về false nếu tài khoản không có quyền xem, người gọi phải chặn nội dung.
    /// </summary>
    Task<bool> LogRecordAccessAsync(
        int accountId,
        int patientId,
        string action,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
