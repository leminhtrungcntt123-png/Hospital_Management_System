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

public class DepositRefundTests
{
    private static AppDbContext CreateDb() => CashierTestDatabase.CreateDbAsync().GetAwaiter().GetResult();

    private static IBillingService CreateBillingService(AppDbContext db)
    {
        var options = Options.Create(new BillingOptions
        {
            ExamFeeServiceId = 1,
            DefaultExamPrice = 150000m,
            BankCode = "ICB",
            AccountNumber = "119002953584",
            AccountName = "BENH VIEN MEDICARE"
        });

        return new BillingService(db, new VietnamTimeProvider(), options);
    }

    private static async Task EnsureOpenShiftAsync(AppDbContext db, int cashierId)
    {
        var hasShift = await db.CashierShifts.AnyAsync(s => s.CashierId == cashierId && s.Status == ShiftStatus.Open);
        if (!hasShift)
        {
            db.CashierShifts.Add(new CashierShift
            {
                CashierId = cashierId,
                OpenedAt = DateTime.Now,
                OpeningCash = 1000000m,
                Status = ShiftStatus.Open
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Patient> CreateIsolatedPatientAsync(AppDbContext db)
    {
        var p = new Patient
        {
            FullName = $"BN Test {Guid.NewGuid().ToString("N")[..6]}",
            DateOfBirth = new DateOnly(1990, 5, 20),
            Gender = Gender.Male,
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
            NationalId = $"079{Random.Shared.Next(100000000, 999999999)}",
            Address = "TP.HCM"
        };
        db.Patients.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public void DepositDto_CalculatesAvailableBalanceCorrectly()
    {
        var d = new DepositDto(
            1, "TU-00000001", 1, "00000001", "Lê Minh Chí", "0890201234",
            null, null, 2000000m, 500000m, 300000m, 1200000m,
            DepositStatus.Active, PaymentMethod.Cash, null, "Tạm ứng", null, "Thu ngân", DateTime.Now, "079085001234");

        Assert.Equal(1200000m, d.Balance);
        Assert.Equal(2000000m, d.Amount);
        Assert.Equal(500000m, d.UsedAmount);
        Assert.Equal(300000m, d.RefundedAmount);
    }

    [Fact]
    public async Task CreateDeposit_ValidRequest_CreatesDepositAndPaymentRecord()
    {
        await using var db = CreateDb();
        await EnsureOpenShiftAsync(db, 1);
        var billingService = CreateBillingService(db);

        var patient = await CreateIsolatedPatientAsync(db);
        Assert.NotNull(patient);

        var req = new CreateDepositRequest(
            patient.Id,
            null,
            2000000m,
            PaymentMethod.Cash,
            "Tạm ứng nhập viện Ngoại khoa",
            null,
            "Thu tiền mặt đầu đợt",
            1);

        var res = await billingService.CreateDepositAsync(req);
        Assert.True(res.IsSuccess);

        var dep = res.Value!;
        Assert.StartsWith("TU-", dep.DepositCode);
        Assert.Equal(2000000m, dep.Amount);
        Assert.Equal(2000000m, dep.Balance);
        Assert.Equal(DepositStatus.Active, dep.Status);

        // Kiểm tra bút toán trong sổ thu ngân
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.DepositId == dep.Id && p.PaymentType == PaymentType.Deposit);
        Assert.NotNull(payment);
        Assert.Equal(2000000m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
    }

    [Fact]
    public async Task RefundDeposit_ExceedsBalance_ReturnsError()
    {
        await using var db = CreateDb();
        await EnsureOpenShiftAsync(db, 1);
        var billingService = CreateBillingService(db);

        var patient = await CreateIsolatedPatientAsync(db);
        Assert.NotNull(patient);

        var createRes = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id,
            null,
            500000m,
            PaymentMethod.Cash,
            "Tạm ứng xét nghiệm",
            null,
            null,
            1));

        Assert.True(createRes.IsSuccess);
        var dep = createRes.Value!;

        // Yêu cầu hoàn 600.000đ trong khi chỉ có 500.000đ
        var refundReq = new RefundDepositRequest(
            dep.Id,
            600000m,
            "Bệnh nhân không làm xét nghiệm",
            PaymentMethod.Cash,
            null,
            1);

        var refundRes = await billingService.RefundDepositAsync(refundReq);
        Assert.False(refundRes.IsSuccess);
        Assert.Equal("AMOUNT_EXCEEDED", refundRes.Error.Code);
    }

    [Fact]
    public async Task RefundDeposit_ValidAmount_UpdatesBalanceAndCreatesRefundPayment()
    {
        await using var db = CreateDb();
        await EnsureOpenShiftAsync(db, 1);
        var billingService = CreateBillingService(db);

        var patient = await CreateIsolatedPatientAsync(db);
        Assert.NotNull(patient);

        var createRes = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id,
            null,
            1000000m,
            PaymentMethod.Cash,
            "Tạm ứng khám chuyên sâu",
            null,
            null,
            1));

        Assert.True(createRes.IsSuccess);
        var dep = createRes.Value!;

        // Hoàn một phần 400.000đ
        var refundReq = new RefundDepositRequest(
            dep.Id,
            400000m,
            "Bệnh nhân yêu cầu hoàn lại tiền thừa",
            PaymentMethod.Cash,
            null,
            1);

        var refundRes = await billingService.RefundDepositAsync(refundReq);
        Assert.True(refundRes.IsSuccess);

        var updatedDep = await db.Deposits.FindAsync(dep.Id);
        Assert.NotNull(updatedDep);
        Assert.Equal(400000m, updatedDep.RefundedAmount);
        Assert.Equal(600000m, updatedDep.Balance);
        Assert.Equal(DepositStatus.Active, updatedDep.Status);

        // Hoàn nốt 600.000đ -> Status chuyển thành Refunded
        var refundAllReq = new RefundDepositRequest(
            dep.Id,
            600000m,
            "Quyết toán toàn bộ xuất viện",
            PaymentMethod.Cash,
            null,
            1);

        var refundAllRes = await billingService.RefundDepositAsync(refundAllReq);
        Assert.True(refundAllRes.IsSuccess);

        var finalDep = await db.Deposits.FindAsync(dep.Id);
        Assert.NotNull(finalDep);
        Assert.Equal(1000000m, finalDep.RefundedAmount);
        Assert.Equal(0m, finalDep.Balance);
        Assert.Equal(DepositStatus.Refunded, finalDep.Status);
    }

    [Fact]
    public async Task RefundInvoice_ExceedsPatientAmount_ReturnsError()
    {
        await using var db = CreateDb();
        await EnsureOpenShiftAsync(db, 1);
        var billingService = CreateBillingService(db);

        // Tìm một hóa đơn đã thanh toán
        var invoice = await db.Invoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Status == InvoiceStatus.Paid && i.PatientAmount > 0);

        if (invoice != null)
        {
            var req = new RefundInvoiceRequest(
                invoice.Id,
                invoice.PatientAmount + 500000m,
                "Hoàn tiền quá mức",
                PaymentMethod.Cash,
                null,
                1);

            var res = await billingService.RefundInvoiceAsync(req);
            Assert.False(res.IsSuccess);
            Assert.Equal("AMOUNT_EXCEEDED", res.Error.Code);
        }
    }

