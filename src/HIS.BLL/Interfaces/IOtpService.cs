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

/// <summary>Nguồn email nhận mã OTP, quyết định theo nhánh người dùng vừa chọn ở màn Tạo hồ sơ.</summary>
public enum ProfileOtpSource
{
    /// <summary>Hồ sơ vừa tạo (chưa từng khám): mã gửi về email của TÀI KHOẢN đang đăng nhập.</summary>
    NewProfile,

    /// <summary>Hồ sơ đã có (đã từng khám): mã gửi về email ĐÃ LƯU trong hồ sơ, không lấy từ form.</summary>
    ExistingProfile
}

public interface IOtpService
{
    /// <summary>
    /// Sinh mã, ghi dấu vết vào notifications và đẩy qua mọi <see cref="IOtpDelivery"/> đã đăng ký.
    /// Email nhận luôn lấy theo <paramref name="source"/> (tài khoản hoặc hồ sơ), không có ô nhập ở form
    /// và không ghi đè patients.email.
    /// </summary>
    Task<OtpIssueResult> IssueProfileOtpAsync(
        int accountId,
        int patientId,
        ProfileOtpSource source,
        CancellationToken cancellationToken = default);
}
