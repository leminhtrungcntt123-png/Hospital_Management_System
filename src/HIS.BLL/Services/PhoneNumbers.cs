using System.Text.RegularExpressions;

namespace HIS.BLL.Services;

/// <summary>
/// Luật số điện thoại của HỒ SƠ người khám: 10-11 chữ số, bắt đầu bằng 0 (thuê bao di động Việt Nam).
/// Trước đây Web kiểm 10-11 số còn PatientProfileService kiểm 9-15 chữ số nên có phiếu lọt qua màn khai,
/// được cấp mã OTP xong mới bị BLL từ chối. Giờ BLL là chỗ duy nhất kiểm và Web chỉ hiển thị lại thông báo.
/// Quầy tiếp nhận (ReceptionService/WinForms) vẫn dùng rộng hơn 9-15 chữ số vì số bàn của bệnh viện cũng
/// được ghi vào hồ sơ giấy - hai luật này cố ý khác nhau, xem docs/DECISIONS.md #25.
/// </summary>
public static partial class PhoneNumbers
{
    /// <summary>Câu tiếng Việt duy nhất cho lỗi SĐT; Web lấy nguyên văn để không phải tự viết lại.</summary>
    public const string ErrorMessage =
        "Số điện thoại phải gồm 10-11 chữ số và bắt đầu bằng 0 (ví dụ 0912345678).";

    [GeneratedRegex(@"^0\d{9,10}$")]
    private static partial Regex MobileRegex();

    /// <summary>Bỏ khoảng trắng, dấu chấm/gạch thường có khi người dùng gõ số điện thoại.</summary>
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim()
            .Replace(" ", string.Empty)
            .Replace(".", string.Empty)
            .Replace("-", string.Empty);

    public static bool IsUsable(string? normalizedPhone) => MobileRegex().IsMatch(normalizedPhone ?? string.Empty);
}
