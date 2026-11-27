using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Services;

public interface IDoctorService
{
    // Lấy danh sách bác sĩ
    Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsAsync(CancellationToken cancellationToken = default);

    // Lấy danh sách bác sĩ hiển thị trang chủ (Public Showcase)
    Task<IReadOnlyList<DoctorPublicDto>> GetPublicFeaturedDoctorsAsync(int count = 8, CancellationToken cancellationToken = default);

    // Lấy danh sách bệnh nhân chờ khám theo bác sĩ và ca khám
    Task<IReadOnlyList<QueuePatientDto>> GetQueuePatientsAsync(int doctorId, DateOnly date, ExamShift shift, CancellationToken cancellationToken = default);

    // Bắt đầu khám bệnh cho lượt khám (ReadyForDoctor -> InProgress)
    Task<Result> StartConsultationAsync(int doctorAccountId, int appointmentId, CancellationToken cancellationToken = default);

    // Tạo hồ sơ bệnh án cho lượt khám
    Task<int> CreateMedicalRecordAsync(CreateMedicalRecordRequest request, CancellationToken cancellationToken = default);

    // Cập nhật kết quả cận lâm sàng cho chỉ định
    Task<Result> SaveLabResultAsync(int doctorAccountId, int labOrderId, string resultText, CancellationToken cancellationToken = default);

    // Chẩn đoán tiếp, kê đơn và hoàn tất ca khám sau khi có kết quả cận lâm sàng
    Task<Result> CompleteRecordAfterLabsAsync(CompleteRecordAfterLabsRequest request, CancellationToken cancellationToken = default);
}
