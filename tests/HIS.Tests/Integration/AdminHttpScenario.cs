using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using HIS.Web.Areas.Admin.Controllers;
using HIS.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace HIS.Tests.Integration;

public static class AdminHttpScenario
{
    public static async Task RunAsync(string connection, int adminId, int nurseId)
    {
        await using var factory = new WebApplicationFactory<ManagementController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(FindWebRoot());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HisDb"] = connection,
                ["Seed:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Error"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new AdminTestClock());
                services.RemoveAll<AppDbContext>();
                services.AddScoped(provider => new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
                        .UseSnakeCaseNamingConvention().Options,
                    provider.GetRequiredService<TimeProvider>()));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "AdminTest";
                    options.DefaultChallengeScheme = "AdminTest";
                    options.DefaultForbidScheme = "AdminTest";
                }).AddScheme<AuthenticationSchemeOptions, AdminTestAuthentication>("AdminTest", _ => { });
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Admin/Management/Directory")).StatusCode);
        client.DefaultRequestHeaders.Add("Test-Account", nurseId.ToString());
        client.DefaultRequestHeaders.Add("Test-Role", "NURSE");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Management/Directory")).StatusCode);
        client.DefaultRequestHeaders.Remove("Test-Account");
        client.DefaultRequestHeaders.Remove("Test-Role");
        client.DefaultRequestHeaders.Add("Test-Account", adminId.ToString());
        client.DefaultRequestHeaders.Add("Test-Role", "ADMIN");

        var page = await client.GetAsync("/Admin/Management");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("admin-management.js", html);
        Assert.DoesNotContain("/ui/js/demo", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Catalog")).StatusCode);
        var directory = await client.GetAsync("/Admin/Management/Directory");
        Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        Assert.DoesNotContain("passwordHash", await directory.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Management/Report?from=2026-11-05&to=2026-11-05")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Admin/Management/Report?from=invalid")).StatusCode);

        var csv = await client.GetAsync("/Admin/Management/Revenue.csv?from=2026-11-05&to=2026-11-05");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);

        var payload = new
        {
            name = "QA HTTP Department",
            description = "Synthetic integration data"
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Admin/Management/Departments", payload)).StatusCode);
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/Admin/Management/Departments", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/css/admin-management.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/js/admin-management.js")).StatusCode);
    }

    private static string FindWebRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HIS.sln")))
                {
                    return Path.Combine(directory.FullName, "src", "HIS.Web");
                }
            }
        }

        throw new InvalidOperationException("Cannot locate HIS.sln for the integration test host.");
    }
}

public sealed class AdminTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Test-Account", out var id))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "QA Admin"),
            new Claim(ClaimTypes.Role, Request.Headers["Test-Role"].ToString())], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
