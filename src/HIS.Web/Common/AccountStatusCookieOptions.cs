using System.Security.Claims;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace HIS.Web.Common;

public sealed class AccountStatusCookieOptions : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name != CookieAuthenticationDefaults.AuthenticationScheme)
        {
            return;
        }

        var previous = options.Events.OnValidatePrincipal;
        options.Events.OnValidatePrincipal = async context =>
        {
            await previous(context);
            var principal = context.Principal;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var staff = context.HttpContext.RequestServices.GetRequiredService<IStaffService>();
            var valid = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId)
                && await staff.IsSessionActiveAsync(accountId, principal.FindFirstValue(ClaimTypes.Role) ?? "",
                    context.HttpContext.RequestAborted);

            if (!valid)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    }
}
