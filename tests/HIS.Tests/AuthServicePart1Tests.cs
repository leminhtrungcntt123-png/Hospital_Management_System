using System.Collections.Concurrent;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Security;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HIS.Tests;

public class AuthServicePart1Tests
{
    private sealed class TestDelivery : IOtpDelivery
    {
        public string Name => "test-channel";
        public List<(OtpRecipient To, string Code, DateTimeOffset ExpiresAt, string? Purpose)> Sent { get; } = [];

        public void Send(OtpRecipient to, string code, DateTimeOffset expiresAt, string? purpose = null)
        {
            Sent.Add((to, code, expiresAt, purpose));
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utc;

        public MutableTimeProvider(DateTime start)
        {
            _utc = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc));
        }

        public override DateTimeOffset GetUtcNow() => _utc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan duration) => _utc = _utc.Add(duration);
    }

    private static (AppDbContext Context, MutableTimeProvider Clock, TestDelivery Delivery, AuthService Service) CreateHarness()
    {
        var clock = new MutableTimeProvider(new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc));
        var dbName = "AuthTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new AppDbContext(options, clock);

        // Seed roles
        context.Roles.AddRange(
            new Role { Id = (int)RoleId.Admin, Code = "ADMIN", Name = "Quản trị viên" },
            new Role { Id = (int)RoleId.Doctor, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = (int)RoleId.Nurse, Code = "NURSE", Name = "Điều dưỡng" },
            new Role { Id = (int)RoleId.DepartmentHead, Code = "DEPT_HEAD", Name = "Trưởng khoa" },
            new Role { Id = (int)RoleId.Patient, Code = "PATIENT", Name = "Bệnh nhân" }
        );
        context.SaveChanges();

        var delivery = new TestDelivery();
        var otpOptions = Options.Create(new OtpOptions
        {
            CodeLength = 6,
            LifetimeMinutes = 5,
            MaxAttempts = 5,
            RevealCodeOnScreen = true
        });

        var service = new AuthService(context, new[] { delivery }, otpOptions, clock);
        return (context, clock, delivery, service);
    }

    [Fact]
    public async Task RegisterPatientAsync_CreatesOnlyAccount_WithoutVirtualPatientProfile()
    {
        var (context, _, _, service) = CreateHarness();

        var request = new RegisterPatientRequest(
            Username: "patient_new",
            Password: "Password123!",
            Email: "patient.new@example.com"
        );

        var result = await service.RegisterPatientAsync(request);

        Assert.True(result.Succeeded);
        Assert.True(result.AccountId > 0);
        Assert.Equal("patient_new", result.Username);

        // Invariant: Account ONLY
        var account = await context.Accounts.Include(a => a.Role).FirstOrDefaultAsync(a => a.Id == result.AccountId);
        Assert.NotNull(account);
        Assert.Equal("PATIENT", account.Role.Code);
        Assert.True(account.IsActive);
        Assert.Equal(0, account.FailedLoginCount);

        // Verify NO patients or profiles were auto-created
        var patientsCount = await context.Patients.CountAsync();
        var profilesCount = await context.AccountPatientProfiles.CountAsync();
        Assert.Equal(0, patientsCount);
        Assert.Equal(0, profilesCount);
    }

    [Fact]
    public async Task RegisterPatientAsync_RejectsDuplicateUsernameOrEmail()
    {
        var (_, _, _, service) = CreateHarness();

        var request1 = new RegisterPatientRequest("user_dup", "Password123!", "dup@example.com");
        var res1 = await service.RegisterPatientAsync(request1);
        Assert.True(res1.Succeeded);

        // Duplicate username
        var request2 = new RegisterPatientRequest("USER_DUP", "Password123!", "other@example.com");
        var res2 = await service.RegisterPatientAsync(request2);
        Assert.False(res2.Succeeded);
        Assert.Contains("đã được sử dụng", res2.ErrorMessage);

        // Duplicate email
        var request3 = new RegisterPatientRequest("user_other", "Password123!", "DUP@example.com");
        var res3 = await service.RegisterPatientAsync(request3);
        Assert.False(res3.Succeeded);
        Assert.Contains("đã được đăng ký", res3.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_LocksAccountAfter5FailedAttempts_AndUnlocksAfterLockDuration()
    {
        var (context, clock, _, service) = CreateHarness();

        await service.RegisterPatientAsync(new RegisterPatientRequest("lock_user", "CorrectPassword123", "lock@example.com"));

        // 4 failed attempts
        for (int i = 1; i <= 4; i++)
        {
            var fail = await service.LoginAsync("lock_user", "WrongPassword", LoginSurface.PatientPortal);
            Assert.False(fail.Succeeded);
            Assert.Contains($"Bạn còn {5 - i} lần thử", fail.ErrorMessage);
        }

        // 5th failed attempt -> locks for 15 minutes
        var lockAttempt = await service.LoginAsync("lock_user", "WrongPassword", LoginSurface.PatientPortal);
        Assert.False(lockAttempt.Succeeded);
        Assert.Contains("tạm khóa 15 phút", lockAttempt.ErrorMessage);

        // Even with correct password, login is blocked
        var blocked = await service.LoginAsync("lock_user", "CorrectPassword123", LoginSurface.PatientPortal);
        Assert.False(blocked.Succeeded);
        Assert.Contains("đang bị tạm khóa", blocked.ErrorMessage);

        // Advance clock by 16 minutes
        clock.Advance(TimeSpan.FromMinutes(16));

        // Now login with correct password succeeds
        var success = await service.LoginAsync("lock_user", "CorrectPassword123", LoginSurface.PatientPortal);
        Assert.True(success.Succeeded);
        Assert.NotNull(success.User);
        Assert.Equal("lock_user", success.User.Username);

        // FailedLoginCount is reset to 0
        var acc = await context.Accounts.FirstAsync(a => a.Username == "lock_user");
        Assert.Equal(0, acc.FailedLoginCount);
        Assert.Null(acc.LockedUntil);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidatesOldPassword_AndUpdatesHash()
    {
        var (context, _, _, service) = CreateHarness();

        var reg = await service.RegisterPatientAsync(new RegisterPatientRequest("change_user", "OldPassword123", "change@example.com"));
        Assert.True(reg.Succeeded);

        // Wrong old password
        var failOld = await service.ChangePasswordAsync(reg.AccountId, "WrongPassword", "NewPassword123");
        Assert.False(failOld.Succeeded);
        Assert.Contains("không chính xác", failOld.ErrorMessage);

        // Same password as old
        var failSame = await service.ChangePasswordAsync(reg.AccountId, "OldPassword123", "OldPassword123");
        Assert.False(failSame.Succeeded);
        Assert.Contains("không được trùng", failSame.ErrorMessage);

        // Short new password
        var failShort = await service.ChangePasswordAsync(reg.AccountId, "OldPassword123", "123");
        Assert.False(failShort.Succeeded);
        Assert.Contains("tối thiểu 6 ký tự", failShort.ErrorMessage);

        // Successful change
        var ok = await service.ChangePasswordAsync(reg.AccountId, "OldPassword123", "NewPassword123!");
        Assert.True(ok.Succeeded);

        // Old password fails to login
        var oldLogin = await service.LoginAsync("change_user", "OldPassword123", LoginSurface.PatientPortal);
        Assert.False(oldLogin.Succeeded);

        // New password succeeds
        var newLogin = await service.LoginAsync("change_user", "NewPassword123!", LoginSurface.PatientPortal);
        Assert.True(newLogin.Succeeded);
    }

    [Fact]
    public async Task PasswordResetOtp_Flow_SendsEmail_VerifiesOtp_AndResetsPassword()
    {
        var (_, clock, delivery, service) = CreateHarness();

        await service.RegisterPatientAsync(new RegisterPatientRequest("reset_user", "OriginalPass123", "reset@example.com"));

        // Request reset
        var forgotRes = await service.SendPasswordResetOtpAsync("reset_user");
        Assert.True(forgotRes.Succeeded);
        Assert.NotNull(forgotRes.DemoOtp);
        Assert.Single(delivery.Sent);

        var sentOtp = delivery.Sent[0].Code;
        Assert.Equal(forgotRes.DemoOtp, sentOtp);

        // Wrong OTP
        var wrongRes = await service.ResetPasswordWithOtpAsync("reset_user", "000000", "NewResetPass123");
        Assert.False(wrongRes.Succeeded);
        Assert.Contains("không chính xác", wrongRes.ErrorMessage);

        // Correct OTP resets password
        var okRes = await service.ResetPasswordWithOtpAsync("reset_user", sentOtp, "NewResetPass123");
        Assert.True(okRes.Succeeded);

        // Login with new password
        var loginRes = await service.LoginAsync("reset_user", "NewResetPass123", LoginSurface.PatientPortal);
        Assert.True(loginRes.Succeeded);

        // Replaying same OTP fails (already consumed)
        var replayRes = await service.ResetPasswordWithOtpAsync("reset_user", sentOtp, "AnotherPass123");
        Assert.False(replayRes.Succeeded);
    }

    [Fact]
    public async Task PasswordReset_BindsToAccountId_PreventsAttackerHijackingVictimAccount()
    {
        var (context, _, delivery, service) = CreateHarness();

        // Victim account: username = victim_user, email = victim@example.com
        var regVictim = await service.RegisterPatientAsync(new RegisterPatientRequest("victim_user", "VictimPass123", "victim@example.com"));
        Assert.True(regVictim.Succeeded);

        // Attacker attempts to register a username that collides with victim's email -> Rejected!
        var collideReg = await service.RegisterPatientAsync(new RegisterPatientRequest("victim@example.com", "AttackerPass123", "attacker@example.com"));
        Assert.False(collideReg.Succeeded);

        // Attacker account: username = attacker_user, email = attacker@example.com
        var regAttacker = await service.RegisterPatientAsync(new RegisterPatientRequest("attacker_user", "AttackerPass123", "attacker@example.com"));
        Assert.True(regAttacker.Succeeded);

        // Attacker requests OTP for their own account
        await service.SendPasswordResetOtpAsync("attacker_user");
        var attackerOtp = delivery.Sent.Last().Code;

        // Attacker resets their own password
        var resetRes = await service.ResetPasswordWithOtpAsync("attacker_user", attackerOtp, "NewAttackerPass123");
        Assert.True(resetRes.Succeeded);

        // Victim's password remains intact
        var victimLogin = await service.LoginAsync("victim_user", "VictimPass123", LoginSurface.PatientPortal);
        Assert.True(victimLogin.Succeeded);
    }

    [Fact]
    public async Task PasswordResetChallenge_ConcurrentAttempts_EnforcesMaxAttemptsUnderLock()
    {
        var (_, _, delivery, service) = CreateHarness();

        await service.RegisterPatientAsync(new RegisterPatientRequest("brute_user", "Pass123456", "brute@example.com"));
        await service.SendPasswordResetOtpAsync("brute_user");

        var otp = delivery.Sent[0].Code;
        var wrongOtp = otp == "123456" ? "654321" : "123456";

        // Run 10 simultaneous wrong OTP attempts
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            service.ResetPasswordWithOtpAsync("brute_user", wrongOtp, "HackedPass123")).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.False(r.Succeeded));

        // After max attempts, even the correct OTP is rejected
        var correctTry = await service.ResetPasswordWithOtpAsync("brute_user", otp, "HackedPass123");
        Assert.False(correctTry.Succeeded);
    }

    [Fact]
    public async Task IsSessionActiveAsync_ValidatesStatusAndLockout()
    {
        var (context, clock, _, service) = CreateHarness();

        var reg = await service.RegisterPatientAsync(new RegisterPatientRequest("active_test", "Pass123456", "active@example.com"));
        Assert.True(reg.Succeeded);

        // Active account
        Assert.True(await service.IsSessionActiveAsync(reg.AccountId, "PATIENT"));
        Assert.False(await service.IsSessionActiveAsync(reg.AccountId, "DOCTOR")); // Role mismatch

        // Deactivated account
        var acc = await context.Accounts.FirstAsync(a => a.Id == reg.AccountId);
        acc.IsActive = false;
        await context.SaveChangesAsync();
        Assert.False(await service.IsSessionActiveAsync(reg.AccountId, "PATIENT"));

        // Reactivated but locked -> Login is blocked, but existing active session is NOT revoked (CWE-645 DoS prevention)
        acc.IsActive = true;
        acc.LockedUntil = clock.GetLocalNow().DateTime.AddMinutes(10);
        await context.SaveChangesAsync();
        Assert.True(await service.IsSessionActiveAsync(reg.AccountId, "PATIENT"));

        // Session version mismatch (e.g. after password change) revokes session (CWE-613)
        int currentVersion = service.GetSessionVersion(reg.AccountId);
        Assert.True(await service.IsSessionActiveAsync(reg.AccountId, "PATIENT", currentVersion));
        Assert.False(await service.IsSessionActiveAsync(reg.AccountId, "PATIENT", currentVersion + 1));
    }
}

