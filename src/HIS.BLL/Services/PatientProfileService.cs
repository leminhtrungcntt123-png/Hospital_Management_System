using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

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

    public PatientProfileService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ProfileLinkResult> CreateProfileAsync(
        int accountId,
        CreateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        string fullName = (request.FullName ?? string.Empty).Trim();
        string phone = NormalizePhone(request.PhoneNumber);
        string? nationalId = NullIfWhiteSpace(request.NationalId);
        PatientRelationship relationship = ParseRelationship(request.Relationship);

        string? validationError = Validate(fullName, request.DateOfBirth, request.Gender, phone, nationalId);
        if (validationError != null)
        {
            return new ProfileLinkResult(false, validationError);
        }

        Account account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new ArgumentException("Tài khoản đăng nhập không tồn tại.");

        if (relationship == PatientRelationship.Self
            && await _context.AccountPatientProfiles.AnyAsync(
                l => l.AccountId == accountId && l.Relationship == PatientRelationship.Self && l.Status != LinkStatus.Revoked,
                cancellationToken))
        {
            return new ProfileLinkResult(false, "Tài khoản này đã có hồ sơ \"Bản thân\". Hãy dùng chức năng xin liên kết hồ sơ khác.");
        }

        if (nationalId != null && await _context.Patients.AnyAsync(p => p.NationalId == nationalId, cancellationToken))
        {
            return new ProfileLinkResult(false, $"CCCD {nationalId} đã có trong hệ thống. Bấm \"Xin liên kết hồ sơ\" để quầy xác nhận.");
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
        Patient? patient = await FindByKeywordAsync(keyword, cancellationToken);

        if (patient == null)
        {
            return new ProfileLinkResult(
                false,
                "Không tìm thấy hồ sơ phù hợp. Hãy tạo hồ sơ mới nếu đây là người nhà chưa từng khám tại bệnh viện.");
        }

        PatientRelationship parsed = ParseRelationship(relationship);

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

    private async Task<Patient?> FindByKeywordAsync(string keyword, CancellationToken cancellationToken)
    {
        string normalized = (keyword ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.StartsWith("BN", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized.AsSpan(2), out int patientId))
        {
            Patient? byId = await _context.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);
            if (byId != null)
            {
                return byId;
            }
        }

        Patient? exact = await _context.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.PhoneNumber == normalized ||
                p.NationalId == normalized ||
                p.InsuranceCards.Any(c => c.CardNumber == normalized),
                cancellationToken);

        if (exact != null)
        {
            return exact;
        }

        // Họ tên chỉ khớp nguyên văn, giống quy tắc tra cứu ở quầy để tránh nhận nhầm hồ sơ.
        return await _context.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => EF.Functions.ILike(p.FullName, normalized), cancellationToken);
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
