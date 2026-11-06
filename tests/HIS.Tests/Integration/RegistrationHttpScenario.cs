using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HIS.BLL.DTOs;
using HIS.DAL;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HIS.Tests.Integration;

public static class RegistrationHttpScenario
{
    public static async Task RunAsync(string connection, RegisterPatientRequest existing)
    {
        await using (var factory = new IntegratedAuthFactory(connection, new CapturedEmailSender()))
        using (var client = Client(factory))
        {
            await AddTokenAsync(client);
            string? acknowledgement = null;
            foreach (var request in new[]
            {
                existing with { Username = "qa_review_http_new", Email = "qa_review_http_new@example.invalid" },
                existing with { Username = "qa_review_http_email_taken" },
                existing with { Email = "qa_review_http_username_taken@example.invalid" }
            })
            {
                var response = await client.PostAsJsonAsync("/Account/RegisterApi", request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("success").GetBoolean());
                acknowledgement ??= body;
                Assert.Equal(acknowledgement, body);
                Assert.DoesNotContain(request.Username, body);
                Assert.DoesNotContain(request.Email!, body);
                Assert.DoesNotContain("fullName", body);
            }
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Accounts.AnyAsync(account => account.Username == "qa_review_http_email_taken"));
            Assert.False(await db.Accounts.AnyAsync(account => account.Email == "qa_review_http_username_taken@example.invalid"));
        }

        await using (var factory = new IntegratedAuthFactory(connection, new CapturedEmailSender()))
        using (var client = Client(factory))
        {
            await AddTokenAsync(client);
            foreach (var request in new[]
            {
                existing with { Username = "qa_review_mvc_new", Email = "qa_review_mvc_new@example.invalid" },
                existing with { Username = "qa_review_mvc_email_taken" },
                existing with { Email = "qa_review_mvc_username_taken@example.invalid" }
            })
            {
                var response = await client.PostAsync("/Account/Register", Form(request));
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
                var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Account/Login"));
                Assert.Contains("Yêu cầu đăng ký đã được xử lý.", html);
                Assert.DoesNotContain("đã được đăng ký cho tài khoản khác", html);
            }
        }

        await using (var factory = new IntegratedAuthFactory(connection, new CapturedEmailSender()))
        using (var client = Client(factory))
        {
            await AddTokenAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/Account/RegisterApi", existing)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Register", Form(existing))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/Account/RegisterApi", existing)).StatusCode);
            var unused = existing with { Username = "qa_review_limited", Email = "qa_review_limited@example.invalid" };
            foreach (var response in new[]
            {
                await client.PostAsJsonAsync("/Account/RegisterApi", unused),
                await client.PostAsync("/Account/Register", Form(unused))
            })
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                Assert.NotNull(response.Headers.RetryAfter);
                Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
            }
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Accounts.AnyAsync(account => account.Username == unused.Username));
        }

        await using (var factory = new IntegratedAuthFactory(connection, new CapturedEmailSender()))
        using (var client = Client(factory))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Account/RegisterApi", existing)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Register", Form(existing))).StatusCode);
            await AddTokenAsync(client);
            var invalid = await client.PostAsJsonAsync("/Account/RegisterApi", existing with { Password = "short" });
            Assert.False((await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        }
    }

    internal static HttpClient Client(IntegratedAuthFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    internal static async Task AddTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
    }

    private static FormUrlEncodedContent Form(RegisterPatientRequest request) => new(new Dictionary<string, string>
    {
        ["Username"] = request.Username,
        ["Password"] = request.Password,
        ["ConfirmPassword"] = request.Password,
        ["FullName"] = request.FullName,
        ["DateOfBirth"] = request.DateOfBirth.ToString("yyyy-MM-dd"),
        ["PhoneNumber"] = request.PhoneNumber,
        ["Email"] = request.Email ?? ""
    });
}
