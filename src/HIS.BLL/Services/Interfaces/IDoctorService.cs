using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Services;

public interface IDoctorService
{
    // Lấy danh sách bác sĩ
    Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsAsync(CancellationToken cancellationToken = default);

    // Lấy danh sách bệnh nhân chờ khám theo bác sĩ và ca khám
    Task<IReadOnlyList<QueuePatientDto>> GetQueuePatientsAsync(int doctorId, DateOnly date, ExamShift shift, CancellationToken cancellationToken = default);

    // Bắt đầu khám bệnh cho lượt khám (ReadyForDoctor -> InProgress)
    Task<Result> StartConsultationAsync(int doctorAccountId, int appointmentId, CancellationToken cancellationToken = default);

    // Tạo hồ sơ bệnh án cho lượt khám
    Task<int> CreateMedicalRecordAsync(CreateMedicalRecordRequest request, CancellationToken cancellationToken = default);
}
