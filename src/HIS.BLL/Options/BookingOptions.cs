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

    /// <summary>Hủy trước bấy nhiêu giờ thì không tính là hủy trễ.</summary>
    public int FreeCancelHours { get; set; } = 24;

    /// <summary>Số lần hủy trễ cho phép trong một tháng dương lịch.</summary>
    public int MaxLateCancelsPerMonth { get; set; } = 1;

    /// <summary>Số ngày chặn đặt lịch web khi vượt hạn mức.</summary>
    public int BookingBlockDays { get; set; } = 45;

    /// <summary>Trẻ dưới độ tuổi này được xếp ưu tiên mức 1.</summary>
    public int PriorityChildUnderAge { get; set; } = 6;

    /// <summary>Người từ độ tuổi này trở lên được xếp ưu tiên mức 1.</summary>
    public int PrioritySeniorFromAge { get; set; } = 70;

    /// <summary>Số lần NO_SHOW/EXPIRED trong cửa sổ khiến tài khoản bị chặn đặt web.</summary>
    public int MaxNoShowsBeforeBlock { get; set; } = 3;

    /// <summary>Cửa sổ ngày dùng để đếm NO_SHOW/EXPIRED.</summary>
    public int NoShowWindowDays { get; set; } = 90;
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
            .Validate(o => o.FreeCancelHours >= 0, "FreeCancelHours không được âm.")
            .Validate(o => o.MaxLateCancelsPerMonth >= 1, "MaxLateCancelsPerMonth phải >= 1.")
            .Validate(o => o.BookingBlockDays >= 1, "BookingBlockDays phải >= 1.")
            .Validate(o => o.PriorityChildUnderAge >= 0, "PriorityChildUnderAge không được âm.")
            .Validate(o => o.PrioritySeniorFromAge > o.PriorityChildUnderAge, "PrioritySeniorFromAge phải lớn hơn PriorityChildUnderAge.")
            .Validate(o => o.MaxNoShowsBeforeBlock >= 1, "MaxNoShowsBeforeBlock phải >= 1.")
            .Validate(o => o.NoShowWindowDays >= 1, "NoShowWindowDays phải >= 1.")
            .ValidateOnStart();

        return services;
    }
}
