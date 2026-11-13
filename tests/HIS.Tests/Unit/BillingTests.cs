using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Unit;

public static class CashierTestDatabase
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static string? _resolvedConnectionString;
    private static PostgreSqlContainer? _container;

    public static async Task<AppDbContext> CreateDbAsync()
    {
        if (_resolvedConnectionString == null)
        {
            await InitLock.WaitAsync();
            try
            {
                if (_resolvedConnectionString == null)
                {
                    var localCandidates = new[]
                    {
                        "Host=localhost;Port=5432;Database=his_db;Username=postgres;Password=123;Timeout=2;Command Timeout=5",
                        "Host=localhost;Port=5432;Database=his_db;Username=postgres;Password=123456789;Timeout=2;Command Timeout=5"
                    };

                    foreach (var candidate in localCandidates)
                    {
                        var testOpts = new DbContextOptionsBuilder<AppDbContext>()
                            .UseNpgsql(candidate)
                            .UseSnakeCaseNamingConvention()
                            .Options;
                        await using var probeDb = new AppDbContext(testOpts, new VietnamTimeProvider());
                        if (await probeDb.Database.CanConnectAsync())
                        {
                            _resolvedConnectionString = candidate;
                            break;
                        }
                    }

                    if (_resolvedConnectionString == null)
                    {
                        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
                        await _container.StartAsync();
                        _resolvedConnectionString = _container.GetConnectionString();

                        var initOpts = new DbContextOptionsBuilder<AppDbContext>()
                            .UseNpgsql(_resolvedConnectionString)
                            .UseSnakeCaseNamingConvention()
                            .Options;
                        await using var initDb = new AppDbContext(initOpts, new VietnamTimeProvider());
                        await initDb.Database.EnsureCreatedAsync();
                        await SeedMinimalCashierDataAsync(initDb);
                    }
                }
            }
            finally
            {
                InitLock.Release();
            }
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_resolvedConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, new VietnamTimeProvider());
    }

    private static async Task SeedMinimalCashierDataAsync(AppDbContext db)
    {
        if (await db.Patients.AnyAsync(p => p.FullName == "Trần Quốc Bảo"))
        {
            return;
        }

        var dept = new Department { Name = "Khoa Ngoại tổng quát", Description = "Khoa Ngoại" };
        db.Departments.Add(dept);

        var cashierAcc = new Account
        {
            Username = "ketoan",
            Email = "ketoan@his.local",
            PasswordHash = "hash",
            RoleId = (int)RoleId.Accountant,
            IsActive = true
        };
        var docAcc = new Account
        {
            Username = "bs.test.cashier",
            Email = "bs.test.cashier@his.local",
            PasswordHash = "hash",
            RoleId = (int)RoleId.Doctor,
            IsActive = true
        };
        db.Accounts.AddRange(cashierAcc, docAcc);
        await db.SaveChangesAsync();

        var doc = new Doctor
        {
            AccountId = docAcc.Id,
            DepartmentId = dept.Id,
            FullName = "BS. Test Thu Ngân"
        };
        var room = new ClinicRoom
        {
            DepartmentId = dept.Id,
            RoomName = "Phòng Khám 101",
            RoomNumber = "PK101"
        };
        var service = new Service
        {
            Name = "Khám lâm sàng tổng quát",
            ServiceTypeId = 1,
            Price = 150000m,
            IsInsuranceCovered = true,
            IsActive = true
        };
        var plan = new InsurancePlan
        {
            Name = "Đúng tuyến - 80%",
            CoveragePct = 80m,
            ReferralType = ReferralType.InNetwork
        };
        var patient = new Patient
        {
            FullName = "Trần Quốc Bảo",
            DateOfBirth = new DateOnly(1988, 5, 10),
            Gender = Gender.Male,
            NationalId = "001084000001",
            PhoneNumber = "0912001100",
            Address = "TP.HCM"
        };
        db.Doctors.Add(doc);
        db.ClinicRooms.Add(room);
        db.Services.Add(service);
        db.InsurancePlans.Add(plan);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var card = new InsuranceCard
        {
            PatientId = patient.Id,
            CardNumber = "DN4790011223344",
            InsurancePlanId = plan.Id,
            RegisteredFacility = "BV MediCare",
            IsRegisteredHere = true,
            ValidFrom = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            ValidTo = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
            IsActive = true
        };
        var app = new Appointment
        {
            AppointmentCode = "AP-TEST-0001",
            PatientId = patient.Id,
            DoctorId = doc.Id,
            DepartmentId = dept.Id,
            RoomId = room.Id,
            StatusId = (int)AppointmentStatusId.Completed,
            BookingChannel = BookingChannel.OnSite,
            VisitType = VisitType.WalkIn,
            CheckedInAt = DateTime.Now.AddHours(-1),
            CreatedAt = DateTime.Now.AddHours(-1)
        };
        db.InsuranceCards.Add(card);
        db.Appointments.Add(app);
        await db.SaveChangesAsync();
    }
}

public class BillingTests
{
    private static Task<AppDbContext> CreateDbAsync() => CashierTestDatabase.CreateDbAsync();

    [Fact]
    public async Task GetInvoiceDetail_WithValidInNetworkInsurance_CalculatesCoverageCorrectly()
    {
        await using var db = await CreateDbAsync();
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
        await using var db = await CreateDbAsync();
        var options = Options.Create(new BillingOptions { ExamFeeServiceId = 1, DefaultExamPrice = 150000m });
        var billingService = new BillingService(db, new VietnamTimeProvider(), options);

        var result = await billingService.GetInvoiceDetailAsync(999999);
        Assert.False(result.IsSuccess);
        Assert.Equal("NOT_FOUND", result.Error.Code);
    }
}
