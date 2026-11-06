using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class AuthenticationReviewTests
{
    [Fact]
    public async Task Authentication_ProtectsTimingRegistrationAndPasswordValidation()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await AuthenticationReviewScenario.RunAsync(postgres.GetConnectionString());
    }
}

public static class AuthenticationReviewScenario
{
    public static async Task RunAsync(string connection)
    {
        var clock = new VietnamTestClock();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(options, clock);
        await db.Database.EnsureCreatedAsync();
        const string password = "QA_review_password_2026!";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var now = clock.GetLocalNow().DateTime;
        var active = new Account { Username = "qa_review_active", RoleId = 5, PasswordHash = hash };
        var inactive = new Account { Username = "qa_review_inactive", RoleId = 5, PasswordHash = hash, IsActive = false };
        var locked = new Account { Username = "qa_review_locked", RoleId = 5, PasswordHash = hash, FailedLoginCount = 5, LockedUntil = now.AddMinutes(15) };
        var legacy = new Account { Username = "qa_review_legacy", RoleId = 5, PasswordHash = "malformed-test-hash" };
        db.AddRange(active, inactive, locked, legacy);
        await db.SaveChangesAsync();
        var verifications = 0;
        var hashes = 0;
        var lastVerifiedHash = "";
        var auth = new AuthService(db, clock, (submitted, stored) =>
        {
            verifications++;
            lastVerifiedHash = stored;
            return BCrypt.Net.BCrypt.Verify(submitted, stored);
        }, submitted =>
        {
            hashes++;
            return BCrypt.Net.BCrypt.HashPassword(submitted);
        });
        foreach (var username in new[] { active.Username, inactive.Username, locked.Username, legacy.Username, "qa_review_missing" })
        {
            var before = verifications;
            var result = await auth.LoginAsync(new(username, "incorrect_password"));
            Assert.False(result.IsSuccess);
            Assert.Equal("AUTH_INVALID", result.Error.Code);
            Assert.Equal(username == legacy.Username ? 2 : 1, verifications - before);
            Assert.Equal(BCrypt.Net.BCrypt.InterrogateHash(hash).WorkFactor,
                BCrypt.Net.BCrypt.InterrogateHash(lastVerifiedHash).WorkFactor);
        }

        var request = new RegisterPatientRequest("qa_review_patient", password, "QA Review Patient",
            new DateOnly(1998, 3, 20), "0900000012", "qa_review_patient@example.invalid");
        Assert.True((await auth.RegisterPatientAsync(request)).IsSuccess);
        Assert.Equal(1, hashes);
        foreach (var duplicate in new[] { request with { Username = "qa_review_email_taken" }, request with { Email = "qa_review_other@example.invalid" } })
        {
            var before = hashes;
            Assert.Equal("AUTH_REGISTRATION_CONFLICT", (await auth.RegisterPatientAsync(duplicate)).Error.Code);
            Assert.Equal(1, hashes - before);
        }

        var gate = new RegistrationSaveGate();
        var racingOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
            .UseSnakeCaseNamingConvention().AddInterceptors(gate).Options;
        await using (var first = new AppDbContext(racingOptions, clock))
        await using (var second = new AppDbContext(racingOptions, clock))
        {
            var results = await Task.WhenAll(
                new AuthService(first, clock).RegisterPatientAsync(request with { Username = "qa_review_race_a", Email = "qa_review_race@example.invalid" }),
                new AuthService(second, clock).RegisterPatientAsync(request with { Username = "qa_review_race_b", Email = "qa_review_race@example.invalid" }));
            Assert.Single(results, result => result.IsSuccess);
            Assert.Single(results, result => !result.IsSuccess && result.Error.Code == "AUTH_REGISTRATION_CONFLICT");
        }
        Assert.Equal(1, await db.Accounts.CountAsync(account => account.Email == "qa_review_race@example.invalid"));
        Assert.Equal(2, await db.Patients.CountAsync());
        Assert.Equal(2, await db.AccountPatientProfiles.CountAsync());
        await RegistrationHttpScenario.RunAsync(connection, request);
        await PasswordChangeHttpScenario.RunAsync(connection, active.Username, password);
    }

    private sealed class RegistrationSaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }
}
