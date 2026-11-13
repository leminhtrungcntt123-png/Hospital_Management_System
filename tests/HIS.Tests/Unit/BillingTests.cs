using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HIS.Tests.Unit;

public class BillingTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=his_db;Username=postgres;Password=123456789";

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, new VietnamTimeProvider());
    }

    [Fact]
    public async Task GetInvoiceDetail_WithValidInNetworkInsurance_CalculatesCoverageCorrectly()
    {
        await using var db = CreateDb();
        var options = Options.Create(new BillingOptions { ExamFeeServiceId = 1, DefaultExamPrice = 150000m });
        var billingService = new BillingService(db, new VietnamTimeProvider(), options);

        // Bệnh nhân Trần Quốc Bảo đã được seed thẻ BHYT 80% đúng tuyến
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Trần Quốc Bảo");
        Assert.NotNull(patient);

        var app = await db.Appointments.FirstOrDefaultAsync(a => a.PatientId == patient.Id);
        if (app != null)
        {
            var result = await billingService.GetInvoiceDetailAsync(app.Id);
            Assert.True(result.IsSuccess);
            var detail = result.Value!;
            Assert.True(detail.HasInsurance);
            Assert.Equal(80m, detail.CoveragePct);
            Assert.True(detail.TotalAmount > 0);
            Assert.True(detail.InsuranceCoveredAmount > 0);
            Assert.Equal(detail.TotalAmount - detail.InsuranceCoveredAmount, detail.PatientAmount);
        }
    }

    [Fact]
    public async Task GetInvoiceDetail_WithNonExistentAppointment_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var options = Options.Create(new BillingOptions { ExamFeeServiceId = 1, DefaultExamPrice = 150000m });
        var billingService = new BillingService(db, new VietnamTimeProvider(), options);

        var result = await billingService.GetInvoiceDetailAsync(999999);
        Assert.False(result.IsSuccess);
        Assert.Equal("NOT_FOUND", result.Error.Code);
    }
}
