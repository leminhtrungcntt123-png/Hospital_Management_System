using HIS.BLL.Common;

namespace HIS.BLL.Services;

/// <summary>
/// Tính viện phí và thanh toán:
/// - BHYT chỉ áp dụng ở bước này; thẻ phải thuộc đúng patient_id; kiểm tra hạn thẻ theo NGÀY KHÁM; lưu coverage_pct_applied.
/// - Gửi email biên lai điện tử qua payments.receipt_email (nếu trống và link ACTIVE thì lấy email booked_by).
/// </summary>
public interface IBillingService
{
    Task<Result<int>> GenerateInvoiceAsync(int appointmentId, int? insuranceCardId, CancellationToken ct = default);
    Task<Result<int>> ProcessPaymentAsync(int invoiceId, decimal amount, string paymentMethod, string? receiptEmail, int? cashierId, CancellationToken ct = default);
}
