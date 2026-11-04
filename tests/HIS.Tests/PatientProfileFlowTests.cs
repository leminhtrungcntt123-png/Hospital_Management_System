using HIS.BLL.DTOs;
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
/// Luồng "cách A" trên CSDL thật: tạo hồ sơ mới -> SELF ACTIVE xem được hồ sơ;
/// xin liên kết hồ sơ đã có -> PENDING, quầy duyệt -> ACTIVE;
/// danh sách tiếp đón phải hiện "Đặt bởi" và cờ chờ duyệt liên kết.
/// DB lấy từ biến môi trường HIS_REHEARSAL_CONNECTION.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class PatientProfileFlowTests
{
    private const int OwnerAccountId = 7;
    private const int RelativeAccountId = 8;
    private const int CounterAccountId = 10;

    private readonly ITestOutputHelper _out;

    public PatientProfileFlowTests(ITestOutputHelper out1) => _out = out1;

    private static string? Connection => Environment.GetEnvironmentVariable("HIS_REHEARSAL_CONNECTION");

    private static AppDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }

    [Fact]
    public async Task Tao_ho_so_moi_va_duyen_lien_ket_theo_cach_A()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        string nationalId = $"97{Random.Shared.Next(1000000000):D9}";
        int newPatientId = 0;

        try
        {
            await using (var ctx = NewContext(cs))
            {
                var profiles = new PatientProfileService(ctx);

                // Tài khoản 7 đã có hồ sơ "Bản thân" (ràng buộc uq_link_self_account),
                // nên phần tạo mới kiểm chứng bằng hồ sơ con do chính tài khoản khai.
                ProfileLinkResult created = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test Hồ Sơ Cách A",
                    DateOfBirth = new DateOnly(1988, 1, 12),
                    Gender = "FEMALE",
                    PhoneNumber = "0912345678",
                    NationalId = nationalId,
                    Address = "Test address",
                    Relationship = "CHILD"
                });

                _out.WriteLine($"CreateProfileAsync: {created.Success} | {created.Message} | status={created.LinkStatus} | view={created.CanViewRecords}");
                Assert.True(created.Success, created.Message);
                Assert.Equal("ACTIVE", created.LinkStatus);
                Assert.True(created.CanViewRecords);
                newPatientId = created.PatientId;

                // Người thân xin liên kết đúng hồ sơ vừa tạo => PENDING, chưa được xem hồ sơ.
                ProfileLinkResult linked = await profiles.RequestLinkAsync(RelativeAccountId, nationalId, "SIBLING");
                _out.WriteLine($"RequestLinkAsync: {linked.Success} | {linked.Message} | status={linked.LinkStatus} | view={linked.CanViewRecords}");
                Assert.True(linked.Success, linked.Message);
                Assert.Equal("PENDING", linked.LinkStatus);
                Assert.False(linked.CanViewRecords);

                IReadOnlyList<PatientProfileLinkDto> pending = await profiles.GetPendingLinksAsync();
                Assert.Contains(pending, l => l.AccountId == RelativeAccountId && l.PatientId == newPatientId);

                ProfileLinkResult verified = await profiles.VerifyLinkAsync(RelativeAccountId, newPatientId, CounterAccountId);
                _out.WriteLine($"VerifyLinkAsync: {verified.Success} | {verified.Message} | status={verified.LinkStatus}");
                Assert.True(verified.Success, verified.Message);
                Assert.Equal("ACTIVE", verified.LinkStatus);

                IReadOnlyList<PatientProfileLinkDto> ownerLinks = await profiles.GetLinksForAccountAsync(OwnerAccountId);
                Assert.Contains(ownerLinks, l => l.PatientId == newPatientId && l.RelationshipCode == "CHILD");

                // uq_link_self_account: một tài khoản chỉ một hồ sơ "Bản thân" còn hiệu lực.
                ProfileLinkResult secondSelf = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test Bản Thân Thứ Hai",
                    DateOfBirth = new DateOnly(2001, 3, 3),
                    Gender = "MALE",
                    PhoneNumber = "0912340001",
                    NationalId = $"96{Random.Shared.Next(1000000000):D9}",
                    Relationship = "SELF"
                });
                _out.WriteLine($"CreateProfileAsync SELF lần hai: {secondSelf.Success} | {secondSelf.Message}");
                Assert.False(secondSelf.Success);
                Assert.Equal(0, secondSelf.PatientId);

                await using var audit = NewContext(cs);
                AccountPatientProfile link = await audit.AccountPatientProfiles
                    .AsNoTracking()
                    .SingleAsync(l => l.AccountId == RelativeAccountId && l.PatientId == newPatientId);

                Assert.NotNull(link.VerifiedBy);
                Assert.Equal(CounterAccountId, link.VerifiedBy);
                Assert.NotNull(link.VerifiedAt);

                // Mỗi lần đọc hồ sơ của người khác phải để lại dấu vết trong access_logs.
                var logs = await audit.AccessLogs
                    .AsNoTracking()
                    .Where(l => l.PatientId == newPatientId)
                    .Select(l => l.Action)
                    .ToListAsync();
                _out.WriteLine($"access_logs cho hồ sơ mới: {string.Join(", ", logs)}");
                Assert.Contains("CREATE_PROFILE", logs);
                Assert.Contains("LINK_VERIFY", logs);
            }
        }
        finally
        {
            await CleanupAsync(cs, newPatientId);
        }
    }

    [Fact]
    public async Task Danh_sach_tiep_don_hien_dat_boi_va_co_co_duyet_lien_ket()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        await using var ctx = NewContext(cs);
        var bookingOptions = Options.Create(new BookingOptions());
        var reception = new ReceptionService(ctx, new QueuePositionAllocator(ctx, bookingOptions, TimeProvider.System), bookingOptions, TimeProvider.System);
        var profiles = new PatientProfileService(ctx);

        IReadOnlyList<ReceptionItemDto> firstLoad = await reception.GetReceptionListAsync(ReceptionTab.Waiting);
        if (firstLoad.Count == 0)
        {
            _out.WriteLine("Bỏ qua: DB này chưa có lượt chờ tiếp nhận trong ngày.");
            return;
        }

        int[] alreadyLinked = await ctx.AccountPatientProfiles
            .AsNoTracking()
            .Where(l => l.AccountId == RelativeAccountId)
            .Select(l => l.PatientId)
            .ToArrayAsync();

        ReceptionItemDto? target = firstLoad.FirstOrDefault(i => !alreadyLinked.Contains(i.PatientId));
        if (target is null)
        {
            _out.WriteLine("Bỏ qua: mọi lượt chờ hôm nay đều đã liên kết với tài khoản thử nghiệm.");
            return;
        }
        _out.WriteLine($"Trước khi gửi liên kết: #{target.QueueNumber} {target.PatientName} | đặt bởi: {target.BookedByLabel} | chờ duyệt: {target.HasPendingLink}");
        Assert.False(target.HasPendingLink);

        int linkedPatientId = target.PatientId;
        try
        {
            ProfileLinkResult pending = await profiles.RequestLinkAsync(RelativeAccountId, target.PatientCode, "OTHER");
            Assert.True(pending.Success, pending.Message);
            Assert.Equal("PENDING", pending.LinkStatus);

            IReadOnlyList<ReceptionItemDto> waiting = await reception.GetReceptionListAsync(ReceptionTab.Waiting);
            ReceptionItemDto row = Assert.Single(waiting, i => i.AppointmentId == target.AppointmentId);

            _out.WriteLine($"Sau khi gửi liên kết: #{row.QueueNumber} {row.PatientName} | đặt bởi: {row.BookedByLabel} | chờ duyệt: {row.HasPendingLink}");
            Assert.True(row.HasPendingLink);
            Assert.NotEmpty(row.BookedByLabel);
        }
        finally
        {
            await CleanupAsync(cs, linkedPatientId);
        }
    }

    /// <summary>Xóa hồ sơ do test tạo theo thứ tự ngược của FK; không đụng lượt khám của các hồ sơ thật.</summary>
    private static async Task CleanupAsync(string connectionString, int patientId)
    {
        if (patientId == 0)
        {
            return;
        }

        await using var ctx = NewContext(connectionString);
        int[] ids = [patientId];

        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM access_logs WHERE patient_id = ANY ({0})", [ids]);

        bool hasAppointment = await ctx.Appointments.AnyAsync(a => a.PatientId == patientId);
        if (hasAppointment)
        {
            // Hồ sơ đã có lịch hẹn thật (nhánh duyệt liên kết): chỉ gỡ liên kết test, giữ hồ sơ.
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM account_patient_profiles WHERE patient_id = ANY ({0}) AND account_id = {1}",
                [ids, RelativeAccountId]);
            return;
        }

        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM account_patient_profiles WHERE patient_id = ANY ({0})", [ids]);
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM insurance_cards WHERE patient_id = ANY ({0})", [ids]);
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM patients WHERE id = ANY ({0})", [ids]);
    }
}
