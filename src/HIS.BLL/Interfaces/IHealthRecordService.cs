using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Interfaces;

public interface IHealthRecordService
{
    // Lấy chi tiết hồ sơ sức khỏe điện tử theo lượt khám (chuẩn hóa theo mẫu Hồ sơ sức khỏe hiển thị)
    Task<Result<HealthRecordDetailDto>> GetHealthRecordDetailAsync(int appointmentId, int currentAccountId, CancellationToken ct = default);

    // Lấy danh sách lịch sử các lần khám bệnh của một bệnh nhân
    Task<IReadOnlyList<HealthRecordHistoryItemDto>> GetPatientHealthHistoryAsync(int patientId, int currentAccountId, CancellationToken ct = default);
}

