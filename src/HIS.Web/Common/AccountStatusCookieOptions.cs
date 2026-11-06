using System.Security.Claims;
using HIS.BLL.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace HIS.Web.Common;

/// <summary>
/// Hậu kiểm trạng thái tài khoản mỗi request: tự động thu hồi phiên đăng nhập (SignOut)
/// nếu tài khoản bị khóa, vô hiệu hóa (IsActive = false) hoặc vai trò đã thay đổi.
/// </summary>
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
            if (previous != null)
            {
                await previous(context);
            }

            var principal = context.Principal;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
            var valid = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId)
                && await auth.IsSessionActiveAsync(
                    accountId,
                    principal.FindFirstValue(ClaimTypes.Role),
                    context.HttpContext.RequestAborted);

            if (!valid)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    }
}

