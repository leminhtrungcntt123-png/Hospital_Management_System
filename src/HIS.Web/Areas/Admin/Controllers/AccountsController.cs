using HIS.BLL.Common;
using HIS.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public class AccountsController : Controller
{
    private const string GenericErrorMessage = "Đã xảy ra lỗi trong quá trình xử lý. Vui lòng thử lại sau.";
    private readonly IStaffService _staffService;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(IStaffService staffService, ILogger<AccountsController> logger)
    {
        _staffService = staffService;
        _logger = logger;
    }

    /// <summary>
    /// Hiển thị danh sách tài khoản nhân sự và vai trò trong hệ thống.
    /// </summary>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var staff = await _staffService.GetStaffAsync(ct);
        ViewBag.Roles = await _staffService.GetRolesAsync(ct);
        return View(staff);
    }

    /// <summary>
    /// Thêm mới tài khoản nhân viên (bác sĩ, điều dưỡng, thu ngân, trưởng khoa).
    /// </summary>
    /// <param name="username">Tên đăng nhập.</param>
    /// <param name="password">Mật khẩu ban đầu.</param>
    /// <param name="fullName">Họ và tên nhân viên.</param>
    /// <param name="roleId">Mã vai trò.</param>
    /// <param name="isActive">Trạng thái kích hoạt.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string username, string password, string fullName, int roleId, bool isActive, CancellationToken ct)
    {
        try
        {
            await _staffService.CreateStaffAsync(username, password, fullName, roleId, isActive, ct);
            TempData["SuccessMessage"] = "Tạo tài khoản nhân viên thành công.";
        }
        catch (AdminOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi tạo tài khoản nhân viên.");
            TempData["ErrorMessage"] = GenericErrorMessage;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Cập nhật thông tin nhân viên hoặc đổi mật khẩu/trạng thái hoạt động.
    /// </summary>
    /// <param name="id">Mã tài khoản.</param>
    /// <param name="password">Mật khẩu mới (nếu có).</param>
    /// <param name="fullName">Họ và tên mới.</param>
    /// <param name="roleId">Mã vai trò mới.</param>
    /// <param name="isActive">Trạng thái kích hoạt mới.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? password, string fullName, int roleId, bool isActive, CancellationToken ct)
    {
        try
        {
            await _staffService.UpdateStaffAsync(id, password, fullName, roleId, isActive, ct);
            TempData["SuccessMessage"] = "Cập nhật tài khoản nhân viên thành công.";
        }
        catch (AdminOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi cập nhật tài khoản nhân viên {AccountId}.", id);
            TempData["ErrorMessage"] = GenericErrorMessage;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Xóa tài khoản nhân viên khỏi hệ thống (ngăn xóa admin mặc định).
    /// </summary>
    /// <param name="id">Mã tài khoản.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
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
        catch (AdminOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi xóa tài khoản {AccountId}.", id);
            TempData["ErrorMessage"] = GenericErrorMessage;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Đặt lại mật khẩu nhân viên trực tiếp từ quầy quản trị.
    /// </summary>
    /// <param name="accountId">Mã tài khoản.</param>
    /// <param name="newPassword">Mật khẩu mới.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int accountId, string newPassword, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                TempData["ErrorMessage"] = "Mật khẩu mới không được để trống.";
                return RedirectToAction(nameof(Index));
            }

            var staff = await _staffService.GetStaffAsync(ct);
            var target = staff.FirstOrDefault(s => s.Id == accountId);
            if (target == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản.";
                return RedirectToAction(nameof(Index));
            }

            var roles = await _staffService.GetRolesAsync(ct);
            var role = roles.FirstOrDefault(r => r.Code == target.RoleCode);
            if (role == null)
            {
                TempData["ErrorMessage"] = "Không xác định được vai trò hợp lệ của tài khoản.";
                return RedirectToAction(nameof(Index));
            }

            await _staffService.UpdateStaffAsync(accountId, newPassword.Trim(), target.FullName, role.Id, target.IsActive, ct);
            TempData["SuccessMessage"] = $"Đặt lại mật khẩu cho tài khoản '{target.Username}' thành công.";
        }
        catch (AdminOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi đặt lại mật khẩu cho tài khoản {AccountId}.", accountId);
            TempData["ErrorMessage"] = GenericErrorMessage;
        }

        return RedirectToAction(nameof(Index));
    }
}

