using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Services;

public interface IBillingService
{
    /// <summary>
    /// Lấy danh sách các lượt khám đang tiếp nhận hoặc đã hoàn tất cần thu ngân xử lý viện phí.
    /// </summary>
    Task<IReadOnlyList<PendingInvoiceItemDto>> GetPendingInvoicesAsync(string? search = null, InvoiceStatus? invoiceStatus = null, CancellationToken ct = default);

    /// <summary>
    /// Lấy bảng kê chi tiết viện phí (tiền khám, CLS, thuốc) và tính toán khấu trừ BHYT theo ngày khám.
    /// </summary>
    Task<Result<InvoiceDetailDto>> GetInvoiceDetailAsync(int appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Tiếp nhận thanh toán (tiền mặt / chuyển khoản), sinh mã hóa đơn, mã giao dịch bằng sequence DB, chống ghi trùng.
    /// </summary>
    Task<Result<int>> CheckoutInvoiceAsync(CheckoutInvoiceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lấy sổ thu ngân ghi nhận các giao dịch thu/hoàn tiền theo thời gian, nhân viên, phương thức.
    /// </summary>
    Task<IReadOnlyList<LedgerItemDto>> GetLedgerAsync(LedgerFilterDto filter, bool exportAll = false, CancellationToken ct = default);

    /// <summary>
    /// Lấy lịch sử chi phí khám bệnh và thanh toán của bệnh nhân.
    /// </summary>
    Task<IReadOnlyList<PatientBillingHistoryItemDto>> GetPatientBillingHistoryAsync(int patientId, CancellationToken ct = default);

    /// <summary>
    /// Thu tạm ứng viện phí (nhập viện, chỉ định xét nghiệm, phẫu thuật...).
    /// </summary>
    Task<Result<DepositDto>> CreateDepositAsync(CreateDepositRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lấy tổng quan và danh sách số dư tạm ứng của một bệnh nhân.
    /// </summary>
    Task<PatientDepositSummaryDto> GetPatientDepositSummaryAsync(int patientId, CancellationToken ct = default);

    /// <summary>
    /// Danh sách phiếu tạm ứng (lọc theo từ khóa tìm kiếm Mã BN/CCCD/Họ tên/Mã phiếu, hình thức thanh toán, trạng thái).
    /// </summary>
    Task<IReadOnlyList<DepositDto>> GetDepositsAsync(
        string? search = null,
        PaymentMethod? method = null,
        DepositStatus? status = null,
        int? patientId = null,
        CancellationToken ct = default);
    Task<DepositSummaryTotalsDto> GetDepositTotalsAsync(CancellationToken ct = default);

    /// <summary>
    /// Hoàn tiền tạm ứng còn dư cho bệnh nhân theo từng phiếu.
    /// </summary>
    Task<Result<string>> RefundDepositAsync(RefundDepositRequest request, CancellationToken ct = default);

    /// <summary>
    /// Tất toán & Hoàn toàn bộ số dư tạm ứng của một bệnh nhân trong một lần thao tác (batch refund tất cả các phiếu còn dư).
    /// </summary>
    Task<Result<RefundAllDepositsResultDto>> RefundAllPatientDepositsAsync(RefundAllPatientDepositsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Tra cứu nhanh số dư và danh sách phiếu tạm ứng khả dụng của bệnh nhân qua Mã BN / CCCD / SĐT.
    /// </summary>
    Task<Result<QuickPatientDepositLookupDto>> QuickLookupPatientDepositsAsync(string search, CancellationToken ct = default);

    /// <summary>
    /// Hoàn trả viện phí (hủy dịch vụ hoặc điều chỉnh hóa đơn đã thu).
    /// </summary>
    Task<Result<string>> RefundInvoiceAsync(RefundInvoiceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Tìm kiếm danh sách bệnh nhân để lập phiếu thu tạm ứng hoặc đối chiếu hồ sơ người bệnh.
    /// </summary>
    Task<IReadOnlyList<PatientSearchResultDto>> SearchPatientsAsync(string search, CancellationToken ct = default);

    /// <summary>
    /// Quyết toán viện phí toàn diện tại quầy thu ngân: cấn trừ viện phí vào tạm ứng, tự động xử lý thu thêm (nếu thiếu) hoặc hoàn tiền thừa (nếu dư).
    /// </summary>
    Task<Result<SettleDischargeResultDto>> SettleDischargeAsync(SettleDischargeRequest request, CancellationToken ct = default);
}
