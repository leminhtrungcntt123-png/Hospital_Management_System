namespace HIS.BLL.Interfaces;

/// <summary>
/// Kết quả cấp một mã OTP cho hồ sơ. Code luôn là mã vừa sinh (host giữ trong Session để đối chiếu);
/// RevealCode cho biết màn hình có được IN mã ra hay không — bản thật phải tắt vì mã chỉ nằm trong email.
/// </summary>
public sealed record OtpIssueResult(
    bool Success,
    string Message,
    string? RecipientEmail = null,
    string? Code = null,
    DateTimeOffset? ExpiresAt = null,
    bool RevealCode = false,
    string PatientName = "");

public interface IOtpService
{
    /// <summary>
    /// Nhánh "đã từng khám": sinh mã gửi về email ĐÃ LƯU trong hồ sơ (patients.email), không lấy từ form
    /// và không cho ghi đè — mã phải chứng minh người xin đúng là người đứng tên hồ sơ.
    /// </summary>
    Task<OtpIssueResult> IssueProfileOtpAsync(
        int accountId,
        int patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhánh "chưa từng khám": hồ sơ còn chưa tồn tại nên mã gửi thẳng về email vừa gõ trên form.
    /// Không ghi gì vào tables hồ sơ — chỉ một dòng notifications làm dấu vết.
    /// </summary>
    Task<OtpIssueResult> IssueNewProfileOtpAsync(
        int accountId,
        string email,
        string fullName,
        CancellationToken cancellationToken = default);
}
