using System.Security.Claims;
using HIS.BLL.Services;
using HIS.Web.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HIS.Tests.Unit;

public class AdminSecurityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CookieValidation_RejectsInactiveAccount(bool active)
    {
        var staff = new Mock<IStaffService>();
        staff.Setup(service => service.IsSessionActiveAsync(5, "NURSE", It.IsAny<CancellationToken>())).ReturnsAsync(active);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.SignOutAsync(It.IsAny<HttpContext>(),
            CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<AuthenticationProperties>())).Returns(Task.CompletedTask);
        using var provider = new ServiceCollection().AddSingleton(staff.Object)
            .AddSingleton(authentication.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        var options = new CookieAuthenticationOptions();
        new AccountStatusCookieOptions().PostConfigure(CookieAuthenticationDefaults.AuthenticationScheme, options);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "5"), new Claim(ClaimTypes.Role, "NURSE")], "Cookies"));
        var scheme = new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler));
        var context = new CookieValidatePrincipalContext(http, scheme, options,
            new AuthenticationTicket(principal, "Cookies"));

        await options.Events.OnValidatePrincipal(context);

        Assert.Equal(active, context.Principal != null);
        authentication.Verify(service => service.SignOutAsync(It.IsAny<HttpContext>(), "Cookies",
            It.IsAny<AuthenticationProperties>()), active ? Times.Never() : Times.Once());
    }
}
