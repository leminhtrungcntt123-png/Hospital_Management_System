using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Interfaces;

public interface IBillingService
{
    // Lấy danh sách lượt khám chờ hoặc đã thanh toán tại quầy thu ngân
    Task<IReadOnlyList<PendingInvoiceItemDto>> GetPendingInvoicesAsync(string? search = null, CancellationToken ct = default);

    // Lấy chi tiết bảng kê viện phí, BHYT và các mục chi phí (tiền khám, cận lâm sàng, thuốc)
    Task<Result<InvoiceDetailDto>> GetInvoiceDetailAsync(int appointmentId, CancellationToken ct = default);

    // Xác nhận thu tiền viện phí (tiền mặt / chuyển khoản VietQR / thẻ) và xuất hóa đơn
    Task<Result<int>> CheckoutInvoiceAsync(CheckoutInvoiceRequest request, CancellationToken ct = default);
}

