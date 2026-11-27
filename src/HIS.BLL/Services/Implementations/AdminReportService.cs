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
    private static void ValidateReportRange(DateOnly from, DateOnly to) =>
        AdminRules.Require(from <= to && to < DateOnly.MaxValue && to.DayNumber - from.DayNumber <= 365,
            "Chọn khoảng báo cáo từ 1 đến 366 ngày.");

    private sealed record RevenueEntry(DateTime OccurredAt, int DepartmentId, decimal Amount);

    private async Task<List<RevenueEntry>> LoadEarnedRevenueEntriesAsync(
        DateTime start, DateTime end, CancellationToken ct)
    {
        // 1. Doanh thu từ hóa đơn đã tất toán (PatientAmount sau khi trừ BHYT), lấy mốc ngày thanh toán cuối hoặc ngày lập hóa đơn
        var settledInvoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Paid
                && i.PatientAmount > 0
                && !i.Patient.FullName.StartsWith("BN Test"))
            .Select(i => new
            {
                SettledAt = i.Payments
                    .Where(p => p.PaymentType == PaymentType.Payment)
                    .Select(p => (DateTime?)p.PaidAt)
                    .Max() ?? i.CreatedAt,
                DeptId = i.Appointment.DepartmentId ?? (i.Appointment.Doctor != null ? i.Appointment.Doctor.DepartmentId : 0),
                Amount = i.PatientAmount
            })
            .Where(x => x.SettledAt >= start && x.SettledAt < end)
            .ToListAsync(ct);

        var entries = settledInvoices
            .Select(x => new RevenueEntry(x.SettledAt, x.DeptId, x.Amount))
            .ToList();

        // 2. Các khoản thu trực tiếp trên hóa đơn chưa chốt Paid (thu một phần) và hoàn tiền hóa đơn (không trừ nhầm hoàn tạm ứng)
        var paymentAdjustments = await db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start
                && p.PaidAt < end
                && !p.Invoice.Patient.FullName.StartsWith("BN Test")
                && ((p.PaymentType == PaymentType.Payment && (p.Invoice.Status != InvoiceStatus.Paid || p.Invoice.PatientAmount <= 0))
                    || (p.PaymentType == PaymentType.Refund && (p.Note == null || !p.Note.StartsWith("Tất toán hoàn trả")))))
            .Select(p => new
            {
                p.PaidAt,
                DeptId = p.Invoice.Appointment.DepartmentId ?? (p.Invoice.Appointment.Doctor != null ? p.Invoice.Appointment.Doctor.DepartmentId : 0),
                Amount = p.PaymentType == PaymentType.Refund ? -p.Amount : p.Amount
            })
            .ToListAsync(ct);

        foreach (var item in paymentAdjustments)
        {
            entries.Add(new RevenueEntry(item.PaidAt, item.DeptId, item.Amount));
        }

        return entries;
    }

    public async Task<RevenueReport> GetRevenueAsync(
        int actorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(db, actorId, ct);
        ValidateReportRange(from, to);

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var payments = await db.Payments.AsNoTracking()
            .Where(payment => payment.PaidAt >= start
                && payment.PaidAt < end
                && !payment.Invoice.Patient.FullName.StartsWith("BN Test"))
            .GroupBy(payment => payment.PaidAt.Date)
            .Select(group => new
            {
                Date = group.Key,
                Deposits = group.Sum(payment => payment.PaymentType == PaymentType.Deposit ? payment.Amount : 0),
                Payments = group.Sum(payment => payment.PaymentType == PaymentType.Payment ? payment.Amount : 0),
                Refunds = group.Sum(payment => payment.PaymentType == PaymentType.Refund ? payment.Amount : 0)
            })
            .ToDictionaryAsync(row => DateOnly.FromDateTime(row.Date), ct);

        var revenueByDate = (await LoadEarnedRevenueEntriesAsync(start, end, ct))
            .GroupBy(e => DateOnly.FromDateTime(e.OccurredAt))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var visits = await db.Appointments.AsNoTracking()
            .Where(appointment => appointment.CheckedInAt >= start
                && appointment.CheckedInAt < end
                && !appointment.AppointmentCode.StartsWith("DEP-")
                && !appointment.Patient.FullName.StartsWith("BN Test"))
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
                revenueByDate.GetValueOrDefault(day), visits.GetValueOrDefault(day)));
        }

        return new RevenueReport(from, to, days.Sum(day => day.Deposits),
            days.Sum(day => day.Payments), days.Sum(day => day.Refunds),
            days.Sum(day => day.Revenue), days.Sum(day => day.Visits), days);
    }

    public async Task<RevenueSeriesReport> GetRevenueSeriesAsync(
        int actorId, string mode, DateOnly anchor, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(db, actorId, ct);
        var normalized = (mode ?? "week").Trim().ToLowerInvariant();
        if (normalized is not ("day" or "week" or "month" or "year"))
        {
            normalized = "week";
        }

        if (normalized is "day" or "week")
        {
            var offset = ((int)anchor.DayOfWeek + 6) % 7; // Thứ 2 = 0 .. CN = 6
            var monday = anchor.AddDays(-offset);
            var sunday = monday.AddDays(6);
            var weekReport = await GetRevenueAsync(actorId, monday, sunday, ct);

            var buckets = weekReport.Days
                .Select(d => new RevenueBucket(
                    d.Date.ToString("dd/MM", CultureInfo.InvariantCulture),
                    d.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    d.Deposits, d.Payments, d.Refunds, d.Revenue, d.Visits))
                .ToList();

            if (normalized == "day")
            {
                var singleDay = weekReport.Days.First(d => d.Date == anchor);
                return new RevenueSeriesReport(
                    "day", anchor, anchor,
                    singleDay.Deposits, singleDay.Payments, singleDay.Refunds, singleDay.Revenue, singleDay.Visits,
                    buckets);
            }

            return new RevenueSeriesReport(
                "week", monday, sunday,
                weekReport.Deposits, weekReport.Payments, weekReport.Refunds, weekReport.Revenue, weekReport.Visits,
                buckets);
        }

        if (normalized == "month")
        {
            var yearStart = new DateOnly(anchor.Year, 1, 1);
            var yearEnd = new DateOnly(anchor.Year, 12, 31);
            var start = yearStart.ToDateTime(TimeOnly.MinValue);
            var end = yearEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var payments = await db.Payments.AsNoTracking()
                .Where(p => p.PaidAt >= start && p.PaidAt < end && !p.Invoice.Patient.FullName.StartsWith("BN Test"))
                .GroupBy(p => p.PaidAt.Month)
                .Select(g => new
                {
                    Month = g.Key,
                    Deposits = g.Sum(p => p.PaymentType == PaymentType.Deposit ? p.Amount : 0),
                    Payments = g.Sum(p => p.PaymentType == PaymentType.Payment ? p.Amount : 0),
                    Refunds = g.Sum(p => p.PaymentType == PaymentType.Refund ? p.Amount : 0)
                })
                .ToDictionaryAsync(x => x.Month, ct);

            var visits = await db.Appointments.AsNoTracking()
                .Where(a => a.CheckedInAt >= start && a.CheckedInAt < end
                    && !a.AppointmentCode.StartsWith("DEP-") && !a.Patient.FullName.StartsWith("BN Test"))
                .GroupBy(a => a.CheckedInAt!.Value.Month)
                .Select(g => new { Month = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Month, x => x.Count, ct);

            var revByMonth = (await LoadEarnedRevenueEntriesAsync(start, end, ct))
                .GroupBy(e => e.OccurredAt.Month)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

            var buckets = Enumerable.Range(1, 12).Select(m =>
            {
                payments.TryGetValue(m, out var money);
                var dep = money?.Deposits ?? 0;
                var paid = money?.Payments ?? 0;
                var refAmt = money?.Refunds ?? 0;
                return new RevenueBucket($"T{m}", $"Tháng {m:00}/{anchor.Year}", dep, paid, refAmt, revByMonth.GetValueOrDefault(m), visits.GetValueOrDefault(m));
            }).ToList();

            return new RevenueSeriesReport(
                "month", yearStart, yearEnd,
                buckets.Sum(b => b.Deposits), buckets.Sum(b => b.Payments), buckets.Sum(b => b.Refunds),
                buckets.Sum(b => b.Revenue), buckets.Sum(b => b.Visits), buckets);
        }

        // mode == "year": 5 năm gần nhất kết thúc ở anchor.Year
        var startYear = anchor.Year - 4;
        var fiveYearStart = new DateOnly(startYear, 1, 1);
        var fiveYearEnd = new DateOnly(anchor.Year, 12, 31);
        var yStart = fiveYearStart.ToDateTime(TimeOnly.MinValue);
        var yEnd = fiveYearEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var yearPayments = await db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= yStart && p.PaidAt < yEnd && !p.Invoice.Patient.FullName.StartsWith("BN Test"))
            .GroupBy(p => p.PaidAt.Year)
            .Select(g => new
            {
                Year = g.Key,
                Deposits = g.Sum(p => p.PaymentType == PaymentType.Deposit ? p.Amount : 0),
                Payments = g.Sum(p => p.PaymentType == PaymentType.Payment ? p.Amount : 0),
                Refunds = g.Sum(p => p.PaymentType == PaymentType.Refund ? p.Amount : 0)
            })
            .ToDictionaryAsync(x => x.Year, ct);

        var yearVisits = await db.Appointments.AsNoTracking()
            .Where(a => a.CheckedInAt >= yStart && a.CheckedInAt < yEnd
                && !a.AppointmentCode.StartsWith("DEP-") && !a.Patient.FullName.StartsWith("BN Test"))
            .GroupBy(a => a.CheckedInAt!.Value.Year)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Year, x => x.Count, ct);

        var revByYear = (await LoadEarnedRevenueEntriesAsync(yStart, yEnd, ct))
            .GroupBy(e => e.OccurredAt.Year)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var yearBuckets = Enumerable.Range(startYear, 5).Select(y =>
        {
            yearPayments.TryGetValue(y, out var money);
            var dep = money?.Deposits ?? 0;
            var paid = money?.Payments ?? 0;
            var refAmt = money?.Refunds ?? 0;
            return new RevenueBucket(y.ToString(CultureInfo.InvariantCulture), $"Năm {y}", dep, paid, refAmt, revByYear.GetValueOrDefault(y), yearVisits.GetValueOrDefault(y));
        }).ToList();

        return new RevenueSeriesReport(
            "year", fiveYearStart, fiveYearEnd,
            yearBuckets.Sum(b => b.Deposits), yearBuckets.Sum(b => b.Payments), yearBuckets.Sum(b => b.Refunds),
            yearBuckets.Sum(b => b.Revenue), yearBuckets.Sum(b => b.Visits), yearBuckets);
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
        AdminRules.Require(from <= to && to < DateOnly.MaxValue && to.DayNumber - from.DayNumber <= 366 * 5,
            "Khoảng thống kê khoa tối đa 5 năm.");
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var departments = await db.Departments.AsNoTracking().ToListAsync(ct);

        var visitsByDept = await db.Appointments.AsNoTracking()
            .Where(a => a.CheckedInAt >= start
                && a.CheckedInAt < end
                && a.StatusId == (int)AppointmentStatusId.Completed
                && !a.AppointmentCode.StartsWith("DEP-")
                && !a.Patient.FullName.StartsWith("BN Test"))
            .GroupBy(a => a.DepartmentId ?? (a.Doctor != null ? a.Doctor.DepartmentId : 0))
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);

        var paymentsByDept = (await LoadEarnedRevenueEntriesAsync(start, end, ct))
            .GroupBy(e => e.DepartmentId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        return departments.Select(d => new DepartmentVisitStat(
            d.Id,
            d.Name,
            visitsByDept.GetValueOrDefault(d.Id),
            paymentsByDept.GetValueOrDefault(d.Id)
        )).OrderByDescending(s => s.Visits).ThenByDescending(s => s.Revenue).ToList();
    }

    public async Task<IReadOnlyList<AdminAuditLogRow>> GetAuditLogsAsync(
        int actorId, DateOnly? from = null, DateOnly? to = null, string? action = null, string? search = null, int limit = 200, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(db, actorId, ct);
        var query = db.AccessLogs.AsNoTracking()
            .Include(a => a.Account!)
                .ThenInclude(acc => acc.Doctor)
            .Include(a => a.Account!)
                .ThenInclude(acc => acc.Nurse)
            .Include(a => a.Account!)
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

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a =>
                (a.Account != null && a.Account.Username.Contains(term)) ||
                (a.Account != null && a.Account.Doctor != null && a.Account.Doctor.FullName.Contains(term)) ||
                (a.Account != null && a.Account.Nurse != null && a.Account.Nurse.FullName.Contains(term)) ||
                (a.Account != null && a.Account.DepartmentHead != null && a.Account.DepartmentHead.FullName.Contains(term)) ||
                (a.Patient != null && a.Patient.FullName.Contains(term)) ||
                (a.IpAddress != null && a.IpAddress.Contains(term)) ||
                a.Action.Contains(term));
        }

        var maxRows = limit > 0 ? Math.Min(limit, 1000) : 200;

        return await query
            .OrderByDescending(a => a.OccurredAt)
            .Take(maxRows)
            .Select(a => new AdminAuditLogRow(
                a.Id,
                a.AccountId,
                a.Account != null ? a.Account.Username : "—",
                a.Account != null && a.Account.Doctor != null ? a.Account.Doctor.FullName :
                a.Account != null && a.Account.Nurse != null ? a.Account.Nurse.FullName :
                a.Account != null && a.Account.DepartmentHead != null ? a.Account.DepartmentHead.FullName :
                (a.Account != null ? a.Account.Username : "—"),
                a.Action,
                a.Patient != null ? a.Patient.FullName : null,
                a.MedicalRecordId,
                a.OccurredAt,
                a.IpAddress))
            .ToListAsync(ct);
    }

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
