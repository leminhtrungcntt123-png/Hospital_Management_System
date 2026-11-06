using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class StaffService : IStaffService
{
    private readonly AppDbContext _context;
    private readonly IDoctorService _doctorService;
    private readonly TimeProvider _clock;

    public StaffService(AppDbContext context, IDoctorService doctorService, TimeProvider? timeProvider = null)
    {
        _context = context;
        _doctorService = doctorService;
        _clock = timeProvider ?? TimeProvider.System;
    }

    public Task<bool> IsSessionActiveAsync(int accountId, string role, CancellationToken ct = default) =>
        _context.Accounts.AnyAsync(account => account.Id == accountId && account.IsActive && account.Role.Code == role, ct);

    public async Task<IReadOnlyList<AdminAccountRow>> GetAdminStaffAsync(int actorId, CancellationToken ct = default) =>
        (await GetAdminAccountsAsync(actorId, ct))
            .Where(account => account.Role is "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD" or "DEPT_HEAD").ToList();

    public async Task<IReadOnlyList<AdminRoleOption>> GetAdminRolesAsync(int actorId, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(_context, actorId, ct);
        return (await GetRolesAsync(ct)).Select(role => new AdminRoleOption(
            role.Id,
            role.Code,
            role.Name,
            role.Code != "ADMIN",
            role.Code is "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD" or "DEPT_HEAD",
            role.Code == "PATIENT")).ToList();
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
            await using var transaction = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await AdminRules.RequireAdminAsync(_context, actorId, ct);

            var username = request.Username.Trim().ToLowerInvariant();
            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
            AdminRules.Require(!await _context.Accounts.AnyAsync(account => account.Username.ToLower() == username, ct),
                "Tên đăng nhập đã được sử dụng.");
            AdminRules.Require(email == null || !await _context.Accounts.AnyAsync(account => account.Email == email, ct),
                "Email đã được sử dụng.");

            var role = await _context.Roles.SingleOrDefaultAsync(r => r.Id == request.RoleId, ct);
            AdminRules.Require(role != null, "Vai trò không tồn tại.");
            AdminRules.Require(role!.Code is "ADMIN" or "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD" or "DEPT_HEAD" or "PATIENT" or "RECEPTIONIST" or "CASHIER",
                "Vai trò này chưa được hỗ trợ tại màn quản trị.");

            if (role!.Code is "DOCTOR" or "NURSE" or "DEPARTMENT_HEAD" or "DEPT_HEAD")
            {
                AdminRules.Require(request.DepartmentId.HasValue
                    && await _context.Departments.AnyAsync(department => department.Id == request.DepartmentId, ct),
                    "Cần chọn phòng ban của nhân sự.");
            }

            if (role.Code is "DEPARTMENT_HEAD" or "DEPT_HEAD")
            {
                AdminRules.Require(!await _context.DepartmentHeads.AnyAsync(head => head.DepartmentId == request.DepartmentId, ct),
                    "Phòng ban đã có trưởng khoa phụ trách.");
            }

            var now = DateTime.SpecifyKind(_clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);
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
                case "DEPT_HEAD":
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
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
            return account.Id;
        });
    }

    public async Task SetAccountActiveAsync(int actorId, int accountId, bool active, CancellationToken ct = default)
    {
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await AdminRules.RequireAdminAsync(_context, actorId, ct);
            AdminRules.Require(actorId != accountId, "Không đổi trạng thái tài khoản đang đăng nhập.");

            var account = await _context.Accounts.Include(a => a.Role)
                .SingleOrDefaultAsync(a => a.Id == accountId, ct);
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
                OccurredAt = DateTime.SpecifyKind(_clock.GetLocalNow().DateTime, DateTimeKind.Unspecified)
            });
            await _context.SaveChangesAsync(ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
        });
    }

    // Lấy danh sách tài khoản nhân sự và vai trò tương ứng trong hệ thống
    public async Task<IReadOnlyList<StaffListItemDto>> GetStaffAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .OrderBy(a => a.Role.Name)
            .ThenBy(a => a.Username)
            .Select(a => new StaffListItemDto(
                a.Id,
                a.Username,
                a.Doctor != null ? a.Doctor.FullName
                    : a.Nurse != null ? a.Nurse.FullName
                    : a.DepartmentHead != null ? a.DepartmentHead.FullName
                    : a.PatientLinks.Any(l => l.Relationship == PatientRelationship.Self)
                        ? a.PatientLinks.Where(l => l.Relationship == PatientRelationship.Self)
                            .Select(l => l.Patient.FullName).First()
                    : a.Username,
                a.Role.Name,
                a.Role.Code,
                a.IsActive))
            .ToListAsync(cancellationToken);
    }

    // Lấy danh sách các vai trò trong hệ thống
    public async Task<IReadOnlyList<RoleOptionDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleOptionDto(r.Id, r.Code, r.Name))
            .ToListAsync(cancellationToken);
    }

    // Thêm mới tài khoản nhân sự
    public async Task<StaffListItemDto> CreateStaffAsync(string username, string password, string fullName, int roleId, bool isActive = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Tên đăng nhập không được để trống.", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Mật khẩu không được để trống.", nameof(password));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Họ và tên không được để trống.", nameof(fullName));

        var trimmedUsername = username.Trim();
        if (await _context.Accounts.AnyAsync(a => a.Username == trimmedUsername, cancellationToken))
            throw new InvalidOperationException($"Tên đăng nhập '{trimmedUsername}' đã được sử dụng.");

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException("Vai trò được chọn không tồn tại.");

        var now = DateTime.SpecifyKind(_clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);
        var account = new Account
        {
            Username = trimmedUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = roleId,
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);

        var trimmedFullName = fullName.Trim();
        if (role.Code == "DOCTOR")
        {
            var firstDept = await _context.Departments.FirstOrDefaultAsync(cancellationToken);
            if (firstDept != null)
            {
                _context.Doctors.Add(new Doctor
                {
                    AccountId = account.Id,
                    FullName = trimmedFullName,
                    DepartmentId = firstDept.Id
                });
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        else if (role.Code == "NURSE")
        {
            _context.Nurses.Add(new Nurse
            {
                AccountId = account.Id,
                FullName = trimmedFullName
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
        else if (role.Code is "DEPT_HEAD" or "DEPARTMENT_HEAD")
        {
            var firstDept = await _context.Departments.FirstOrDefaultAsync(cancellationToken);
            if (firstDept != null)
            {
                _context.DepartmentHeads.Add(new DepartmentHead
                {
                    AccountId = account.Id,
                    FullName = trimmedFullName,
                    DepartmentId = firstDept.Id
                });
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        return new StaffListItemDto(account.Id, account.Username, trimmedFullName, role.Name, role.Code, account.IsActive);
    }

    // Cập nhật thông tin tài khoản nhân sự
    public async Task<StaffListItemDto> UpdateStaffAsync(int id, string? password, string fullName, int roleId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Họ và tên không được để trống.", nameof(fullName));

        var account = await _context.Accounts
            .Include(a => a.Role)
            .Include(a => a.Doctor)
            .Include(a => a.Nurse)
            .Include(a => a.DepartmentHead)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Không tìm thấy tài khoản có mã {id}.");

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException("Vai trò được chọn không tồn tại.");

        account.RoleId = roleId;
        account.IsActive = isActive;
        account.UpdatedAt = DateTime.SpecifyKind(_clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);

        if (!string.IsNullOrWhiteSpace(password))
        {
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        }

        var trimmedFullName = fullName.Trim();
        if (account.Doctor != null)
            account.Doctor.FullName = trimmedFullName;
        else if (account.Nurse != null)
            account.Nurse.FullName = trimmedFullName;
        else if (account.DepartmentHead != null)
            account.DepartmentHead.FullName = trimmedFullName;
        else if (role.Code == "DOCTOR")
        {
            var firstDept = await _context.Departments.FirstOrDefaultAsync(cancellationToken);
            if (firstDept != null)
                _context.Doctors.Add(new Doctor { AccountId = account.Id, FullName = trimmedFullName, DepartmentId = firstDept.Id });
        }
        else if (role.Code == "NURSE")
        {
            _context.Nurses.Add(new Nurse { AccountId = account.Id, FullName = trimmedFullName });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new StaffListItemDto(account.Id, account.Username, trimmedFullName, role.Name, role.Code, account.IsActive);
    }

    // Xóa tài khoản nhân sự
    public async Task<bool> DeleteStaffAsync(int id, CancellationToken cancellationToken = default)
    {
        var account = await _context.Accounts
            .Include(a => a.Doctor)
            .Include(a => a.Nurse)
            .Include(a => a.DepartmentHead)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (account == null) return false;

        if (account.Username.Equals("admin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không thể xóa tài khoản quản trị viên mặc định (admin).");

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Lấy danh sách bác sĩ (ủy quyền cho DoctorService)
    public Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsAsync(CancellationToken cancellationToken = default)
        => _doctorService.GetDoctorsAsync(cancellationToken);
}
