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
/// Nhánh "chưa từng khám" gõ email ngay trên form và mã gửi về đúng địa chỉ đó KHI hồ sơ còn chưa tồn tại
/// (bản ghi patients chỉ được tạo sau khi nhập đúng mã); nhánh "đã từng khám" gửi về email ĐÃ LƯU trong
/// patients.email, form không có ô email và không được ghi đè. Mỗi lần cấp mã để lại một dòng notifications
/// chứa email đã che nhưng KHÔNG chứa mã; tài khoản xin mã dày hơn ngưỡng thì bị chặn.
/// Liên kết PENDING thành ACTIVE qua VerifyLinkAsync ở màn "Nhập mã xác thực".
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

    private static OtpService NewOtp(AppDbContext ctx, RecordingOtpDelivery channel, OtpOptions options)
        => new(ctx, Options.Create(options), new IOtpDelivery[] { channel });

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

                // Caller không kèm email (WinForms, dữ liệu cũ) thì hồ sơ mới mang email của tài khoản tạo ra nó.
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

                OtpIssueResult issued = await otp.IssueProfileOtpAsync(OwnerAccountId, patientId);
                _out.WriteLine($"IssueProfileOtpAsync: {issued.Success} | {issued.Message} | recipient={issued.RecipientEmail} | reveal={issued.RevealCode}");
                Assert.True(issued.Success, issued.Message);
                Assert.Equal(accountEmail, issued.RecipientEmail);
                // Message có thể được render ở trang người dùng chưa chứng minh quyền nên chỉ mang email đã che.
                Assert.DoesNotContain(accountEmail, issued.Message);
                Assert.Contains(ProfileEmail.Mask(accountEmail), issued.Message);
                Assert.NotNull(issued.Code);
                Assert.Equal(6, issued.Code!.Length);
                Assert.True(issued.Code.All(char.IsDigit));

                // Kênh gửi nhận đúng mã + người nhận; hạn do OtpOptions.LifetimeMinutes quyết định.
                Assert.Single(channel.Sent);
                Assert.Equal(issued.Code, channel.Sent[0].Code);
                Assert.Equal(accountEmail, channel.Sent[0].To.Email);
                // Tiêu đề thư chỉ dùng MÃ BỆNH NHÂN: người nhận email chưa chắc là chủ hồ sơ
                // (docs/DECISIONS.md #25 - không lộ họ tên trước khi xác thực).
                Assert.Equal(saved.Id.ToPatientCode(), channel.Sent[0].To.DisplayName);
                Assert.True(channel.Sent[0].ExpiresAt > DateTimeOffset.Now);

                // Dấu vết trong DB để quầy/trợ lý tra được mà không cần log application.
                Notification note = await ctx.Notifications.AsNoTracking()
                    .Where(n => n.AccountId == OwnerAccountId && n.Title == OtpService.TraceTitle)
                    .OrderByDescending(n => n.Id)
                    .FirstAsync();
                _out.WriteLine($"notifications #{note.Id}: {note.Content}");
                // Dấu vết để counter tra được nhưng dump DB không đọc trộm được mã còn hiệu lực.
                Assert.DoesNotContain(issued.Code, note.Content);
                Assert.DoesNotContain(accountEmail, note.Content);
                Assert.Contains(ProfileEmail.Mask(accountEmail), note.Content);
                // created_at PHẢI do ứng dụng ghi: cột là "timestamp without time zone" nên nếu để Postgres
                // tự DEFAULT now() trên máy chạy UTC, hạn mức 15 phút sẽ đếm lệch hàng giờ và không bao giờ chặn.
                _out.WriteLine($"created_at của dấu vết: {note.CreatedAt:dd/MM/yyyy HH:mm:ss} | đồng hồ app: {DateTime.Now:HH:mm:ss}");
                Assert.InRange((DateTime.Now - note.CreatedAt).TotalSeconds, -5, 5);
                // Nội dung phải nói GIỜ GỬI, không phải giờ hết hạn.
                Assert.Contains($"lúc {note.CreatedAt:HH:mm}", note.Content);
                Assert.Contains("mã hết hạn lúc", note.Content);

                // Gửi lại mã: email không đổi và patients.email không bị ghi đè.
                OtpIssueResult resent = await otp.IssueProfileOtpAsync(OwnerAccountId, patientId);
                Assert.True(resent.Success, resent.Message);
                Assert.Equal(accountEmail, resent.RecipientEmail);
                saved = await ctx.Patients.AsNoTracking().SingleAsync(p => p.Id == patientId);
                Assert.Equal(accountEmail, saved.Email);
                Assert.Equal(2, channel.Sent.Count);

                // Hồ sơ không thuộc tài khoản thì không được cấp mã cho nó.
                OtpIssueResult notMine = await otp.IssueProfileOtpAsync(RelativeAccountId, patientId);
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
    /// Nhánh "chưa từng khám": cấp mã cho địa chỉ gõ trên form lúc DB chưa có hồ sơ nào.
    /// Mã đi trước, bản ghi tạo sau — đó là cách chặn hồ sơ "ma" báo lỗi mà vẫn nằm trong danh sách.
    /// </summary>
    [Fact]
    public async Task Cap_ma_khi_ho_so_chua_ton_tai_khong_tao_ra_ho_so()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        string draftEmail = TestEmail("draft");

        await using (var ctx = NewContext(cs))
        {
            var channel = new RecordingOtpDelivery();
            OtpService otp = NewOtp(ctx, channel);
            int patientsBefore = await ctx.Patients.CountAsync();

            OtpIssueResult issued = await otp.IssueNewProfileOtpAsync(OwnerAccountId, $" {draftEmail} ", "Test OTP Henh Chua Luu");
            _out.WriteLine($"IssueNewProfileOtpAsync: {issued.Success} | {issued.Message} | recipient={issued.RecipientEmail}");

            Assert.True(issued.Success, issued.Message);
            Assert.Equal(draftEmail, issued.RecipientEmail);
            Assert.DoesNotContain(draftEmail, issued.Message);
            Assert.NotNull(issued.Code);
            Assert.Equal(6, issued.Code!.Length);

            Assert.Single(channel.Sent);
            Assert.Equal(draftEmail, channel.Sent[0].To.Email);
            Assert.Equal("Test OTP Henh Chua Luu", channel.Sent[0].To.DisplayName);

            Assert.Equal(patientsBefore, await ctx.Patients.CountAsync());

            Notification note = await ctx.Notifications.AsNoTracking()
                .Where(n => n.AccountId == OwnerAccountId && n.Title == OtpService.TraceTitle)
                .OrderByDescending(n => n.Id)
                .FirstAsync();
            _out.WriteLine($"notifications #{note.Id}: {note.Content}");
            Assert.DoesNotContain(issued.Code, note.Content);
            Assert.DoesNotContain(draftEmail, note.Content);
            Assert.Contains(ProfileEmail.Mask(draftEmail), note.Content);

            await CleanupNotificationsAsync(ctx);
        }
    }

    /// <summary>
    /// Mỗi lần cấp mã đều reset lại số lần nhập, nên xin mã không giới hạn làm "5 lần sai" mất nghĩa.
    /// Ngưỡng đếm từ chính dấu vết notifications của tài khoản đó: lần thứ MaxIssuesPerWindow + 1 phải bị chặn.
    /// </summary>
    [Fact]
    public async Task Xin_ma_day_dac_hon_nguong_thi_bi_chan()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        var options = new OtpOptions { MaxIssuesPerWindow = 2, IssueWindowMinutes = 15 };
        string draftEmail = TestEmail("quota");

        await using (var ctx = NewContext(cs))
        {
            await CleanupNotificationsAsync(ctx);

            var channel = new RecordingOtpDelivery();
            OtpService otp = NewOtp(ctx, channel, options);

            for (int lan = 1; lan <= options.MaxIssuesPerWindow; lan++)
            {
                OtpIssueResult issued = await otp.IssueNewProfileOtpAsync(
                    OwnerAccountId, draftEmail, "Test OTP Henh Quota");
                Assert.True(issued.Success, $"lần {lan}: {issued.Message}");
            }

            OtpIssueResult blocked = await otp.IssueNewProfileOtpAsync(
                OwnerAccountId, draftEmail, "Test OTP Henh Quota");
            _out.WriteLine($"Lần thứ {options.MaxIssuesPerWindow + 1}: {blocked.Message}");

            Assert.False(blocked.Success);
            Assert.Contains("xin mã xác thực", blocked.Message);
            Assert.Equal(options.MaxIssuesPerWindow, channel.Sent.Count);
            Assert.Equal(
                options.MaxIssuesPerWindow,
                await ctx.Notifications.CountAsync(
                    n => n.AccountId == OwnerAccountId && n.Title == OtpService.TraceTitle));

            await CleanupNotificationsAsync(ctx);
        }
    }

    /// <summary>
    /// Hạn mức phải đếm bằng ĐỒNG HỒ ỨNG DỤNG ở cả hai đầu. Cột created_at là "timestamp without time zone"
    /// nên bản cũ để Postgres tự DEFAULT now(): máy nào có DB chạy UTC sẽ ghi dòng trông như đã cũ 7 tiếng,
    /// giới hạn 5 mã/15 phút không bao giờ kích hoạt. Test chèn đúng ngưỡng mang giờ của app
    /// (DateTime.Now - 1 phút) rồi đòi lần xin kế tiếp bị chặn.
    /// </summary>
    [Fact]
    public async Task Han_muc_dem_theo_dong_ho_ung_dung_khong_phai_dong_ho_server()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        string draftEmail = TestEmail("clock");

        await using (var ctx = NewContext(cs))
        {
            await CleanupNotificationsAsync(ctx);
            var options = new OtpOptions();

            DateTime stamped = DateTime.Now.AddMinutes(-1);
            for (int i = 1; i <= options.MaxIssuesPerWindow; i++)
            {
                ctx.Notifications.Add(new Notification
                {
                    AccountId = OwnerAccountId,
                    Title = OtpService.TraceTitle,
                    Content = $"dong mui {i} de kiem chan theo gio cua app",
                    IsRead = false,
                    CreatedAt = stamped
                });
            }
            await ctx.SaveChangesAsync();
            _out.WriteLine($"Đã chèn {options.MaxIssuesPerWindow} dòng \"{OtpService.TraceTitle}\" mang giờ app " +
                           $"{stamped:HH:mm:ss} (cùng lúc đó UTC: {DateTime.UtcNow:HH:mm:ss})");

            var channel = new RecordingOtpDelivery();
            OtpService otp = NewOtp(ctx, channel, options);

            OtpIssueResult blocked = await otp.IssueNewProfileOtpAsync(OwnerAccountId, draftEmail, "Test OTP Henh Mui Gio");
            _out.WriteLine($"Lần xin kế tiếp: {blocked.Success} | {blocked.Message}");

            Assert.False(blocked.Success);
            Assert.Contains("xin mã xác thực", blocked.Message);
            Assert.Empty(channel.Sent);

            await CleanupNotificationsAsync(ctx);
        }
    }

    /// <summary>
    /// Thiếu email thì báo rõ ràng bằng tiếng Việt thay vì âm thầm gửi vào khoảng không:
    /// hồ sơ chưa có email trong hệ thống (nhánh đã từng khám) và địa chỉ gõ sai trên form (nhánh tạo mới).
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

                // Hồ sơ không có email (tài khoản tạo ra nó cũng không có) thì không gửi được mã.
                OtpIssueResult noProfileMail = await otp.IssueProfileOtpAsync(OwnerAccountId, patientId);
                _out.WriteLine($"Hồ sơ chưa có email: {noProfileMail.Message}");
                Assert.False(noProfileMail.Success);
                Assert.Contains("chưa có email trong hệ thống", noProfileMail.Message);
                Assert.Contains("0808 0808", noProfileMail.Message);

                // Nhánh "chưa từng khám" gõ email sai: từ chối ngay cả khi hồ sơ còn chưa tồn tại.
                OtpIssueResult badDraftMail = await otp.IssueNewProfileOtpAsync(OwnerAccountId, "chưa phải email", "Test OTP Henh Sai");
                _out.WriteLine($"Email form không hợp lệ: {badDraftMail.Message}");
                Assert.False(badDraftMail.Success);
                Assert.Contains("không hợp lệ", badDraftMail.Message);

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

                OtpIssueResult issued = await otp.IssueProfileOtpAsync(RelativeAccountId, target.Id);
                _out.WriteLine($"IssueProfileOtpAsync (email đã lưu trong hồ sơ): {issued.Success} | recipient={issued.RecipientEmail}");
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
