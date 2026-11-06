using System.Net;
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
public class LoginWorkflowTests
{
    [Fact]
    public async Task Login_UsesDatabaseCredentialsAndGrantsOnlyTheStoredRole()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await LoginWorkflowScenario.RunAsync(postgres.GetConnectionString());
    }
}

public static class LoginWorkflowScenario
{
    public static async Task RunAsync(string connection)
    {
        const string password = "QA_login_password_2026!";
        var clock = new LoginClock();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new AppDbContext(options, clock);
        await db.Database.EnsureCreatedAsync();
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4);
        var admin = new Account
        {
            Username = "qa_login_admin",
            PasswordHash = hash,
            RoleId = 1
        };
        var nurse = new Account
        {
            Username = "qa_login_nurse",
            PasswordHash = hash,
            RoleId = 4
        };
        var inactive = new Account
        {
            Username = "qa_login_inactive",
            PasswordHash = hash,
            RoleId = 1
        };
        var malformed = new Account
        {
            Username = "qa_login_legacy",
            PasswordHash = "invalid-hash",
            RoleId = 1
        };
        db.AddRange(admin, nurse, inactive, malformed);
        await db.SaveChangesAsync();
        var adminId = admin.Id;
        await db.Accounts.Where(account => account.Id == inactive.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(account => account.IsActive, false));
        var auth = new AuthService(db, clock);

        Assert.False((await auth.LoginAsync(new("missing_account", password))).IsSuccess);
        Assert.False((await auth.LoginAsync(new("' OR 1=1 --", password))).IsSuccess);
        Assert.False((await auth.LoginAsync(new(inactive.Username, password))).IsSuccess);
        Assert.False((await auth.LoginAsync(new(malformed.Username, password))).IsSuccess);
        Assert.False((await auth.LoginAsync(new(admin.Username, new string('x', 73)))).IsSuccess);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.False((await auth.LoginAsync(new(admin.Username, "incorrect_password"))).IsSuccess);
        }
        var locked = await db.Accounts.AsNoTracking().SingleAsync(account => account.Id == adminId);
        Assert.Equal(5, locked.FailedLoginCount);
        Assert.NotNull(locked.LockedUntil);
        Assert.False((await auth.LoginAsync(new(admin.Username, password))).IsSuccess);
        clock.Advance(TimeSpan.FromMinutes(16));
        var success = await auth.LoginAsync(new("  QA_LOGIN_ADMIN  ", password));
        Assert.True(success.IsSuccess);
        Assert.Equal(new AuthResponse(adminId, admin.Username, "ADMIN", admin.Username), success.Value);
        var unlocked = await db.Accounts.AsNoTracking().SingleAsync(account => account.Id == adminId);
        Assert.Equal(0, unlocked.FailedLoginCount);
        Assert.Null(unlocked.LockedUntil);

        await VerifyHttpAsync(connection, clock, adminId, password);
    }

    private static async Task VerifyHttpAsync(string connection, TimeProvider clock, int adminId, string password)
    {
        var root = FindWebRoot();
        await using var factory = new WebApplicationFactory<AccountController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(root);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Error",
                    ["Seed:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
                services.RemoveAll<AppDbContext>();
                services.AddScoped(provider => new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
                        .UseSnakeCaseNamingConvention().Options,
                    provider.GetRequiredService<TimeProvider>()));
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Management")).StatusCode);
        var loginPage = await client.GetStringAsync("/Account/Login?ReturnUrl=%2FAdmin%2FManagement");
        var token = Token(loginPage);
        var values = new Dictionary<string, string>
        {
            ["Username"] = "qa_login_admin",
            ["Password"] = password,
            ["ReturnUrl"] = "/Admin/Management"
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(values))).StatusCode);
        values["__RequestVerificationToken"] = token;
        values["Password"] = "incorrect_password";
        var wrongPassword = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.False(wrongPassword.Headers.TryGetValues("Set-Cookie", out var failedCookies)
            && failedCookies.Any(cookie => cookie.Contains(".AspNetCore.Cookies=")));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Management")).StatusCode);
        values["Password"] = password;
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Admin/Management", login.Headers.Location?.OriginalString);
        var dashboard = await client.GetAsync("/Admin/Management");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Management/Directory")).StatusCode);
        var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(await dashboard.Content.ReadAsStringAsync())
        }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Management")).StatusCode);

        values["ReturnUrl"] = "https://outside.example/path";
        values["__RequestVerificationToken"] = Token(await client.GetStringAsync("/Account/Login"));
        var safeRedirect = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.Redirect, safeRedirect.StatusCode);
        Assert.Equal("/Admin/Management", safeRedirect.Headers.Location?.OriginalString);

        using var nurseClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        values["Username"] = "qa_login_nurse";
        values["ReturnUrl"] = "/Admin/Management";
        values["Role"] = "ADMIN";
        values["__RequestVerificationToken"] = Token(await nurseClient.GetStringAsync("/Account/Login"));
        var nurseLogin = await nurseClient.PostAsync("/Account/Login", new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.Redirect, nurseLogin.StatusCode);
        var denied = await nurseClient.GetAsync("/Admin/Management");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("https://localhost/Account/AccessDenied", denied.Headers.Location?.OriginalString);

        var limited = await nurseClient.PostAsync("/Account/Login", new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        await using var changed = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options, clock);
        await changed.Accounts.Where(account => account.Id == adminId)
            .ExecuteUpdateAsync(set => set.SetProperty(account => account.IsActive, false));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Management")).StatusCode);
    }

    private static string Token(string html)
    {
        var token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        return token;
    }

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HIS.sln")))
            {
                return Path.Combine(directory.FullName, "src", "HIS.Web");
            }
        }
        throw new InvalidOperationException("Run the login check from the solution directory.");
    }

    private sealed class LoginClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 5, 2, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        public void Advance(TimeSpan interval) => _now = _now.Add(interval);
    }
}
