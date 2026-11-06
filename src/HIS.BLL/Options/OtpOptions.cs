using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HIS.BLL.Options;

/// <summary>
/// Thông số mã OTP cấp khi thêm hồ sơ. Host ghi đè qua section "Otp" trong appsettings
/// (bản production tắt RevealCodeOnScreen để không lộ mã ra màn hình).
/// </summary>
public class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Số chữ của mã gửi đi.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>Số phút mã còn dùng được.</summary>
    public int LifetimeMinutes { get; set; } = 5;

    /// <summary>Số lần nhập sai tối đa cho một mã.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Số giây phải chờ giữa hai lần xin gửi lại mã.</summary>
    public int ResendDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Số lần xin mã tối đa của MỘT tài khoản trong cửa sổ IssueWindowMinutes. Chặn người dùng
    /// (hoặc script) bấm "gửi lại" vô hạn, vì mỗi lần cấp mã đều reset số lần nhập và ghi thêm một dòng notifications.
    /// </summary>
    public int MaxIssuesPerWindow { get; set; } = 5;

    /// <summary>Độ dài cửa sổ (phút) đếm số lần xin mã ở trên.</summary>
    public int IssueWindowMinutes { get; set; } = 15;

    /// <summary>
    /// TRUE = in mã ra màn hình "Nhập mã xác thực". Mặc định FALSE vì ai biết mã BN cũng đọc được mã ngay
    /// trên màn hình mà không cần chứng minh quyền sở hữu hộp thư; bản dev chỉ bật cờ này qua
    /// appsettings.Development.json (xem HIS.Web/Program.cs, cờ bật ngoài Development là lỗi khởi động).
    /// </summary>
    public bool RevealCodeOnScreen { get; set; }
}

public static class OtpOptionsRegistration
{
    public static IServiceCollection AddOtpOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .Validate(o => o.CodeLength is >= 4 and <= 8, "CodeLength phải nằm trong 4..8.")
            .Validate(o => o.LifetimeMinutes >= 1, "LifetimeMinutes phải >= 1.")
            .Validate(o => o.MaxAttempts >= 1, "MaxAttempts phải >= 1.")
            .Validate(o => o.ResendDelaySeconds >= 0, "ResendDelaySeconds không được âm.")
            .Validate(o => o.MaxIssuesPerWindow >= 1, "MaxIssuesPerWindow phải >= 1.")
            .Validate(o => o.IssueWindowMinutes >= 1, "IssueWindowMinutes phải >= 1.")
            .ValidateOnStart();

        return services;
    }
}
