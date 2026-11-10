using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Options;

/// <summary>
/// Thông số nghiệp vụ đặt lịch theo cơ chế đánh số A/T. Giá trị mặc định là thông số đã chốt;
/// host ghi đè qua section "Booking" trong appsettings.
/// </summary>
public class BookingOptions
{
    public const string SectionName = "Booking";

    /// <summary>Phần trăm suất của khung giờ dành cho kênh A (đặt online); phần còn lại là suất T.</summary>
    public int OnlineRatioPct { get; set; } = 60;

    /// <summary>Số phút trước giờ bắt đầu khung mà suất A chưa check-in bị giải phóng cho T.</summary>
    public int ReleaseCutoffMinutes { get; set; } = 10;

    /// <summary>Bệnh nhân khẩn (ưu tiên mức 2) tối đa được xếp trước bấy nhiêu lượt đang chờ.</summary>
    public int MaxOvertakeAhead { get; set; } = 3;

    /// <summary>Một lượt hẹn bị chen lên trước tối đa bấy nhiêu lần thì không bị chen thêm.</summary>
    public int MaxTimesOvertaken { get; set; } = 3;

    /// <summary>Chỉ đặt được trước giờ này của ngày liền trước ngày khám (15 = trước 15:00).</summary>
    public int BookingCutoffHour { get; set; } = 15;

    /// <summary>Đặt khám tối đa bao nhiêu ngày kể từ hôm nay.</summary>
    public int MaxAdvanceDays { get; set; } = 30;

    /// <summary>Số hồ sơ tối đa gắn với một tài khoản; REVOKED không đếm.</summary>
    public int MaxProfilesPerAccount { get; set; } = 5;

    /// <summary>Số hồ sơ tối đa tính theo SĐT chủ tài khoản (SĐT của hồ sơ "Bản thân").</summary>
    public int MaxProfilesPerOwnerPhone { get; set; } = 5;

    /// <summary>Số hồ sơ tối đa được phép dùng chung một địa chỉ email (mặc định 3 hồ sơ cho gia đình: bản thân + 2 người thân).</summary>
    public int MaxProfilesPerEmail { get; set; } = 3;

    /// <summary>
    /// Người dưới độ tuổi này (chưa đến tuổi được cấp CCCD) bắt buộc phải khai CCCD của một người thân
    /// đã có hồ sơ ACTIVE thuộc chính tài khoản. Từ độ tuổi này trở lên thì bắt buộc có CCCD của chính mình.
    /// </summary>
    public int OwnIdFromAge { get; set; } = 14;

    /// <summary>Số hồ sơ người bệnh khác nhau được đặt trong một ngày (00:00-23:59).</summary>
    public int MaxRelativesPerDay { get; set; } = 3;

    /// <summary>Số lượt đăng ký tối đa trong một ngày (00:00-23:59), chỉ tính lượt đặt thành công.</summary>
    public int MaxBookingsPerDay { get; set; } = 6;

    /// <summary>Trẻ dưới độ tuổi này được xếp ưu tiên mức 1.</summary>
    public int PriorityChildUnderAge { get; set; } = 6;

    /// <summary>Người từ độ tuổi này trở lên được xếp ưu tiên mức 1.</summary>
    public int PrioritySeniorFromAge { get; set; } = 70;
}

public static class BookingOptionsRegistration
{
    public static IServiceCollection AddBookingOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BookingOptions>()
            .Bind(configuration.GetSection(BookingOptions.SectionName))
            .Validate(o => o.OnlineRatioPct is >= 0 and <= 100, "OnlineRatioPct phải nằm trong 0..100.")
            .Validate(o => o.ReleaseCutoffMinutes >= 0, "ReleaseCutoffMinutes không được âm.")
            .Validate(o => o.MaxOvertakeAhead >= 1, "MaxOvertakeAhead phải >= 1.")
            .Validate(o => o.MaxTimesOvertaken >= 1, "MaxTimesOvertaken phải >= 1.")
            .Validate(o => o.BookingCutoffHour is >= 0 and <= 23, "BookingCutoffHour phải nằm trong 0..23.")
            .Validate(o => o.MaxAdvanceDays >= 1, "MaxAdvanceDays phải >= 1.")
            .Validate(o => o.MaxProfilesPerAccount >= 1, "MaxProfilesPerAccount phải >= 1.")
            .Validate(o => o.MaxProfilesPerOwnerPhone >= 1, "MaxProfilesPerOwnerPhone phải >= 1.")
            .Validate(o => o.MaxProfilesPerEmail >= 1, "MaxProfilesPerEmail phải >= 1.")
            .Validate(o => o.OwnIdFromAge is >= 0 and <= 60, "OwnIdFromAge phải nằm trong 0..60.")
            .Validate(o => o.MaxRelativesPerDay >= 1, "MaxRelativesPerDay phải >= 1.")
            .Validate(o => o.MaxBookingsPerDay >= o.MaxRelativesPerDay,
                      "MaxBookingsPerDay phải >= MaxRelativesPerDay vì một người thân có thể đặt nhiều lượt.")
            .Validate(o => o.PriorityChildUnderAge >= 0, "PriorityChildUnderAge không được âm.")
            .Validate(o => o.PrioritySeniorFromAge > o.PriorityChildUnderAge, "PrioritySeniorFromAge phải lớn hơn PriorityChildUnderAge.")
            .ValidateOnStart();

        return services;
    }
}
