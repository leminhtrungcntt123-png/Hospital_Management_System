using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "StaffAny")]
public class CashierController : Controller
{
    private readonly IBillingService _billingService;
    private readonly AppDbContext _context;

    public CashierController(IBillingService billingService, AppDbContext context)
    {
        _billingService = billingService;
        _context = context;
    }

    /// <summary>
    /// Hiển thị màn hình quầy thu ngân và danh sách hóa đơn chờ xử lý viện phí.
    /// </summary>
    /// <param name="search">Từ khóa tìm kiếm bệnh nhân hoặc mã hẹn.</param>
    /// <param name="selectedId">Mã lượt khám đang chọn xem chi tiết.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Index(string? search = null, int? selectedId = null, CancellationToken ct = default)
    {
        var invoices = await _billingService.GetPendingInvoicesAsync(search, ct);

        InvoiceDetailDto? selectedInvoice = null;
        int activeId = selectedId ?? invoices.FirstOrDefault()?.AppointmentId ?? 0;
        if (activeId > 0)
        {
            var detailResult = await _billingService.GetInvoiceDetailAsync(activeId, ct);
            if (detailResult.IsSuccess)
            {
                selectedInvoice = detailResult.Value;
            }
        }

        ViewBag.Search = search;
        ViewBag.SelectedInvoice = selectedInvoice;
        ViewBag.SelectedId = activeId;

        return View(invoices);
    }

    /// <summary>
    /// API trả về dữ liệu JSON chi tiết viện phí của lượt khám cho giao diện.
    /// </summary>
    /// <param name="appointmentId">Mã lượt khám.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> GetDetailJson(int appointmentId, CancellationToken ct = default)
    {
        var result = await _billingService.GetInvoiceDetailAsync(appointmentId, ct);
        if (!result.IsSuccess)
        {
            return NotFound(new { message = result.Error.Message });
        }

        return Json(result.Value);
    }

    /// <summary>
    /// Tiếp nhận thanh toán viện phí (tiền mặt / chuyển khoản) từ giao diện thu ngân.
    /// </summary>
    /// <param name="request">Yêu cầu thanh toán viện phí.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost]
    public async Task<IActionResult> Checkout([FromBody] CheckoutInvoiceRequest request, CancellationToken ct = default)
    {
        int cashierId = GetCurrentAccountId();
        var reqWithCashier = request with { CashierAccountId = cashierId };

        var result = await _billingService.CheckoutInvoiceAsync(reqWithCashier, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        return Ok(new
        {
            success = true,
            invoiceNumber = $"HD-{result.Value:D6}",
            message = $"Thanh toán thành công! Hóa đơn: HD-{result.Value:D6}"
        });
    }

    /// <summary>
    /// Hiển thị biên lai / hóa đơn thu tiền viện phí để in ấn hoặc đối chiếu.
    /// </summary>
    /// <param name="appointmentId">Mã lượt khám.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet]
    public async Task<IActionResult> Receipt(int appointmentId, CancellationToken ct = default)
    {
        var result = await _billingService.GetInvoiceDetailAsync(appointmentId, ct);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    private int GetCurrentAccountId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}
