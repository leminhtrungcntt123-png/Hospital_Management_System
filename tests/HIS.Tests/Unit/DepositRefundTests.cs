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

[Trait("Category", "Integration")]
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

    /// <summary>
    /// Tạo tài khoản thu ngân riêng cho từng test và mở ca cho tài khoản đó,
    /// để test không đụng tới ca đang mở của tài khoản thật (vd. ketoan) trong DB dùng chung.
    /// </summary>
    private static async Task<int> CreateCashierWithOpenShiftAsync(AppDbContext db)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var cashier = new Account
        {
            Username = $"test_cashier_{unique}",
            Email = $"test_cashier_{unique}@his.local",
            PasswordHash = "hash",
            RoleId = (int)RoleId.Nurse,
            IsActive = true
        };
        db.Accounts.Add(cashier);
        await db.SaveChangesAsync();

        var shiftService = new CashierShiftService(db, new VietnamTimeProvider());
        var openRes = await shiftService.OpenShiftAsync(new OpenShiftRequest(cashier.Id, 1000000m, "Test shift"));
        Assert.True(openRes.IsSuccess, openRes.Error?.Message);
        return cashier.Id;
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
        var cashierId = await CreateCashierWithOpenShiftAsync(db);
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
            cashierId);

        var res = await billingService.CreateDepositAsync(req);
        Assert.True(res.IsSuccess);

        var dep = res.Value!;
        Assert.StartsWith("TU-", dep.DepositCode);
        Assert.Equal(2000000m, dep.Amount);
        Assert.Equal(2000000m, dep.Balance);
        Assert.Equal(DepositStatus.Active, dep.Status);

        // Kiểm tra bút toán trong sổ thu ngân
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == dep.Id && p.PaymentType == PaymentType.Deposit);
        Assert.NotNull(payment);
        Assert.Equal(2000000m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
    }

    [Fact]
    public async Task RefundDeposit_ExceedsBalance_ReturnsError()
    {
        await using var db = CreateDb();
        var cashierId = await CreateCashierWithOpenShiftAsync(db);
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
            cashierId));

        Assert.True(createRes.IsSuccess);
        var dep = createRes.Value!;

        // Yêu cầu hoàn 600.000đ trong khi chỉ có 500.000đ
        var refundReq = new RefundDepositRequest(
            dep.Id,
            600000m,
            "Bệnh nhân không làm xét nghiệm",
            PaymentMethod.Cash,
            null,
            cashierId);

        var refundRes = await billingService.RefundDepositAsync(refundReq);
        Assert.False(refundRes.IsSuccess);
        Assert.Equal("AMOUNT_EXCEEDED", refundRes.Error.Code);
    }

    [Fact]
    public async Task RefundDeposit_ValidAmount_UpdatesBalanceAndCreatesRefundPayment()
    {
        await using var db = CreateDb();
        var cashierId = await CreateCashierWithOpenShiftAsync(db);
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
            cashierId));

        Assert.True(createRes.IsSuccess);
        var dep = createRes.Value!;

        // Hoàn một phần 400.000đ
        var refundReq = new RefundDepositRequest(
            dep.Id,
            400000m,
            "Bệnh nhân yêu cầu hoàn lại tiền thừa",
            PaymentMethod.Cash,
            null,
            cashierId);

        var refundRes = await billingService.RefundDepositAsync(refundReq);
        Assert.True(refundRes.IsSuccess);

        var summaryAfterPartial = await billingService.GetPatientDepositSummaryAsync(patient.Id);
        var updatedDep = summaryAfterPartial.ActiveDeposits.FirstOrDefault(d => d.Id == dep.Id);
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
            cashierId);

        var refundAllRes = await billingService.RefundDepositAsync(refundAllReq);
        Assert.True(refundAllRes.IsSuccess);

        var summaryAfterAll = await billingService.GetPatientDepositSummaryAsync(patient.Id);
        var finalDep = summaryAfterAll.ActiveDeposits.FirstOrDefault(d => d.Id == dep.Id);
        Assert.NotNull(finalDep);
        Assert.Equal(1000000m, finalDep.RefundedAmount);
        Assert.Equal(0m, finalDep.Balance);
        Assert.Equal(DepositStatus.Refunded, finalDep.Status);
    }

    private static async Task<Invoice> CreatePaidInvoiceAsync(AppDbContext db, decimal patientAmount = 150000m)
    {
        var patient = await CreateIsolatedPatientAsync(db);
        var doctor = await db.Doctors.FirstAsync();
        var dept = await db.Departments.FirstAsync();
        var slot = await db.AppointmentSlots.FirstOrDefaultAsync();
        var unique = Guid.NewGuid().ToString("N")[..6];

        var app = new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            DepartmentId = dept.Id,
            SlotId = slot?.Id,
            AppointmentCode = $"AP-{unique}",
            StatusId = (int)AppointmentStatusId.CheckedIn,
            CreatedAt = DateTime.Now
        };
        db.Appointments.Add(app);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            AppointmentId = app.Id,
            PatientId = patient.Id,
            TotalAmount = patientAmount,
            InsuranceCoveredAmount = 0m,
            PatientAmount = patientAmount,
            Status = InvoiceStatus.Paid,
            CreatedAt = DateTime.Now
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.Id,
            Amount = patientAmount,
            PaymentType = PaymentType.Payment,
            PaymentMethod = PaymentMethod.Cash,
            CashierId = null,
            Note = "Thanh toán hóa đơn test",
            PaidAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task RefundInvoice_ExceedsPatientAmount_ReturnsError()
    {
        await using var db = CreateDb();
        var cashierId = await CreateCashierWithOpenShiftAsync(db);
        var billingService = CreateBillingService(db);

        var invoice = await CreatePaidInvoiceAsync(db);
        Assert.NotNull(invoice);

        var req = new RefundInvoiceRequest(
            invoice.Id,
            invoice.PatientAmount + 500000m,
            "Hoàn tiền quá mức",
            PaymentMethod.Cash,
            null,
            cashierId);

        var res = await billingService.RefundInvoiceAsync(req);
        Assert.False(res.IsSuccess);
        Assert.Equal("AMOUNT_EXCEEDED", res.Error.Code);
    }

    [Fact]
    public async Task RefundInvoice_ValidAmount_CreatesRefundPayment()
    {
        await using var db = CreateDb();
        var cashierId = await CreateCashierWithOpenShiftAsync(db);
        var billingService = CreateBillingService(db);

        var invoice = await CreatePaidInvoiceAsync(db);
        Assert.NotNull(invoice);

        decimal refundAmount = 10000m;
        var req = new RefundInvoiceRequest(
            invoice.Id,
            refundAmount,
            "Hủy dịch vụ xét nghiệm theo yêu cầu bác sĩ",
            PaymentMethod.Cash,
            null,
            cashierId);

        var res = await billingService.RefundInvoiceAsync(req);
        Assert.True(res.IsSuccess);

        var refundPayment = await db.Payments
            .FirstOrDefaultAsync(p => p.InvoiceId == invoice.Id && p.PaymentType == PaymentType.Refund && p.Amount == refundAmount);
        Assert.NotNull(refundPayment);
        Assert.Contains("Hủy dịch vụ xét nghiệm theo yêu cầu bác sĩ", refundPayment.Note);
    }

    [Fact]
    public async Task RefundAllPatientDeposits_MultipleSlips_SettlesAllInOneGo()
    {
        await using var db = CreateDb();
        var cashierId = await CreateCashierWithOpenShiftAsync(db);
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
            patient.Id, null, 300000m, PaymentMethod.Cash, "Tạm ứng khám ban đầu", null, null, cashierId));
        Assert.True(dep1.IsSuccess);

        // 2. Nộp tạm ứng lần 2 (Chuyển khoản 700.000đ)
        var dep2 = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id, null, 700000m, PaymentMethod.BankTransfer, "Tạm ứng phẫu thuật", "REF_INIT_001", null, cashierId));
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
            cashierId,
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
        var updatedSummary = await billingService.GetPatientDepositSummaryAsync(patient.Id);
        Assert.Equal(2, updatedSummary.ActiveDeposits.Count);
        Assert.All(updatedSummary.ActiveDeposits, d =>
        {
            Assert.Equal(DepositStatus.Refunded, d.Status);
            Assert.Equal(0m, d.Balance);
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
        var cashierId = await CreateCashierWithOpenShiftAsync(db);

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
        await db.SaveChangesAsync();

        var depRes = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id, null, 1000000m, PaymentMethod.BankTransfer, "Tạm ứng khám", "REF_INIT_SET", null, cashierId));
        Assert.True(depRes.IsSuccess, depRes.Error?.Message);
        var dep = depRes.Value!;

        var req = new SettleDischargeRequest(
            app.Id,
            PaymentMethod.BankTransfer,
            "REF_BANK_123456",
            "Tất toán xuất viện và hoàn tiền",
            cashierId);

        var result = await billingService.SettleDischargeAsync(req);
        Assert.True(result.IsSuccess, result.Error?.Message);

        var res = result.Value!;
        Assert.Equal(150000m, res.TotalInvoiceAmount);
        Assert.Equal(150000m, res.DepositDeducted);
        Assert.Equal(0m, res.AdditionalPaid);
        Assert.Equal(850000m, res.RefundedToPatient);

        var updatedSummary = await billingService.GetPatientDepositSummaryAsync(patient.Id);
        var updatedDep = updatedSummary.ActiveDeposits.FirstOrDefault(d => d.Id == dep.Id);
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
        var cashierId = await CreateCashierWithOpenShiftAsync(db);

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
        await db.SaveChangesAsync();

        // Tạm ứng 50.000 đ < Viện phí 150.000 đ
        var depRes = await billingService.CreateDepositAsync(new CreateDepositRequest(
            patient.Id, null, 50000m, PaymentMethod.Cash, "Tạm ứng ít", null, null, cashierId));
        Assert.True(depRes.IsSuccess, depRes.Error?.Message);
        var dep = depRes.Value!;

        var req = new SettleDischargeRequest(
            app.Id,
            PaymentMethod.Cash,
            null,
            "Thu thêm tiền mặt khi quyết toán",
            cashierId);

        var result = await billingService.SettleDischargeAsync(req);
        Assert.True(result.IsSuccess, result.Error?.Message);

        var res = result.Value!;
        Assert.Equal(150000m, res.TotalInvoiceAmount);
        Assert.Equal(50000m, res.DepositDeducted);
        Assert.Equal(100000m, res.AdditionalPaid);
        Assert.Equal(0m, res.RefundedToPatient);

        var updatedSummary = await billingService.GetPatientDepositSummaryAsync(patient.Id);
        var updatedDep = updatedSummary.ActiveDeposits.FirstOrDefault(d => d.Id == dep.Id);
        Assert.NotNull(updatedDep);
        Assert.Equal(DepositStatus.Depleted, updatedDep.Status);
        Assert.Equal(50000m, updatedDep.UsedAmount);
        Assert.Equal(0m, updatedDep.RefundedAmount);
    }
}
