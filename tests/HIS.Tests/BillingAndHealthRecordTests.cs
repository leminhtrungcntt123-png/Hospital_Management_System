using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HIS.Tests;

public class BillingAndHealthRecordTests
{
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utc;

        public MutableTimeProvider(DateTime start)
        {
            _utc = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc));
        }

        public override DateTimeOffset GetUtcNow() => _utc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static (
        AppDbContext Context,
        MutableTimeProvider Clock,
        BillingService Billing,
        HealthRecordService HealthRecord,
        int PatientId,
        int PatientAccountId,
        int OtherAccountId,
        int DoctorAccountId,
        int CashierAccountId,
        int AppointmentId
    ) CreateFixture()
    {
        var clock = new MutableTimeProvider(new DateTime(2026, 11, 9, 8, 30, 0, DateTimeKind.Utc));
        var dbName = "BillingAndHealthRecordTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new AppDbContext(options, clock);

        // 1. Roles
        context.Roles.AddRange(
            new Role { Id = 1, Code = "ADMIN", Name = "Quản trị viên" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "NURSE", Name = "Điều dưỡng" },
            new Role { Id = 4, Code = "DEPT_HEAD", Name = "Trưởng khoa" },
            new Role { Id = 5, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 6, Code = "CASHIER", Name = "Thu ngân" }
        );

        // 2. Appointment Statuses
        context.AppointmentStatuses.AddRange(
            new AppointmentStatus { Id = 1, Code = "PENDING", Name = "Chờ duyệt", SortOrder = 1, IsTerminal = false },
            new AppointmentStatus { Id = 2, Code = "CONFIRMED", Name = "Đã xác nhận", SortOrder = 2, IsTerminal = false },
            new AppointmentStatus { Id = 3, Code = "CHECKED_IN", Name = "Đã tiếp nhận", SortOrder = 3, IsTerminal = false },
            new AppointmentStatus { Id = 4, Code = "READY_FOR_DOCTOR", Name = "Chờ khám", SortOrder = 4, IsTerminal = false },
            new AppointmentStatus { Id = 5, Code = "IN_PROGRESS", Name = "Đang khám", SortOrder = 5, IsTerminal = false },
            new AppointmentStatus { Id = 6, Code = "COMPLETED", Name = "Đã khám xong", SortOrder = 6, IsTerminal = true }
        );

        // 3. Department & Doctor & Accounts
        var dept = new Department { Id = 1, Name = "Khoa Khám bệnh" };
        context.Departments.Add(dept);

        var patientAcc = new Account { Id = 10, Username = "patient1", PasswordHash = "hash", RoleId = 5, IsActive = true };
        var otherAcc = new Account { Id = 11, Username = "patient2", PasswordHash = "hash", RoleId = 5, IsActive = true };
        var doctorAcc = new Account { Id = 20, Username = "doctor1", PasswordHash = "hash", RoleId = 2, IsActive = true };
        var cashierAcc = new Account { Id = 30, Username = "cashier1", PasswordHash = "hash", RoleId = 6, IsActive = true };
        context.Accounts.AddRange(patientAcc, otherAcc, doctorAcc, cashierAcc);

        var doctor = new Doctor { Id = 1, AccountId = doctorAcc.Id, DepartmentId = dept.Id, FullName = "BS. Lê Văn C", Specialty = "Nội tổng quát" };
        context.Doctors.Add(doctor);

        // 4. Patient
        var patient = new Patient
        {
            Id = 1,
            FullName = "Nguyễn Văn Anh",
            DateOfBirth = new DateOnly(2025, 1, 1), // 1 year old
            Gender = Gender.Male,
            PhoneNumber = "0987654321",
            Address = "Hà Nội"
        };
        context.Patients.Add(patient);

        var profileLink = new AccountPatientProfile
        {
            AccountId = patientAcc.Id,
            PatientId = patient.Id,
            Relationship = PatientRelationship.Self,
            Status = LinkStatus.Active,
            CanViewRecords = true
        };
        context.AccountPatientProfiles.Add(profileLink);

        // 5. Insurance Plan & Card (80% coverage)
        var plan = new InsurancePlan
        {
            Id = 1,
            Name = "BHYT Học sinh - Sinh viên",
            CoveragePct = 80.0m
        };
        context.InsurancePlans.Add(plan);

        var card = new InsuranceCard
        {
            Id = 1,
            PatientId = patient.Id,
            InsurancePlanId = plan.Id,
            CardNumber = "DN4010123456789",
            RegisteredFacility = "Bệnh viện Đa khoa MediCare",
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            IsActive = true
        };
        context.InsuranceCards.Add(card);

        // 6. Clinical Services (Matching the 4 services in the user's photo)
        var sTypeExam = new ServiceType { Id = 1, Code = "EXAM", Name = "Khám bệnh" };
        var sTypeImaging = new ServiceType { Id = 2, Code = "IMAGING", Name = "Chẩn đoán hình ảnh" };
        var sTypeLab = new ServiceType { Id = 3, Code = "LAB_TEST", Name = "Xét nghiệm" };
        var sTypeProcedure = new ServiceType { Id = 4, Code = "PROCEDURE", Name = "Thủ thuật" };
        context.ServiceTypes.AddRange(sTypeExam, sTypeImaging, sTypeLab, sTypeProcedure);

        var svcExam = new Service { Id = 1, ServiceTypeId = 1, Name = "Khám chuyên khoa", Price = 150000m, IsActive = true };
        var svcUltrasound = new Service { Id = 2, ServiceTypeId = 2, Name = "Siêu âm tuyến giáp", Price = 120000m, IsActive = true };
        var svcCytology = new Service { Id = 3, ServiceTypeId = 4, Name = "Tế bào học dịch các tổn thương dạng nang", Price = 165000m, IsActive = true };
        var svcTSH = new Service { Id = 4, ServiceTypeId = 3, Name = "Định lượng TSH (Thyroid Stimulating hormone) [Máu]", Price = 87000m, IsActive = true };
        var svcFT4 = new Service { Id = 5, ServiceTypeId = 3, Name = "Định lượng FT4 (Free Thyroxine) [Máu]", Price = 124000m, IsActive = true };
        context.Services.AddRange(svcExam, svcUltrasound, svcCytology, svcTSH, svcFT4);

        // 7. Medication
        var med = new Medication
        {
            Id = 1,
            Name = "Levothyrox 50mcg",
            ActiveIngredient = "Levothyroxine natri",
            Unit = "Viên",
            Price = 3500m,
            IsActive = true
        };
        context.Medications.Add(med);

        // 8. Appointment & Medical Record
        var app = new Appointment
        {
            Id = 100,
            AppointmentCode = "LKB2026110901",
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            DepartmentId = dept.Id,
            StatusId = (int)AppointmentStatusId.Completed,
            CreatedAt = new DateTime(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc),
            BookedBy = patientAcc.Id
        };
        context.Appointments.Add(app);

        var mr = new MedicalRecord
        {
            Id = 1,
            AppointmentId = app.Id,
            DoctorId = doctor.Id,
            Symptoms = "Cổ to, khó nuốt nhẹ",
            ClinicalExamination = "Tuyến giáp to độ I, mềm, không đau",
            PrimaryDiagnosisCode = "E04.1",
            PrimaryDiagnosisDesc = "Bướu nhân tuyến giáp đơn độc",
            Conclusion = "Dùng thuốc theo đơn, tái khám sau 3 tháng",
            CreatedAt = new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc)
        };
        context.MedicalRecords.Add(mr);

        // Lab orders & imaging
        var lo1 = new LabOrder { Id = 1, MedicalRecordId = mr.Id, ServiceId = svcUltrasound.Id, Status = LabOrderStatus.Completed };
        var lo2 = new LabOrder { Id = 2, MedicalRecordId = mr.Id, ServiceId = svcCytology.Id, Status = LabOrderStatus.Completed };
        var lo3 = new LabOrder { Id = 3, MedicalRecordId = mr.Id, ServiceId = svcTSH.Id, Status = LabOrderStatus.Completed };
        var lo4 = new LabOrder { Id = 4, MedicalRecordId = mr.Id, ServiceId = svcFT4.Id, Status = LabOrderStatus.Completed };
        context.LabOrders.AddRange(lo1, lo2, lo3, lo4);

        var r3 = new LabResult { Id = 1, OrderId = lo3.Id, Result = "1.85 uIU/mL (Bình thường: 0.27 - 4.2)" };
        var r4 = new LabResult { Id = 2, OrderId = lo4.Id, Result = "15.2 pmol/L (Bình thường: 12.0 - 22.0)" };
        context.LabResults.AddRange(r3, r4);

        // Prescription
        var pres = new Prescription { Id = 1, MedicalRecordId = mr.Id, PrescribedDate = new DateOnly(2026, 11, 9) };
        context.Prescriptions.Add(pres);
        var presItem = new PrescriptionItem
        {
            Id = 1,
            PrescriptionId = pres.Id,
            MedicationId = med.Id,
            Quantity = 30,
            Dosage = "Uống 1 viên vào buổi sáng trước ăn 30 phút"
        };
        context.PrescriptionItems.Add(presItem);

        context.SaveChanges();

        var billing = new BillingService(context, clock);
        var healthRecord = new HealthRecordService(context, clock);

        return (context, clock, billing, healthRecord, patient.Id, patientAcc.Id, otherAcc.Id, doctorAcc.Id, cashierAcc.Id, app.Id);
    }

    [Fact]
    public async Task GetPendingInvoices_ReturnsVisitsWithAccurateTotalsAndBHYTStatus()
    {
        var (_, _, billing, _, _, _, _, _, _, _) = CreateFixture();

        var pending = await billing.GetPendingInvoicesAsync();

        Assert.NotEmpty(pending);
        var item = Assert.Single(pending, p => p.AppointmentCode == "LKB2026110901");
        Assert.Equal("Nguyễn Văn Anh", item.PatientName);
        Assert.True(item.HasInsurance);
        Assert.Equal(InvoiceStatus.Unpaid, item.InvoiceStatus);
    }

    [Fact]
    public async Task GetInvoiceDetail_CalculatesFeesAndApplies80PercentBHYTProperly()
    {
        var (_, _, billing, _, _, _, _, _, _, appId) = CreateFixture();

        var result = await billing.GetInvoiceDetailAsync(appId);

        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.NotNull(detail);

        // Total should include:
        // Exam: 150,000
        // Ultrasound: 120,000
        // Cytology: 165,000
        // TSH: 87,000
        // FT4: 124,000
        // Med (30 * 3,500 = 105,000)
        // Total = 150k + 120k + 165k + 87k + 124k + 105k = 751,000đ
        Assert.Equal(751000m, detail.TotalAmount);

        // Insurance card is 80% coverage
        Assert.True(detail.HasInsurance);
        Assert.Equal(80.0m, detail.CoveragePct);

        // Covered = 80% of 751,000 = 600,800
        Assert.Equal(600800m, detail.InsuranceCoveredAmount);

        // Patient Amount = 751,000 - 600,800 = 150,200đ
        Assert.Equal(150200m, detail.PatientAmount);
        Assert.Equal(InvoiceStatus.Unpaid, detail.Status);
    }

    [Fact]
    public async Task CheckoutInvoice_MarksAsPaid_CreatesPayment_AndPreventsDoublePayment()
    {
        var (context, _, billing, _, _, _, _, _, cashierId, appId) = CreateFixture();

        var request = new CheckoutInvoiceRequest(appId, PaymentMethod.Cash, "Thu tại quầy", cashierId);
        var checkoutResult = await billing.CheckoutInvoiceAsync(request);

        Assert.True(checkoutResult.IsSuccess);
        Assert.True(checkoutResult.Value > 0);

        // Verify Invoice & Payment in DB
        var app = await context.Appointments
            .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
            .FirstAsync(a => a.Id == appId);

        Assert.NotNull(app.Invoice);
        Assert.Equal(InvoiceStatus.Paid, app.Invoice.Status);
        Assert.Single(app.Invoice.Payments);
        Assert.Equal(PaymentMethod.Cash, app.Invoice.Payments.First().PaymentMethod);
        Assert.Equal(150200m, app.Invoice.Payments.First().Amount);

        // Attempting to checkout again should fail
        var secondAttempt = await billing.CheckoutInvoiceAsync(request);
        Assert.False(secondAttempt.IsSuccess);
        Assert.Contains("đã được thanh toán", secondAttempt.Error.Message);
    }

    [Fact]
    public async Task CheckoutInvoice_ConcurrentCalls_OnlyOneSucceeds_AndNoDuplicatePayments()
    {
        var (context, _, billing, _, _, _, _, _, cashierId, appId) = CreateFixture();

        var request = new CheckoutInvoiceRequest(appId, PaymentMethod.Cash, "Thu tại quầy đồng thời", cashierId);

        // Chạy 5 tác vụ thanh toán đồng thời cho cùng 1 lượt khám
        var tasks = Enumerable.Range(0, 5).Select(_ => billing.CheckoutInvoiceAsync(request)).ToList();
        var results = await Task.WhenAll(tasks);

        int successCount = results.Count(r => r.IsSuccess);
        int alreadyPaidCount = results.Count(r => !r.IsSuccess && r.Error.Code == "ALREADY_PAID");

        Assert.Equal(1, successCount);
        Assert.Equal(4, alreadyPaidCount);

        var app = await context.Appointments
            .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
            .FirstAsync(a => a.Id == appId);

        Assert.Single(app.Invoice!.Payments);
    }

    [Fact]
    public async Task HealthRecordDetail_MatchesUploadedPhotoStructure()
    {
        var (_, _, _, healthRecord, _, patientAccId, _, _, _, appId) = CreateFixture();

        var result = await healthRecord.GetHealthRecordDetailAsync(appId, patientAccId);

        Assert.True(result.IsSuccess);
        var record = result.Value;
        Assert.NotNull(record);

        // Patient ID card
        Assert.Equal("NGUYỄN VĂN ANH", record.PatientName);
        Assert.Equal("Nam", record.Gender);
        Assert.Equal(1, record.Age); // 1 year old
        Assert.Equal("LKB2026110901", record.AppointmentCode);

        // Services list (Ultrasound, Cytology, TSH, FT4, Exam)
        Assert.True(record.Services.Count >= 4);
        Assert.Contains(record.Services, s => s.Name.Contains("Siêu âm tuyến giáp"));
        Assert.Contains(record.Services, s => s.Name.Contains("Tế bào học"));
        Assert.Contains(record.Services, s => s.Name.Contains("TSH"));
        Assert.Contains(record.Services, s => s.Name.Contains("FT4"));

        // Clinical exam
        Assert.NotNull(record.Examination);
        Assert.Contains("bướu", record.Examination.DiagnosisDesc.ToLower());
        Assert.Equal("E04.1", record.Examination.DiagnosisCode);

        // Lab results
        Assert.NotEmpty(record.LabResults);
        Assert.Contains(record.LabResults, l => l.TestName.Contains("TSH") && l.ResultValue.Contains("1.85"));

        // Imaging results
        Assert.NotEmpty(record.ImagingResults);
        Assert.Contains(record.ImagingResults, img => img.ServiceName.Contains("Siêu âm"));

        // Prescription
        Assert.NotEmpty(record.Prescriptions);
        Assert.Contains(record.Prescriptions, p => p.MedicineName.Contains("Levothyrox") && p.Quantity == 30);
    }

    [Fact]
    public async Task HealthRecordDetail_EnforcesAccessControl_ForbidsUnrelatedAccount()
    {
        var (_, _, _, healthRecord, _, _, otherAccId, doctorAccId, cashierAccId, appId) = CreateFixture();

        // 1. Tài khoản bệnh nhân lạ -> FORBIDDEN
        var patientResult = await healthRecord.GetHealthRecordDetailAsync(appId, otherAccId);
        Assert.False(patientResult.IsSuccess);
        Assert.Equal("FORBIDDEN", patientResult.Error.Code);

        // 2. Nhân viên không thuộc lâm sàng (Thu ngân) truy cập hồ sơ bệnh án -> FORBIDDEN
        var cashierResult = await healthRecord.GetHealthRecordDetailAsync(appId, cashierAccId);
        Assert.False(cashierResult.IsSuccess);
        Assert.Equal("FORBIDDEN", cashierResult.Error.Code);

        // 3. Bác sĩ điều trị lượt khám -> SUCCESS
        var doctorResult = await healthRecord.GetHealthRecordDetailAsync(appId, doctorAccId);
        Assert.True(doctorResult.IsSuccess);

        // 4. Bệnh nhân có liên kết nhưng CanViewRecords = false -> FORBIDDEN (CWE-285, không để BookedBy qua mặt)
        var (ctx, _, _, hr, patId, patAccId, _, _, _, _) = CreateFixture();
        var link = await ctx.AccountPatientProfiles.FirstAsync(p => p.AccountId == patAccId && p.PatientId == patId);
        link.CanViewRecords = false;
        await ctx.SaveChangesAsync();
        var noConsentResult = await hr.GetHealthRecordDetailAsync(appId, patAccId);
        Assert.False(noConsentResult.IsSuccess);
        Assert.Equal("FORBIDDEN", noConsentResult.Error.Code);
    }
}
