using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace HIS.Tests.Integration;

public static class PasswordChangeHttpScenario
{
    public static async Task RunAsync(string connection, string username, string password)
    {
        await using var factory = new IntegratedAuthFactory(connection, new CapturedEmailSender());
        using var client = RegistrationHttpScenario.Client(factory);
        await RegistrationHttpScenario.AddTokenAsync(client);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var page = await client.GetStringAsync("/Account/ChangePassword");
        Assert.Contains("jquery.validate.min.js", page);
        Assert.Contains("jquery.validate.unobtrusive.min.js", page);
        var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);

        foreach (var (oldPassword, newPassword, field, expected) in new[]
        {
            (password, "short", "NewPassword", "Mật khẩu mới từ 6 đến 72 ký tự"),
            (password, new string('x', 73), "NewPassword", "Mật khẩu mới từ 6 đến 72 ký tự"),
            ("", "QA_new_password_2026!", "OldPassword", "Vui lòng nhập mật khẩu hiện tại")
        })
        {
            var response = await client.PostAsync("/Account/ChangePassword", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["OldPassword"] = oldPassword,
                ["NewPassword"] = newPassword
            }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            var error = Regex.Match(html, "<span[^>]*data-valmsg-for=\"" + field + "\"[^>]*>(.*?)</span>", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains(expected, error);
        }

        var valid = await client.PostAsync("/Account/ChangePassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OldPassword"] = password,
            ["NewPassword"] = "QA_changed_password_2026!"
        }));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Equal("/Account/Login", valid.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/ChangePassword")).StatusCode);
        await RegistrationHttpScenario.AddTokenAsync(client);
        var newLogin = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "QA_changed_password_2026!"
        }));
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
        Assert.Equal("/Patient/Booking", newLogin.Headers.Location?.OriginalString);
        foreach (var returnUrl in new[] { "/Account/Logout", "/account/logout", "~/Account/Logout" })
        {
            var signedInRedirect = await client.GetAsync("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
            Assert.Equal(HttpStatusCode.Redirect, signedInRedirect.StatusCode);
            Assert.Equal("/Patient/Booking", signedInRedirect.Headers.Location?.OriginalString);
        }

        using var expired = RegistrationHttpScenario.Client(factory);
        var logout = await expired.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("ReturnUrl=", logout.Headers.Location?.OriginalString);
        await RegistrationHttpScenario.AddTokenAsync(expired);
        var relogin = await expired.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "QA_changed_password_2026!",
            ["ReturnUrl"] = "/Account/Logout"
        }));
        Assert.Equal(HttpStatusCode.Redirect, relogin.StatusCode);
        Assert.Equal("/Patient/Booking", relogin.Headers.Location?.OriginalString);
    }
}
