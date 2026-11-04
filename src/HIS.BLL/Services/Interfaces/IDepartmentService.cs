using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IDepartmentService
{
    // Lấy danh sách chuyên khoa phòng
    Task<IReadOnlyList<DepartmentListItemDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    // Lấy danh sách phòng khám theo chuyên khoa
    Task<IReadOnlyList<ClinicRoomDto>> GetRoomsByDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

    // Thêm mới chuyên khoa phòng
    Task<DepartmentListItemDto> CreateDepartmentAsync(string name, string? description, CancellationToken cancellationToken = default);

    // Cập nhật thông tin chuyên khoa phòng
    Task<DepartmentListItemDto> UpdateDepartmentAsync(int id, string name, string? description, CancellationToken cancellationToken = default);

    // Xóa chuyên khoa phòng
    Task<bool> DeleteDepartmentAsync(int id, CancellationToken cancellationToken = default);
}
