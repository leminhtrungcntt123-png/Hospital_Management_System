using HIS.BLL.Common;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class PasswordRecoveryTests
{
    [Fact]
    public async Task Recovery_EnforcesConcurrentAttemptLimitsAndProtectsPublicResponses()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await PasswordRecoveryScenario.RunAsync(postgres.GetConnectionString());
    }
}

public static class PasswordRecoveryScenario
{
    public static async Task RunAsync(string connection)
    {
        var clock = new RecoveryClock();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(options, clock);
        await db.Database.EnsureCreatedAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var account = new Account { Username = "qa_recovery_" + suffix, Email = suffix + "@example.invalid", RoleId = 1, PasswordHash = BCrypt.Net.BCrypt.HashPassword("QA_old_password_2026!", 4) };
        var inactive = new Account { Username = "qa_inactive_" + suffix, Email = "inactive_" + suffix + "@example.invalid", RoleId = 5, PasswordHash = "test-only", IsActive = false };
        var noEmail = new Account { Username = "qa_no_email_" + suffix, RoleId = 5, PasswordHash = "test-only" };
        db.AddRange(account, inactive, noEmail);
        await db.SaveChangesAsync();
        var mail = new CapturedEmailSender();
        var auth = new AuthService(db, clock, mail);
        const string newPassword = "QA_recovery_password_2026!";

        async Task<Result> Attempt(string identifier, string code)
        {
            await using var attemptDb = new AppDbContext(options, clock);
            return await new AuthService(attemptDb, clock, mail).ResetPasswordWithOtpAsync(identifier, code, newPassword);
        }

        Assert.True((await auth.SendPasswordResetOtpAsync(account.Username)).IsSuccess);
        var code = mail.LastOtp;
        var wrong = code == "000000" ? "111111" : "000000";
        var guesses = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(index => Attempt(index % 2 == 0 ? account.Username : account.Email!, wrong)));
        Assert.All(guesses, result => Assert.Equal("AUTH_INVALID_RESET", result.Error.Code));
        Assert.False((await Attempt(account.Email!, code)).IsSuccess);

        await auth.SendPasswordResetOtpAsync(account.Email!);
        var freshCode = mail.LastOtp;
        var simultaneous = await Task.WhenAll(Attempt(account.Username, freshCode), Attempt(account.Email!, freshCode));
        Assert.Single(simultaneous, result => result.IsSuccess);
        Assert.False((await Attempt(account.Username, freshCode)).IsSuccess);
        var stored = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, stored.PasswordHash));

        await auth.SendPasswordResetOtpAsync(account.Username);
        var expiredCode = mail.LastOtp;
        clock.Advance(TimeSpan.FromMinutes(5));
        var expired = await Attempt(account.Username, expiredCode);
        var unknown = await Attempt("qa_unknown_" + suffix, expiredCode);
        var disabled = await Attempt(inactive.Username, expiredCode);
        var missing = await Attempt(noEmail.Username, expiredCode);
        Assert.False(expired.IsSuccess);
        Assert.Equal(expired.Error, unknown.Error);
        Assert.Equal(expired.Error, disabled.Error);
        Assert.Equal(expired.Error, missing.Error);

        var controlledMail = new ControlledRecoveryEmail();
        await using (var firstDb = new AppDbContext(options, clock))
        await using (var secondDb = new AppDbContext(options, clock))
        {
            var firstSend = new AuthService(firstDb, clock, controlledMail).SendPasswordResetOtpAsync(account.Username);
            await controlledMail.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await new AuthService(secondDb, clock, controlledMail).SendPasswordResetOtpAsync(account.Email!);
            }
            finally
            {
                controlledMail.Release.TrySetResult();
            }
            Assert.True((await firstSend).IsSuccess);
        }
        Assert.True((await Attempt(account.Username, controlledMail.LastOtp)).IsSuccess);

        await PasswordRecoveryHttpScenario.RunAsync(connection, account.Username, account.Email!, inactive.Username, noEmail.Username);
    }
}

public sealed class RecoveryClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 6, 2, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan duration) => _now += duration;
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
}
