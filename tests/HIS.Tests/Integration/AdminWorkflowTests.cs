using System.Text;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class AdminWorkflowTests
{
    [Fact]
    public async Task Admin_ReportsAccountsAndScheduleChangesPersistCorrectly()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await AdminWorkflowScenario.RunAsync(postgres.GetConnectionString());
    }
}

public static class AdminWorkflowScenario
{
    public static async Task RunAsync(string connection)
    {
        var clock = new AdminTestClock();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(options, clock);
        await db.Database.EnsureCreatedAsync();

        var admin = Account("admin_qa", 1);
        var secondAdmin = Account("admin_second_qa", 1);
        var doctorAccount = Account("doctor_a_qa", 3);
        var replacementAccount = Account("doctor_b_qa", 3);
        var otherDoctorAccount = Account("doctor_other_qa", 3);
        var nurseAccount = Account("nurse_qa", 4);
        var booker = Account("patient_qa", 5);
        var department = new Department { Name = "QA Department" };
        var otherDepartment = new Department { Name = "QA Other" };
        var doctor = new Doctor
        {
            Account = doctorAccount,
            Department = department,
            FullName = "QA Doctor A"
        };
        var replacement = new Doctor
        {
            Account = replacementAccount,
            Department = department,
            FullName = "QA Doctor B"
        };
        var otherDoctor = new Doctor
        {
            Account = otherDoctorAccount,
            Department = otherDepartment,
            FullName = "QA Other Doctor"
        };
        var nurse = new Nurse
        {
            Account = nurseAccount,
            Department = department,
            FullName = "QA Nurse"
        };
        var room = new ClinicRoom
        {
            Department = department,
            RoomName = "QA Room A",
            RoomNumber = "QA01"
        };
        var targetRoom = new ClinicRoom
        {
            Department = department,
            RoomName = "QA Room B",
            RoomNumber = "QA02"
        };
        var patient = new Patient
        {
            FullName = "QA Patient",
            DateOfBirth = new DateOnly(1990, 1, 1),
            PhoneNumber = "0900000888"
        };
        db.AddRange(admin, secondAdmin, doctor, replacement, otherDoctor, nurse, booker, room, targetRoom, patient);
        await db.SaveChangesAsync();

        var schedules = new[] { (doctor, room, 4), (replacement, targetRoom, 1) };
        foreach (var (person, clinic, online) in schedules)
        {
            db.DoctorSchedules.Add(new DoctorSchedule
            {
                Doctor = person,
                Department = department,
                Room = clinic,
                WorkDate = new DateOnly(2026, 11, 6),
                Shift = ExamShift.Morning,
                StartTime = new TimeOnly(7, 30),
                EndTime = new TimeOnly(11, 30),
                Status = DoctorScheduleStatus.Approved,
                OnlineSlots = online,
                MaxPatients = 8
            });
        }

        AppointmentSlot Slot(Doctor person, ClinicRoom clinic, int hour, int minute, int booked) => new()
        {
            Doctor = person,
            Room = clinic,
            StartTime = new DateTime(2026, 11, 6, hour, minute, 0),
            EndTime = new DateTime(2026, 11, 6, hour, minute, 0).AddMinutes(15),
            Capacity = 1,
            BookedCount = booked,
            Status = booked == 1 ? SlotStatus.Full : SlotStatus.Available
        };

        var sourceA = Slot(doctor, room, 9, 30, 1);
        var sourceB = Slot(doctor, room, 10, 0, 1);
        var sourceHold = Slot(doctor, room, 11, 0, 1);
        var targetA = Slot(replacement, targetRoom, 9, 30, 0);
        var targetB = Slot(replacement, targetRoom, 10, 0, 0);
        db.AddRange(targetA, targetB);
        Appointment Visit(string code, AppointmentSlot slot, int status) => new()
        {
            AppointmentCode = code,
            Slot = slot,
            Patient = patient,
            BookedByAccount = booker,
            Doctor = doctor,
            Room = room,
            Department = department,
            StatusId = status,
            CreatedAt = new DateTime(2026, 11, 5, 8, 0, 0)
        };
        var confirmedA = Visit("QA_CONFIRMED_A", sourceA, 2);
        var confirmedB = Visit("QA_CONFIRMED_B", sourceB, 2);
        var hold = Visit("QA_PENDING", sourceHold, 1);
        db.AddRange(confirmedA, confirmedB, hold);

        var completed = new Appointment
        {
            AppointmentCode = "QA_COMPLETED",
            Patient = patient,
            Department = department,
            Doctor = doctor,
            Room = room,
            StatusId = 6,
            BookingChannel = BookingChannel.OnSite,
            VisitType = VisitType.WalkIn,
            CheckedInAt = new DateTime(2026, 11, 5, 8, 0, 0),
            CreatedAt = new DateTime(2026, 11, 5, 8, 0, 0)
        };
        var secondVisit = new Appointment
        {
            AppointmentCode = "QA_SECOND_VISIT",
            Patient = patient,
            Department = department,
            Doctor = doctor,
            Room = room,
            StatusId = 3,
            BookingChannel = BookingChannel.OnSite,
            VisitType = VisitType.WalkIn,
            CheckedInAt = new DateTime(2026, 11, 5, 9, 0, 0),
            CreatedAt = new DateTime(2026, 11, 5, 9, 0, 0)
        };
        db.AddRange(completed, secondVisit);
        var invoice = new Invoice
        {
            Appointment = completed,
            Patient = patient,
            TotalAmount = 800
        };
        db.Invoices.Add(invoice);
        var change = new ScheduleException
        {
            Doctor = doctor,
            Date = new DateOnly(2026, 11, 6),
            Shift = ExamShift.Morning,
            Type = ScheduleExceptionType.Leave,
            Reason = "QA schedule change"
        };
        db.ScheduleExceptions.Add(change);
        await db.SaveChangesAsync();

        // Seed ledger values explicitly; this feature reads the ledger and does not implement billing writes.
        var paidAt = new DateTime(2026, 11, 5, 10, 0, 0);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO payments (invoice_id,amount,payment_type,payment_method,paid_at) VALUES ({0},200,'DEPOSIT','CASH',{1}),({0},500,'PAYMENT','CASH',{1}),({0},50,'REFUND','CASH',{1})",
            invoice.Id, new Npgsql.NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = paidAt });
        await db.Database.ExecuteSqlRawAsync("INSERT INTO payments (invoice_id,amount,payment_type,payment_method,paid_at) VALUES ({0},100,'PAYMENT','CASH',{1})",
            invoice.Id, new Npgsql.NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = paidAt.AddDays(1) });

        var actor = admin.Id;
        var nurseId = nurseAccount.Id;
        var departmentId = department.Id;
        var changeId = change.Id;
        var sourceId = sourceA.Id;
        var confirmedId = confirmedA.Id;
        var rebookId = confirmedB.Id;
        var holdId = hold.Id;
        var targetDoctorId = replacement.Id;
        var otherDoctorId = otherDoctor.Id;
        var reports = new AdminReportService(db);
        var staff = new StaffService(db, new DoctorService(db, clock), clock);
        var service = new ScheduleChangeService(db, clock);

        var from = new DateOnly(2026, 11, 5);
        var report = await reports.GetRevenueAsync(actor, from, from);
        Assert.Equal(200m, report.Deposits);
        Assert.Equal(500m, report.Payments);
        Assert.Equal(50m, report.Refunds);
        Assert.Equal(650m, report.Revenue);
        Assert.Equal(2, report.Visits);
        Assert.Single(report.Days);
        var csv = await reports.ExportRevenueAsync(actor, from, from);
        Assert.Equal(new byte[] { 239, 187, 191 }, csv.Take(3));
        Assert.Contains("650.00,2", Encoding.UTF8.GetString(csv));
        Assert.DoesNotContain("QA Patient", Encoding.UTF8.GetString(csv));
        await Assert.ThrowsAsync<AdminOperationException>(() => reports.GetRevenueAsync(nurseId, from, from));
        await Assert.ThrowsAsync<AdminOperationException>(() => reports.GetRevenueAsync(actor, from, from.AddDays(-1)));
        await Assert.ThrowsAsync<AdminOperationException>(() => reports.GetRevenueAsync(actor, from, from.AddDays(366)));

        var newNurse = await staff.CreateAdminAccountAsync(actor, new CreateAdminAccountRequest
        {
            Username = "qa_new_nurse",
            Password = "QA_test_password_2026!",
            FullName = "QA New Nurse",
            RoleId = 4,
            DepartmentId = departmentId
        });
        Assert.True(await db.Nurses.AnyAsync(person => person.AccountId == newNurse && person.DepartmentId == departmentId));
        Assert.True(BCrypt.Net.BCrypt.Verify("QA_test_password_2026!", (await db.Accounts.FindAsync(newNurse))!.PasswordHash));
        await Assert.ThrowsAsync<AdminOperationException>(() => staff.CreateAdminAccountAsync(actor, new CreateAdminAccountRequest
        {
            Username = "QA_NEW_NURSE",
            Password = "QA_test_password_2026!",
            FullName = "Duplicate",
            RoleId = 4,
            DepartmentId = departmentId
        }));
        var accountsBefore = await db.Accounts.CountAsync();
        await Assert.ThrowsAsync<AdminOperationException>(() => staff.CreateAdminAccountAsync(actor, new CreateAdminAccountRequest
        {
            Username = "qa_bad_dept",
            Password = "QA_test_password_2026!",
            FullName = "Bad Dept",
            RoleId = 3,
            DepartmentId = int.MaxValue
        }));
        Assert.Equal(accountsBefore, await db.Accounts.CountAsync());
        var head = await staff.CreateAdminAccountAsync(actor, new CreateAdminAccountRequest
        {
            Username = "qa_new_head",
            Password = "QA_test_password_2026!",
            FullName = "QA Head",
            RoleId = 2,
            DepartmentId = departmentId
        });
        Assert.True(await db.DepartmentHeads.AnyAsync(person => person.AccountId == head && person.DepartmentId == departmentId));
        await Assert.ThrowsAsync<AdminOperationException>(() => staff.SetAccountActiveAsync(actor, actor, false));
        await staff.SetAccountActiveAsync(actor, newNurse, false);
        Assert.False(await staff.IsSessionActiveAsync(newNurse, "NURSE"));
        await staff.SetAccountActiveAsync(actor, newNurse, true);
        Assert.True(await staff.IsSessionActiveAsync(newNurse, "NURSE"));
        await Assert.ThrowsAsync<AdminOperationException>(() => staff.SetAccountActiveAsync(nurseId, newNurse, false));
        Assert.DoesNotContain(await staff.GetAdminStaffAsync(actor), person => person.Role is "PATIENT" or "ADMIN");

        var departments = new DepartmentService(db);
        var added = await departments.CreateDepartmentAsync("QA New Department", "QA only");
        Assert.Contains(await departments.GetDepartmentsAsync(), row => row.Id == added.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => departments.CreateDepartmentAsync("QA New Department", null));

        Assert.Contains(await service.GetRequestsAsync(actor), row => row.Id == changeId && row.CanApprove);
        await Assert.ThrowsAsync<AdminOperationException>(() => service.ReviewAsync(nurseId, new(changeId, true, targetDoctorId)));
        await Assert.ThrowsAsync<AdminOperationException>(() => service.ReviewAsync(actor, new(changeId, true, otherDoctorId)));
        Assert.Equal(ScheduleExceptionStatus.Pending, (await db.ScheduleExceptions.AsNoTracking().SingleAsync(row => row.Id == changeId)).Status);
        var decision = await service.ReviewAsync(actor, new(changeId, true, targetDoctorId));
        Assert.Equal(new ScheduleDecisionResult(1, 1, 1), decision);
        Assert.Equal(targetDoctorId, (await db.Appointments.AsNoTracking().SingleAsync(row => row.Id == confirmedId)).DoctorId);
        var needsRebooking = await db.Appointments.AsNoTracking().SingleAsync(row => row.Id == rebookId);
        Assert.Equal(10, needsRebooking.StatusId);
        Assert.Null(needsRebooking.SlotId);
        Assert.Equal(9, (await db.Appointments.AsNoTracking().SingleAsync(row => row.Id == holdId)).StatusId);
        var blocked = await db.AppointmentSlots.AsNoTracking().SingleAsync(row => row.Id == sourceId);
        Assert.Equal(SlotStatus.Blocked, blocked.Status);
        Assert.Equal(0, blocked.BookedCount);
        Assert.Equal(1, await db.AppointmentSlots.Where(row => row.DoctorId == targetDoctorId).SumAsync(row => row.BookedCount));
        Assert.Equal(3, await db.Notifications.CountAsync());
        await Assert.ThrowsAsync<AdminOperationException>(() => service.ReviewAsync(actor, new(changeId, true, targetDoctorId)));

        var rejected = new ScheduleException
        {
            DoctorId = otherDoctorId,
            Date = new DateOnly(2026, 11, 7),
            Type = ScheduleExceptionType.Training
        };
        db.ScheduleExceptions.Add(rejected);
        await db.SaveChangesAsync();
        await service.ReviewAsync(actor, new(rejected.Id, false, null));
        Assert.Equal(ScheduleExceptionStatus.Rejected, (await db.ScheduleExceptions.AsNoTracking().SingleAsync(row => row.Id == rejected.Id)).Status);

        // On-site visits have no online slot but must still prevent changing a shift already in use.
        var onSite = new Appointment
        {
            AppointmentCode = "QA_RECEIVED_ON_SITE",
            PatientId = patient.Id,
            DoctorId = otherDoctorId,
            DepartmentId = otherDepartment.Id,
            StatusId = (int)AppointmentStatusId.CheckedIn,
            BookingChannel = BookingChannel.OnSite,
            VisitType = VisitType.WalkIn,
            CreatedAt = new DateTime(2026, 11, 8, 8, 0, 0),
            CheckedInAt = new DateTime(2026, 11, 8, 8, 0, 0)
        };
        var receivedChange = new ScheduleException
        {
            DoctorId = otherDoctorId,
            Date = new DateOnly(2026, 11, 8),
            Shift = ExamShift.Morning,
            Type = ScheduleExceptionType.Leave
        };
        db.AddRange(onSite, receivedChange);
        await db.SaveChangesAsync();
        var receivedChangeId = receivedChange.Id;
        await Assert.ThrowsAsync<AdminOperationException>(() => service.ReviewAsync(actor, new(receivedChangeId, true, null)));
        Assert.Equal(ScheduleExceptionStatus.Pending,
            (await db.ScheduleExceptions.AsNoTracking().SingleAsync(row => row.Id == receivedChangeId)).Status);
        await AdminHttpScenario.RunAsync(connection, actor, nurseId);
    }

    private static Account Account(string username, int roleId) => new()
    {
        Username = username,
        PasswordHash = "test-only",
        RoleId = roleId,
        IsActive = true
    };
}

public sealed class AdminTestClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 11, 5, 2, 0, 0, TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
}
