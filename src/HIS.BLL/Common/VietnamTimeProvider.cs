namespace HIS.BLL.Common;

public class VietnamTimeProvider : TimeProvider
{
    private static readonly TimeZoneInfo VietnamTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time") ??
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public override TimeZoneInfo LocalTimeZone => VietnamTimeZone;
}

