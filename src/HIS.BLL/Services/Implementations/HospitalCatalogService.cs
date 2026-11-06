using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public sealed class HospitalCatalogService(AppDbContext db) : IHospitalCatalogService
{
    // 1. Quản lý buồng / phòng khám
    public async Task<IReadOnlyList<AdminRoomRow>> GetRoomsAsync(int? departmentId = null, CancellationToken ct = default)
    {
        var query = db.ClinicRooms.AsNoTracking().Include(r => r.Department).AsQueryable();

        if (departmentId.HasValue && departmentId.Value > 0)
        {
            query = query.Where(r => r.DepartmentId == departmentId.Value);
        }

        return await query
            .OrderBy(r => r.Department.Name)
            .ThenBy(r => r.RoomNumber)
            .Select(r => new AdminRoomRow(
                r.Id,
                r.DepartmentId,
                r.Department.Name,
                r.RoomName,
                r.RoomNumber,
                r.Schedules.Count))
            .ToListAsync(ct);
    }

    public async Task<int> CreateRoomAsync(CreateRoomRequest request, CancellationToken ct = default)
    {
        var deptExists = await db.Departments.AnyAsync(d => d.Id == request.DepartmentId, ct);
        if (!deptExists)
            throw new AdminOperationException("Khoa chuyên môn không tồn tại.");

        var trimmedName = request.RoomName.Trim();
        var trimmedNumber = request.RoomNumber.Trim();

        var duplicate = await db.ClinicRooms.AnyAsync(
            r => r.DepartmentId == request.DepartmentId && (r.RoomNumber == trimmedNumber || r.RoomName == trimmedName), ct);
        if (duplicate)
            throw new AdminOperationException($"Phòng khám với tên '{trimmedName}' hoặc số phòng '{trimmedNumber}' đã tồn tại trong khoa này.");

        var room = new ClinicRoom
        {
            DepartmentId = request.DepartmentId,
            RoomName = trimmedName,
            RoomNumber = trimmedNumber
        };

        db.ClinicRooms.Add(room);
        await db.SaveChangesAsync(ct);
        return room.Id;
    }

    public async Task<bool> DeleteRoomAsync(int roomId, CancellationToken ct = default)
    {
        var room = await db.ClinicRooms
            .Include(r => r.Appointments)
            .Include(r => r.Schedules)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room == null)
            return false;

        if (room.Appointments.Any())
            throw new AdminOperationException("Không thể xóa phòng khám đã có lịch hẹn hoặc hồ sơ bệnh nhân tiếp nhận.");

        if (room.Schedules.Any())
            throw new AdminOperationException("Không thể xóa phòng khám đang có lịch trực hoặc phân công ca khám.");

        db.ClinicRooms.Remove(room);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // 2. Loại dịch vụ & Dịch vụ kỹ thuật
    public async Task<IReadOnlyList<AdminOption>> GetServiceTypesAsync(CancellationToken ct = default)
    {
        return await db.ServiceTypes.AsNoTracking()
            .OrderBy(st => st.Id)
            .Select(st => new AdminOption(st.Id, st.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdminServiceRow>> GetServicesAsync(int? serviceTypeId = null, CancellationToken ct = default)
    {
        var query = db.Services.AsNoTracking().Include(s => s.ServiceType).AsQueryable();

        if (serviceTypeId.HasValue && serviceTypeId.Value > 0)
        {
            query = query.Where(s => s.ServiceTypeId == serviceTypeId.Value);
        }

        return await query
            .OrderBy(s => s.ServiceTypeId)
            .ThenBy(s => s.Name)
            .Select(s => new AdminServiceRow(
                s.Id,
                s.ServiceTypeId,
                s.ServiceType.Name,
                s.Name,
                s.Price,
                s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<int> UpsertServiceAsync(UpsertServiceRequest request, CancellationToken ct = default)
    {
        var typeExists = await db.ServiceTypes.AnyAsync(st => st.Id == request.ServiceTypeId, ct);
        if (!typeExists)
            throw new AdminOperationException("Loại dịch vụ kỹ thuật không hợp lệ.");

        var trimmedName = request.Name.Trim();

        if (request.Id.HasValue && request.Id.Value > 0)
        {
            var existing = await db.Services.FirstOrDefaultAsync(s => s.Id == request.Id.Value, ct)
                ?? throw new AdminOperationException("Không tìm thấy dịch vụ kỹ thuật cần cập nhật.");

            var nameConflict = await db.Services.AnyAsync(s => s.Id != request.Id.Value && s.Name == trimmedName, ct);
            if (nameConflict)
                throw new AdminOperationException($"Dịch vụ y tế tên '{trimmedName}' đã tồn tại trong hệ thống.");

            existing.ServiceTypeId = request.ServiceTypeId;
            existing.Name = trimmedName;
            existing.Price = request.Price;
            existing.IsActive = request.IsActive;

            await db.SaveChangesAsync(ct);
            return existing.Id;
        }
        else
        {
            var nameConflict = await db.Services.AnyAsync(s => s.Name == trimmedName, ct);
            if (nameConflict)
                throw new AdminOperationException($"Dịch vụ y tế tên '{trimmedName}' đã tồn tại trong hệ thống.");

            var service = new Service
            {
                ServiceTypeId = request.ServiceTypeId,
                Name = trimmedName,
                Price = request.Price,
                IsActive = request.IsActive
            };

            db.Services.Add(service);
            await db.SaveChangesAsync(ct);
            return service.Id;
        }
    }

    // 3. Danh mục thuốc
    public async Task<IReadOnlyList<AdminMedicationRow>> GetMedicationsAsync(CancellationToken ct = default)
    {
        return await db.Medications.AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new AdminMedicationRow(
                m.Id,
                m.Name,
                m.ActiveIngredient,
                m.Strength,
                m.Unit,
                m.Price,
                m.IsActive))
            .ToListAsync(ct);
    }

    public async Task<int> UpsertMedicationAsync(UpsertMedicationRequest request, CancellationToken ct = default)
    {
        var trimmedName = request.Name.Trim();
        var trimmedUnit = request.Unit.Trim();
        var activeIngredient = string.IsNullOrWhiteSpace(request.ActiveIngredient) ? null : request.ActiveIngredient.Trim();
        var strength = string.IsNullOrWhiteSpace(request.Strength) ? null : request.Strength.Trim();

        if (request.Id.HasValue && request.Id.Value > 0)
        {
            var existing = await db.Medications.FirstOrDefaultAsync(m => m.Id == request.Id.Value, ct)
                ?? throw new AdminOperationException("Không tìm thấy thuốc cần cập nhật.");

            var nameConflict = await db.Medications.AnyAsync(m => m.Id != request.Id.Value && m.Name == trimmedName && m.Strength == strength, ct);
            if (nameConflict)
                throw new AdminOperationException($"Thuốc '{trimmedName}' với hàm lượng '{strength}' đã tồn tại.");

            existing.Name = trimmedName;
            existing.ActiveIngredient = activeIngredient;
            existing.Strength = strength;
            existing.Unit = trimmedUnit;
            existing.Price = request.Price;
            existing.IsActive = request.IsActive;

            await db.SaveChangesAsync(ct);
            return existing.Id;
        }
        else
        {
            var nameConflict = await db.Medications.AnyAsync(m => m.Name == trimmedName && m.Strength == strength, ct);
            if (nameConflict)
                throw new AdminOperationException($"Thuốc '{trimmedName}' với hàm lượng '{strength}' đã tồn tại.");

            var medication = new Medication
            {
                Name = trimmedName,
                ActiveIngredient = activeIngredient,
                Strength = strength,
                Unit = trimmedUnit,
                Price = request.Price,
                IsActive = request.IsActive
            };

            db.Medications.Add(medication);
            await db.SaveChangesAsync(ct);
            return medication.Id;
        }
    }
}
