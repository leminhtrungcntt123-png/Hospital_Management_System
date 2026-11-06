using HIS.BLL.DTOs;

namespace HIS.BLL.Interfaces;

public interface IStaffService
{
    Task<bool> IsActiveAdminAsync(int actorId, CancellationToken ct = default);
    Task<bool> IsSessionActiveAsync(int accountId, string role, CancellationToken ct = default);
    Task<IReadOnlyList<AdminAccountRow>> GetAdminAccountsAsync(int actorId, CancellationToken ct = default);
    Task<IReadOnlyList<AdminAccountRow>> GetAdminStaffAsync(int actorId, CancellationToken ct = default);
    Task<IReadOnlyList<AdminRoleOption>> GetAdminRolesAsync(int actorId, CancellationToken ct = default);
    Task<int> CreateAdminAccountAsync(int actorId, CreateAdminAccountRequest request, CancellationToken ct = default);
    Task SetAccountActiveAsync(int actorId, int accountId, bool active, CancellationToken ct = default);

    // Lấy danh sách tài khoản nhân sự
    Task<IReadOnlyList<StaffListItemDto>> GetStaffAsync(CancellationToken cancellationToken = default);

    // Lấy danh sách vai trò hệ thống
    Task<IReadOnlyList<RoleOptionDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    // Thêm mới tài khoản nhân sự
    Task<StaffListItemDto> CreateStaffAsync(string username, string password, string fullName, int roleId, bool isActive = true, CancellationToken cancellationToken = default);

    // Cập nhật thông tin tài khoản nhân sự
    Task<StaffListItemDto> UpdateStaffAsync(int id, string? password, string fullName, int roleId, bool isActive, CancellationToken cancellationToken = default);

    // Xóa tài khoản nhân sự
    Task<bool> DeleteStaffAsync(int id, CancellationToken cancellationToken = default);

    // Lấy danh sách bác sĩ (ủy quyền cho DoctorService)
    Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsAsync(CancellationToken cancellationToken = default);
}