    [Fact]
    public async Task RefundInvoice_ValidAmount_CreatesRefundPayment()
    {
        await using var db = CreateDb();
        await EnsureOpenShiftAsync(db, 1);
        var billingService = CreateBillingService(db);

        var invoice = await db.Invoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Status == InvoiceStatus.Paid && i.PatientAmount >= 50000m);

        if (invoice != null)
        {
            decimal refundAmount = 10000m;
            var req = new RefundInvoiceRequest(
                invoice.Id,
                refundAmount,
                "Hủy dịch vụ xét nghiệm theo yêu cầu bác sĩ",
                PaymentMethod.Cash,
                null,
                1);

            var res = await billingService.RefundInvoiceAsync(req);
            Assert.True(res.IsSuccess);

            var refundPayment = await db.Payments
                .FirstOrDefaultAsync(p => p.InvoiceId == invoice.Id && p.PaymentType == PaymentType.Refund && p.Amount == refundAmount);
            Assert.NotNull(refundPayment);
            Assert.Equal("Hủy dịch vụ xét nghiệm theo yêu cầu bác sĩ", refundPayment.RefundReason);
        }
    }

    [Fact]
    public async Task RefundAllPatientDeposits_MultipleSlips_SettlesAllInOneGo()
    {
        await using var db = CreateDb();
        await EnsureOpenShiftAsync(db, 1);
        var billingService = CreateBillingService(db);

        // Tạo 1 bệnh nhân test riêng
        var unique = Guid.NewGuid().ToString("N")[..8];
        var patient = new Patient
        {
            FullName = $"BN Test Tất Toán {unique}",
            DateOfBirth = new DateOnly(1995, 5, 20),
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
            NationalId = $"079{Random.Shared.Next(100000000, 999999999)}"
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        // 1. Nộp tạm ứng lần 1 (Tiền mặt 300.000đ)
        var dep1 = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id, null, 300000m, PaymentMethod.Cash, "Tạm ứng khám ban đầu", null, null, 1));
        Assert.True(dep1.IsSuccess);

        // 2. Nộp tạm ứng lần 2 (Chuyển khoản 700.000đ)
        var dep2 = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id, null, 700000m, PaymentMethod.BankTransfer, "Tạm ứng phẫu thuật", "REF_INIT_001", null, 1));
        Assert.True(dep2.IsSuccess);

        // 3. Tra cứu nhanh số dư bệnh nhân
        var lookupRes = await billingService.QuickLookupPatientDepositsAsync(patient.NationalId!);
        Assert.True(lookupRes.IsSuccess);
        var lookup = lookupRes.Value!;
        Assert.Equal(patient.Id, lookup.PatientId);
        Assert.Equal(2, lookup.ActiveSlipsCount);
        Assert.Equal(1000000m, lookup.AvailableBalance);
        Assert.Equal(300000m, lookup.CashBalance);
        Assert.Equal(700000m, lookup.BankTransferBalance);

        // 4. Thực hiện Tất toán 1-Click
        var refundAllReq = new RefundAllPatientDepositsRequest(
            patient.Id,
            "Tất toán xuất viện và hoàn tiền toàn bộ",
            1,
            "REF_REFUND_999");

        var batchRes = await billingService.RefundAllPatientDepositsAsync(refundAllReq);
        Assert.True(batchRes.IsSuccess);
        var resDto = batchRes.Value!;
        Assert.Equal(1000000m, resDto.TotalRefunded);
        Assert.Equal(2, resDto.SlipsRefundedCount);
        Assert.Equal(300000m, resDto.CashRefunded);
        Assert.Equal(700000m, resDto.BankTransferRefunded);
        Assert.Equal(2, resDto.TransactionCodes.Count);

        // 5. Kiểm tra trạng thái trong DB: cả 2 phiếu phải chuyển sang Refunded và balance = 0
        var updatedDeposits = await db.Deposits.Where(d => d.PatientId == patient.Id).ToListAsync();
        Assert.All(updatedDeposits, d =>
        {
            Assert.Equal(DepositStatus.Refunded, d.Status);
            Assert.Equal(0m, d.Amount - d.UsedAmount - d.RefundedAmount);
        });
    }

    [Fact]
    public async Task SearchPatientsAsync_ByCodeOrCCCDOrName_ReturnsPatientWithDetails()
    {
        await using var db = CreateDb();
        var billingService = CreateBillingService(db);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var patient = new Patient
        {
            FullName = $"Nguyễn Văn Tra Cứu {unique}",
            DateOfBirth = new DateOnly(1988, 3, 15),
            Gender = Gender.Male,
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
            NationalId = $"079{Random.Shared.Next(100000000, 999999999)}",
            Address = "123 Quận 1 TP.HCM"
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        // 1. Tìm theo CCCD
        var resCccd = await billingService.SearchPatientsAsync(patient.NationalId);
        Assert.Single(resCccd);
        Assert.Equal(patient.Id, resCccd[0].Id);
        Assert.Equal(patient.FullName, resCccd[0].FullName);
        Assert.Equal(patient.Id.ToString("D8"), resCccd[0].PatientCode);
        Assert.Equal("Nam", resCccd[0].Gender);

        // 2. Tìm theo Mã BN (định dạng 8 số)
        var resCode = await billingService.SearchPatientsAsync(patient.Id.ToString("D8"));
        Assert.Single(resCode);
        Assert.Equal(patient.Id, resCode[0].Id);

        // 3. Tìm theo tên
        var resName = await billingService.SearchPatientsAsync(unique);
        Assert.Contains(resName, p => p.Id == patient.Id);
    }

    [Fact]
    public async Task SettleDischargeAsync_WhenPatientHasExcessDeposit_AutoDeductsFeeAndRefundsSurplus()
    {
        await using var db = CreateDb();
        var billingService = CreateBillingService(db);
        await EnsureOpenShiftAsync(db, 1);

        var patient = await CreateIsolatedPatientAsync(db);
        var doctor = await db.Doctors.FirstAsync();
        var dept = await db.Departments.FirstAsync();
        var slot = await db.AppointmentSlots.FirstOrDefaultAsync();

        var app = new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            DepartmentId = dept.Id,
            SlotId = slot?.Id,
            AppointmentCode = $"AP-{Guid.NewGuid().ToString("N")[..6]}",
            StatusId = (int)AppointmentStatusId.CheckedIn,
            CreatedAt = DateTime.Now
        };
        db.Appointments.Add(app);

        var dep = new Deposit
        {
            PatientId = patient.Id,
            DepositCode = $"TU-{Guid.NewGuid().ToString("N")[..6]}",
            Amount = 1000000m,
            UsedAmount = 0m,
            RefundedAmount = 0m,
            Status = DepositStatus.Active,
            PaymentMethod = PaymentMethod.BankTransfer,
            Purpose = "Tạm ứng khám",
            CreatedAt = DateTime.Now
        };
        db.Deposits.Add(dep);
        await db.SaveChangesAsync();

        var req = new SettleDischargeRequest(
            app.Id,
            PaymentMethod.BankTransfer,
            "REF_BANK_123456",
            "Tất toán xuất viện và hoàn tiền",
            1);

        var result = await billingService.SettleDischargeAsync(req);
        Assert.True(result.IsSuccess, result.Error?.Message);

        var res = result.Value!;
        Assert.Equal(150000m, res.TotalInvoiceAmount);
        Assert.Equal(150000m, res.DepositDeducted);
        Assert.Equal(0m, res.AdditionalPaid);
        Assert.Equal(850000m, res.RefundedToPatient);

        var updatedDep = await db.Deposits.FindAsync(dep.Id);
        Assert.NotNull(updatedDep);
        Assert.Equal(DepositStatus.Refunded, updatedDep.Status);
        Assert.Equal(150000m, updatedDep.UsedAmount);
        Assert.Equal(850000m, updatedDep.RefundedAmount);
    }

    [Fact]
    public async Task SettleDischargeAsync_WhenPatientOwesExtra_AutoDeductsDepositAndCollectsRemainder()
    {
        await using var db = CreateDb();
        var billingService = CreateBillingService(db);
        await EnsureOpenShiftAsync(db, 1);

        var patient = await CreateIsolatedPatientAsync(db);
        var doctor = await db.Doctors.FirstAsync();
        var dept = await db.Departments.FirstAsync();
        var slot = await db.AppointmentSlots.FirstOrDefaultAsync();

        var app = new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            DepartmentId = dept.Id,
            SlotId = slot?.Id,
            AppointmentCode = $"AP-{Guid.NewGuid().ToString("N")[..6]}",
            StatusId = (int)AppointmentStatusId.CheckedIn,
            CreatedAt = DateTime.Now
        };
        db.Appointments.Add(app);

        // Tạm ứng 50.000 đ < Viện phí 150.000 đ
        var dep = new Deposit
        {
            PatientId = patient.Id,
            DepositCode = $"TU-{Guid.NewGuid().ToString("N")[..6]}",
            Amount = 50000m,
            UsedAmount = 0m,
            RefundedAmount = 0m,
            Status = DepositStatus.Active,
            PaymentMethod = PaymentMethod.Cash,
            Purpose = "Tạm ứng ít",
            CreatedAt = DateTime.Now
        };
        db.Deposits.Add(dep);
        await db.SaveChangesAsync();

        var req = new SettleDischargeRequest(
            app.Id,
            PaymentMethod.Cash,
            null,
            "Thu thêm tiền mặt khi quyết toán",
            1);

        var result = await billingService.SettleDischargeAsync(req);
        Assert.True(result.IsSuccess, result.Error?.Message);

        var res = result.Value!;
        Assert.Equal(150000m, res.TotalInvoiceAmount);
        Assert.Equal(50000m, res.DepositDeducted);
        Assert.Equal(100000m, res.AdditionalPaid);
        Assert.Equal(0m, res.RefundedToPatient);

        var updatedDep = await db.Deposits.FindAsync(dep.Id);
        Assert.NotNull(updatedDep);
        Assert.Equal(DepositStatus.Depleted, updatedDep.Status);
        Assert.Equal(50000m, updatedDep.UsedAmount);
        Assert.Equal(0m, updatedDep.RefundedAmount);
    }
}
