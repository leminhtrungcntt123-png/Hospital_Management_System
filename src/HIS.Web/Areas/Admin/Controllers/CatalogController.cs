using HIS.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public class CatalogController : Controller
{
    private readonly IDepartmentService _departmentService;

    public CatalogController(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    /// <summary>
    /// Hiển thị danh sách các chuyên khoa phòng trong bệnh viện.
    /// </summary>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var departments = await _departmentService.GetDepartmentsAsync(ct);
        return View(departments);
    }

    /// <summary>
    /// API lấy danh sách phòng khám theo chuyên khoa dạng JSON.
    /// </summary>
    /// <param name="departmentId">Mã khoa phòng.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Rooms(int departmentId, CancellationToken ct)
    {
        var rooms = await _departmentService.GetRoomsByDepartmentAsync(departmentId, ct);
        return Json(rooms);
    }

    /// <summary>
    /// Thêm mới chuyên khoa phòng trong bệnh viện.
    /// </summary>
    /// <param name="name">Tên khoa phòng.</param>
    /// <param name="description">Mô tả chức năng nhiệm vụ.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDepartment(string name, string? description, CancellationToken ct)
    {
        try
        {
            await _departmentService.CreateDepartmentAsync(name, description, ct);
            TempData["SuccessMessage"] = "Thêm chuyên khoa thành công.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Cập nhật thông tin (tên, mô tả) của chuyên khoa phòng.
    /// </summary>
    /// <param name="id">Mã khoa phòng.</param>
    /// <param name="name">Tên khoa phòng mới.</param>
    /// <param name="description">Mô tả mới.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDepartment(int id, string name, string? description, CancellationToken ct)
    {
        try
        {
            await _departmentService.UpdateDepartmentAsync(id, name, description, ct);
            TempData["SuccessMessage"] = "Cập nhật chuyên khoa thành công.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Xóa chuyên khoa phòng (ngăn xóa nếu đang có bác sĩ hoặc phòng khám liên kết).
    /// </summary>
    /// <param name="id">Mã khoa phòng.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDepartment(int id, CancellationToken ct)
    {
        try
        {
            var success = await _departmentService.DeleteDepartmentAsync(id, ct);
            if (success)
                TempData["SuccessMessage"] = "Xóa chuyên khoa thành công.";
            else
                TempData["ErrorMessage"] = "Chuyên khoa không tồn tại.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}

