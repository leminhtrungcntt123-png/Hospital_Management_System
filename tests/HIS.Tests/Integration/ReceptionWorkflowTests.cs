using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class ReceptionWorkflowTests
{
    [Fact]
    public async Task Reception_PersistsVisitsAndEnforcesDepartmentAndQuota()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        await ReceptionWorkflowScenario.RunAsync(container.GetConnectionString());
    }
}

public static class ReceptionWorkflowScenario
{
    public static async Task RunAsync(string connection)
    {
        var clock = new ReceptionClock();
        var config = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(config, clock);
        await db.Database.EnsureCreatedAsync();
        var dept = new Department { Name = "QA Reception" };
        var otherDept = new Department { Name = "QA Other" };
        var nurseAccount = new Account { Username = "qa_nurse", PasswordHash = "test-only", RoleId = 4, IsActive = true };
        var otherAccount = new Account { Username = "qa_other_nurse", PasswordHash = "test-only", RoleId = 4, IsActive = true };
        var doctorAccount = new Account { Username = "qa_doctor", PasswordHash = "test-only", RoleId = 3, IsActive = true };
        var patientAccount = new Account { Username = "qa_patient", PasswordHash = "test-only", RoleId = 5, IsActive = true };
        var nurse = new Nurse { Account = nurseAccount, Department = dept, FullName = "QA Nurse" };
        var other = new Nurse { Account = otherAccount, Department = otherDept, FullName = "QA Other Nurse" };
        var doctor = new Doctor { Account = doctorAccount, Department = dept, FullName = "QA Doctor" };
        var room = new ClinicRoom { Department = dept, RoomNumber = "QA01", RoomName = "QA Room" };
        var patient = new Patient { FullName = "QA Web Patient", PhoneNumber = "0900000001", DateOfBirth = new DateOnly(1990, 1, 1) };
        var oldPatient = new Patient { FullName = "QA Existing Patient", PhoneNumber = "0900000002", DateOfBirth = new DateOnly(1992, 1, 1) };
        db.AddRange(nurse, other, doctor, room, patientAccount, patient, oldPatient);
        var schedule = new DoctorSchedule
        {
            Doctor = doctor,
            Department = dept,
            Room = room,
            WorkDate = new DateOnly(2026, 11, 5),
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 30),
            EndTime = new TimeOnly(11, 30),
            Status = DoctorScheduleStatus.Approved,
            MaxPatients = 4,
            OnlineSlots = 2
        };
        db.Add(schedule);
        var slot = new AppointmentSlot
        {
            Doctor = doctor,
            Room = room,
            StartTime = new DateTime(2026, 11, 5, 9, 30, 0),
            EndTime = new DateTime(2026, 11, 5, 9, 45, 0),
            Capacity = 1,
            BookedCount = 1,
            Status = SlotStatus.Full
        };
        var appointment = new Appointment
        {
            AppointmentCode = "QA_WEB_VISIT",
            Patient = patient,
            BookedByAccount = patientAccount,
            Doctor = doctor,
            Department = dept,
            Room = room,
            Slot = slot,
            StatusId = 2,
            CreatedAt = new DateTime(2026, 11, 5, 8, 0, 0)
        };
        db.Add(appointment);
        db.AccountPatientProfiles.Add(new AccountPatientProfile
        {
            Account = patientAccount,
            Patient = patient,
            Relationship = PatientRelationship.Parent,
            Status = LinkStatus.Pending,
            CreatedAt = new DateTime(2026, 11, 5, 8, 0, 0)
        });
        await db.SaveChangesAsync();
        var nurseId = nurseAccount.Id; var otherId = otherAccount.Id; var doctorId = doctorAccount.Id;
        var appointmentId = appointment.Id; var scheduleId = schedule.Id; var oldPatientId = oldPatient.Id;
        var service = new ReceptionService(db, clock, Options.Create(new BookingOptions()));

        Assert.False((await service.GetDeskAsync(doctorId, null)).IsSuccess);
        Assert.Empty((await service.GetDeskAsync(otherId, null)).Value!.Visits);
        Assert.False((await service.CheckInAsync(otherId, appointmentId, true)).IsSuccess);
        Assert.False((await service.CheckInAsync(nurseId, appointmentId, false)).IsSuccess);
        clock.Hour = 1; clock.Minute = 29;
        Assert.False((await service.CheckInAsync(nurseId, appointmentId, true)).IsSuccess);
        clock.Hour = 2; clock.Minute = 0;
        Assert.Equal("O01", (await service.CheckInAsync(nurseId, appointmentId, true)).Value);
        Assert.Equal("O01", (await service.CheckInAsync(nurseId, appointmentId, true)).Value);
        Assert.Equal(1, await db.QueueCounters.AsNoTracking().Where(q => q.Channel == BookingChannel.Web).Select(q => q.LastNumber).SingleAsync());
        Assert.Single(await db.AppointmentStatusHistories.Where(h => h.AppointmentId == appointmentId).ToListAsync());
        Assert.False(await db.AccountPatientProfiles.Where(l => l.PatientId == patient.Id).Select(l => l.CanViewRecords).SingleAsync());

        WalkInReceptionRequest Existing() => new() { PatientId = oldPatientId, ScheduleId = scheduleId, IdentityVerified = true };
        Assert.False((await service.RegisterWalkInAsync(otherId, Existing())).IsSuccess);
        Assert.Equal("W01", (await service.RegisterWalkInAsync(nurseId, Existing())).Value);
        Assert.False((await service.RegisterWalkInAsync(nurseId, Existing())).IsSuccess);
        Assert.Equal("W02", (await service.RegisterWalkInAsync(nurseId, new WalkInReceptionRequest
        {
            FullName = "QA New Patient",
            DateOfBirth = new DateOnly(2000, 1, 1),
            PhoneNumber = "0900000003",
            ScheduleId = scheduleId,
            IdentityVerified = true,
            PriorityLevel = 2
        })).Value);
        Assert.False((await service.RegisterWalkInAsync(nurseId, new WalkInReceptionRequest
        {
            FullName = "QA Over Quota",
            DateOfBirth = new DateOnly(2000, 1, 1),
            PhoneNumber = "0900000004",
            ScheduleId = scheduleId,
            IdentityVerified = true
        })).IsSuccess);
        Assert.False(await db.Patients.AnyAsync(p => p.PhoneNumber == "0900000004"));
        Assert.Equal(2, await db.Appointments.CountAsync(a => a.BookingChannel == BookingChannel.OnSite && a.SlotId == null && a.StatusId == 3));
        Assert.Equal(3, (await service.GetDeskAsync(nurseId, null)).Value!.Visits.Count);
        Assert.Single((await service.GetDeskAsync(nurseId, "0900000002")).Value!.Patients);
        Assert.Equal(3, await db.AccessLogs.CountAsync());
    }

    private sealed class ReceptionClock : TimeProvider
    {
        public int Hour { get; set; } = 2;
        public int Minute { get; set; }
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 5, Hour, Minute, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    }
}
