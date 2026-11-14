using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class AuditFixRegressionTests
{
    [Fact]
    public async Task Priority_queue_cross_dept_idor_reallocate_busy_and_expire_holds_behave_correctly()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();

        var clock = new AuditClock();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new AppDbContext(options, clock);
        await db.Database.EnsureCreatedAsync();

        var deptA = new Department { Name = "Nội tổng quát" };
        var deptB = new Department { Name = "Tim mạch" };
        var roomA = new ClinicRoom { Department = deptA, RoomName = "Phòng A1", RoomNumber = "A101" };
        var roomA2 = new ClinicRoom { Department = deptA, RoomName = "Phòng A2", RoomNumber = "A102" };

        var headAccountA = new Account { Username = "head_a", PasswordHash = "x", RoleId = 2, IsActive = true };
        var headAccountB = new Account { Username = "head_b", PasswordHash = "x", RoleId = 2, IsActive = true };
        var headA = new DepartmentHead { Account = headAccountA, Department = deptA, FullName = "Trưởng khoa A" };
        var headB = new DepartmentHead { Account = headAccountB, Department = deptB, FullName = "Trưởng khoa B" };

        var docAcc = new Account { Username = "doc_a1", PasswordHash = "x", RoleId = 3, IsActive = true };
        var doctor = new Doctor { Account = docAcc, Department = deptA, FullName = "BS A1" };

        var nurseAcc = new Account { Username = "nurse_qa", PasswordHash = "x", RoleId = 4, IsActive = true };
        var nurse = new Nurse { Account = nurseAcc, Department = deptA, FullName = "Điều dưỡng QA" };

        var workDate = new DateOnly(2026, 11, 20);
        var bidSchedule = new DoctorSchedule
        {
            Doctor = doctor,
            Department = deptA,
            WorkDate = workDate,
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 30),
            EndTime = new TimeOnly(11, 30),
            SlotDurationMinutes = 30,
            MaxPatients = 20,
            OnlineSlots = 10,
            Status = DoctorScheduleStatus.Bidding
        };

        var approvedTargetSchedule = new DoctorSchedule
        {
            Doctor = doctor,
            Department = deptA,
            Room = roomA2,
            WorkDate = workDate.AddDays(1),
            Shift = ExamShift.Afternoon,
            StartTime = new TimeOnly(13, 0),
            EndTime = new TimeOnly(16, 30),
            SlotDurationMinutes = 30,
            MaxPatients = 20,
            OnlineSlots = 10,
            Status = DoctorScheduleStatus.Approved
        };

        var rejectedSchedule = new DoctorSchedule
        {
            Doctor = doctor,
            Department = deptA,
            WorkDate = workDate.AddDays(2),
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 30),
            EndTime = new TimeOnly(11, 30),
            SlotDurationMinutes = 30,
            MaxPatients = 20,
            OnlineSlots = 10,
            Status = DoctorScheduleStatus.Rejected,
            IsHotShift = true
        };

        var pNormal = new Patient { FullName = "Bệnh nhân thường", DateOfBirth = new DateOnly(1990, 1, 1), Gender = Gender.Male, PhoneNumber = "0900000001" };
        var pUrgent = new Patient { FullName = "Bệnh nhân ưu tiên 2", DateOfBirth = new DateOnly(1950, 1, 1), Gender = Gender.Female, PhoneNumber = "0900000002" };

        var slot = new AppointmentSlot
        {
            Schedule = approvedTargetSchedule,
            Doctor = doctor,
            Room = roomA2,
            StartTime = new DateTime(2026, 11, 21, 13, 0, 0),
            EndTime = new DateTime(2026, 11, 21, 13, 30, 0),
            Capacity = 1,
            BookedCount = 1,
            Status = SlotStatus.Full
        };

        var expiredPending = new Appointment
        {
            AppointmentCode = "EXP001",
            Patient = pNormal,
            Doctor = doctor,
            Department = deptA,
            Room = roomA2,
            Slot = slot,
            StatusId = (int)AppointmentStatusId.Pending,
            HoldUntil = clock.GetLocalNow().DateTime.AddMinutes(-5)
        };

        var now = clock.GetLocalNow().DateTime;
        var readyNormal = new Appointment
        {
            AppointmentCode = "Q001",
            Patient = pNormal,
            Doctor = doctor,
            Department = deptA,
            Room = roomA,
            StatusId = (int)AppointmentStatusId.ReadyForDoctor,
            PriorityLevel = 0,
            QueueNumber = "O01",
            CreatedAt = now,
            VitalSigns = new VitalSigns
            {
                RecordedByNurse = nurse,
                Temperature = 36.5m,
                Pulse = 75,
                RecordedAt = now
            }
        };

        var readyUrgent = new Appointment
        {
            AppointmentCode = "Q002",
            Patient = pUrgent,
            Doctor = doctor,
            Department = deptA,
            Room = roomA,
            StatusId = (int)AppointmentStatusId.ReadyForDoctor,
            PriorityLevel = 2,
            QueueNumber = "O02",
            CreatedAt = now,
            VitalSigns = new VitalSigns
            {
                RecordedByNurse = nurse,
                Temperature = 36.8m,
                Pulse = 82,
                RecordedAt = now
            }
        };

        db.AddRange(deptA, deptB, roomA, roomA2, headA, headB, doctor, nurse, bidSchedule, approvedTargetSchedule, rejectedSchedule, slot, expiredPending, readyNormal, readyUrgent);
        await db.SaveChangesAsync();

        var deptService = new DepartmentHeadService(db, clock);

        // 1. Cross-dept IDOR check: Head B cannot approve Dept A's schedule
        var idorResult = await deptService.ApproveShiftAsync(new ApproveShiftRequest(bidSchedule.Id, roomA.Id, headAccountB.Id));
        Assert.False(idorResult.IsSuccess);
        Assert.Equal("FORBIDDEN", idorResult.Error.Code);

        // Head A succeeds
        var approveResult = await deptService.ApproveShiftAsync(new ApproveShiftRequest(bidSchedule.Id, roomA.Id, headAccountA.Id));
        Assert.True(approveResult.IsSuccess);

        // 2. Reallocate into a slot where doctor already has an Approved shift returns DOCTOR_BUSY cleanly
        var busyResult = await deptService.ReallocateShiftAsync(
            new ReallocateShiftRequest(rejectedSchedule.Id, workDate.AddDays(1), ExamShift.Afternoon, roomA.Id, headAccountA.Id));
        Assert.False(busyResult.IsSuccess);
        Assert.Equal("DOCTOR_BUSY", busyResult.Error.Code);

        // 3. Priority queue calls PriorityLevel 2 ("O02" -> 2) before PriorityLevel 0 ("O01" -> 1)
        var queueService = new QueueService(db, clock);
        var called = await queueService.CallNextPatientAsync(roomA.Id, docAcc.Id);
        Assert.True(called.IsSuccess);
        Assert.Equal(2, called.Value);

        // 4. ExpirePendingHoldsAsync transitions Pending -> Expired and restores Slot availability
        var slotService = new SlotGenerationService(db, clock);
        var expired = await slotService.ExpirePendingHoldsAsync();
        Assert.True(expired.IsSuccess);
        Assert.Equal(1, expired.Value);

        await db.Entry(expiredPending).ReloadAsync();
        await db.Entry(slot).ReloadAsync();
        Assert.Equal((int)AppointmentStatusId.Expired, expiredPending.StatusId);
        Assert.Equal(0, slot.BookedCount);
        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    private sealed class AuditClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
    }
}

