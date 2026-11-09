using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class DepartmentService : IDepartmentService
{
    private readonly AppDbContext _context;

    public DepartmentService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách chuyên khoa phòng sắp xếp theo tên.
    /// </summary>
    /// <param name="cancellationToken">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Danh sách chuyên khoa.</returns>
    public async Task<IReadOnlyList<DepartmentListItemDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentListItemDto(
                d.Id,
                d.Name,
                d.Description ?? ""))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lấy danh sách các phòng khám trực thuộc một chuyên khoa.
    /// </summary>
    /// <param name="departmentId">Mã khoa phòng.</param>
    /// <param name="cancellationToken">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Danh sách phòng khám.</returns>
    public async Task<IReadOnlyList<ClinicRoomDto>> GetRoomsByDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicRooms
            .AsNoTracking()
            .Where(r => r.DepartmentId == departmentId)
            .OrderBy(r => r.RoomNumber)
            .Select(r => new ClinicRoomDto(
                r.Id,
                r.DepartmentId,
                r.RoomName,
                r.RoomNumber))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Thêm mới chuyên khoa phòng trong bệnh viện.
    /// </summary>
    /// <param name="name">Tên khoa phòng.</param>
    /// <param name="description">Mô tả chức năng nhiệm vụ.</param>
    /// <param name="cancellationToken">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Thông tin khoa phòng vừa tạo.</returns>
    public async Task<DepartmentListItemDto> CreateDepartmentAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên khoa/phòng không được để trống.", nameof(name));

        var trimmedName = name.Trim();
        if (await _context.Departments.AnyAsync(d => d.Name == trimmedName, cancellationToken))
            throw new InvalidOperationException($"Khoa/phòng '{trimmedName}' đã tồn tại.");

        var dept = new Department
        {
            Name = trimmedName,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };

        _context.Departments.Add(dept);
        await _context.SaveChangesAsync(cancellationToken);

        return new DepartmentListItemDto(dept.Id, dept.Name, dept.Description ?? "");
    }

    /// <summary>
    /// Cập nhật thông tin (tên, mô tả) của chuyên khoa phòng.
    /// </summary>
    /// <param name="id">Mã khoa phòng.</param>
    /// <param name="name">Tên khoa phòng mới.</param>
    /// <param name="description">Mô tả mới.</param>
    /// <param name="cancellationToken">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Thông tin khoa phòng sau khi cập nhật.</returns>
    public async Task<DepartmentListItemDto> UpdateDepartmentAsync(int id, string name, string? description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên khoa/phòng không được để trống.", nameof(name));

        var dept = await _context.Departments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Không tìm thấy khoa/phòng có mã {id}.");

        var trimmedName = name.Trim();
        if (await _context.Departments.AnyAsync(d => d.Id != id && d.Name == trimmedName, cancellationToken))
            throw new InvalidOperationException($"Khoa/phòng '{trimmedName}' đã tồn tại.");

        dept.Name = trimmedName;
        dept.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        return new DepartmentListItemDto(dept.Id, dept.Name, dept.Description ?? "");
    }

    /// <summary>
    /// Xóa chuyên khoa phòng (kiểm tra không cho phép xóa nếu đang có bác sĩ hoặc phòng khám liên kết).
    /// </summary>
    /// <param name="id">Mã khoa phòng.</param>
    /// <param name="cancellationToken">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>True nếu xóa thành công.</returns>
    public async Task<bool> DeleteDepartmentAsync(int id, CancellationToken cancellationToken = default)
    {
        var dept = await _context.Departments
            .Include(d => d.Doctors)
            .Include(d => d.ClinicRooms)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (dept == null) return false;

        if (dept.Doctors.Any() || dept.ClinicRooms.Any())
            throw new InvalidOperationException("Không thể xóa khoa đang có bác sĩ hoặc phòng khám liên kết.");

        _context.Departments.Remove(dept);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
