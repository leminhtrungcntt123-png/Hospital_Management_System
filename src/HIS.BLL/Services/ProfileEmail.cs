using System.Net.Mail;

namespace HIS.BLL.Services;

/// <summary>Chuẩn hóa và kiểm tra địa chỉ email dùng cho hồ sơ và mã OTP.</summary>
public static class ProfileEmail
{
    /// <summary>Bỏ khoảng trắng và hạ chữ thường để so sánh/lưu không bị phân biệt hoa thường.</summary>
    public static string? Normalize(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>Đúng định dạng và vừa độ dài cột patients.email (VARCHAR(200)).</summary>
    public static bool IsUsable(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200)
        {
            return false;
        }

        try
        {
            return new MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Chuẩn hóa và giữ lại chỉ khi địa chỉ dùng được; dữ liệu cũ bẩn coi như chưa có email.</summary>
    public static string? NormalizeUsable(string? value)
    {
        string? normalized = Normalize(value);
        return normalized is not null && IsUsable(normalized) ? normalized : null;
    }
}
