using System.Security.Claims;
using System.Text;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QRCoder;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "StaffAny")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CashierController : Controller
{
    private readonly IBillingService _billingService;
    private readonly ICashierShiftService _shiftService;
    private readonly BillingOptions _billingOptions;

    public CashierController(
        IBillingService billingService,
        ICashierShiftService shiftService,
        IOptions<BillingOptions> billingOptions)
    {
        _billingService = billingService;
        _shiftService = shiftService;
        _billingOptions = billingOptions.Value;
    }

    private int CurrentAccountId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search = null,
        InvoiceStatus? invoiceStatus = null,
        int? selectedId = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<PendingInvoiceItemDto> invoices = Array.Empty<PendingInvoiceItemDto>();
        InvoiceDetailDto? selectedInvoice = null;
        int activeId = 0;

        if (!string.IsNullOrWhiteSpace(search))
        {
            invoices = await _billingService.GetPendingInvoicesAsync(search, InvoiceStatus.Unpaid, ct);
            activeId = selectedId ?? invoices.FirstOrDefault()?.AppointmentId ?? 0;
            if (activeId > 0)
            {
                var detailResult = await _billingService.GetInvoiceDetailAsync(activeId, ct);
                if (detailResult.IsSuccess && detailResult.Value?.Status != InvoiceStatus.Paid)
                {
                    selectedInvoice = detailResult.Value;
                }
            }
        }
        else if (selectedId.HasValue && selectedId.Value > 0)
        {
            activeId = selectedId.Value;
            var detailResult = await _billingService.GetInvoiceDetailAsync(activeId, ct);
            if (detailResult.IsSuccess && detailResult.Value?.Status != InvoiceStatus.Paid)
            {
                selectedInvoice = detailResult.Value;
            }
        }

        var currentShift = await _shiftService.GetCurrentShiftAsync(CurrentAccountId, ct);

        ViewBag.Search = search;
        ViewBag.InvoiceStatus = InvoiceStatus.Unpaid;
        ViewBag.SelectedInvoice = selectedInvoice;
        ViewBag.SelectedId = activeId;
        ViewBag.CurrentShift = currentShift;
        ViewBag.UnpaidCount = invoices.Count;
        ViewBag.PaidCount = 0;
        ViewBag.AllCount = invoices.Count;

        return View(invoices);
    }

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

    [HttpGet]
    public async Task<IActionResult> GetDetailPartial(int appointmentId, CancellationToken ct = default)
    {
        var detailResult = await _billingService.GetInvoiceDetailAsync(appointmentId, ct);
        if (!detailResult.IsSuccess)
        {
            return NotFound($"<div style='padding: 30px; text-align: center; color: #dc2626; font-size: 14px;'>Không tìm thấy thông tin lượt khám: {detailResult.Error.Message}</div>");
        }

        var currentShift = await _shiftService.GetCurrentShiftAsync(CurrentAccountId, ct);
        ViewBag.CurrentShift = currentShift;

        return PartialView("_InvoiceDetail", detailResult.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout([FromBody] CheckoutInvoiceRequest request, CancellationToken ct = default)
    {
        int cashierId = CurrentAccountId;
        var reqWithCashier = request with { CashierAccountId = cashierId };

        var result = await _billingService.CheckoutInvoiceAsync(reqWithCashier, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        return Ok(new
        {
            success = true,
            invoiceId = result.Value,
            invoiceNumber = $"HD-{result.Value:D6}",
            message = $"Thanh toán thành công! Mã hóa đơn: HD-{result.Value:D6}"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenShift(decimal openingCash, string? note, CancellationToken ct = default)
    {
        var result = await _shiftService.OpenShiftAsync(new OpenShiftRequest(CurrentAccountId, openingCash, note), ct);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"Đã mở ca làm việc thành công với tiền đầu ca: {openingCash:N0} đ.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseShift(int shiftId, decimal countedCash, string? note, CancellationToken ct = default)
    {
        var result = await _shiftService.CloseShiftAsync(new CloseShiftRequest(shiftId, CurrentAccountId, countedCash, note), ct);
        if (result.IsSuccess)
        {
            var s = result.Value!;
            var diffStr = s.Difference == 0 ? "Khớp hoàn toàn" : (s.Difference > 0 ? $"Thừa {s.Difference:N0} đ" : $"Thiếu {Math.Abs(s.Difference!.Value):N0} đ");
            TempData["SuccessMessage"] = $"Đã kết ca thành công! Tiền thực đếm: {s.CountedCash:N0} đ. Chênh lệch: {diffStr}.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(int appointmentId, CancellationToken ct = default)
    {
        var result = await _billingService.GetInvoiceDetailAsync(appointmentId, ct);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        if (result.Value!.Status != Domain.InvoiceStatus.Paid)
        {
            TempData["ErrorMessage"] = "Hồ sơ này chưa thanh toán hoàn tất, chưa thể in biên lai thu tiền.";
            return RedirectToAction(nameof(Index), new { selectedId = appointmentId });
        }

        return View(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> Ledger(LedgerFilterDto filter, CancellationToken ct = default)
    {
        var ledger = await _billingService.GetLedgerAsync(filter, ct: ct);
        var currentShift = await _shiftService.GetCurrentShiftAsync(CurrentAccountId, ct);
        ViewBag.Filter = filter;
        ViewBag.CurrentShift = currentShift;
        return View(ledger);
    }

    [HttpGet]
    public async Task<IActionResult> LedgerExport(LedgerFilterDto filter, CancellationToken ct = default)
    {
        var ledger = await _billingService.GetLedgerAsync(filter, exportAll: true, ct: ct);
        var sb = new StringBuilder();
        sb.AppendLine("Mã GD,Thời gian,Số HĐ,Mã lượt khám,Bệnh nhân,Số tiền (VNĐ),Loại GD,Hình thức,Mã tham chiếu NH,Thu ngân");

        static string CsvEscape(string? val) => $"\"{(val ?? string.Empty).Replace("\"", "\"\"")}\"";

        foreach (var item in ledger)
        {
            var pType = item.PaymentType == Domain.PaymentType.Payment ? "Thanh toán" : (item.PaymentType == Domain.PaymentType.Deposit ? "Tạm ứng" : "Hoàn tiền");
            var pMethod = item.PaymentMethod == Domain.PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản";
            sb.AppendLine($"{CsvEscape(item.TransactionCode)},\"{item.PaidAt:dd/MM/yyyy HH:mm:ss}\",{CsvEscape(item.InvoiceNumber)},{CsvEscape(item.AppointmentCode)},{CsvEscape(item.PatientName)},{item.Amount},{CsvEscape(pType)},{CsvEscape(pMethod)},{CsvEscape(item.ReferenceCode)},{CsvEscape(item.CashierName)}");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"SoThuNgan_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> PatientHistory(int patientId, CancellationToken ct = default)
    {
        var history = await _billingService.GetPatientBillingHistoryAsync(patientId, ct);
        return Json(history);
    }

    [HttpGet]
    public async Task<IActionResult> GenerateQr(int appointmentId, decimal? customAmount = null, CancellationToken ct = default)
    {
        var detailRes = await _billingService.GetInvoiceDetailAsync(appointmentId, ct);
        if (!detailRes.IsSuccess)
        {
            return BadRequest(new { success = false, message = detailRes.Error.Message });
        }

        var detail = detailRes.Value!;
        decimal amount = customAmount ?? detail.PatientAmount;
        if (amount <= 0)
        {
            return BadRequest(new { success = false, message = "Số tiền thanh toán phải lớn hơn 0 để tạo mã VietQR." });
        }

        string msg = !string.IsNullOrWhiteSpace(detail.InvoiceNumber) && detail.InvoiceNumber != "Chưa lập"
            ? $"HIS {detail.InvoiceNumber}"
            : $"HIS {detail.AppointmentCode}";

        var payload = VietQrBuilder.GeneratePayload(
            _billingOptions.BankCode,
            _billingOptions.BankAccountNumber,
            amount,
            msg);

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrBytes = qrCode.GetGraphic(20);
        string qrDataUri = $"data:image/png;base64,{Convert.ToBase64String(qrBytes)}";

        return Ok(new VietQrResponseDto(
            _billingOptions.BankCode,
            _billingOptions.BankName,
            _billingOptions.BankAccountNumber,
            _billingOptions.BankAccountHolder,
            amount,
            msg,
            payload,
            qrDataUri));
    }

    [HttpGet]
    public async Task<IActionResult> GenerateDepositQr(int patientId, decimal amount, CancellationToken ct = default)
    {
        if (patientId <= 0)
        {
            return BadRequest(new { success = false, message = "Vui lòng chọn hồ sơ bệnh nhân trước khi tạo mã QR." });
        }

        if (amount <= 0)
        {
            return BadRequest(new { success = false, message = "Số tiền tạm ứng phải lớn hơn 0 đ để tạo mã VietQR." });
        }

        var patients = await _billingService.SearchPatientsAsync(patientId.ToString(), ct);
        var patient = patients.FirstOrDefault(p => p.Id == patientId);
        string patientCode = patient?.PatientCode ?? patientId.ToString("D8");
        string cleanName = patient != null ? VietQrBuilder.SanitizeMessage(patient.FullName).ToUpperInvariant().Replace(" ", "") : "BN";
        if (cleanName.Length > 12) cleanName = cleanName[..12];

        string msg = $"TU {patientCode[4..]} {cleanName}".Trim();
        if (msg.Length > 25) msg = msg[..25];

        var payload = VietQrBuilder.GeneratePayload(
            _billingOptions.BankCode,
            _billingOptions.BankAccountNumber,
            amount,
            msg);

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrBytes = qrCode.GetGraphic(20);
        string qrDataUri = $"data:image/png;base64,{Convert.ToBase64String(qrBytes)}";

        return Ok(new VietQrResponseDto(
            _billingOptions.BankCode,
            _billingOptions.BankName,
            _billingOptions.BankAccountNumber,
            _billingOptions.BankAccountHolder,
            amount,
            msg,
            payload,
            qrDataUri));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SettleDischarge([FromBody] SettleDischargeRequest request, CancellationToken ct = default)
    {
        int cashierId = CurrentAccountId;
        var reqWithCashier = request with { CashierAccountId = cashierId };

        var result = await _billingService.SettleDischargeAsync(reqWithCashier, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        var res = result.Value!;
        string actionMsg = res.RefundedToPatient > 0
            ? $"Quyết toán thành công! Đã cấn trừ {res.DepositDeducted:N0} đ và hoàn trả {res.RefundedToPatient:N0} đ cho người bệnh."
            : (res.AdditionalPaid > 0
                ? $"Quyết toán thành công! Đã cấn trừ {res.DepositDeducted:N0} đ và thu thêm {res.AdditionalPaid:N0} đ."
                : $"Quyết toán thành công! Đã cấn trừ đủ 100% viện phí ({res.DepositDeducted:N0} đ).");

        return Ok(new
        {
            success = true,
            data = res,
            message = actionMsg
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDeposit([FromBody] CreateDepositRequest request, CancellationToken ct = default)
    {
        var reqWithCashier = request with { CashierAccountId = CurrentAccountId };
        var result = await _billingService.CreateDepositAsync(reqWithCashier, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        return Ok(new
        {
            success = true,
            deposit = result.Value,
            message = $"Thu tạm ứng thành công! Mã phiếu: {result.Value!.DepositCode}"
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetPatientDeposits(int patientId, CancellationToken ct = default)
    {
        var summary = await _billingService.GetPatientDepositSummaryAsync(patientId, ct);
        return Json(summary);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundDeposit([FromBody] RefundDepositRequest request, CancellationToken ct = default)
    {
        var reqWithCashier = request with { CashierAccountId = CurrentAccountId };
        var result = await _billingService.RefundDepositAsync(reqWithCashier, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        return Ok(new
        {
            success = true,
            transactionCode = result.Value,
            message = $"Hoàn tiền tạm ứng thành công! Mã phiếu chi: {result.Value}"
        });
    }

    [HttpGet]
    public async Task<IActionResult> SearchPatients(string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Ok(Array.Empty<PatientSearchResultDto>());
        }

        var results = await _billingService.SearchPatientsAsync(term, ct);
        return Ok(results);
    }

    [HttpGet]
    public async Task<IActionResult> QuickLookupPatient(string search, CancellationToken ct = default)
    {
        var result = await _billingService.QuickLookupPatientDepositsAsync(search, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        return Ok(new { success = true, data = result.Value });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundAllDeposits([FromBody] RefundAllPatientDepositsRequest request, CancellationToken ct = default)
    {
        var reqWithCashier = request with { CashierAccountId = CurrentAccountId };
        var result = await _billingService.RefundAllPatientDepositsAsync(reqWithCashier, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        var res = result.Value!;
        return Ok(new
        {
            success = true,
            data = res,
            message = $"Tất toán thành công {res.TotalRefunded:N0} đ (trên {res.SlipsRefundedCount} phiếu) cho bệnh nhân {res.PatientName}!"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundInvoice([FromBody] RefundInvoiceRequest request, CancellationToken ct = default)
    {
        var reqWithCashier = request with { CashierAccountId = CurrentAccountId };
        var result = await _billingService.RefundInvoiceAsync(reqWithCashier, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }

        return Ok(new
        {
            success = true,
            transactionCode = result.Value,
            message = $"Hoàn viện phí thành công! Mã phiếu chi: {result.Value}"
        });
    }

    [HttpGet]
    public async Task<IActionResult> Deposits(
        string? search = null,
        PaymentMethod? method = null,
        DepositStatus? status = null,
        int? patientId = null,
        CancellationToken ct = default)
    {
        var deposits = await _billingService.GetDepositsAsync(search, method, status, patientId, ct);
        var totals = await _billingService.GetDepositTotalsAsync(ct);
        var currentShift = await _shiftService.GetCurrentShiftAsync(CurrentAccountId, ct);
        ViewBag.CurrentShift = currentShift;
        ViewBag.Totals = totals;
        ViewBag.Search = search;
        ViewBag.Method = method;
        ViewBag.Status = status;
        ViewBag.PatientId = patientId;
        return View(deposits);
    }
}
