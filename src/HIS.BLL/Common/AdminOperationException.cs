using System.ComponentModel.DataAnnotations;
using HIS.DAL;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Common;

public sealed class AdminOperationException(string message) : Exception(message);

internal static class AdminRules
{
    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new AdminOperationException(message);
        }
    }

    public static void Validate(object request)
    {
        var errors = new List<ValidationResult>();
        Require(Validator.TryValidateObject(request, new ValidationContext(request), errors, true),
            string.Join(" ", errors.Select(error => error.ErrorMessage)));
    }

    public static async Task RequireAdminAsync(AppDbContext db, int actorId, CancellationToken ct)
    {
        Require(await db.Accounts.AnyAsync(account => account.Id == actorId
            && account.IsActive && account.Role.Code == "ADMIN", ct),
            "Tài khoản không còn quyền quản trị đang hoạt động.");
    }
}
