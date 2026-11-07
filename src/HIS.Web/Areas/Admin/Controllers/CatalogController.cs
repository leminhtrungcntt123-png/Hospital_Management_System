using HIS.BLL.Services;
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

    // 1. Danh sách chuyên khoa phòng
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var departments = await _departmentService.GetDepartmentsAsync(ct);
        return View(departments);
    }

    // 2. Lấy danh sách phòng khám theo chuyên khoa (trả về JSON phục vụ dropdown/modal)
    [HttpGet]
    public async Task<IActionResult> Rooms(int departmentId, CancellationToken ct)
    {
        var rooms = await _departmentService.GetRoomsByDepartmentAsync(departmentId, ct);
        return Json(rooms);
    }

    // 3. Thêm mới chuyên khoa
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

    // 4. Cập nhật thông tin chuyên khoa
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

    // 5. Xóa chuyên khoa phòng (kiểm tra ràng buộc an toàn)
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
