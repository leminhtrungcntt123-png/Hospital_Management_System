using System.Text;
using System.Threading.Channels;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using Microsoft.Extensions.Options;

namespace HIS.Web.Workers;

public sealed record PaymentReceiptDispatchJob(
    int AppointmentId,
    int InvoiceId,
    string InvoiceNumber,
    string? ToEmail,
    int? PatientAccountId,
    decimal PatientPayable,
    decimal DepositDeducted,
    decimal AdditionalPaid,
    decimal RefundedToPatient);

public sealed class PaymentReceiptEmailQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentReceiptEmailQueue> logger) : BackgroundService
{
    private readonly Channel<PaymentReceiptDispatchJob> _jobs = Channel.CreateBounded<PaymentReceiptDispatchJob>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait
    });

    public bool Enqueue(PaymentReceiptDispatchJob job)
    {
        if (!_jobs.Writer.TryWrite(job))
        {
            logger.LogWarning("Payment receipt dispatch queue is full. Skipped invoice {InvoiceNumber}.", job.InvoiceNumber);
            return false;
        }

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                if (job.PatientAccountId.HasValue && job.PatientAccountId.Value > 0)
                {
                    var summaryText = job.RefundedToPatient > 0
                        ? $"Hóa đơn {job.InvoiceNumber} đã quyết toán xong. Đã cấn trừ {job.DepositDeducted:N0} đ và hoàn trả {job.RefundedToPatient:N0} đ."
                        : (job.AdditionalPaid > 0
                            ? $"Hóa đơn {job.InvoiceNumber} đã thanh toán thành công (BN chi trả: {job.PatientPayable:N0} đ, thu tại quầy: {job.AdditionalPaid:N0} đ)."
                            : $"Hóa đơn {job.InvoiceNumber} đã quyết toán thành công qua tạm ứng ({job.DepositDeducted:N0} đ).");

                    await notificationService.SendInAppNotificationAsync(
                        job.PatientAccountId.Value,
                        job.AppointmentId,
                        $"Biên lai thanh toán {job.InvoiceNumber}",
                        summaryText,
                        stoppingToken);
                }

                if (!string.IsNullOrWhiteSpace(job.ToEmail))
                {
                    var emailOptions = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<EmailOptions>>().Value;
                    if (!emailOptions.Enabled)
                    {
                        logger.LogInformation(
                            "Skipped sending payment receipt email for {InvoiceNumber} because Email:Enabled is false.",
                            job.InvoiceNumber);
                        continue;
                    }

                    var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();
                    var detailResult = await billingService.GetInvoiceDetailAsync(job.AppointmentId, stoppingToken);
                    if (!detailResult.IsSuccess || detailResult.Value == null)
                    {
                        logger.LogWarning(
                            "Could not load invoice detail for receipt email (AppointmentId={AppointmentId}, Invoice={InvoiceNumber}).",
                            job.AppointmentId,
                            job.InvoiceNumber);
                        continue;
                    }

                    var subject = $"[MediCare HIS] Biên lai thanh toán viện phí {job.InvoiceNumber}";
                    var body = BuildReceiptEmailHtml(detailResult.Value);

                    await notificationService.SendEmailNotificationAsync(job.ToEmail, subject, body, stoppingToken);
                    logger.LogInformation(
                        "Processed payment receipt email dispatch for {InvoiceNumber} to {Recipient}.",
                        job.InvoiceNumber,
                        job.ToEmail);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Payment receipt dispatch failed for {InvoiceNumber}: {Message}",
                    job.InvoiceNumber,
                    ex.Message);
            }
        }
    }

    /// <summary>
    /// Xây dựng nội dung biên lai điện tử bằng HTML (chỉ gồm thông tin tài chính, không đưa chẩn đoán bệnh hay đơn thuốc nhạy cảm).
    /// </summary>
    public static string BuildReceiptEmailHtml(InvoiceDetailDto detail)
    {
        static string H(string? s) => (s ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

        var paidAtText = detail.PaidAt?.ToString("HH:mm, dd/MM/yyyy") ?? DateTime.Now.ToString("HH:mm, dd/MM/yyyy");
        var methodText = string.IsNullOrWhiteSpace(detail.PaymentMethod) ? "Tiền mặt" : detail.PaymentMethod;

        var categoryTotals = detail.Items
            .GroupBy(i => i.Category)
            .Select(g => new
            {
                Category = g.Key,
                Total = g.Sum(x => x.Total),
                Covered = g.Sum(x => x.CoveredAmount),
                Patient = g.Sum(x => x.PatientAmount)
            })
            .ToList();

        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="vi">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
</head>
<body style="margin:0;padding:24px;background-color:#f1f5f9;font-family:Arial,Helvetica,sans-serif;color:#0f172a;">
  <div style="max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;">
    <div style="background:#0284c7;color:#ffffff;padding:20px 24px;">
      <div style="font-size:12px;letter-spacing:0.6px;text-transform:uppercase;opacity:0.9;">Bệnh viện Đa khoa MediCare · Phòng Tài chính - Kế toán</div>
      <h2 style="margin:6px 0 0;font-size:20px;">BIÊN LAI THANH TOÁN VIỆN PHÍ ĐIỆN TỬ</h2>
    </div>
    <div style="padding:20px 24px;font-size:14px;line-height:1.6;">
""");

        sb.Append($"<p style=\"margin-top:0;\">Kính gửi <strong>{H(detail.PatientName)}</strong>,</p>");
        sb.Append("<p>Hệ thống xin gửi thông báo xác nhận quyết toán viện phí của Quý khách với các thông tin tài chính như sau:</p>");

        sb.Append("""
      <table style="width:100%;border-collapse:collapse;margin:14px 0;font-size:13.5px;">
""");
        sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;width:180px;\">Số hóa đơn:</td><td style=\"padding:6px 0;font-weight:700;\">{H(detail.InvoiceNumber)}</td></tr>");
        sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;\">Mã lượt khám:</td><td style=\"padding:6px 0;font-weight:700;\">{H(detail.AppointmentCode)}</td></tr>");
        sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;\">Mã người bệnh:</td><td style=\"padding:6px 0;\">{H(detail.PatientCode)}</td></tr>");
        sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;\">Khoa khám:</td><td style=\"padding:6px 0;\">{H(detail.DepartmentName)}</td></tr>");
        sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;\">Ngày khám:</td><td style=\"padding:6px 0;\">{detail.ExamDate:dd/MM/yyyy}</td></tr>");
        sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;\">Thời gian quyết toán:</td><td style=\"padding:6px 0;\">{H(paidAtText)}</td></tr>");
        sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;\">Hình thức thanh toán:</td><td style=\"padding:6px 0;\">{H(methodText)}</td></tr>");
        if (!string.IsNullOrWhiteSpace(detail.TransactionCode))
        {
            sb.Append($"<tr><td style=\"padding:6px 0;color:#475569;\">Mã chứng từ:</td><td style=\"padding:6px 0;font-family:monospace;\">{H(detail.TransactionCode)}</td></tr>");
        }
        sb.Append("</table>");

        if (categoryTotals.Count > 0)
        {
            sb.Append("""
      <div style="font-weight:700;margin:16px 0 8px;color:#1e293b;">Tổng hợp theo nhóm chi phí:</div>
      <table style="width:100%;border-collapse:collapse;font-size:13px;border:1px solid #cbd5e1;">
        <thead>
          <tr style="background:#f8fafc;">
            <th style="text-align:left;padding:8px 10px;border:1px solid #cbd5e1;">Nhóm chi phí</th>
            <th style="text-align:right;padding:8px 10px;border:1px solid #cbd5e1;">Thành tiền</th>
            <th style="text-align:right;padding:8px 10px;border:1px solid #cbd5e1;">BHYT trả</th>
            <th style="text-align:right;padding:8px 10px;border:1px solid #cbd5e1;">BN chi trả</th>
          </tr>
        </thead>
        <tbody>
""");
            foreach (var row in categoryTotals)
            {
                sb.Append($"""
          <tr>
            <td style="padding:8px 10px;border:1px solid #e2e8f0;">{H(row.Category)}</td>
            <td style="padding:8px 10px;border:1px solid #e2e8f0;text-align:right;">{row.Total:N0} đ</td>
            <td style="padding:8px 10px;border:1px solid #e2e8f0;text-align:right;color:#15803d;">{row.Covered:N0} đ</td>
            <td style="padding:8px 10px;border:1px solid #e2e8f0;text-align:right;font-weight:700;">{row.Patient:N0} đ</td>
          </tr>
""");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("""
      <div style="margin-top:16px;padding:14px 16px;background:#f8fafc;border:1px solid #cbd5e1;border-radius:8px;font-size:13.5px;">
""");
        sb.Append($"<div style=\"display:flex;justify-content:space-between;margin-bottom:6px;\"><span>Tổng chi phí:</span><strong>{detail.TotalAmount:N0} đ</strong></div>");
        sb.Append($"<div style=\"display:flex;justify-content:space-between;margin-bottom:6px;color:#15803d;\"><span>Quỹ BHYT chi trả:</span><strong>- {detail.InsuranceCoveredAmount:N0} đ</strong></div>");
        sb.Append($"<div style=\"display:flex;justify-content:space-between;margin-bottom:6px;\"><span>Người bệnh cùng chi trả:</span><strong>{detail.PatientAmount:N0} đ</strong></div>");
        if (detail.DepositDeducted > 0)
        {
            sb.Append($"<div style=\"display:flex;justify-content:space-between;margin-bottom:6px;color:#0369a1;\"><span>Đã cấn trừ tạm ứng:</span><strong>{detail.DepositDeducted:N0} đ</strong></div>");
        }
        if (detail.AdditionalPaid > 0)
        {
            sb.Append($"<div style=\"display:flex;justify-content:space-between;padding-top:8px;border-top:1px dashed #cbd5e1;font-size:15px;\"><span>Thực thu tại quầy:</span><strong style=\"color:#15803d;\">{detail.AdditionalPaid:N0} đ</strong></div>");
        }
        else if (detail.DepositRefundedAmount > 0)
        {
            sb.Append($"<div style=\"display:flex;justify-content:space-between;padding-top:8px;border-top:1px dashed #cbd5e1;font-size:15px;\"><span>Hoàn trả tiền thừa tạm ứng:</span><strong style=\"color:#0284c7;\">{detail.DepositRefundedAmount:N0} đ</strong></div>");
        }
        sb.Append("</div>");

        sb.Append("""
      <p style="margin-top:18px;font-size:12.5px;color:#64748b;">
        * Vì lý do bảo mật thông tin y tế, email này chỉ thể hiện thông tin tài chính và không bao gồm chẩn đoán hay chi tiết đơn thuốc. Quý khách có thể tra cứu hồ sơ chi tiết trực tiếp tại Cổng người bệnh MediCare.
      </p>
    </div>
    <div style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:12px 24px;font-size:12px;color:#64748b;text-align:center;">
      Bệnh viện Đa khoa MediCare · Email tự động, vui lòng không trả lời trực tiếp.
    </div>
  </div>
</body>
</html>
""");

        return sb.ToString();
    }
}
