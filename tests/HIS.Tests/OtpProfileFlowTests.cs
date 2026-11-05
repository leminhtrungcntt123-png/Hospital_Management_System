using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace HIS.Tests;

/// <summary>
/// Mã OTP khi thêm hồ sơ, chạy trên CSDL thật (HIS_REHEARSAL_CONNECTION).
/// Hai nguồn email đã chốt: hồ sơ MỚI gửi về email TÀI KHOẢN đang đăng nhập (và được chép vào
/// patients.email của hồ sơ mới), hồ sơ ĐÃ TỪNG KHÁM gửi về email ĐÃ LƯU trong hồ sơ, không lấy từ
/// form và không ghi đè. Mỗi lần cấp mã để lại một dòng notifications; liên kết PENDING chỉ thành
/// ACTIVE sau khi nhập đúng mã (HoSoController gọi VerifyLinkAsync ở màn "Nhập mã xác thực").
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class OtpProfileFlowTests
{
    private const int OwnerAccountId = 7;
    private const int RelativeAccountId = 8;

    private readonly ITestOutputHelper _out;

    public OtpProfileFlowTests(ITestOutputHelper out1) => _out = out1;

    private static string? Connection => Environment.GetEnvironmentVariable("HIS_REHEARSAL_CONNECTION");

    /// <summary>Địa chỉ random để không đụng email thật của dữ liệu demo (accounts.email có UNIQUE).</summary>
    private static string TestEmail(string tag) => $"otp.{tag}.{Random.Shared.Next(1000000):D6}@example.com";

    private static AppDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }

    private static OtpService NewOtp(AppDbContext ctx, RecordingOtpDelivery channel)
        => new(ctx, Options.Create(new OtpOptions()), new IOtpDelivery[] { channel });

    /// <summary>Đặt email cho tài khoản rồi trả lại giá trị cũ khi test xong.</summary>
    private static async Task<string?> SetAccountEmailAsync(AppDbContext ctx, int accountId, string? email)
    {
        Account account = await ctx.Accounts.SingleAsync(a => a.Id == accountId);
        string? previous = account.Email;
        account.Email = email;
        await ctx.SaveChangesAsync();
        return previous;
    }

    [Fact]
    public async Task Ho_so_moi_nhan_ma_theo_email_tai_khoan_va_chep_vao_ho_so()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        string accountEmail = TestEmail("owner");
        string nationalId = $"95{Random.Shared.Next(1000000000):D9}";
        int patientId = 0;
        string? previousEmail = null;

        try
        {
            await using (var ctx = NewContext(cs))
            {
                previousEmail = await SetAccountEmailAsync(ctx, OwnerAccountId, accountEmail);
                _out.WriteLine($"accounts.email #{OwnerAccountId}: {previousEmail} -> {accountEmail}");

                var profiles = new PatientProfileService(ctx, Options.Create(new BookingOptions()));
                var channel = new RecordingOtpDelivery();
                OtpService otp = NewOtp(ctx, channel);

                // Form không còn ô email: hồ sơ mới phải tự mang email của tài khoản tạo ra nó.
                var created = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test OTP Henh Mo",
                    DateOfBirth = new DateOnly(1992, 4, 20),
                    Gender = "MALE",
                    PhoneNumber = "0987650101",
                    NationalId = nationalId,
                    Relationship = "CHILD"
                });
                Assert.True(created.Success, created.Message);
                patientId = created.PatientId;

                Patient saved = await ctx.Patients.AsNoTracking().SingleAsync(p => p.Id == patientId);
                Assert.Equal(accountEmail, saved.Email);

                OtpIssueResult issued = await otp.IssueProfileOtpAsync(OwnerAccountId, patientId, ProfileOtpSource.NewProfile);
                _out.WriteLine($"IssueProfileOtpAsync: {issued.Success} | {issued.Message} | recipient={issued.RecipientEmail} | reveal={issued.RevealCode}");
                Assert.True(issued.Success, issued.Message);
                Assert.Equal(accountEmail, issued.RecipientEmail);
                Assert.NotNull(issued.Code);
                Assert.Equal(6, issued.Code!.Length);
                Assert.True(issued.Code.All(char.IsDigit));

                // Kênh gửi nhận đúng mã + người nhận; hạn do OtpOptions.LifetimeMinutes quyết định.
                Assert.Single(channel.Sent);
                Assert.Equal(issued.Code, channel.Sent[0].Code);
                Assert.Equal(accountEmail, channel.Sent[0].To.Email);
                Assert.Equal(saved.FullName, channel.Sent[0].To.DisplayName);
                Assert.True(channel.Sent[0].ExpiresAt > DateTimeOffset.Now);

                // Dấu vết trong DB để quầy/trợ lý tra được mà không cần log application.
                Notification note = await ctx.Notifications.AsNoTracking()
                    .Where(n => n.AccountId == OwnerAccountId && n.Title == "Mã xác thực hồ sơ")
                    .OrderByDescending(n => n.Id)
                    .FirstAsync();
                _out.WriteLine($"notifications #{note.Id}: {note.Content}");
                Assert.Contains(issued.Code, note.Content);
                Assert.Contains(accountEmail, note.Content);

                // Gửi lại mã: email không đổi và patients.email không bị ghi đè.
                OtpIssueResult resent = await otp.IssueProfileOtpAsync(OwnerAccountId, patientId, ProfileOtpSource.NewProfile);
                Assert.True(resent.Success, resent.Message);
                Assert.Equal(accountEmail, resent.RecipientEmail);
                saved = await ctx.Patients.AsNoTracking().SingleAsync(p => p.Id == patientId);
                Assert.Equal(accountEmail, saved.Email);
                Assert.Equal(2, channel.Sent.Count);

                // Hồ sơ không thuộc tài khoản thì không được cấp mã cho nó.
                OtpIssueResult notMine = await otp.IssueProfileOtpAsync(RelativeAccountId, patientId, ProfileOtpSource.NewProfile);
                Assert.False(notMine.Success);
                Assert.Contains("không thuộc tài khoản", notMine.Message);

                await CleanupNotificationsAsync(ctx);
            }
        }
        finally
        {
            await CleanupCreatedProfileAsync(cs, patientId, OwnerAccountId, previousEmail);
        }
    }

    /// <summary>
    /// Thiếu email thì báo rõ ràng bằng tiếng Việt thay vì âm thầm gửi vào khoảng không:
    /// tài khoản chưa có email (nhánh hồ sơ mới) và hồ sơ chưa có email (nhánh đã từng khám).
    /// </summary>
    [Fact]
    public async Task Thieu_email_thi_khong_cap_ma_va_bao_ro_ly_do()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        int patientId = 0;
        string? previousEmail = null;

        try
        {
            await using (var ctx = NewContext(cs))
            {
                var profiles = new PatientProfileService(ctx, Options.Create(new BookingOptions()));
                var channel = new RecordingOtpDelivery();
                OtpService otp = NewOtp(ctx, channel);

                previousEmail = await SetAccountEmailAsync(ctx, OwnerAccountId, null);
                _out.WriteLine($"Xóa tạm email tài khoản #{OwnerAccountId} (giá trị cũ: {previousEmail ?? "(trống)"}).");

                var created = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test OTP Thieu Email",
                    DateOfBirth = new DateOnly(1985, 7, 1),
                    Gender = "OTHER",
                    PhoneNumber = "0987650102",
                    NationalId = $"94{Random.Shared.Next(1000000000):D9}",
                    Relationship = "CHILD"
                });
                Assert.True(created.Success, created.Message);
                patientId = created.PatientId;

                // Tài khoản không có email => hồ sơ mới cũng không có email => không gửi được mã.
                OtpIssueResult noAccountMail = await otp.IssueProfileOtpAsync(OwnerAccountId, patientId, ProfileOtpSource.NewProfile);
                _out.WriteLine($"NewProfile thiếu email: {noAccountMail.Message}");
                Assert.False(noAccountMail.Success);
                Assert.Contains("Tài khoản đăng nhập chưa có email", noAccountMail.Message);
                Assert.Contains("0808 0808", noAccountMail.Message);

                OtpIssueResult noProfileMail = await otp.IssueProfileOtpAsync(OwnerAccountId, patientId, ProfileOtpSource.ExistingProfile);
                _out.WriteLine($"ExistingProfile thiếu email: {noProfileMail.Message}");
                Assert.False(noProfileMail.Success);
                Assert.Contains("chưa có email trong hệ thống", noProfileMail.Message);

                Assert.Empty(channel.Sent);
                await CleanupNotificationsAsync(ctx);
            }
        }
        finally
        {
            await CleanupCreatedProfileAsync(cs, patientId, OwnerAccountId, previousEmail);
        }
    }

    /// <summary>
    /// Nhánh "Đã từng khám": chỉ tra được bằng mã bệnh nhân, mã OTP gửi về email đã lưu trong hồ sơ
    /// (không ghi đè), và VerifyLinkAsync - bước HoSoController chạy sau khi nhập đúng mã - mở liên kết ACTIVE.
    /// </summary>
    [Fact]
    public async Task Ho_so_da_tung_kham_tim_theo_ma_bn_va_nhan_ma_theo_email_ho_so()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        string profileEmail = TestEmail("patient");
        int linkedPatientId = 0;

        try
        {
            await using (var ctx = NewContext(cs))
            {
                var profiles = new PatientProfileService(ctx, Options.Create(new BookingOptions()));
                var channel = new RecordingOtpDelivery();
                OtpService otp = NewOtp(ctx, channel);

                Patient? target = await ctx.Patients
                    .Where(p => p.NationalId != null
                                && p.Email == null
                                && !ctx.AccountPatientProfiles.Any(l => l.AccountId == RelativeAccountId && l.PatientId == p.Id))
                    .OrderBy(p => p.Id)
                    .FirstOrDefaultAsync();

                if (target is null)
                {
                    _out.WriteLine("Bỏ qua: DB thử nghiệm không còn hồ sơ chưa liên kết với tài khoản 8.");
                    return;
                }

                target.Email = profileEmail;
                await ctx.SaveChangesAsync();
                linkedPatientId = target.Id;
                string patientCode = target.Id.ToPatientCode();

                // Không còn tra bằng CCCD/SĐT/thẻ BHYT: chỉ định danh bằng mã bệnh nhân.
                ProfileLinkResult byCccd = await profiles.RequestLinkAsync(RelativeAccountId, target.NationalId!, "OTHER");
                _out.WriteLine($"Xin liên kết bằng CCCD: {byCccd.Message}");
                Assert.False(byCccd.Success);
                Assert.Contains("mã bệnh nhân", byCccd.Message, StringComparison.OrdinalIgnoreCase);

                ProfileLinkResult byCode = await profiles.RequestLinkAsync(RelativeAccountId, patientCode, "OTHER");
                _out.WriteLine($"Xin liên kết bằng {patientCode}: {byCode.Success} | status={byCode.LinkStatus}");
                Assert.True(byCode.Success, byCode.Message);
                Assert.Equal("PENDING", byCode.LinkStatus);

                OtpIssueResult issued = await otp.IssueProfileOtpAsync(RelativeAccountId, target.Id, ProfileOtpSource.ExistingProfile);
                _out.WriteLine($"IssueProfileOtpAsync (ExistingProfile): {issued.Success} | recipient={issued.RecipientEmail}");
                Assert.True(issued.Success, issued.Message);
                Assert.Equal(profileEmail, issued.RecipientEmail);
                Assert.True(OtpCodes.Matches(issued.Code, $"  {issued.Code}  "));
                Assert.False(OtpCodes.Matches(issued.Code, "000000"));
                Assert.False(OtpCodes.Matches(issued.Code, issued.Code + "1"));

                Patient saved = await ctx.Patients.AsNoTracking().SingleAsync(p => p.Id == target.Id);
                Assert.Equal(profileEmail, saved.Email);

                ProfileLinkResult verified = await profiles.VerifyLinkAsync(RelativeAccountId, target.Id, RelativeAccountId);
                _out.WriteLine($"Sau khi nhập đúng mã: status={verified.LinkStatus} | {verified.Message}");
                Assert.True(verified.Success, verified.Message);
                Assert.Equal("ACTIVE", verified.LinkStatus);

                await CleanupNotificationsAsync(ctx);
            }
        }
        finally
        {
            await CleanupExistingLinkAsync(cs, linkedPatientId, RelativeAccountId);
        }
    }

    private static async Task CleanupNotificationsAsync(AppDbContext ctx) =>
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM notifications WHERE account_id IN ({0}, {1}) AND title = 'Mã xác thực hồ sơ'",
            [OwnerAccountId, RelativeAccountId]);

    /// <summary>Xóa hồ sơ do test tạo (kèm liên kết, log) và trả email về cho tài khoản như cũ.</summary>
    private static async Task CleanupCreatedProfileAsync(
        string? connectionString, int patientId, int accountId, string? previousAccountEmail)
    {
        if (connectionString is null)
        {
            return;
        }

        await using var ctx = NewContext(connectionString);

        Account account = await ctx.Accounts.SingleAsync(a => a.Id == accountId);
        account.Email = previousAccountEmail;
        await ctx.SaveChangesAsync();

        if (patientId == 0)
        {
            return;
        }

        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM access_logs WHERE patient_id = {0}", patientId);
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM account_patient_profiles WHERE patient_id = {0}", patientId);
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM insurance_cards WHERE patient_id = {0}", patientId);
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM patients WHERE id = {0}", patientId);
    }

    /// <summary>
    /// Nhánh "đã từng khám" đụng hồ sơ thật nên chỉ trả lại email và gỡ liên kết test, không xóa hồ sơ.
    /// </summary>
    private static async Task CleanupExistingLinkAsync(string? connectionString, int patientId, int accountId)
    {
        if (connectionString is null || patientId == 0)
        {
            return;
        }

        await using var ctx = NewContext(connectionString);

        await ctx.Database.ExecuteSqlRawAsync("UPDATE patients SET email = NULL WHERE id = {0}", patientId);
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM account_patient_profiles WHERE patient_id = {0} AND account_id = {1}", patientId, accountId);
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM access_logs WHERE patient_id = {0} AND account_id = {1} AND action IN ('LINK_REQUEST','LINK_VERIFY')",
            patientId, accountId);
    }

    private sealed record SentOtp(OtpRecipient To, string Code, DateTimeOffset ExpiresAt);

    /// <summary>Kênh giả: ghi lại những gì OtpService đẩy ra thay vì gửi mail thật.</summary>
    private sealed class RecordingOtpDelivery : IOtpDelivery
    {
        public List<SentOtp> Sent { get; } = [];
        public string Name => "kenh gia";

        public void Send(OtpRecipient to, string code, DateTimeOffset expiresAt) => Sent.Add(new SentOtp(to, code, expiresAt));
    }
}
