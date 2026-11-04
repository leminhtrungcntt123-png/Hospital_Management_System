using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Interfaces;

/// <summary>
/// Hồ sơ do tài khoản tự tạo trên Web (cách A đã chốt):
/// tạo mới => link ACTIVE + được xem hồ sơ ngay; xin liên kết hồ sơ đã có => PENDING, quầy duyệt.
/// </summary>
public sealed class CreateProfileRequest
{
    public string FullName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? Address { get; set; }
    public string? InsuranceCardNumber { get; set; }

    /// <summary>Mã quan hệ theo góc nhìn chủ tài khoản: SELF, SPOUSE, CHILD, PARENT, SIBLING, OTHER.</summary>
    public string Relationship { get; set; } = nameof(PatientRelationship.Self).ToUpperInvariant();
}

public sealed record ProfileLinkResult(
    bool Success,
    string Message,
    int PatientId = 0,
    string PatientCode = "",
    string LinkStatus = "",
    bool CanViewRecords = false);

public interface IPatientProfileService
{
    /// <summary>Hồ sơ mới hoàn toàn + link SELF/... ACTIVE cho chính tài khoản vừa tạo.</summary>
    Task<ProfileLinkResult> CreateProfileAsync(
        int accountId,
        CreateProfileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xin liên kết một hồ sơ ĐÃ tồn tại (tìm theo CCCD / SĐT / mã BN / số BHYT / họ tên đúng nguyên văn).
    /// Không có hồ sơ => thất bại, hướng dẫn tạo mới. Đã có link => trả về trạng thái hiện tại, không tạo trùng.
    /// </summary>
    Task<ProfileLinkResult> RequestLinkAsync(
        int accountId,
        string keyword,
        string relationship,
        CancellationToken cancellationToken = default);

    /// <summary>Quầy xác nhận giấy tờ: PENDING -> ACTIVE. Link SELF không bao giờ ở PENDING.</summary>
    Task<ProfileLinkResult> VerifyLinkAsync(
        int accountId,
        int patientId,
        int verifierAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>Thu hồi liên kết (cách A: vẫn giữ lịch sử khám, chỉ ngắt quyền đặt lịch/xem hồ sơ).</summary>
    Task<ProfileLinkResult> RevokeLinkAsync(
        int accountId,
        int patientId,
        int revokerAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>Danh sách hồ sơ của một tài khoản, kèm nhãn tiếng Việt để Web/WinForms hiển thị thẳng.</summary>
    Task<IReadOnlyList<PatientProfileLinkDto>> GetLinksForAccountAsync(
        int accountId,
        CancellationToken cancellationToken = default);

    /// <summary>Liên kết chờ duyệt, ưu tiên những hồ sơ đang có lịch hẹn hôm nay để quầy xử lý.</summary>
    Task<IReadOnlyList<PatientProfileLinkDto>> GetPendingLinksAsync(
        string? search = null,
        CancellationToken cancellationToken = default);
}
