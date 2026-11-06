using HIS.BLL.DTOs;
using HIS.Domain;

namespace HIS.BLL.Interfaces;

/// <summary>
/// Hồ sơ do tài khoản tự tạo trên Web (cách A đã chốt):
/// tạo mới => link ACTIVE + được xem hồ sơ ngay; xin liên kết hồ sơ đã có => PENDING, chỉ đặt lịch được
/// sau khi người xin nhập đúng mã OTP gửi về email trong hồ sơ (đường khác: quầy xác nhận giấy tờ ở WinForms).
/// </summary>
public sealed class CreateProfileRequest
{
    public string FullName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalId { get; set; }

    /// <summary>
    /// CCCD của người thân, chỉ bắt buộc khi tạo hồ sơ cho trẻ dưới tuổi cấp CCCD và đứa trẻ chưa có
    /// định danh cá nhân. Phải khớp CCCD của một hồ sơ ACTIVE thuộc chính tài khoản; không lưu thành cột mới.
    /// </summary>
    public string? GuardianNationalId { get; set; }
    public string? Address { get; set; }

    /// <summary>
    /// Email của hồ sơ. Nhánh "chưa từng khám" trên Web bắt buộc nhập ô này và mã OTP được gửi về đúng
    /// địa chỉ đó TRƯỚC khi hồ sơ được tạo. Để trống (WinForms, dữ liệu cũ) thì dịch vụ tự lấy email của
    /// tài khoản tạo ra nó, nhờ hồ sơ luôn có nơi nhận mã về sau.
    /// </summary>
    public string? Email { get; set; }
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
    /// Chạy TOÀN BỘ quy tắc của hồ sơ "chưa từng khám" (nhân thân, hạn mức hồ sơ, CCCD người thân, trùng SELF)
    /// mà không ghi gì xuống DB. Web gọi trước khi xin mã OTP để người dùng không nhận mã rồi mới biết phiếu sai;
    /// CreateProfileAsync vẫn kiểm lại y hệt vì nó là cổng duy nhất ghi patients.
    /// Trả null khi phiếu hợp lệ, ngược lại là câu tiếng Việt chỉ rõ nguyên nhân.
    /// </summary>
    Task<string?> ValidateNewProfileAsync(
        int accountId,
        CreateProfileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xin liên kết một hồ sơ ĐÃ tồn tại theo MÃ BỆNH NHÂN (BN######). Không tra CCCD/SĐT/thẻ BHYT/họ tên.
    /// Không có hồ sơ => thất bại, hướng dẫn tạo mới hoặc gọi hotline. Đã có link => trả trạng thái hiện tại, không tạo trùng.
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
