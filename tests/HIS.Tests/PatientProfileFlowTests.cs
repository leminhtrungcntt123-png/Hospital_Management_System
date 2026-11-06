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
                var profiles = new PatientProfileService(ctx, Options.Create(new BookingOptions()));

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
                // Nhánh "đã từng khám" chỉ tra được bằng mã bệnh nhân, không dùng CCCD.
                ProfileLinkResult linked = await profiles.RequestLinkAsync(RelativeAccountId, created.PatientCode, "SIBLING");
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

    /// <summary>
    /// Nhân chứng cho quy tắc giấy tờ trên màn "Tạo hồ sơ" (trên CSDL thật):
    /// người từ 14 tuổi bắt buộc có CCCD; trẻ nhỏ chưa có CCCD thì CCCD người thân phải khớp một hồ sơ
    /// ACTIVE của chính tài khoản; CCCD đã tồn tại thì bị đẩy sang nhánh "Đã từng khám".
    /// </summary>
    [Fact]
    public async Task Cccd_dang_dinh_va_cccd_nguoi_than_khi_tao_ho_so()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        var bookingOptions = new BookingOptions();
        int createdPatientId = 0;

        try
        {
            await using (var ctx = NewContext(cs))
            {
                var profiles = new PatientProfileService(ctx, Options.Create(bookingOptions));

                string? ownerCccd = await ctx.AccountPatientProfiles.AsNoTracking()
                    .Where(l => l.AccountId == OwnerAccountId
                                && l.Relationship == PatientRelationship.Self
                                && l.Status == LinkStatus.Active)
                    .Select(l => l.Patient.NationalId)
                    .FirstOrDefaultAsync();

                int existingLinks = await ctx.AccountPatientProfiles.AsNoTracking()
                    .CountAsync(l => l.AccountId == OwnerAccountId && l.Status != LinkStatus.Revoked);

                if (ownerCccd is null || existingLinks >= bookingOptions.MaxProfilesPerAccount)
                {
                    _out.WriteLine($"Bỏ qua: tài khoản #{OwnerAccountId} chưa có CCCD \"Bản thân\" hoặc đã dùng hết {existingLinks} hồ sơ.");
                    return;
                }

                DateOnly childDob = DateOnly.FromDateTime(DateTime.Today).AddYears(-5);

                // 1) Người lớn (>= OwnIdFromAge) mà bỏ trống CCCD thì không tạo được.
                var adultWithoutId = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test Nguoi Lon Khong CCCD",
                    DateOfBirth = new DateOnly(1990, 5, 5),
                    Gender = "MALE",
                    PhoneNumber = "0987654001",
                    Relationship = "OTHER"
                });
                _out.WriteLine($"Người lớn không CCCD: {adultWithoutId.Message}");
                Assert.False(adultWithoutId.Success);
                Assert.Contains("bắt buộc phải khai CCCD", adultWithoutId.Message);

                // 2) Trẻ 5 tuổi, chưa có CCCD, cũng không khai CCCD người thân.
                var childWithoutGuardian = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test Tre Thieu CCCD Nguoi Than",
                    DateOfBirth = childDob,
                    Gender = "FEMALE",
                    PhoneNumber = "0987654002",
                    Relationship = "CHILD"
                });
                _out.WriteLine($"Trẻ thiếu CCCD người thân: {childWithoutGuardian.Message}");
                Assert.False(childWithoutGuardian.Success);
                Assert.Contains("CCCD của người thân", childWithoutGuardian.Message);

                // 3) CCCD người thân không thuộc hồ sơ ACTIVE của tài khoản này.
                var strangerGuardian = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test Tre CCCD Nguoi Than La",
                    DateOfBirth = childDob,
                    Gender = "MALE",
                    PhoneNumber = "0987654003",
                    Relationship = "CHILD",
                    GuardianNationalId = $"99{Random.Shared.Next(1000000000):D9}"
                });
                _out.WriteLine($"CCCD người thân lạ: {strangerGuardian.Message}");
                Assert.False(strangerGuardian.Success);
                Assert.Contains("không khớp với hồ sơ nào đang hoạt động", strangerGuardian.Message);

                // 4) CCCD người thân đúng là CCCD hồ sơ "Bản thân" ACTIVE của tài khoản -> tạo được.
                var childOk = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test Tre Co CCCD Nguoi Than",
                    DateOfBirth = childDob,
                    Gender = "FEMALE",
                    PhoneNumber = "0987654004",
                    Relationship = "CHILD",
                    GuardianNationalId = ownerCccd,
                    Address = "Thành phố Hồ Chí Minh",
                    InsuranceCardNumber = $"BT{Random.Shared.Next(100000000):D8}"
                });
                _out.WriteLine($"Trẻ khai CCCD người thân hợp lệ: {childOk.Success} | {childOk.Message}");
                Assert.True(childOk.Success, childOk.Message);
                createdPatientId = childOk.PatientId;

                // 5) CCCD đã có trong hệ thống -> từ chối và hướng dẫn dùng nhánh "Đã từng khám".
                var duplicate = await profiles.CreateProfileAsync(OwnerAccountId, new CreateProfileRequest
                {
                    FullName = "Test CCCD Trung",
                    DateOfBirth = new DateOnly(1995, 9, 9),
                    Gender = "MALE",
                    PhoneNumber = "0987654005",
                    NationalId = ownerCccd,
                    Relationship = "SPOUSE"
                });
                _out.WriteLine($"CCCD trùng: {duplicate.Message}");
                Assert.False(duplicate.Success);
                Assert.Contains("đã có trong hệ thống", duplicate.Message);
                Assert.Contains("Đã từng khám", duplicate.Message);

                await using var audit = NewContext(cs);
                Patient child = await audit.Patients.AsNoTracking().SingleAsync(p => p.Id == createdPatientId);
                Assert.Null(child.NationalId);
                Assert.Equal("Thành phố Hồ Chí Minh", child.Address);
                Assert.True(await audit.InsuranceCards.AnyAsync(c => c.PatientId == createdPatientId),
                            "Số thẻ BHYT khai ở form phải được lưu vào insurance_cards.");
            }
        }
        finally
        {
            await CleanupAsync(cs, createdPatientId);
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
        var profiles = new PatientProfileService(ctx, Options.Create(new BookingOptions()));

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

    /// <summary>
    /// Quy tắc mới (docs/DECISIONS.md #25): liên kết PENDING chưa đặt lịch được, vì lúc đó người xin
    /// chưa nhập xong mã OTP. Chỉ ACTIVE (nhập đúng mã hoặc quầy duyệt giấy tờ) mới qua CanBookAsync.
    /// </summary>
    [Fact]
    public async Task Lien_ket_pending_khong_dat_lich_duoc_den_khi_chuyen_active()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        var bookingOptions = new BookingOptions();
        await using var ctx = NewContext(cs);
        var profiles = new PatientProfileService(ctx, Options.Create(bookingOptions));
        var access = new PatientAccessService(ctx);

        int linkedCount = await ctx.AccountPatientProfiles.AsNoTracking()
            .CountAsync(l => l.AccountId == RelativeAccountId && l.Status != LinkStatus.Revoked);
        if (linkedCount >= bookingOptions.MaxProfilesPerAccount)
        {
            _out.WriteLine($"Bỏ qua: tài khoản #{RelativeAccountId} đã dùng hết {linkedCount} hồ sơ.");
            return;
        }

        int[] alreadyLinked = await ctx.AccountPatientProfiles.AsNoTracking()
            .Where(l => l.AccountId == RelativeAccountId)
            .Select(l => l.PatientId)
            .ToArrayAsync();

        Patient? target = await ctx.Patients.AsNoTracking()
            .Where(p => !alreadyLinked.Contains(p.Id))
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync();
        if (target is null) { _out.WriteLine("Bỏ qua: DB thử nghiệm không còn hồ sơ chưa liên kết."); return; }

        int patientId = target.Id;
        try
        {
            ProfileLinkResult pending = await profiles.RequestLinkAsync(RelativeAccountId, patientId.ToPatientCode(), "OTHER");
            _out.WriteLine($"RequestLinkAsync: {pending.Success} | {pending.Message} | status={pending.LinkStatus}");
            Assert.True(pending.Success, pending.Message);
            Assert.Equal("PENDING", pending.LinkStatus);

            Assert.False(await access.CanBookAsync(RelativeAccountId, patientId));

            ProfileLinkResult verified = await profiles.VerifyLinkAsync(RelativeAccountId, patientId, CounterAccountId);
            _out.WriteLine($"VerifyLinkAsync: {verified.Success} | status={verified.LinkStatus}");
            Assert.True(verified.Success, verified.Message);
            Assert.Equal("ACTIVE", verified.LinkStatus);

            Assert.True(await access.CanBookAsync(RelativeAccountId, patientId));
        }
        finally
        {
            // Hồ sơ này là dữ liệu thật của DB nên chỉ gỡ liên kết test, không xoá bệnh nhân.
            await using var clean = NewContext(cs);
            await clean.Database.ExecuteSqlRawAsync(
                "DELETE FROM account_patient_profiles WHERE patient_id = {0} AND account_id = {1}",
                patientId, RelativeAccountId);
        }
    }

    /// <summary>
    /// ValidateNewProfileAsync phải trả đúng lời từ chối mà CreateProfileAsync sẽ trả (quy tắc nghiệp vụ
    /// chạy TRƯỚC khi cấp mã OTP) và không được ghi dòng nào xuống DB.
    /// </summary>
    [Fact]
    public async Task ValidateNewProfile_noi_dung_mot_loi_CreateProfile_tu_choi_va_khong_ghi()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        await using var ctx = NewContext(cs);
        var profiles = new PatientProfileService(ctx, Options.Create(new BookingOptions()));

        int patientsBefore = await ctx.Patients.CountAsync();
        int linksBefore = await ctx.AccountPatientProfiles.CountAsync();

        // 1) Người lớn thiếu CCCD.
        var adultWithoutId = new CreateProfileRequest
        {
            FullName = "Test Validate Thieu CCCD",
            DateOfBirth = new DateOnly(1990, 5, 5),
            Gender = "MALE",
            PhoneNumber = "0987654101",
            Relationship = "OTHER"
        };
        string? adultError = await profiles.ValidateNewProfileAsync(OwnerAccountId, adultWithoutId);
        ProfileLinkResult adultCreate = await profiles.CreateProfileAsync(OwnerAccountId, adultWithoutId);
        _out.WriteLine($"Thiếu CCCD -> validate: {adultError} | create: {adultCreate.Message}");
        Assert.NotNull(adultError);
        Assert.False(adultCreate.Success);
        Assert.Equal(adultError, adultCreate.Message);

        // 2) Số điện thoại sai (luật 10-11 chữ số bắt đầu bằng 0, dùng chung cho Web và BLL).
        var badPhone = new CreateProfileRequest
        {
            FullName = "Test Validate Sdt Sai",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Gender = "MALE",
            PhoneNumber = "12345",
            NationalId = $"95{Random.Shared.Next(1000000000):D9}",
            Relationship = "OTHER"
        };
        string? phoneError = await profiles.ValidateNewProfileAsync(OwnerAccountId, badPhone);
        ProfileLinkResult phoneCreate = await profiles.CreateProfileAsync(OwnerAccountId, badPhone);
        _out.WriteLine($"SĐT sai -> validate: {phoneError} | create: {phoneCreate.Message}");
        Assert.Equal(PhoneNumbers.ErrorMessage, phoneError);
        Assert.Equal(phoneError, phoneCreate.Message);

        Assert.Equal(patientsBefore, await ctx.Patients.CountAsync());
        Assert.Equal(linksBefore, await ctx.AccountPatientProfiles.CountAsync());
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
