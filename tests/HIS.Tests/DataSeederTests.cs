using System;
using System.Linq;
using System.Threading.Tasks;
using HIS.BLL.Data;
using HIS.DAL;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HIS.Tests;

public class DataSeederTests
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

    [Fact]
    public async Task SeedAsync_OnCleanDatabase_PopulatesUnifiedTestData()
    {
        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var dbName = "SeederTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = new AppDbContext(options, clock);

        // Run Seeder
        await DbSeeder.SeedAsync(context, forceFresh: true);

        // Verify Roles
        Assert.True(await context.Roles.CountAsync() >= 7);
        Assert.True(await context.Roles.AnyAsync(r => r.Code == "ADMIN"));
        Assert.True(await context.Roles.AnyAsync(r => r.Code == "DOCTOR"));
        Assert.True(await context.Roles.AnyAsync(r => r.Code == "RECEPTIONIST"));
        Assert.True(await context.Roles.AnyAsync(r => r.Code == "PATIENT"));

        // Verify Departments and Rooms
        Assert.True(await context.Departments.CountAsync() >= 5);
        Assert.True(await context.ClinicRooms.CountAsync() >= 6);

        // Verify Doctors
        Assert.True(await context.Doctors.CountAsync() >= 6);
        var cardioDoctor = await context.Doctors.FirstOrDefaultAsync(d => d.Specialty == "Tim mạch");
        Assert.NotNull(cardioDoctor);

        // Verify Accounts and BCrypt Password
        var admin = await context.Accounts.FirstOrDefaultAsync(a => a.Username == "admin");
        Assert.NotNull(admin);
        Assert.True(BCrypt.Net.BCrypt.Verify(DbSeeder.DefaultPassword, admin.PasswordHash));

        var receptionist = await context.Accounts.FirstOrDefaultAsync(a => a.Username == "receptionist");
        Assert.NotNull(receptionist);
        Assert.True(BCrypt.Net.BCrypt.Verify(DbSeeder.DefaultPassword, receptionist.PasswordHash));

        // Verify Patients (Adult & Child)
        var adultPatient = await context.Patients.FirstOrDefaultAsync(p => p.NationalId == "001190012345");
        Assert.NotNull(adultPatient);

        var childPatient = await context.Patients.FirstOrDefaultAsync(p => p.FullName == "Trần Bảo Nam");
        Assert.NotNull(childPatient);
        Assert.Null(childPatient.NationalId); // Child has no CCCD

        // Verify Slots for today
        var today = DateTime.Today;
        var todaySlots = await context.AppointmentSlots.Where(s => s.StartTime.Date == today).ToListAsync();
        Assert.NotEmpty(todaySlots);

        // Verify Doctor Schedules
        var schedules = await context.DoctorSchedules.ToListAsync();
        Assert.NotEmpty(schedules);

        // Verify Sample Appointments
        var appointments = await context.Appointments.ToListAsync();
        Assert.Equal(32, appointments.Count);
        Assert.Contains(appointments, a => a.QueueNumber == "A001");
        Assert.Contains(appointments, a => a.QueueNumber == "A017");

        // Verify that multiple rooms in the department are distributed
        var doctors = await context.Doctors.Include(d => d.Department).ToListAsync();
        var internalDocs = doctors.Where(d => d.Department.Name == "Khoa Nội tổng hợp").ToList();
        if (internalDocs.Count > 1)
        {
            var internalDocIds = internalDocs.Select(d => d.Id).ToList();
            var distinctRooms = schedules.Where(s => internalDocIds.Contains(s.DoctorId)).Select(s => s.RoomId).Distinct().Count();
            Assert.True(distinctRooms > 1, "Các bác sĩ cùng khoa phải được phân bổ các phòng khám khác nhau");
        }
    }
}
