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

public class CashierShiftTests
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
    public async Task OpenAndCloseShift_Workflow_WorksAsExpected()
    {
        await using var db = CreateDb();
        var cashier = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "ketoan");
        if (cashier == null)
        {
            var unique = Guid.NewGuid().ToString("N")[..8];
            cashier = new Account
            {
                Username = $"ketoan_{unique}",
                Email = $"shift_{unique}@his.local",
                PasswordHash = "hash",
                RoleId = (int)RoleId.Accountant,
                IsActive = true
            };
            db.Accounts.Add(cashier);
            await db.SaveChangesAsync();
        }

        var shiftService = new CashierShiftService(db, new VietnamTimeProvider());

        // Đóng bất kỳ ca mở cũ nào của user này để test sạch
        var old = await db.CashierShifts.Where(s => s.CashierId == cashier.Id && s.Status == ShiftStatus.Open).ToListAsync();
        foreach (var s in old)
        {
            s.Status = ShiftStatus.Closed;
        }
        await db.SaveChangesAsync();

        // 1. Mở ca
        var openRes = await shiftService.OpenShiftAsync(new OpenShiftRequest(cashier.Id, 1000000m, "Test mở ca"));
        Assert.True(openRes.IsSuccess);
        int shiftId = openRes.Value!.Id;
        Assert.Equal(ShiftStatus.Open, openRes.Value!.Status);
        Assert.Equal(1000000m, openRes.Value!.OpeningCash);

        // 2. Không được mở ca thứ hai khi đang có ca mở
        var dupRes = await shiftService.OpenShiftAsync(new OpenShiftRequest(cashier.Id, 500000m, "Mở trùng"));
        Assert.False(dupRes.IsSuccess);
        Assert.Equal("SHIFT_ALREADY_OPEN", dupRes.Error.Code);

        // 3. Đóng ca
        var closeRes = await shiftService.CloseShiftAsync(new CloseShiftRequest(shiftId, cashier.Id, 1000000m, "Chốt ca"));
        Assert.True(closeRes.IsSuccess);
        Assert.Equal(ShiftStatus.Closed, closeRes.Value!.Status);
        Assert.Equal(1000000m, closeRes.Value!.ExpectedCash);
        Assert.Equal(1000000m, closeRes.Value!.CountedCash);
        Assert.Equal(0m, closeRes.Value!.Difference);
    }

    [Fact]
    public async Task CheckoutOrDeposit_WithoutOpenShift_ReturnsShiftRequiredError()
    {
        await using var db = CreateDb();
        var options = Options.Create(new BillingOptions
        {
            ExamFeeServiceId = 1,
            DefaultExamPrice = 150000m,
            BankCode = "ICB",
            AccountNumber = "119002953584",
            AccountName = "BENH VIEN MEDICARE"
        });
        var billingService = new BillingService(db, new VietnamTimeProvider(), options);

        // Tạo 1 cashier riêng biệt và đảm bảo không có ca mở nào
        var unique = Guid.NewGuid().ToString("N")[..8];
        var cashier = new Account
        {
            Username = $"cashier_no_shift_{unique}",
            Email = $"noshift_{unique}@his.local",
            PasswordHash = "hash",
            RoleId = (int)RoleId.Accountant,
            IsActive = true
        };
        db.Accounts.Add(cashier);
        await db.SaveChangesAsync();

        var patient = await db.Patients.FirstOrDefaultAsync();
        Assert.NotNull(patient);

        // 1. Thử thu tạm ứng khi chưa mở ca -> phải trả về SHIFT_REQUIRED
        var depRes = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id,
            null,
            500000m,
            PaymentMethod.Cash,
            "Tạm ứng thử",
            null,
            null,
            cashier.Id));

        Assert.False(depRes.IsSuccess);
        Assert.Equal("SHIFT_REQUIRED", depRes.Error.Code);

        // 2. Thử thanh toán viện phí khi chưa mở ca -> phải trả về SHIFT_REQUIRED
        var app = await db.Appointments.FirstOrDefaultAsync();
        if (app != null)
        {
            var checkoutRes = await billingService.CheckoutInvoiceAsync(new CheckoutInvoiceRequest(
                app.Id,
                PaymentMethod.Cash,
                null,
                "Thanh toán test",
                cashier.Id));

            Assert.False(checkoutRes.IsSuccess);
            Assert.Equal("SHIFT_REQUIRED", checkoutRes.Error.Code);
        }
    }
}
