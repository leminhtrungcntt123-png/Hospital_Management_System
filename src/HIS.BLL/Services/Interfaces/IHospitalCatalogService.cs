using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IHospitalCatalogService
{
    // Phòng khám
    Task<IReadOnlyList<AdminRoomRow>> GetRoomsAsync(int? departmentId = null, CancellationToken ct = default);
    Task<int> CreateRoomAsync(CreateRoomRequest request, CancellationToken ct = default);
    Task<bool> DeleteRoomAsync(int roomId, CancellationToken ct = default);

    // Loại dịch vụ & Dịch vụ kỹ thuật
    Task<IReadOnlyList<AdminOption>> GetServiceTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdminServiceRow>> GetServicesAsync(int? serviceTypeId = null, CancellationToken ct = default);
    Task<int> UpsertServiceAsync(UpsertServiceRequest request, CancellationToken ct = default);

    // Danh mục thuốc
    Task<IReadOnlyList<AdminMedicationRow>> GetMedicationsAsync(CancellationToken ct = default);
    Task<int> UpsertMedicationAsync(UpsertMedicationRequest request, CancellationToken ct = default);
}

