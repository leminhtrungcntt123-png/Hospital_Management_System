using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

/// <summary>
/// Nghiệp vụ liên kết tài khoản <-> hồ sơ theo "cách A" đã chốt với nhóm:
/// - hồ sơ mới do tài khoản tạo => link SELF/CHILD... ACTIVE, can_view_records = true ngay;
/// - xin liên kết hồ sơ ĐÃ có => PENDING, can_view_records = false, quầy xác nhận giấy tờ khi bệnh nhân tới;
/// - MỘT hồ sơ được phép thuộc nhiều tài khoản (khác với ràng buộc account_id UNIQUE cũ trên patients).
/// Mọi ghi/Xóa đều kèm access_logs vì đây là dữ liệu nhân thân.
/// </summary>
public class PatientProfileService : IPatientProfileService
{
    private readonly AppDbContext _context;
    private readonly BookingOptions _options;

    public PatientProfileService(AppDbContext context, IOptions<BookingOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    /// <summary>
    /// Hạn mức hồ sơ phía portal: MaxProfilesPerAccount hồ sơ gắn với một tài khoản,
    /// MaxProfilesPerOwnerPhone hồ sơ tính theo SĐT chủ tài khoản (SĐT của hồ sơ "Bản thân")
    /// và một SĐT chỉ được làm chủ một tài khoản.
    /// REVOKED không đếm nên thu hồi liên kết là giải phóng chỉ tiêu.
    /// </summary>
    private async Task<string?> ValidateProfileQuotaAsync(
        int accountId, string phone, PatientRelationship relationship, CancellationToken cancellationToken)
    {
        int linkedProfiles = await _context.AccountPatientProfiles.AsNoTracking()
            .CountAsync(l => l.AccountId == accountId && l.Status != LinkStatus.Revoked, cancellationToken);

        if (linkedProfiles >= _options.MaxProfilesPerAccount)
        {
            return $"Mỗi tài khoản chỉ được quản lý tối đa {_options.MaxProfilesPerAccount} hồ sơ. " +
                   $"Tài khoản này đã có {linkedProfiles} hồ sơ.";
        }

        string? ownerPhone = await OwnerPhoneAsync(accountId, cancellationToken);

        if (ownerPhone is null && relationship != PatientRelationship.Self)
        {
            // Chưa có hồ sơ "Bản thân" thì chưa biết SĐT chủ tài khoản là gì nên chỉ giới hạn theo tài khoản.
            return null;
        }

        string effectiveOwnerPhone = ownerPhone ?? phone;

        if (relationship == PatientRelationship.Self)
        {
            bool phoneTakenByOtherAccount = await _context.AccountPatientProfiles.AsNoTracking()
                .AnyAsync(l => l.AccountId != accountId
                               && l.Relationship == PatientRelationship.Self
                               && l.Status != LinkStatus.Revoked
                               && l.Patient.PhoneNumber == effectiveOwnerPhone, cancellationToken);

            if (phoneTakenByOtherAccount)
            {
                return $"Số điện thoại {effectiveOwnerPhone} đã được đăng ký. " +
                       "Vui lòng đăng nhập bằng tài khoản đó hoặc dùng Quên mật khẩu.";
            }
        }

        var accountIdsWithOwnerPhone = await _context.AccountPatientProfiles.AsNoTracking()
            .Where(l => l.Relationship == PatientRelationship.Self
                        && l.Status != LinkStatus.Revoked
                        && l.Patient.PhoneNumber == effectiveOwnerPhone)
            .Select(l => l.AccountId)
            .ToListAsync(cancellationToken);

        accountIdsWithOwnerPhone.Add(accountId);

        int profilesUnderOwnerPhone = await _context.AccountPatientProfiles.AsNoTracking()
            .CountAsync(l => l.Status != LinkStatus.Revoked
                             && accountIdsWithOwnerPhone.Contains(l.AccountId), cancellationToken);

        if (profilesUnderOwnerPhone >= _options.MaxProfilesPerOwnerPhone)
        {
            return $"SĐT chủ tài khoản {effectiveOwnerPhone} đã quản lý đủ {_options.MaxProfilesPerOwnerPhone} hồ sơ. " +
                   "Thu hồi hồ sơ không dùng hoặc liên hệ hotline 0808 0808.";
        }

        return null;
    }

    /// <summary>
    /// Quy tắc giấy tờ khi tạo hồ sơ mới: CCCD đã có trong hệ thống thì phải dùng nhánh "Đã từng khám"
    /// để liên kết; người từ OwnIdFromAge tuổi bắt buộc có CCCD của chính mình; trẻ nhỏ chưa có CCCD
    /// thì bắt buộc CCCD của một người thân đang có hồ sơ ACTIVE thuộc chính tài khoản.
    /// </summary>
    private async Task<string?> ValidateIdentityDocumentsAsync(
        int accountId,
        DateOnly dateOfBirth,
        string? nationalId,
        string? guardianNationalId,
        CancellationToken cancellationToken)
    {
        if (nationalId != null && await _context.Patients.AnyAsync(p => p.NationalId == nationalId, cancellationToken))
        {
            return $"CCCD {nationalId} đã có trong hệ thống. Ở màn Tạo hồ sơ, hãy chọn \"Đã từng khám\" và nhập đúng CCCD này để liên kết hồ sơ đó.";
        }

        int age = PatientAge.YearsOn(dateOfBirth, DateOnly.FromDateTime(DateTime.Today));

        if (age >= _options.OwnIdFromAge)
        {
            return nationalId is null
                ? $"Người từ {_options.OwnIdFromAge} tuổi bắt buộc phải khai CCCD / số định danh cá nhân."
                : null;
        }

        if (nationalId != null)
        {
            return null;
        }

        if (guardianNationalId is null)
        {
            return $"Trẻ dưới {_options.OwnIdFromAge} tuổi chưa có CCCD thì phải nhập CCCD của người thân kèm quan hệ.";
        }

        bool guardianIsOwnProfile = await _context.AccountPatientProfiles.AsNoTracking()
            .AnyAsync(l => l.AccountId == accountId
                           && l.Status == LinkStatus.Active
                           && l.Patient.NationalId == guardianNationalId,
                cancellationToken);

        return guardianIsOwnProfile
            ? null
            : $"CCCD người thân {guardianNationalId} không khớp với hồ sơ nào đang hoạt động của tài khoản này. " +
              "Hãy liên kết hồ sơ của người đó trước hoặc nhập CCCD của người đã có hồ sơ trong danh sách.";
    }

    private async Task<string?> OwnerPhoneAsync(int accountId, CancellationToken cancellationToken) =>
        await _context.AccountPatientProfiles.AsNoTracking()
            .Where(l => l.AccountId == accountId
                        && l.Relationship == PatientRelationship.Self
                        && l.Status != LinkStatus.Revoked)
            .Select(l => l.Patient.PhoneNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ProfileLinkResult> CreateProfileAsync(
        int accountId,
        CreateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        string fullName = (request.FullName ?? string.Empty).Trim();
        string phone = NormalizePhone(request.PhoneNumber);
        string? nationalId = NullIfWhiteSpace(request.NationalId);
        string? email = ProfileEmail.Normalize(request.Email);
        PatientRelationship relationship = ParseRelationship(request.Relationship);

        string? validationError = Validate(fullName, request.DateOfBirth, request.Gender, phone, nationalId);
        if (validationError != null)
        {
            return new ProfileLinkResult(false, validationError);
        }

        if (email is not null && !ProfileEmail.IsUsable(email))
        {
            return new ProfileLinkResult(false, $"Email \"{email}\" không hợp lệ. Vui lòng kiểm tra lại.");
        }

        Account account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new ArgumentException("Tài khoản đăng nhập không tồn tại.");

        // Màn Tạo hồ sơ không thu email nữa, nên hồ sơ mới mang email của tài khoản tạo ra nó.
        // Nhờ vậy lần sau người khác liên kết lại hồ sơ này (nhánh "đã từng khám") vẫn gửi được mã OTP.
        email ??= ProfileEmail.NormalizeUsable(account.Email);

        string? quotaError = await ValidateProfileQuotaAsync(accountId, phone, relationship, cancellationToken);
        if (quotaError != null)
        {
            return new ProfileLinkResult(false, quotaError);
        }

        string? identityError = await ValidateIdentityDocumentsAsync(
            accountId, request.DateOfBirth, nationalId, NullIfWhiteSpace(request.GuardianNationalId), cancellationToken);
        if (identityError != null)
        {
            return new ProfileLinkResult(false, identityError);
        }

        if (relationship == PatientRelationship.Self
            && await _context.AccountPatientProfiles.AnyAsync(
                l => l.AccountId == accountId && l.Relationship == PatientRelationship.Self && l.Status != LinkStatus.Revoked,
                cancellationToken))
        {
            return new ProfileLinkResult(false, "Tài khoản này đã có hồ sơ \"Bản thân\". Nếu muốn thêm người đã khám, hãy chọn nhánh \"Đã từng khám\".");
        }

        DateTime now = DateTime.Now;

        var patient = new Patient
        {
            FullName = fullName,
            DateOfBirth = request.DateOfBirth,
            Gender = EnumCodes.ParseDatabaseCode<Gender>(request.Gender) ?? Gender.Other,
            NationalId = nationalId,
            PhoneNumber = phone,
            Address = NullIfWhiteSpace(request.Address),
            Email = email,
            CreatedAt = now
        };

        _context.Patients.Add(patient);

        // Cách A: chính tài khoản tạo hồ sơ mới nên coi như đã tự khai và chịu trách nhiệm -> ACTIVE.
        // verified_by ghi chính chủ để vượt ck_link_adult_records_consent với các quan hệ không phải SELF/CHILD.
        var link = new AccountPatientProfile
        {
            AccountId = accountId,
            Patient = patient,
            Relationship = relationship,
            Status = LinkStatus.Active,
            CanViewRecords = true,
            CreatedBy = accountId,
            VerifiedBy = accountId,
            VerifiedAt = now,
            CreatedAt = now
        };

        _context.AccountPatientProfiles.Add(link);
        LogAccess(accountId, patient, "CREATE_PROFILE", null);
        await TrackInsuranceCardAsync(patient, NullIfWhiteSpace(request.InsuranceCardNumber), now, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();
            throw new InvalidOperationException("Không tạo được hồ sơ mới (CCCD hoặc thông tin trùng lặp).", ex);
        }

        return new ProfileLinkResult(
            true,
            $"Đã tạo hồ sơ {patient.FullName} ({patient.Id.ToPatientCode()}).",
            patient.Id,
            patient.Id.ToPatientCode(),
            LinkStatus.Active.ToDatabaseCode(),
            true);
    }

    public async Task<ProfileLinkResult> RequestLinkAsync(
        int accountId,
        string keyword,
        string relationship,
        CancellationToken cancellationToken = default)
    {
        Patient? patient = await FindByPatientCodeAsync(keyword, cancellationToken);

        if (patient == null)
        {
            return new ProfileLinkResult(
                false,
                "Không tìm thấy hồ sơ với mã bệnh nhân vừa nhập. Mã gồm chữ BN và 6 số in trên phiếu khám (ví dụ BN000123); " +
                "quên mã thì gọi 0808 0808. Nếu người này chưa từng khám tại bệnh viện, hãy chọn nhánh \"Chưa từng khám\".");
        }

        PatientRelationship parsed = ParseRelationship(relationship);

        string? linkQuotaError = await ValidateProfileQuotaAsync(accountId, patient.PhoneNumber, parsed, cancellationToken);
        if (linkQuotaError != null)
        {
            return new ProfileLinkResult(false, linkQuotaError);
        }

        if (parsed == PatientRelationship.Self
            && await _context.AccountPatientProfiles.AnyAsync(
                l => l.AccountId == accountId && l.Relationship == PatientRelationship.Self && l.Status != LinkStatus.Revoked,
                cancellationToken))
        {
            return new ProfileLinkResult(false, "Tài khoản này đã có hồ sơ \"Bản thân\" nên không thể xin liên kết thêm hồ sơ thứ hai.");
        }

        AccountPatientProfile? existing = await _context.AccountPatientProfiles
            .FirstOrDefaultAsync(l => l.AccountId == accountId && l.PatientId == patient.Id, cancellationToken);

        if (existing != null && existing.Status != LinkStatus.Revoked)
        {
            return new ProfileLinkResult(
                true,
                existing.Status == LinkStatus.Active
                    ? $"Hồ sơ {patient.FullName} đã liên kết với tài khoản này."
                    : $"Hồ sơ {patient.FullName} đang chờ quầy xác nhận.",
                patient.Id,
                patient.Id.ToPatientCode(),
                existing.Status.ToDatabaseCode(),
                existing.CanViewRecords);
        }

        DateTime now = DateTime.Now;

        // Cách A: hồ sơ đã có sẵn (do quầy hoặc tài khoản khác tạo) => PENDING + chưa được xem hồ sơ.
        // uq_link_self_patient vẫn cho phép vì trạng thái chưa phải ACTIVE.
        var link = new AccountPatientProfile
        {
            AccountId = accountId,
            PatientId = patient.Id,
            Relationship = parsed == PatientRelationship.Self ? PatientRelationship.Self : parsed,
            Status = LinkStatus.Pending,
            CanViewRecords = false,
            CreatedBy = accountId,
            CreatedAt = now
        };

        // SELF không được ở PENDING (ck_link_self_not_pending): nếu chính chủ xin lại hồ sơ của mình
        // thì hồ sơ đó chắc chắn đang thuộc tài khoản khác -> bắt buộc ra quầy.
        if (link.Relationship == PatientRelationship.Self)
        {
            int? ownerAccountId = await _context.AccountPatientProfiles
                .Where(l => l.PatientId == patient.Id && l.Relationship == PatientRelationship.Self && l.Status == LinkStatus.Active)
                .Select(l => (int?)l.AccountId)
                .FirstOrDefaultAsync(cancellationToken);

            if (ownerAccountId == accountId)
            {
                return new ProfileLinkResult(true, "Hồ sơ này đã là hồ sơ \"Bản thân\" của tài khoản bạn.",
                    patient.Id, patient.Id.ToPatientCode(), LinkStatus.Active.ToDatabaseCode(), true);
            }

            return new ProfileLinkResult(
                false,
                ownerAccountId == null
                    ? "Hồ sơ chưa có chủ tài khoản nào liên kết \"Bản thân\", hãy chọn quan hệ người nhà rồi gửi yêu cầu."
                    : "Hồ sơ này đã thuộc tài khoản khác. Bạn cần ra quầy để xác minh giấy tờ.");
        }

        _context.AccountPatientProfiles.Add(link);
        LogAccess(accountId, patient, "LINK_REQUEST", null);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();
            throw new InvalidOperationException("Không gửi được yêu cầu liên kết hồ sơ.", ex);
        }

        return new ProfileLinkResult(
            true,
            $"Đã ghi nhận yêu cầu liên kết hồ sơ {patient.FullName}. Quầy sẽ xác nhận khi bạn tới khám.",
            patient.Id,
            patient.Id.ToPatientCode(),
            LinkStatus.Pending.ToDatabaseCode(),
            false);
    }

    public async Task<ProfileLinkResult> VerifyLinkAsync(
        int accountId,
        int patientId,
        int verifierAccountId,
        CancellationToken cancellationToken = default)
    {
        AccountPatientProfile? link = await _context.AccountPatientProfiles
            .Include(l => l.Patient)
            .FirstOrDefaultAsync(l => l.AccountId == accountId && l.PatientId == patientId, cancellationToken);

        if (link == null)
        {
            return new ProfileLinkResult(false, "Không tìm thấy liên kết hồ sơ cần xác nhận.");
        }

        if (link.Status == LinkStatus.Active)
        {
            return new ProfileLinkResult(true, $"Hồ sơ {link.Patient.FullName} đã được xác nhận trước đó.",
                patientId, patientId.ToPatientCode(), LinkStatus.Active.ToDatabaseCode(), link.CanViewRecords);
        }

        if (link.Status == LinkStatus.Revoked)
        {
            return new ProfileLinkResult(false, "Liên kết này đã bị thu hồi nên không xác nhận lại được.");
        }

        DateTime now = DateTime.Now;
        link.Status = LinkStatus.Active;
        link.VerifiedBy = verifierAccountId;
        link.VerifiedAt = now;

        // Duyệt liên kết chỉ mở quyền ĐẶT LỊCH. Quyền XEM HỒ sơ vẫn theo cách A:
        // chỉ SELF/CHILD giữ quyền đã có, các quan hệ khác phải được cấp riêng (can_view_records ở nguyên FALSE).
        LogAccess(accountId, link.Patient, "LINK_VERIFY", null);

        await _context.SaveChangesAsync(cancellationToken);

        return new ProfileLinkResult(
            true,
            $"Đã xác nhận liên kết hồ sơ {link.Patient.FullName} cho tài khoản #{accountId}.",
            patientId,
            patientId.ToPatientCode(),
            LinkStatus.Active.ToDatabaseCode(),
            link.CanViewRecords);
    }

    public async Task<ProfileLinkResult> RevokeLinkAsync(
        int accountId,
        int patientId,
        int revokerAccountId,
        CancellationToken cancellationToken = default)
    {
        AccountPatientProfile? link = await _context.AccountPatientProfiles
            .Include(l => l.Patient)
            .FirstOrDefaultAsync(l => l.AccountId == accountId && l.PatientId == patientId, cancellationToken);

        if (link == null)
        {
            return new ProfileLinkResult(false, "Không tìm thấy liên kết hồ sơ cần thu hồi.");
        }

        if (link.Status == LinkStatus.Revoked)
        {
            return new ProfileLinkResult(true, "Liên kết này đã thu hồi.", patientId, patientId.ToPatientCode(),
                LinkStatus.Revoked.ToDatabaseCode(), false);
        }

        link.Status = LinkStatus.Revoked;
        link.CanViewRecords = false;

        LogAccess(accountId, link.Patient, "LINK_REVOKE", null);
        await _context.SaveChangesAsync(cancellationToken);

        return new ProfileLinkResult(
            true,
            $"Đã thu hồi liên kết hồ sơ {link.Patient.FullName}.",
            patientId,
            patientId.ToPatientCode(),
            LinkStatus.Revoked.ToDatabaseCode(),
            false);
    }

    public async Task<IReadOnlyList<PatientProfileLinkDto>> GetLinksForAccountAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        DateTime today = DateTime.Today;
        DateTime tomorrow = today.AddDays(1);

        List<AccountPatientProfile> links = await _context.AccountPatientProfiles
            .AsNoTracking()
            .Include(l => l.Patient)
            .Include(l => l.Account)
            .Where(l => l.AccountId == accountId && l.Status != LinkStatus.Revoked)
            .OrderByDescending(l => l.Relationship == PatientRelationship.Self)
            .ThenBy(l => l.Patient.FullName)
            .ToListAsync(cancellationToken);

        List<int> patientIds = links.Select(l => l.PatientId).ToList();

        List<int> activeAppointmentPatientIds = await _context.Appointments
            .AsNoTracking()
            .Where(a => patientIds.Contains(a.PatientId)
                        && a.CreatedAt >= today && a.CreatedAt < tomorrow)
            .Select(a => a.PatientId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return links.Select(l => Map(l, activeAppointmentPatientIds.Contains(l.PatientId))).ToList();
    }

    public async Task<IReadOnlyList<PatientProfileLinkDto>> GetPendingLinksAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AccountPatientProfile> query = _context.AccountPatientProfiles
            .AsNoTracking()
            .Include(l => l.Patient)
            .Include(l => l.Account)
            .Where(l => l.Status == LinkStatus.Pending);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string pattern = $"%{search.Trim()}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.Patient.FullName, pattern) ||
                EF.Functions.ILike(l.Account.Username, pattern) ||
                (l.Patient.NationalId != null && EF.Functions.ILike(l.Patient.NationalId, pattern)) ||
                EF.Functions.ILike(l.Patient.PhoneNumber, pattern));
        }

        List<AccountPatientProfile> links = await query
            .OrderBy(l => l.Patient.FullName)
            .ToListAsync(cancellationToken);

        List<int> patientIds = links.Select(l => l.PatientId).ToList();
        DateTime today = DateTime.Today;

        List<int> todayBooked = patientIds.Count == 0
            ? new List<int>()
            : await _context.Appointments
                .AsNoTracking()
                .Where(a => patientIds.Contains(a.PatientId) && a.CreatedAt >= today && a.CreatedAt < today.AddDays(1))
                .Select(a => a.PatientId)
                .Distinct()
                .ToListAsync(cancellationToken);

        return links.Select(l => Map(l, todayBooked.Contains(l.PatientId))).ToList();
    }

    private PatientProfileLinkDto Map(AccountPatientProfile link, bool hasAppointmentToday)
    {
        return new PatientProfileLinkDto(
            link.AccountId,
            link.Account?.Username ?? string.Empty,
            link.PatientId,
            link.PatientId.ToPatientCode(),
            link.Patient.FullName,
            link.Patient.PhoneNumber ?? string.Empty,
            link.Patient.DateOfBirth,
            link.Patient.Gender.ToLabel(),
            link.Relationship.ToDatabaseCode(),
            link.Relationship.ToLabel(),
            link.Status.ToDatabaseCode(),
            link.Status.ToLabel(),
            link.CanViewRecords,
            hasAppointmentToday,
            link.CreatedAt,
            link.VerifiedAt);
    }

    private void LogAccess(int accountId, Patient patient, string action, string? ipAddress)
    {
        // Hồ sơ vừa tạo trong cùng SaveChanges chưa có id nên phải bám navigation để EF nối khóa ngoại.
        // Hồ sơ đã tồn tại (tra về kiểu AsNoTracking) thì ghi id thô: gắn navigation sẽ khiến EF coi
        // bệnh nhân là bản ghi mới và INSERT vào cột id GENERATED ALWAYS -> lỗi 428C9.
        bool isNewPatient = patient.Id == 0;

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = accountId,
            PatientId = isNewPatient ? null : patient.Id,
            Patient = isNewPatient ? patient : null,
            Action = action,
            OccurredAt = DateTime.Now,
            IpAddress = ipAddress
        });
    }

    /// <summary>
    /// Nhánh "đã từng khám" chỉ chấp nhận MÃ BỆNH NHÂN (BN######) in trên phiếu khám.
    /// Không tra bằng CCCD/SĐT/thẻ BHYT nữa để người dùng không dò được hồ sơ người khác;
    /// quên mã thì màn hình đã có hướng dẫn liên hệ bệnh viện.
    /// </summary>
    private async Task<Patient?> FindByPatientCodeAsync(string keyword, CancellationToken cancellationToken)
    {
        string normalized = (keyword ?? string.Empty).Trim();

        if (!normalized.StartsWith("BN", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(normalized.AsSpan(2), out int patientId)
            || patientId <= 0)
        {
            return null;
        }

        return await _context.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);
    }

    private async Task TrackInsuranceCardAsync(Patient patient, string? cardNumber, DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return;
        }

        bool exists = await _context.InsuranceCards.AnyAsync(c => c.CardNumber == cardNumber, cancellationToken);
        if (exists)
        {
            return;
        }

        int? defaultPlanId = await _context.InsurancePlans
            .AsNoTracking()
            .Where(p => p.ReferralType == ReferralType.InNetwork)
            .OrderBy(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultPlanId is null)
        {
            return;
        }

        int year = now.Year;
        _context.InsuranceCards.Add(new InsuranceCard
        {
            Patient = patient,
            CardNumber = cardNumber,
            InsurancePlanId = defaultPlanId.Value,
            ValidFrom = new DateOnly(year, 1, 1),
            ValidTo = new DateOnly(year, 12, 31),
            IsActive = true,
            CreatedAt = now
        });
    }

    private static PatientRelationship ParseRelationship(string? code) =>
        EnumCodes.ParseDatabaseCode<PatientRelationship>(code) ?? PatientRelationship.Other;

    private static string? Validate(string fullName, DateOnly dateOfBirth, string genderCode, string phone, string? nationalId)
    {
        if (fullName.Length == 0 || fullName.Length > 100)
        {
            return "Vui lòng nhập họ tên từ 1 đến 100 ký tự.";
        }

        if (dateOfBirth == default || dateOfBirth > DateOnly.FromDateTime(DateTime.Today))
        {
            return "Ngày sinh không hợp lệ.";
        }

        if (EnumCodes.ParseDatabaseCode<Gender>(genderCode) is null)
        {
            return "Vui lòng chọn giới tính.";
        }

        if (phone.Length is < 9 or > 15 || !phone.All(char.IsDigit))
        {
            return "Số điện thoại phải gồm 9 đến 15 chữ số.";
        }

        if (nationalId != null && nationalId.Length > 20)
        {
            return "Số CCCD/hộ chiếu không được vượt quá 20 ký tự.";
        }

        return null;
    }

    private static string NormalizePhone(string? value) =>
        (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace(".", string.Empty).Replace("-", string.Empty);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
