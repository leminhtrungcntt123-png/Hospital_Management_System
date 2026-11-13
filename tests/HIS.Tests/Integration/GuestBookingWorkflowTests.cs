using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using HIS.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class GuestBookingWorkflowTests
{
    [Fact]
    public async Task Guests_book_real_slots_without_exceeding_capacity()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        await GuestBookingScenario.RunAsync(container.GetConnectionString());
    }
}

public static class GuestBookingScenario
{
    public static async Task RunAsync(string connection)
    {
        var clock = new GuestClock();
        var config = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(config, clock);
        await db.Database.EnsureCreatedAsync();
        var department = new Department { Name = "Guest booking test department" };
        Doctor Doctor(string name) => new() { Department = department, FullName = name, Account = new Account { Username = name.Replace(" ", "_"), PasswordHash = "test-only", RoleId = 3, IsActive = true } };
        var doctor = Doctor("Test Doctor");
        var raceDoctor = Doctor("Test Race Doctor");
        var httpDoctor = Doctor("Test HTTP Doctor");
        DoctorSchedule Schedule(Doctor d, int quota) => new()
        {
            Doctor = d,
            Department = department,
            Room = new ClinicRoom
            {
                Department = department,
                RoomName = "Test room",
                RoomNumber = d.FullName.Replace(" ", "")
            },
            WorkDate = new DateOnly(2026, 11, 12),
            Status = DoctorScheduleStatus.Approved,
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 30),
            EndTime = new TimeOnly(8, 30),
            SlotDurationMinutes = 15,
            OnlineSlots = quota,
            MaxPatients = 4
        };
        var schedule = Schedule(doctor, 2);
        var raceSchedule = Schedule(raceDoctor, 1);
        var httpSchedule = Schedule(httpDoctor, 2);
        var nurseAccount = new Account { Username = "test_guest_nurse", PasswordHash = "test-only", RoleId = 4, IsActive = true };
        db.AddRange(schedule, raceSchedule, httpSchedule, new Nurse { Account = nurseAccount, Department = department, FullName = "Test Nurse" });
        await db.SaveChangesAsync();
        var service = new GuestBookingService(db, clock, Options.Create(new BookingOptions()));
        var date = new DateOnly(2026, 11, 12);
        var slots = await service.GetSlotsAsync(doctor.Id, date);
        Assert.Equal(2, slots.Value!.Count);
        Assert.False((await service.GetSlotsAsync(doctor.Id, date.AddDays(-1))).IsSuccess);
        Assert.False((await service.GetSlotsAsync(doctor.Id, date.AddDays(15))).IsSuccess);
        Assert.Empty((await service.GetSlotsAsync(-1, date)).Value ?? []);
        GuestBookingRequest Request(int scheduleId, string phone, string name = "Synthetic Guest") => new() { ScheduleId = scheduleId, StartTime = date.ToDateTime(new TimeOnly(7, 30)), FullName = name, DateOfBirth = new DateOnly(1995, 1, 1), PhoneNumber = phone, Gender = Gender.Other };
        var invalid = Request(schedule.Id, "bad phone");
        Assert.False((await service.ConfirmAsync(invalid)).IsSuccess);
        var invalidName = Request(schedule.Id, "0900000001", "<script>Guest</script>");
        Assert.False((await service.ConfirmAsync(invalidName)).IsSuccess);
        Assert.Empty(await db.Patients.ToListAsync());
        var first = await service.ConfirmAsync(Request(schedule.Id, "0900000001"));
        Assert.True(first.IsSuccess, first.Error.Message);
        Assert.Matches(@"^HIS-20261112-[A-Z2-9]{6}$", first.Value!.AppointmentCode);
        var visit = await db.Appointments.AsNoTracking().SingleAsync();
        Assert.Equal(2, visit.StatusId);
        Assert.Null(visit.BookedBy);
        Assert.Null(visit.QueueNumber);
        Assert.Equal(BookingChannel.Web, visit.BookingChannel);
        Assert.Single(await db.AppointmentStatusHistories.ToListAsync());
        Assert.Empty(await db.AccountPatientProfiles.ToListAsync());
        var duplicate = Request(schedule.Id, "+84900000001");
        duplicate.StartTime = duplicate.StartTime.AddMinutes(15);
        Assert.False((await service.ConfirmAsync(duplicate)).IsSuccess);
        Assert.Single(await db.Patients.ToListAsync());

