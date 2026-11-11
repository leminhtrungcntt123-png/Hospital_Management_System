using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class PatientProfileService(AppDbContext db, TimeProvider clock) : IPatientProfileService
{
    private static string GetRelationshipLabel(PatientRelationship rel) => rel switch
    {
        PatientRelationship.Self => "Chính tôi",
        PatientRelationship.Spouse => "Vợ / Chồng",
        PatientRelationship.Child => "Con",
        PatientRelationship.Parent => "Bố / Mẹ",
        PatientRelationship.Sibling => "Anh / Chị / Em",
        _ => "Khác"
    };

    public async Task<Result<AccountProfilesOverviewDto>> GetAccountProfilesAsync(int accountId, CancellationToken ct = default)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .Include(a => a.PatientLinks)
                .ThenInclude(l => l.Patient)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account == null)
            return Result.Failure<AccountProfilesOverviewDto>("AccountNotFound", "Không tìm thấy tài khoản.");

        var activeLinks = account.PatientLinks
            .Where(l => l.Status == LinkStatus.Active && l.Patient != null)
            .OrderByDescending(l => l.Relationship == PatientRelationship.Self)
            .ThenByDescending(l => l.CreatedAt)
            .ToList();

        PatientProfileItemDto? selfProfile = null;
        var relatives = new List<PatientProfileItemDto>();

        foreach (var link in activeLinks)
        {
            var p = link.Patient!;
            var item = new PatientProfileItemDto(
                p.Id,
                p.FullName,
                p.DateOfBirth,
                p.Gender,
                p.PhoneNumber,
                account.Email,
                p.Address,
                p.NationalId,
                link.Relationship.ToString(),
                GetRelationshipLabel(link.Relationship)
            );

            if (link.Relationship == PatientRelationship.Self && selfProfile == null)
            {
                selfProfile = item;
            }
            else
            {
                relatives.Add(item);
            }
        }

        return Result.Success(new AccountProfilesOverviewDto(
            account.Id,
            account.Username,
            account.Email,
            selfProfile,
            relatives
        ));
    }

    public async Task<Result> SavePersonalProfileAsync(int accountId, SavePersonalProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
            return Result.Failure("InvalidName", "Họ và tên không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Result.Failure("InvalidPhone", "Vui lòng nhập số điện thoại.");

        var account = await db.Accounts
            .Include(a => a.PatientLinks)
                .ThenInclude(l => l.Patient)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account == null)
            return Result.Failure("AccountNotFound", "Không tìm thấy tài khoản.");

        var now = DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var emailExists = await db.Accounts.AnyAsync(a => a.Id != accountId && a.Email == normalizedEmail, ct);
            if (emailExists)
                return Result.Failure("EmailTaken", "Địa chỉ email đã được sử dụng bởi tài khoản khác.");
            account.Email = normalizedEmail;
        }

        var selfLink = account.PatientLinks.FirstOrDefault(l => l.Relationship == PatientRelationship.Self && l.Status == LinkStatus.Active);
        if (selfLink?.Patient != null)
        {
            var p = selfLink.Patient;
            p.FullName = request.FullName.Trim();
            p.DateOfBirth = request.DateOfBirth;
            p.Gender = request.Gender;
            p.PhoneNumber = request.PhoneNumber.Trim();
            if (request.Address != null) p.Address = request.Address.Trim();
        }
        else
        {
            var newPatient = new Patient
            {
                FullName = request.FullName.Trim(),
                DateOfBirth = request.DateOfBirth,
                Gender = request.Gender,
                PhoneNumber = request.PhoneNumber.Trim(),
                Address = request.Address?.Trim(),
                CreatedAt = now
            };
            db.Patients.Add(newPatient);

            var link = new AccountPatientProfile
            {
                Account = account,
                Patient = newPatient,
                Relationship = PatientRelationship.Self,
                Status = LinkStatus.Active,
                CanViewRecords = true,
                CreatedAt = now
            };
            db.AccountPatientProfiles.Add(link);
        }

        account.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<int>> AddRelativeProfileAsync(int accountId, AddRelativeProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
            return Result.Failure<int>("InvalidName", "Vui lòng nhập họ và tên người thân.");

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Result.Failure<int>("InvalidPhone", "Vui lòng nhập số điện thoại người thân.");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account == null)
            return Result.Failure<int>("AccountNotFound", "Không tìm thấy tài khoản.");

        var now = DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);

        var relativePatient = new Patient
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            PhoneNumber = request.PhoneNumber.Trim(),
            NationalId = string.IsNullOrWhiteSpace(request.MedicalCode) ? null : request.MedicalCode.Trim(),
            CreatedAt = now
        };
        db.Patients.Add(relativePatient);

        var rel = request.Relationship == PatientRelationship.Self ? PatientRelationship.Other : request.Relationship;
        var link = new AccountPatientProfile
        {
            Account = account,
            Patient = relativePatient,
            Relationship = rel,
            Status = LinkStatus.Active,
            CanViewRecords = rel == PatientRelationship.Child && (DateOnly.FromDateTime(now).Year - request.DateOfBirth.Year < 18),
            CreatedAt = now
        };
        db.AccountPatientProfiles.Add(link);

        await db.SaveChangesAsync(ct);
        return Result.Success(relativePatient.Id);
    }

    public Task<Result<int>> CreateProfileAsync(int accountId, CreateProfileRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> RequestLinkAsync(int accountId, RequestLinkRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> VerifyLinkAsync(int linkId, bool grantRecordAccess, int verifiedByAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> RevokeLinkAsync(int linkId, int revokedByAccountId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
