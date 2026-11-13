using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public partial class StaffService
{
    public Task<bool> IsSessionActiveAsync(int accountId, string role, CancellationToken ct = default) =>
        _context.Accounts.AnyAsync(account => account.Id == accountId && account.IsActive && account.Role.Code == role, ct);

    public async Task<IReadOnlyList<AdminAccountRow>> GetAdminStaffAsync(int actorId, CancellationToken ct = default) =>
        (await GetAdminAccountsAsync(actorId, ct))
            .Where(account => account.Role is "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD").ToList();

    public async Task<IReadOnlyList<AdminRoleOption>> GetAdminRolesAsync(int actorId, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(_context, actorId, ct);
        return (await GetRolesAsync(ct)).Select(role => new AdminRoleOption(role.Id, role.Code, role.Name,
            role.Code != "ADMIN", role.Code is "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD", role.Code == "PATIENT")).ToList();
    }

    public Task<bool> IsActiveAdminAsync(int actorId, CancellationToken ct = default) =>
        _context.Accounts.AnyAsync(account => account.Id == actorId
            && account.IsActive && account.Role.Code == "ADMIN", ct);

    public async Task<IReadOnlyList<AdminAccountRow>> GetAdminAccountsAsync(int actorId, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(_context, actorId, ct);

        return await _context.Accounts.AsNoTracking()
            .OrderBy(account => account.Role.Code).ThenBy(account => account.Username)
            .Select(account => new AdminAccountRow(
                account.Id, account.Username,
                account.Doctor != null ? account.Doctor.FullName
                    : account.Nurse != null ? account.Nurse.FullName
                    : account.DepartmentHead != null ? account.DepartmentHead.FullName
                    : account.Username,
                account.Role.Code, account.Role.Name,
                account.Doctor != null ? account.Doctor.Department.Name
                    : account.Nurse != null && account.Nurse.Department != null ? account.Nurse.Department.Name
                    : account.DepartmentHead != null ? account.DepartmentHead.Department.Name : null,
                account.IsActive, account.Id != actorId))
            .ToListAsync(ct);
    }

    public async Task<int> CreateAdminAccountAsync(
        int actorId, CreateAdminAccountRequest request, CancellationToken ct = default)
    {
        AdminRules.Validate(request);
        await AdminRules.RequireAdminAsync(_context, actorId, ct);
        AdminRules.Require(Encoding.UTF8.GetByteCount(request.Password) <= 72,
            "Mật khẩu không vượt quá 72 byte UTF-8.");
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await AdminRules.RequireAdminAsync(_context, actorId, ct);

            var username = request.Username.Trim().ToLowerInvariant();
            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
            AdminRules.Require(!await _context.Accounts.AnyAsync(account => account.Username.ToLower() == username, ct),
                "Tên đăng nhập đã được sử dụng.");
            AdminRules.Require(email == null || !await _context.Accounts.AnyAsync(account => account.Email == email, ct),
                "Email đã được sử dụng.");

            var role = await _context.Roles.SingleOrDefaultAsync(role => role.Id == request.RoleId, ct);
            AdminRules.Require(role != null, "Vai trò không tồn tại.");
            AdminRules.Require(role!.Code is "ADMIN" or "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD" or "PATIENT",
                "Vai trò này chưa được hỗ trợ tại màn quản trị.");

            if (role!.Code is "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD")
            {
                AdminRules.Require(request.DepartmentId.HasValue
                    && await _context.Departments.AnyAsync(department => department.Id == request.DepartmentId, ct),
                    "Cần chọn phòng ban của nhân sự.");
            }

            if (role.Code == "DEPARTMENT_HEAD")
            {
                AdminRules.Require(!await _context.DepartmentHeads.AnyAsync(head => head.DepartmentId == request.DepartmentId, ct),
                    "Phòng ban đã có trưởng khoa phụ trách.");
            }

            var now = DateTime.SpecifyKind(_adminClock.GetLocalNow().DateTime, DateTimeKind.Unspecified);
            var account = new Account
            {
                Username = username,
                Email = email,
                PasswordHash = hash,
                RoleId = role.Id,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Accounts.Add(account);
            var name = role.Code == "ADMIN" ? username : request.FullName?.Trim() ?? "";
            AdminRules.Require(!string.IsNullOrEmpty(name) && name.Length >= 2, "Cần họ tên hợp lệ cho hồ sơ nhân sự/người khám.");

            switch (role.Code)
            {
                case "DOCTOR":
                    _context.Doctors.Add(new Doctor
                    {
                        Account = account,
                        FullName = name,
                        DepartmentId = request.DepartmentId!.Value
                    });
                    break;
                case "NURSE":
                    _context.Nurses.Add(new Nurse
                    {
                        Account = account,
                        FullName = name,
                        DepartmentId = request.DepartmentId
                    });
                    break;
                case "DEPARTMENT_HEAD":
                    _context.DepartmentHeads.Add(new DepartmentHead
                    {
                        Account = account,
                        FullName = name,
                        DepartmentId = request.DepartmentId!.Value
                    });
                    break;
                case "PATIENT":
                    var today = DateOnly.FromDateTime(now);
                    AdminRules.Require(request.DateOfBirth.HasValue && request.DateOfBirth <= today
                        && request.DateOfBirth >= today.AddYears(-130), "Ngày sinh người khám không hợp lệ.");
                    AdminRules.Require(Regex.IsMatch(request.PhoneNumber ?? "", @"^[0-9+ ()-]{9,15}$"),
                        "Cần số điện thoại người khám hợp lệ.");
                    var patient = new Patient
                    {
                        FullName = name,
                        DateOfBirth = request.DateOfBirth!.Value,
                        PhoneNumber = request.PhoneNumber!,
                        CreatedAt = now
                    };
                    _context.AccountPatientProfiles.Add(new AccountPatientProfile
                    {
                        Account = account,
                        Patient = patient,
                        Relationship = PatientRelationship.Self,
                        Status = LinkStatus.Active,
                        CanViewRecords = true,
                        CreatedBy = actorId,
                        CreatedAt = now
                    });
                    break;
            }

            await _context.SaveChangesAsync(ct);
            _context.AccessLogs.Add(new AccessLog
            {
                AccountId = actorId,
                Action = "ADMIN_CREATE_ACCOUNT",
                OccurredAt = now
            });
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return account.Id;
        });
    }

    public async Task SetAccountActiveAsync(int actorId, int accountId, bool active, CancellationToken ct = default)
    {
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await AdminRules.RequireAdminAsync(_context, actorId, ct);
            AdminRules.Require(actorId != accountId, "Không đổi trạng thái tài khoản đang đăng nhập.");

            var account = await _context.Accounts.Include(account => account.Role)
                .SingleOrDefaultAsync(account => account.Id == accountId, ct);
            AdminRules.Require(account != null, "Không tìm thấy tài khoản.");

            if (!active && account!.Role.Code == "ADMIN")
            {
                AdminRules.Require(await _context.Accounts.CountAsync(a => a.IsActive && a.Role.Code == "ADMIN", ct) > 1,
                    "Không ngừng hoạt động quản trị viên cuối cùng.");
            }

            account!.IsActive = active;
            _context.AccessLogs.Add(new AccessLog
            {
                AccountId = actorId,
                Action = active ? "ADMIN_ACTIVATE_ACCOUNT" : "ADMIN_DEACTIVATE_ACCOUNT",
                OccurredAt = DateTime.SpecifyKind(_adminClock.GetLocalNow().DateTime, DateTimeKind.Unspecified)
            });
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }
}
