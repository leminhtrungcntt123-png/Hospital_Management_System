using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Xunit;

namespace HIS.Tests.Integration;

public static class PasswordRecoveryHttpScenario
{
    public static async Task RunAsync(string connection, string username, string email, string inactive, string noEmail)
    {
        var blockedMail = new ControlledRecoveryEmail();
        string acknowledgement;
        await using (var factory = new IntegratedAuthFactory(connection, blockedMail))
        using (var client = CreateClient(factory))
        {
            await AddTokenAsync(client);
            try
            {
                // The public response must finish even while SMTP is still blocked.
                var known = await RequestAsync(client, username).WaitAsync(TimeSpan.FromSeconds(10));
                await blockedMail.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                acknowledgement = await known.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, known.StatusCode);
                Assert.True(known.Headers.CacheControl?.NoStore);
                Assert.False(blockedMail.Release.Task.IsCompleted);
                Assert.DoesNotContain("maskedEmail", acknowledgement);
                Assert.DoesNotContain(email, acknowledgement);
                Assert.DoesNotContain(username, acknowledgement);
                foreach (var identifier in new[] { "qa_unknown_" + Guid.NewGuid().ToString("N"), inactive })
                {
                    var response = await RequestAsync(client, identifier);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Equal(acknowledgement, await response.Content.ReadAsStringAsync());
                }
            }
            finally
            {
                blockedMail.Release.TrySetResult();
            }
        }

        var failingMail = new Mock<IEmailSender>();
        failingMail.Setup(sender => sender.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic SMTP private details"));
        await using (var factory = new IntegratedAuthFactory(connection, failingMail.Object))
        using (var client = CreateClient(factory))
        {
            await AddTokenAsync(client);
            foreach (var identifier in new[] { noEmail, email })
            {
                var response = await RequestAsync(client, identifier);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(acknowledgement, await response.Content.ReadAsStringAsync());
            }
            string? failure = null;
            foreach (var identifier in new[] { username, inactive, "qa_unknown_" + Guid.NewGuid().ToString("N"), noEmail })
            {
                var response = await ConfirmAsync(client, identifier);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("success").GetBoolean());
                failure ??= body;
                Assert.Equal(failure, body);
            }
        }

        var deliveredMail = new CapturedEmailSender();
        await using (var factory = new IntegratedAuthFactory(connection, deliveredMail))
        using (var client = CreateClient(factory))
        {
            await AddTokenAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await RequestAsync(client, username)).StatusCode);
            var code = await deliveredMail.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            const string password = "QA_http_reset_password_2026!";
            var payload = new { emailOrUsername = email, otp = code, newPassword = password };
            var changed = await client.PostAsJsonAsync("/Account/ConfirmPasswordReset", payload);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.True((await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
            var replay = await client.PostAsJsonAsync("/Account/ConfirmPasswordReset", payload);
            Assert.False((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
            var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = username,
                ["Password"] = password
            }));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal("/Admin/Management", login.Headers.Location?.OriginalString);
        }

        await using (var factory = new IntegratedAuthFactory(connection, new CapturedEmailSender()))
        using (var client = CreateClient(factory))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RequestAsync(client, username)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(client, username)).StatusCode);
        }

        await using (var factory = new IntegratedAuthFactory(connection, new CapturedEmailSender()))
        using (var client = CreateClient(factory))
        {
            await AddTokenAsync(client);
            for (var attempt = 0; attempt < 3; attempt++)
                Assert.Equal(HttpStatusCode.OK, (await RequestAsync(client, "request_" + attempt)).StatusCode);
            await AssertLimitedAsync(await RequestAsync(client, "different_identifier"));
            for (var attempt = 0; attempt < 10; attempt++)
                Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(client, "confirm_" + attempt)).StatusCode);
            await AssertLimitedAsync(await ConfirmAsync(client, "another_identifier"));
        }
    }

    private static HttpClient CreateClient(IntegratedAuthFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    private static async Task AddTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string identifier) =>
        client.PostAsJsonAsync("/Account/RequestPasswordReset", new { emailOrUsername = identifier });

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string identifier) =>
        client.PostAsJsonAsync("/Account/ConfirmPasswordReset", new { emailOrUsername = identifier, otp = "000000", newPassword = "QA_recovery_password_2026!" });

    private static async Task AssertLimitedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
    }
}
