using HIS.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public class AccountsController : Controller
{
    private readonly IStaffService _staffService;

    public AccountsController(IStaffService staffService)
    {
        _staffService = staffService;
    }

    // 1. Danh sách tài khoản nhân sự và vai trò hệ thống
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var staff = await _staffService.GetStaffAsync(ct);
        ViewBag.Roles = await _staffService.GetRolesAsync(ct);
        return View(staff);
    }

    // 2. Thêm mới tài khoản nhân viên (bác sĩ, điều dưỡng, thu ngân, trưởng khoa)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string username, string password, string fullName, int roleId, bool isActive, CancellationToken ct)
    {
        try
        {
            await _staffService.CreateStaffAsync(username, password, fullName, roleId, isActive, ct);
            TempData["SuccessMessage"] = "Tạo tài khoản nhân viên thành công.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // 3. Cập nhật thông tin nhân viên hoặc đổi mật khẩu/trạng thái hoạt động
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? password, string fullName, int roleId, bool isActive, CancellationToken ct)
    {
        try
        {
            await _staffService.UpdateStaffAsync(id, password, fullName, roleId, isActive, ct);
            TempData["SuccessMessage"] = "Cập nhật tài khoản nhân viên thành công.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // 4. Xóa tài khoản nhân viên (bảo vệ tài khoản admin)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            var success = await _staffService.DeleteStaffAsync(id, ct);
            if (success)
                TempData["SuccessMessage"] = "Xóa tài khoản thành công.";
            else
                TempData["ErrorMessage"] = "Tài khoản không tồn tại.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // 5. Đặt lại mật khẩu nhân viên tại quầy
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int accountId, string newPassword, CancellationToken ct)
    {
        try
        {
            var staff = await _staffService.GetStaffAsync(ct);
            var target = staff.FirstOrDefault(s => s.Id == accountId);
            if (target == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản.";
                return RedirectToAction(nameof(Index));
            }

            var roles = await _staffService.GetRolesAsync(ct);
            var roleId = roles.FirstOrDefault(r => r.Code == target.RoleCode)?.Id ?? 1;

            await _staffService.UpdateStaffAsync(accountId, newPassword, target.FullName, roleId, target.IsActive, ct);
            TempData["SuccessMessage"] = $"Đặt lại mật khẩu cho tài khoản '{target.Username}' thành công.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
