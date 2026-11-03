namespace HIS.BLL.Services;

/// <summary>
/// Phân quyền dữ liệu phía bệnh nhân:
/// - CanBook: link ACTIVE hoặc PENDING.
/// - CanViewRecords: link ACTIVE và can_view_records=true.
/// - Nhân viên: Bác sĩ chỉ xem bệnh án bệnh nhân có lịch khám với mình hoặc cùng khoa, nhưng chỉ sửa bệnh án có doctor_id là chính mình;
///   y tá xem sinh hiệu/lịch cùng khoa; trưởng khoa xem trong khoa; Admin KHÔNG xem nội dung bệnh án.
/// </summary>
public interface IPatientAccessService
{
    Task<bool> CanBookAsync(int accountId, int patientId, CancellationToken ct = default);
    Task<bool> CanViewRecordsAsync(int accountId, int patientId, CancellationToken ct = default);
    Task<bool> CanStaffAccessRecordAsync(int staffAccountId, int appointmentId, CancellationToken ct = default);
}
