using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HIS.Tests;

public class ReceptionAndStaffPortalPart4Tests
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
        ReceptionService Reception,
        int NurseAccountId,
        int Dept1Id,
        int Doctor1Id,
        int Room1Id,
        int Schedule1Id,
        int PatientId
    ) CreateHarness()
    {
        // 2026-11-09 is a Monday
        var clock = new MutableTimeProvider(new DateTime(2026, 11, 9, 8, 30, 0, DateTimeKind.Utc));
        var dbName = "ReceptionPart4Test_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new AppDbContext(options, clock);

        context.Roles.AddRange(
            new Role { Id = (int)RoleId.Admin, Code = "ADMIN", Name = "Quản trị viên" },
            new Role { Id = (int)RoleId.Doctor, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = (int)RoleId.Nurse, Code = "NURSE", Name = "Điều dưỡng" },
            new Role { Id = (int)RoleId.DepartmentHead, Code = "DEPT_HEAD", Name = "Trưởng khoa" },
            new Role { Id = (int)RoleId.Patient, Code = "PATIENT", Name = "Bệnh nhân" }
        );

        context.AppointmentStatuses.AddRange(
            new AppointmentStatus { Id = 1, Code = "PENDING", Name = "Chờ duyệt", SortOrder = 1, IsTerminal = false },
            new AppointmentStatus { Id = 2, Code = "CONFIRMED", Name = "Đã xác nhận", SortOrder = 2, IsTerminal = false },
            new AppointmentStatus { Id = 3, Code = "CHECKED_IN", Name = "Đã check-in", SortOrder = 3, IsTerminal = false },
            new AppointmentStatus { Id = 4, Code = "READY_FOR_DOCTOR", Name = "Chờ bác sĩ gọi", SortOrder = 4, IsTerminal = false },
            new AppointmentStatus { Id = 5, Code = "IN_PROGRESS", Name = "Đang khám", SortOrder = 5, IsTerminal = false },
            new AppointmentStatus { Id = 6, Code = "COMPLETED", Name = "Hoàn thành", SortOrder = 6, IsTerminal = true },
            new AppointmentStatus { Id = 7, Code = "CANCELLED", Name = "Đã hủy", SortOrder = 7, IsTerminal = true }
        );

        var dept1 = new Department { Name = "Khoa Nội tổng hợp", Description = "Nội tổng quát" };
        context.Departments.Add(dept1);
        context.SaveChanges();

        var room1 = new ClinicRoom { DepartmentId = dept1.Id, RoomNumber = "P101", RoomName = "Phòng Khám Nội 1" };
        context.ClinicRooms.Add(room1);
        context.SaveChanges();

        var doc1Account = new Account
        {
            Username = "dr.minh",
            Email = "minh.doc@hospital.com",
            PasswordHash = "BCRYPT_HASH",
            RoleId = (int)RoleId.Doctor,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(doc1Account);
        context.SaveChanges();

        var doc1 = new Doctor
        {
            AccountId = doc1Account.Id,
            DepartmentId = dept1.Id,
            FullName = "BS. Lê Hoàng Minh",
            LicenseNumber = "CCHN-001"
        };
        context.Doctors.Add(doc1);

        var nurseAccount = new Account
        {
            Username = "nurse.lan",
            Email = "lan.nurse@hospital.com",
            PasswordHash = "BCRYPT_HASH",
            RoleId = (int)RoleId.Nurse,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(nurseAccount);
        context.SaveChanges();

        var nurse = new Nurse
        {
            AccountId = nurseAccount.Id,
            DepartmentId = dept1.Id,
            FullName = "ĐD. Phạm Thị Lan"
        };
        context.Nurses.Add(nurse);

        var patient = new Patient
        {
            FullName = "Nguyễn Văn Hùng",
            DateOfBirth = new DateOnly(1990, 3, 15),
            Gender = Gender.Male,
            PhoneNumber = "0912345678",
            NationalId = "001090012345",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        context.Patients.Add(patient);

        var schedule = new DoctorSchedule
        {
            DoctorId = doc1.Id,
            RoomId = room1.Id,
            DayOfWeek = DayOfWeek.Monday,
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 30),
            EndTime = new TimeOnly(11, 30),
            SlotDurationMinutes = 15,
            OnlineRatioPct = 60,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        };
        context.DoctorSchedules.Add(schedule);
        context.SaveChanges();

        var bookingOpts = Options.Create(new BookingOptions());

        var allocator = new QueuePositionAllocator(context, bookingOpts, clock);
        var reception = new ReceptionService(context, allocator, bookingOpts, clock);

        return (
            context,
            clock,
            reception,
            nurseAccount.Id,
            dept1.Id,
            doc1.Id,
            room1.Id,
            schedule.Id,
            patient.Id
        );
    }

    [Fact]
    public async Task GetDeskAsync_ValidNurse_ReturnsReceptionDeskWithVisitsAndSchedules()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var app = new Appointment
        {
            AppointmentCode = "HIS-20261109-001",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            PriorityLevel = 0,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var result = await reception.GetDeskAsync(nurseAccountId, null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Khoa Nội tổng hợp", result.Value.Department);
        Assert.Single(result.Value.Visits);
        Assert.Equal("HIS-20261109-001", result.Value.Visits[0].Code);
        Assert.NotEmpty(result.Value.Schedules);
    }

    [Fact]
    public async Task GetDeskAsync_WithSearch_FiltersVisits()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var app1 = new Appointment
        {
            AppointmentCode = "HIS-20261109-MATCH",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app1);
        await context.SaveChangesAsync();

        var result = await reception.GetDeskAsync(nurseAccountId, "0912345678");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Visits);
        Assert.Equal("HIS-20261109-MATCH", result.Value.Visits[0].Code);
    }

    [Fact]
    public async Task CheckInAsync_WithoutIdentityVerified_ReturnsFailure()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var app = new Appointment
        {
            AppointmentCode = "HIS-20261109-CHK1",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var result = await reception.CheckInAsync(nurseAccountId, app.Id, identityVerified: false);

        Assert.False(result.IsSuccess);
        Assert.Equal("NOT_VERIFIED", result.Error.Code);
    }

    [Fact]
    public async Task CheckInAsync_WithIdentityVerified_IssuesQueueNumberAndSetsCheckedIn()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 8, 30, 0),
            EndTime = new DateTime(2026, 11, 9, 8, 45, 0),
            Capacity = 1,
            BookedCount = 1,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var app = new Appointment
        {
            AppointmentCode = "HIS-20261109-CHK2",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            SlotId = slot.Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var result = await reception.CheckInAsync(nurseAccountId, app.Id, identityVerified: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value!);

        var updated = await context.Appointments.FindAsync(app.Id);
        Assert.NotNull(updated);
        Assert.Equal((int)AppointmentStatusId.CheckedIn, updated.StatusId);
    }

    [Fact]
    public async Task RegisterWalkInAsync_WithoutIdentityVerified_ReturnsFailure()
    {
        var (_, _, reception, nurseAccountId, _, _, _, scheduleId, _) = CreateHarness();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = false,
            FullName = "Vãng Lai"
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.False(result.IsSuccess);
        Assert.Equal("NOT_VERIFIED", result.Error.Code);
    }

    [Fact]
    public async Task RegisterWalkInAsync_ValidWalkInRequest_CreatesAppointmentAndIssuesQueueNumber()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, _) = CreateHarness();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 9, 0, 0),
            EndTime = new DateTime(2026, 11, 9, 9, 15, 0),
            Capacity = 2,
            OnlineQuota = 1,
            BookedCount = 0,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = true,
            FullName = "Trần Khắc Vãng Lai",
            DateOfBirth = new DateOnly(1985, 4, 10),
            Gender = Gender.Male,
            PhoneNumber = "0988776655",
            NationalId = "034085001234",
            Reason = "Đau đầu chóng mặt",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value!);

        var created = await context.Appointments
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Patient.PhoneNumber == "0988776655");

        Assert.NotNull(created);
        Assert.Equal(BookingChannel.OnSite, created.BookingChannel);
        Assert.Equal(VisitType.WalkIn, created.VisitType);
        Assert.Equal((int)AppointmentStatusId.CheckedIn, created.StatusId);
        Assert.Equal("Trần Khắc Vãng Lai", created.Patient.FullName);
    }
}

