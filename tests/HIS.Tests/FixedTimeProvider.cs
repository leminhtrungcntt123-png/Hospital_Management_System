namespace HIS.Tests;

/// <summary>
/// Đồng hồ cố định cho kiểm thử: mốc cắt giải phóng suất A và ngưỡng NO_SHOW phụ thuộc giờ,
/// nên bài test phải tự chọn giờ thay vì chạy theo đồng hồ máy.
/// LocalTimeZone là UTC để GetLocalNow().DateTime trả về đúng giá trị đã dựng.
/// </summary>
internal sealed class FixedTimeProvider(DateTime localNow) : TimeProvider
{
    private readonly DateTimeOffset _now = new(
        DateTime.SpecifyKind(localNow, DateTimeKind.Unspecified), TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
