using System.Globalization;
using System.Text;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public sealed class AdminReportService(AppDbContext db) : IAdminReportService
{
    /// <summary>
    /// Tổng hợp báo cáo doanh thu tài chính (tạm ứng, thanh toán viện phí, hoàn tiền, doanh thu thuần và lượt khám) theo khoảng thời gian.
    /// </summary>
    /// <param name="actorId">Mã tài khoản quản trị viên yêu cầu.</param>
    /// <param name="from">Ngày bắt đầu.</param>
    /// <param name="to">Ngày kết thúc.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Báo cáo doanh thu chi tiết (RevenueReport).</returns>
    public async Task<RevenueReport> GetRevenueAsync(
        int actorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(db, actorId, ct);
        AdminRules.Require(from <= to && to < DateOnly.MaxValue && to.DayNumber - from.DayNumber <= 365,
            "Chọn khoảng báo cáo từ 1 đến 366 ngày.");

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var payments = await db.Payments.AsNoTracking()
            .Where(payment => payment.PaidAt >= start && payment.PaidAt < end)
            .GroupBy(payment => payment.PaidAt.Date)
            .Select(group => new
            {
                Date = group.Key,
                Deposits = group.Sum(payment => payment.PaymentType == PaymentType.Deposit ? payment.Amount : 0),
                Payments = group.Sum(payment => payment.PaymentType == PaymentType.Payment ? payment.Amount : 0),
                Refunds = group.Sum(payment => payment.PaymentType == PaymentType.Refund ? payment.Amount : 0)
            })
            .ToDictionaryAsync(row => DateOnly.FromDateTime(row.Date), ct);

        var visits = await db.Appointments.AsNoTracking()
            .Where(appointment => appointment.CheckedInAt >= start && appointment.CheckedInAt < end)
            .GroupBy(appointment => appointment.CheckedInAt!.Value.Date)
            .Select(group => new
            {
                Date = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(row => DateOnly.FromDateTime(row.Date), row => row.Count, ct);

        var days = new List<RevenueDay>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            payments.TryGetValue(day, out var money);
            var deposits = money?.Deposits ?? 0;
            var paid = money?.Payments ?? 0;
            var refunds = money?.Refunds ?? 0;

            days.Add(new RevenueDay(day, deposits, paid, refunds,
                deposits + paid - refunds, visits.GetValueOrDefault(day)));
        }

        return new RevenueReport(from, to, days.Sum(day => day.Deposits),
            days.Sum(day => day.Payments), days.Sum(day => day.Refunds),
            days.Sum(day => day.Revenue), days.Sum(day => day.Visits), days);
    }

    /// <summary>
    /// Xuất dữ liệu báo cáo doanh thu tài chính thành tệp CSV kèm BOM UTF-8 để mở chính xác trên Excel.
    /// </summary>
    /// <param name="actorId">Mã tài khoản quản trị viên yêu cầu.</param>
    /// <param name="from">Ngày bắt đầu.</param>
    /// <param name="to">Ngày kết thúc.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Mảng byte tệp CSV UTF-8.</returns>
    public async Task<byte[]> ExportRevenueAsync(
        int actorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var report = await GetRevenueAsync(actorId, from, to, ct);
        var csv = new StringBuilder();
        csv.AppendLine("Ngày,Tạm ứng,Thanh toán,Hoàn tiền,Doanh thu thuần,Lượt khám");

        foreach (var day in report.Days)
        {
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd},{1:F0},{2:F0},{3:F0},{4:F0},{5}",
                day.Date, day.Deposits, day.Payments, day.Refunds, day.Revenue, day.Visits));
        }

        csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Tổng cộng,{0:F0},{1:F0},{2:F0},{3:F0},{4}",
            report.Deposits, report.Payments, report.Refunds, report.Revenue, report.Visits));

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
}

