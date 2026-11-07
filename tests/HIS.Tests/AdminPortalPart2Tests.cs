using System.Security.Claims;
using System.Text;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using HIS.Web.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HIS.Tests;

public class AdminPortalPart2Tests
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

    private static (AppDbContext Context, MutableTimeProvider Clock, StaffService Staff, DepartmentService Dept, AdminReportService Report, ScheduleChangeService Schedule, int AdminId, int DoctorAccountId, int DoctorId, int DeptId) CreateHarness()
    {
        var clock = new MutableTimeProvider(new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc));
        var dbName = "AdminTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new AppDbContext(options, clock);

        context.Roles.AddRange(
            new Role { Id = (int)RoleId.Admin, Code = "ADMIN", Name = "Quản trị viên" },
            new Role { Id = (int)RoleId.Doctor, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = (int)RoleId.Nurse, Code = "NURSE", Name = "Điều dưỡng" },
            new Role { Id = (int)RoleId.DepartmentHead, Code = "DEPT_HEAD", Name = "Trưởng khoa" },
            new Role { Id = (int)RoleId.Patient, Code = "PATIENT", Name = "Bệnh nhân" }
        );

        var dept = new Department { Name = "Khoa Nội", Description = "Khám nội tổng quát" };
        context.Departments.Add(dept);

        var adminAccount = new Account
        {
            Username = "admin_super",
            Email = "admin@hospital.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPass123!"),
            RoleId = (int)RoleId.Admin,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(adminAccount);

        var doctorAccount = new Account
        {
            Username = "doctor_an",
            Email = "doctor.an@hospital.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("DoctorPass123!"),
            RoleId = (int)RoleId.Doctor,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(doctorAccount);

        var doctor = new Doctor
        {
            Account = doctorAccount,
            FullName = "BS. Nguyễn Văn An",
            Department = dept
        };
        context.Doctors.Add(doctor);

        context.SaveChanges();

        var doctorMock = new Mock<IDoctorService>();
        var staffService = new StaffService(context, doctorMock.Object, clock);
        var deptService = new DepartmentService(context);
        var reportService = new AdminReportService(context);
        var scheduleService = new ScheduleChangeService(context, clock);

        return (context, clock, staffService, deptService, reportService, scheduleService, adminAccount.Id, doctorAccount.Id, doctor.Id, dept.Id);
    }

    [Fact]
    public async Task AdminRules_RequireAdmin_RejectsNonAdminAccess()
    {
        var (_, _, staff, _, report, schedule, _, doctorAccountId, _, _) = CreateHarness();

        await Assert.ThrowsAsync<AdminOperationException>(() => staff.GetAdminAccountsAsync(doctorAccountId));
        await Assert.ThrowsAsync<AdminOperationException>(() => report.GetRevenueAsync(doctorAccountId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7)));
        await Assert.ThrowsAsync<AdminOperationException>(() => schedule.GetRequestsAsync(doctorAccountId));
    }

    [Fact]
    public async Task StaffService_CreateAdminAccount_Doctor_CreatesAccountAndProfile()
    {
        var (context, _, staff, _, _, _, adminId, _, _, deptId) = CreateHarness();

        var req = new CreateAdminAccountRequest
        {
            Username = "doctor_minh",
            Password = "SecurePassword123!",
            FullName = "BS. Lê Quang Minh",
            Email = "lqminh@hospital.com",
            RoleId = (int)RoleId.Doctor,
            DepartmentId = deptId
        };

        var createdId = await staff.CreateAdminAccountAsync(adminId, req);

        Assert.True(createdId > 0);
        var acc = await context.Accounts.Include(a => a.Doctor).FirstOrDefaultAsync(a => a.Id == createdId);
        Assert.NotNull(acc);
        Assert.Equal("doctor_minh", acc.Username);
        Assert.NotNull(acc.Doctor);
        Assert.Equal("BS. Lê Quang Minh", acc.Doctor.FullName);
        Assert.Equal(deptId, acc.Doctor.DepartmentId);
        Assert.True(acc.IsActive);
    }

    [Fact]
    public async Task StaffService_SetAccountActive_DeactivatesAndProtectsLastAdmin()
    {
        var (context, _, staff, _, _, _, adminId, doctorAccountId, _, _) = CreateHarness();

        // Cannot deactivate the only admin
        await Assert.ThrowsAsync<AdminOperationException>(() => staff.SetAccountActiveAsync(adminId, adminId, false));

        // Deactivating doctor succeeds
        await staff.SetAccountActiveAsync(adminId, doctorAccountId, false);
        var docAcc = await context.Accounts.FindAsync(doctorAccountId);
        Assert.NotNull(docAcc);
        Assert.False(docAcc.IsActive);

        var isActive = await staff.IsSessionActiveAsync(doctorAccountId, "DOCTOR");
        Assert.False(isActive);
    }

    [Fact]
    public async Task DepartmentService_CreateGetRoomsAndDeleteSafety()
    {
        var (context, _, _, deptService, _, _, _, _, _, deptId) = CreateHarness();

        // Create new department
        var created = await deptService.CreateDepartmentAsync("Khoa Ngoại", "Phẫu thuật ngoại khoa");
        Assert.NotNull(created);
        Assert.Equal("Khoa Ngoại", created.Name);

        // Add a room
        context.ClinicRooms.Add(new ClinicRoom
        {
            DepartmentId = created.Id,
            RoomName = "Phòng Khám Ngoại 1",
            RoomNumber = "P201"
        });
        await context.SaveChangesAsync();

        var rooms = await deptService.GetRoomsByDepartmentAsync(created.Id);
        Assert.Single(rooms);
        Assert.Equal("P201", rooms[0].RoomNumber);

        // Delete department with room must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() => deptService.DeleteDepartmentAsync(created.Id));
    }

    [Fact]
    public async Task AdminReportService_RevenueCalculations_And_CsvExport()
    {
        var (context, _, _, _, reportService, _, adminId, _, _, deptId) = CreateHarness();

        var invoice = new Invoice
        {
            PatientId = 1,
            TotalAmount = 500_000,
            PatientAmount = 500_000,
            Status = InvoiceStatus.Paid,
            CreatedAt = new DateTime(2026, 11, 5, 9, 0, 0)
        };
        context.Invoices.Add(invoice);

        // Deposit: 200,000
        context.Payments.Add(new Payment
        {
            Invoice = invoice,
            Amount = 200_000,
            PaymentType = PaymentType.Deposit,
            PaymentMethod = PaymentMethod.Cash,
            PaidAt = new DateTime(2026, 11, 5, 9, 30, 0)
        });

        // Payment: 300,000
        context.Payments.Add(new Payment
        {
            Invoice = invoice,
            Amount = 300_000,
            PaymentType = PaymentType.Payment,
            PaymentMethod = PaymentMethod.BankTransfer,
            PaidAt = new DateTime(2026, 11, 5, 10, 0, 0)
        });

        // Refund: 50,000
        context.Payments.Add(new Payment
        {
            Invoice = invoice,
            Amount = 50_000,
            PaymentType = PaymentType.Refund,
            PaymentMethod = PaymentMethod.Cash,
            PaidAt = new DateTime(2026, 11, 5, 11, 0, 0)
        });

        // Appointment visit on Nov 5 (CheckedInAt counts as visit)
        context.Appointments.Add(new Appointment
        {
            AppointmentCode = "HIS-20261105-001",
            PatientId = 1,
            DepartmentId = deptId,
            StatusId = (int)AppointmentStatusId.CheckedIn,
            CheckedInAt = new DateTime(2026, 11, 5, 8, 30, 0),
            CreatedAt = new DateTime(2026, 11, 5, 8, 30, 0)
        });

        await context.SaveChangesAsync();

        var report = await reportService.GetRevenueAsync(adminId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10));

        Assert.Equal(200_000, report.Deposits);
        Assert.Equal(300_000, report.Payments);
        Assert.Equal(50_000, report.Refunds);
        Assert.Equal(450_000, report.Revenue); // 200k + 300k - 50k = 450k
        Assert.Equal(1, report.Visits);

        var csvBytes = await reportService.ExportRevenueAsync(adminId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10));
        var csvText = Encoding.UTF8.GetString(csvBytes);
        Assert.Contains("Ngày,Tạm ứng,Thanh toán,Hoàn tiền,Doanh thu thuần,Lượt khám", csvText);
        Assert.Contains("2026-11-05", csvText);
        Assert.Contains("450000", csvText);
    }

    [Fact]
    public async Task ScheduleChangeService_Review_RejectionFlow()
    {
        var (context, _, _, _, _, scheduleService, adminId, _, doctorId, _) = CreateHarness();

        var ex = new ScheduleException
        {
            DoctorId = doctorId,
            Date = new DateOnly(2026, 11, 15),
            Shift = ExamShift.Morning,
            Type = ScheduleExceptionType.Leave,
            Status = ScheduleExceptionStatus.Pending,
            Reason = "Việc gia đình",
            CreatedAt = new DateTime(2026, 11, 7, 7, 0, 0)
        };
        context.ScheduleExceptions.Add(ex);
        await context.SaveChangesAsync();

        var decisionReq = new ScheduleDecisionRequest(ex.Id, false, null);
        var res = await scheduleService.ReviewAsync(adminId, decisionReq);

        Assert.Equal(0, res.Transferred);
        var updated = await context.ScheduleExceptions.FindAsync(ex.Id);
        Assert.NotNull(updated);
        Assert.Equal(ScheduleExceptionStatus.Rejected, updated.Status);
        Assert.Equal(adminId, updated.ApprovedBy);
        Assert.NotNull(updated.ApprovedAt);
    }

    [Fact]
    public async Task ScheduleChangeService_Review_ApproveWithOnlineBooked_ResetsOnlineCountAndBlocksSlot()
    {
        var (context, _, _, _, _, scheduleService, adminId, _, doctorId, _) = CreateHarness();

        var slot = new AppointmentSlot
        {
            DoctorId = doctorId,
            RoomId = 1,
            StartTime = new DateTime(2026, 11, 15, 8, 0, 0),
            EndTime = new DateTime(2026, 11, 15, 8, 30, 0),
            Capacity = 5,
            BookedCount = 1,
            OnlineQuota = 3,
            OnlineBooked = 1,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);

        var ex = new ScheduleException
        {
            DoctorId = doctorId,
            Date = new DateOnly(2026, 11, 15),
            Shift = ExamShift.Morning,
            Type = ScheduleExceptionType.Leave,
            Status = ScheduleExceptionStatus.Pending,
            Reason = "Việc cá nhân",
            CreatedAt = new DateTime(2026, 11, 7, 7, 0, 0)
        };
        context.ScheduleExceptions.Add(ex);
        await context.SaveChangesAsync();

        var decisionReq = new ScheduleDecisionRequest(ex.Id, true, null);
        await scheduleService.ReviewAsync(adminId, decisionReq);

        var updatedSlot = await context.AppointmentSlots.FindAsync(slot.Id);
        Assert.NotNull(updatedSlot);
        Assert.Equal(SlotStatus.Blocked, updatedSlot.Status);
        Assert.Equal(0, updatedSlot.BookedCount);
        Assert.Equal(0, updatedSlot.OnlineBooked);
    }

    [Fact]
    public async Task CookieValidation_RejectsInactiveAccount()
    {
        var authMock = new Mock<IAuthService>();
        authMock.Setup(s => s.IsSessionActiveAsync(10, "ADMIN", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock.Setup(s => s.SignOutAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection()
            .AddSingleton(authMock.Object)
            .AddSingleton(authServiceMock.Object)
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        var options = new CookieAuthenticationOptions();
        new AccountStatusCookieOptions().PostConfigure(CookieAuthenticationDefaults.AuthenticationScheme, options);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "10"),
            new Claim(ClaimTypes.Role, "ADMIN")
        ], "Cookies"));

        var scheme = new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler));
        var context = new CookieValidatePrincipalContext(http, scheme, options, new AuthenticationTicket(principal, "Cookies"));

        await options.Events.OnValidatePrincipal(context);

        Assert.Null(context.Principal);
        authServiceMock.Verify(s => s.SignOutAsync(It.IsAny<HttpContext>(), "Cookies", It.IsAny<AuthenticationProperties>()), Times.Once());
    }
}
