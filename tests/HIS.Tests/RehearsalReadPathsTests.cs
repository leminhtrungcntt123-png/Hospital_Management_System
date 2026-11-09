using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace HIS.Tests;

/// <summary>
/// Chạy các đường đọc của BLL trên CSDL Postgres thật đã align.
/// DB lấy từ biến môi trường HIS_REHEARSAL_CONNECTION (his_rehearsal có nạp backup/seed_rehearsal_day.sql
/// hoặc hospital_his); chưa đặt biến thì bỏ qua để CI không cần DB local.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class RehearsalReadPathsTests
{
    private readonly ITestOutputHelper _out;

    public RehearsalReadPathsTests(ITestOutputHelper out1) => _out = out1;

    private static string? Connection => Environment.GetEnvironmentVariable("HIS_REHEARSAL_CONNECTION");

    private static AppDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }

    [Fact]
    public async Task Quyen_cho_quay_va_web_doc_du_tren_schema_moi()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        using var ctx = NewContext(cs);
        var bookingOptions = Options.Create(new BookingOptions());
        var reception = new ReceptionService(ctx, new QueuePositionAllocator(ctx, bookingOptions, TimeProvider.System), bookingOptions, TimeProvider.System);
        var dashboard = new DashboardService(ctx);
        var patients = new PatientService(ctx);
        var scheduling = new SchedulingService(ctx, bookingOptions);

        var counts = await reception.GetCountsAsync();
        _out.WriteLine($"counts: {counts.Waiting}/{counts.Received}/{counts.Scheduled}/{counts.Cancelled}");

        foreach (var tab in new[] { ReceptionTab.Waiting, ReceptionTab.Received, ReceptionTab.Scheduled, ReceptionTab.Cancelled })
        {
            var rows = await reception.GetReceptionListAsync(tab);
            foreach (var r in rows)
                _out.WriteLine($"{tab}: #{r.QueueNumber} {r.PatientName} | {r.StatusName} | {r.Gender} | {r.BookingChannel} | {r.VisitType}");
        }

        var found = await reception.FindPatientAsync("Nguyễn Văn An");
        Assert.NotNull(found);
        _out.WriteLine($"FindPatientAsync('Nguyễn Văn An'): {found!.FullName} code={found.PatientCode} card={found.InsuranceCardNumber}");

        var byCode = await reception.FindPatientAsync("BN2");
        Assert.NotNull(byCode);
        _out.WriteLine($"FindPatientAsync('BN2'): {byCode!.FullName}");

        var stats = await dashboard.GetDashboardStatsAsync();
        var today = await dashboard.GetTodayAppointmentsAsync();
        _out.WriteLine($"dashboard: total={stats.TodayAppointments} gender-labels-ok, today rows={today.Count}");

        var list = await patients.GetPatientsAsync();
        _out.WriteLine($"patients: {string.Join(", ", list.Select(p => $"{p.FullName}:{p.Gender}"))}");

        var slots = await scheduling.GetSlotsByDepartmentAndDateAsync(1, new DateOnly(2026, 11, 4));
        _out.WriteLine($"slots khoa 1 ngày 04/11: {slots.Count} dòng, ca = {string.Join(",", slots.Select(s => s.StartTime.ToString("HH:mm")).Distinct())}");

        var waiting = await reception.GetReceptionListAsync(ReceptionTab.Waiting);
        var received = await reception.GetReceptionListAsync(ReceptionTab.Received);
        var cancelled = await reception.GetReceptionListAsync(ReceptionTab.Cancelled);
        // his_rehearsal được nạp sẵn 4 dòng HIS-SEED* cho hôm nay (backup/seed_rehearsal_day.sql)
        // nên bắt buộc phải thấy chúng. DB khác chỉ cần các đường đọc chạy không lỗi.
        bool hasSeed = await ctx.Appointments.AsNoTracking()
            .AnyAsync(a => a.AppointmentCode.StartsWith("HIS-SEED-"), default);
        if (hasSeed)
        {
            Assert.Contains(waiting, r => r.AppointmentCode == "HIS-SEED-CONFIRMED");
            Assert.Contains(received, r => r.AppointmentCode == "HIS-SEED-CHECKEDIN");
            Assert.Contains(cancelled, r => r.AppointmentCode == "HIS-SEED-CANCELLED");
            Assert.True(counts.Waiting >= 1 && counts.Received >= 1 && counts.Scheduled >= 1 && counts.Cancelled >= 1);
        }
        else
        {
            _out.WriteLine("DB không có dòng HIS-SEED* hôm nay -> chỉ chạy đường đọc, không so dòng.");
        }

        Assert.All(waiting.Concat(received).Concat(cancelled), r => Assert.False(string.IsNullOrWhiteSpace(r.StatusName)));
        Assert.All(waiting.Concat(received).Concat(cancelled), r => Assert.False(string.IsNullOrWhiteSpace(r.Gender)));
    }
}
