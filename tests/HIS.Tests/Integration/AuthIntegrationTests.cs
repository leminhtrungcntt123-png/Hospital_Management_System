using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HIS.BLL.DTOs;
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
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class AuthIntegrationTests
{
    [Fact]
    public async Task IntegratedAuth_ProtectsRolePagesAndResetsPasswordsThroughEmail()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await AuthIntegrationScenario.RunAsync(postgres.GetConnectionString());
    }
}

public static class AuthIntegrationScenario
{
    public const string Password = "QA_role_password_2026!";

    public static async Task RunAsync(string connection)
    {
        var clock = new VietnamTestClock();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options, clock);
        await db.Database.EnsureCreatedAsync();
        var admin = new Account
        {
            Username = "qa_matrix_admin",
            Email = "qa_admin@example.invalid",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4),
            RoleId = 1
        };
        var department = new Department { Name = "QA Role Matrix" };
        db.AddRange(admin, department);
        await db.SaveChangesAsync();
        var adminId = admin.Id;
        var departmentId = department.Id;
        var staff = new StaffService(db, new DoctorService(db, clock), clock);
        foreach (var role in new[] { (Name: "nurse", Id: 4), (Name: "doctor", Id: 3), (Name: "head", Id: 2) })
        {
            await staff.CreateAdminAccountAsync(adminId, new CreateAdminAccountRequest
            {
                Username = "qa_matrix_" + role.Name,
                Password = Password,
                FullName = "QA " + role.Name,
                RoleId = role.Id,
                DepartmentId = departmentId
            });
        }
        var email = new CapturedEmailSender();
        var auth = new AuthService(db, clock, email);
        var registration = new RegisterPatientRequest("qa_matrix_patient", Password, "QA Patient",
            new DateOnly(1998, 3, 20), "0900000001", "qa_patient@example.invalid");
        Assert.False((await auth.RegisterPatientAsync(registration with { Username = "qa_missing_profile", DateOfBirth = default })).IsSuccess);
        Assert.True((await auth.RegisterPatientAsync(registration)).IsSuccess);
        Assert.True((await auth.LoginAsync(new("QA_PATIENT@EXAMPLE.INVALID", Password))).IsSuccess);
        Assert.Equal(5, (await db.Accounts.AsNoTracking().SingleAsync(account => account.Username == registration.Username)).RoleId);

        foreach (var role in new[]
        {
            (Name: "admin", Home: "/Admin/Management", Forbidden: "/Staff/Consultation"),
            (Name: "nurse", Home: "/Staff/Reception", Forbidden: "/Staff/Consultation"),
            (Name: "doctor", Home: "/Staff/Consultation", Forbidden: "/Admin/Management"),
            (Name: "head", Home: "/Staff/Schedule", Forbidden: "/Patient/MedicalRecords"),
            (Name: "patient", Home: "/Patient/Booking", Forbidden: "/Staff/Reception")
        })
        {
            await using var factory = new IntegratedAuthFactory(connection, email);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            var home = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            var page = await client.GetStringAsync("/Account/Login");
            Assert.DoesNotContain("ui-preview-toolbar", page);
            Assert.DoesNotContain("function fill(", page);
            var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = "qa_matrix_" + role.Name,
                ["Password"] = Password,
                ["RememberMe"] = "true",
                ["__RequestVerificationToken"] = Token(page)
            }));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal(role.Home, login.Headers.Location?.OriginalString);
            var ownPage = await client.GetAsync(role.Home);
            Assert.Equal(HttpStatusCode.OK, ownPage.StatusCode);
            var ownHtml = await ownPage.Content.ReadAsStringAsync();
            Assert.DoesNotContain("ui-preview-toolbar", ownHtml);
            Assert.DoesNotContain("demo/seed.js", ownHtml);
            Assert.DoesNotContain("demo/workflows.js", ownHtml);
            Assert.DoesNotContain("database-bridge.js", ownHtml);
            if (role.Name == "nurse")
            {
                Assert.Contains("id=\"reception-sidebar\"", ownHtml);
                Assert.Contains("QA Role Matrix", ownHtml);
                Assert.Contains("/Staff/Cashier", ownHtml);
                var invalidWalkIn = await client.PostAsync("/Staff/Reception/WalkIn", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["WalkIn.ScheduleId"] = "0",
                    ["__RequestVerificationToken"] = Token(ownHtml)
                }));
                Assert.Equal(HttpStatusCode.OK, invalidWalkIn.StatusCode);
                Assert.Contains("id=\"reception-sidebar\"", await invalidWalkIn.Content.ReadAsStringAsync());
            }
            var denied = await client.GetAsync(role.Forbidden);
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/Account/AccessDenied", denied.Headers.Location?.OriginalString);
            if (role.Name is "doctor" or "nurse" or "head")
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Staff/Schedule")).StatusCode);
            }
            if (role.Name == "doctor")
            {
                foreach (var screen in new[] { "labs", "records", "exceptions", "patients" })
                {
                    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Workspace/staff/" + screen)).StatusCode);
                }
                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Workspace/staff/vitals")).StatusCode);
            }
            var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Token(await ownPage.Content.ReadAsStringAsync())
            }));
            Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(role.Home)).StatusCode);
        }

        await using (var publicFactory = new IntegratedAuthFactory(connection, email))
        using (var publicClient = publicFactory.CreateClient())
        {
            var catalogResponse = await publicClient.GetAsync("/Home/Catalog");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            var catalogJson = await catalogResponse.Content.ReadAsStringAsync();
            Assert.Contains("QA doctor", catalogJson);
            Assert.Contains("QA Role Matrix", catalogJson);
            Assert.DoesNotContain("passwordHash", catalogJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("QA Patient", catalogJson);
        }

        Assert.True((await auth.SendPasswordResetOtpAsync(registration.Email!)).IsSuccess);
        var otp = email.LastOtp;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.False((await auth.ResetPasswordWithOtpAsync(attempt % 2 == 0 ? registration.Email! : registration.Username,
                otp == "000000" ? "111111" : "000000", Password)).IsSuccess);
        }
        Assert.False((await auth.ResetPasswordWithOtpAsync(registration.Username, otp, Password)).IsSuccess);
        Assert.True((await auth.SendPasswordResetOtpAsync(registration.Username)).IsSuccess);
        const string newPassword = "QA_reset_password_2026!";
        var newOtp = email.LastOtp;
        Assert.True((await auth.ResetPasswordWithOtpAsync(registration.Email!, newOtp, newPassword)).IsSuccess);
        Assert.False((await auth.ResetPasswordWithOtpAsync(registration.Username, newOtp, Password)).IsSuccess);
        Assert.True((await auth.LoginAsync(new(registration.Username, newPassword))).IsSuccess);
        Assert.False((await new AuthService(db, clock).SendPasswordResetOtpAsync(registration.Email!)).IsSuccess);
    }

    private static string Token(string html)
    {
        var token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        return token;
    }
}

public sealed class CapturedEmailSender : IEmailSender
{
    public string LastOtp { get; private set; } = "";
    public Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        LastOtp = Regex.Match(body, @"(?<!\d)\d{6}(?!\d)").Value;
        return Task.CompletedTask;
    }
}

public sealed class IntegratedAuthFactory(string connection, CapturedEmailSender email) : WebApplicationFactory<AccountController>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var root = new DirectoryInfo(Environment.CurrentDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "HIS.sln"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Run integration checks from the solution directory.");
        builder.UseContentRoot(Path.Combine(root.FullName, "src", "HIS.Web"));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Seed:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Error" }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new VietnamTestClock());
            services.RemoveAll<AppDbContext>();
            services.AddScoped(provider => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options, provider.GetRequiredService<TimeProvider>()));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(email);
        });
    }
}

public sealed class VietnamTestClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 11, 6, 2, 0, 0, TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
}