        var tasks = new[] { "0900000002", "0900000003" }.Select(async phone =>
        {
            await using var concurrent = new AppDbContext(config, clock);
            return await new GuestBookingService(concurrent, clock, Options.Create(new BookingOptions())).ConfirmAsync(Request(raceSchedule.Id, phone, "Synthetic Race Guest"));
        });
        var outcomes = await Task.WhenAll(tasks);
        Assert.Single(outcomes, result => result.IsSuccess);
        var raceSlot = await db.AppointmentSlots.AsNoTracking().SingleAsync(s => s.DoctorId == raceDoctor.Id);
        Assert.Equal(1, raceSlot.BookedCount);
        Assert.Equal(SlotStatus.Full, raceSlot.Status);
        Assert.Single(await db.Appointments.Where(a => a.DoctorId == raceDoctor.Id).ToListAsync());
        Assert.Empty((await service.GetSlotsAsync(raceDoctor.Id, date)).Value!);

        var sharedDoctor = Doctor("Test Shared Slot Doctor");
        var sharedSchedule = Schedule(sharedDoctor, 4);
        sharedSchedule.SlotDurationMinutes = 30;
        db.Add(sharedSchedule);
        await db.SaveChangesAsync();
        var sharedOutcomes = await Task.WhenAll(new[] { "0900000101", "0900000102", "0900000103" }.Select(async phone =>
        {
            await using var concurrent = new AppDbContext(config, clock);
            return await new GuestBookingService(concurrent, clock, Options.Create(new BookingOptions()))
                .ConfirmAsync(Request(sharedSchedule.Id, phone, "Synthetic Shared Slot Guest"));
        }));
        Assert.Equal(2, sharedOutcomes.Count(result => result.IsSuccess));
        var sharedSlot = await db.AppointmentSlots.AsNoTracking().SingleAsync(s => s.DoctorId == sharedDoctor.Id);
        Assert.Equal(2, sharedSlot.Capacity);
        Assert.Equal(2, sharedSlot.BookedCount);
        Assert.Equal(SlotStatus.Full, sharedSlot.Status);

        var reception = new ReceptionService(db, clock, Options.Create(new BookingOptions()));
        var desk = await reception.GetDeskAsync(nurseAccount.Id, first.Value.AppointmentCode);
        Assert.Single(desk.Value!.Visits);
        Assert.Equal("O01", (await reception.CheckInAsync(nurseAccount.Id, visit.Id, true)).Value);
        db.ScheduleExceptions.Add(new ScheduleException { DoctorId = doctor.Id, Date = date, Type = ScheduleExceptionType.Leave, Status = ScheduleExceptionStatus.Approved, ApprovedBy = nurseAccount.Id, ApprovedAt = clock.GetLocalNow().DateTime });
        await db.SaveChangesAsync();
        Assert.Empty((await service.GetSlotsAsync(doctor.Id, date)).Value!);
        await VerifyHttpAsync(connection, httpDoctor.Id, Request(httpSchedule.Id, "0900000004"));
    }

    private static async Task VerifyHttpAsync(string connection, int doctorId, GuestBookingRequest request)
    {
        await using var factory = new WebApplicationFactory<BookingController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "../../../src/HIS.Web")));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:HisDb"] = connection, ["Seed:Enabled"] = "false", ["Email:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Error" }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new GuestClock());
                services.RemoveAll<AppDbContext>();
                services.AddScoped(provider => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options, provider.GetRequiredService<TimeProvider>()));
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var page = await client.GetAsync("/Booking");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Patient/Booking?doctorId=" + doctorId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Booking/Slots?doctorId={doctorId}&date=2026-11-12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/Booking/Slots?doctorId={doctorId}&date=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Booking/Confirm", request)).StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Booking/Confirm", new { fullName = "X" })).StatusCode);
        var booked = await client.PostAsJsonAsync("/Booking/Confirm", request);
        Assert.Equal(HttpStatusCode.OK, booked.StatusCode);
        Assert.Contains("appointmentCode", await booked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Booking/Confirm", request)).StatusCode);
        request.StartTime = request.StartTime.AddHours(10);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Booking/Confirm", request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/Booking/Confirm", request)).StatusCode);
    }

    private sealed class GuestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
