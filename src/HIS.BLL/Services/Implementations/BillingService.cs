using HIS.BLL.Common;

namespace HIS.BLL.Services;

public class BillingService : IBillingService
{
    public Task<Result<int>> GenerateInvoiceAsync(int appointmentId, int? insuranceCardId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result<int>> ProcessPaymentAsync(int invoiceId, decimal amount, string paymentMethod, string? receiptEmail, int? cashierId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
