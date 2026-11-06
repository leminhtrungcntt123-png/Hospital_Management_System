using System.Globalization;
using System.Text;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public sealed class AdminReportService(AppDbContext db) : IAdminReportService
{
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

    public async Task<byte[]> ExportRevenueAsync(
        int actorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var report = await GetRevenueAsync(actorId, from, to, ct);
        var csv = new StringBuilder("Ngày,Tạm ứng,Thanh toán,Hoàn tiền,Doanh thu thực thu,Lượt đã tiếp nhận\r\n");

        foreach (var day in report.Days)
        {
            csv.AppendLine(string.Join(",", day.Date.ToString("yyyy-MM-dd"),
                Number(day.Deposits), Number(day.Payments), Number(day.Refunds),
                Number(day.Revenue), day.Visits.ToString(CultureInfo.InvariantCulture)));
        }

        csv.AppendLine(string.Join(",", "Tổng", Number(report.Deposits), Number(report.Payments),
            Number(report.Refunds), Number(report.Revenue), report.Visits.ToString(CultureInfo.InvariantCulture)));

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public async Task<IReadOnlyList<DepartmentVisitStat>> GetDepartmentStatsAsync(
        int actorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(db, actorId, ct);
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var departments = await db.Departments.AsNoTracking().ToListAsync(ct);

        var visitsByDept = await db.Appointments.AsNoTracking()
            .Where(a => a.CheckedInAt >= start && a.CheckedInAt < end)
            .GroupBy(a => a.DepartmentId ?? (a.Doctor != null ? a.Doctor.DepartmentId : 0))
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);

        var paymentsByDept = await db.Payments.AsNoTracking()
            .Include(p => p.Invoice)
                .ThenInclude(i => i.Appointment)
                    .ThenInclude(a => a.Doctor)
            .Where(p => p.PaidAt >= start && p.PaidAt < end && p.Invoice != null && p.Invoice.Appointment != null)
            .GroupBy(p => p.Invoice.Appointment!.DepartmentId ?? (p.Invoice.Appointment.Doctor != null ? p.Invoice.Appointment.Doctor.DepartmentId : 0))
            .Select(g => new
            {
                DepartmentId = g.Key,
                Revenue = g.Sum(p => p.PaymentType == PaymentType.Refund ? -p.Amount : p.Amount)
            })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Revenue, ct);

        return departments.Select(d => new DepartmentVisitStat(
            d.Id,
            d.Name,
            visitsByDept.GetValueOrDefault(d.Id),
            paymentsByDept.GetValueOrDefault(d.Id)
        )).OrderByDescending(s => s.Visits).ThenByDescending(s => s.Revenue).ToList();
    }

    public async Task<IReadOnlyList<AdminAuditLogRow>> GetAuditLogsAsync(
        int actorId, DateOnly? from = null, DateOnly? to = null, string? action = null, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(db, actorId, ct);
        var query = db.AccessLogs.AsNoTracking()
            .Include(a => a.Account)
                .ThenInclude(acc => acc.Doctor)
            .Include(a => a.Account)
                .ThenInclude(acc => acc.Nurse)
            .Include(a => a.Account)
                .ThenInclude(acc => acc.DepartmentHead)
            .Include(a => a.Patient)
            .AsQueryable();

        if (from.HasValue)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(a => a.OccurredAt >= start);
        }

        if (to.HasValue)
        {
            var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(a => a.OccurredAt < end);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        return await query
            .OrderByDescending(a => a.OccurredAt)
            .Take(200)
            .Select(a => new AdminAuditLogRow(
                a.Id,
                a.AccountId,
                a.Account.Username,
                a.Account.Doctor != null ? a.Account.Doctor.FullName :
                a.Account.Nurse != null ? a.Account.Nurse.FullName :
                a.Account.DepartmentHead != null ? a.Account.DepartmentHead.FullName : a.Account.Username,
                a.Action,
                a.Patient != null ? a.Patient.FullName : null,
                a.MedicalRecordId,
                a.OccurredAt,
                a.IpAddress))
            .ToListAsync(ct);
    }

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
