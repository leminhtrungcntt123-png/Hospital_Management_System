using System.Text.Encodings.Web;
using System.Text.Json;
using HIS.Web.Helpers;

namespace HIS.Web.ViewModels.Booking;

// Các model dữ liệu nhỏ dùng cho quy trình đặt lịch.
// Hậu tố "Vm" để không trùng tên với entity trong HIS.Domain.
// Khi nối database thật, BookingService (BLL) sẽ trả dữ liệu rồi map sang các model này.

public class DepartmentVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Tên icon Lucide, ví dụ "heart-pulse".
    public string Icon { get; set; } = "stethoscope";

    /// <summary>Số chỗ còn lại trong tuần tới. 0 = chưa có lịch khả dụng.</summary>
    public int SlotsThisWeek { get; set; }
}

public class DoctorVm
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Experience { get; set; } = string.Empty;
    public string Initials => PersonName.Initials(FullName);
}

public class ShiftVm
{
    /// <summary>MORNING hoặc AFTERNOON (khớp DoctorSchedule.Shift).</summary>
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public int SlotsLeft { get; set; }

    /// <summary>AVAILABLE | FULL | NO_SCHEDULE</summary>
    public string Status { get; set; } = "NO_SCHEDULE";

    /// <summary>Chỉ có khi hệ thống đã xác định được bác sĩ theo lịch trực.</summary>
    public DoctorVm? Doctor { get; set; }
    public string Room { get; set; } = string.Empty;

    public bool IsSelectable => Status == "AVAILABLE";
}

public class DayVm
{
    public DateOnly Date { get; set; }

    /// <summary>available | few | full | off</summary>
    public string State { get; set; } = "off";
}

/// <summary>
/// Một khung giờ khám ở bước "chọn giờ". SlotsLeft là số suất A (đặt online) còn lại,
/// không phải tổng chỗ của khung vì phần còn lại dành cho bệnh nhân đến quầy lấy số T.
/// </summary>
public class SlotVm
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>AVAILABLE | FULL</summary>
    public string Status { get; set; } = "FULL";

    public int SlotsLeft { get; set; }
    public int OnlineQuota { get; set; }

    public DoctorVm? Doctor { get; set; }
    public string Room { get; set; } = string.Empty;

    public bool IsSelectable => Status == "AVAILABLE" && SlotsLeft > 0;
}

public class PatientVm
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>Nhãn hiển thị: Bản thân, Mẹ, Con, Người thân...</summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>Mã quan hệ theo DB (SELF, PARENT...) để mặc định chọn hồ sơ của chính mình.</summary>
    public string RelationshipCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Phone { get; set; } = string.Empty;

    /// <summary>ACTIVE = hồ sơ đã được xác minh giấy tờ.</summary>
    public bool IsVerified { get; set; }

    /// <summary>ACTIVE hoặc PENDING: spec cho phép đặt lịch với hồ sơ chờ quầy duyệt giấy tờ.</summary>
    public bool IsBookable { get; set; }

    /// <summary>PENDING: hiển thị note "chờ quầy xác nhận giấy tờ khi bệnh nhân tới".</summary>
    public bool NeedsApproval { get; set; }

    public string Initials => PersonName.Initials(FullName);
}

/// <summary>Những gì người dùng đã chọn, lưu trong Session giữa các bước.</summary>
public class BookingState
{
    public int? DepartmentId { get; set; }
    public DateOnly? Date { get; set; }
    public string? Shift { get; set; }

    /// <summary>Khung giờ đã chọn ở bước 3; chính là suất A sẽ bị giữ khi xác nhận.</summary>
    public int? SlotId { get; set; }

    public int? PatientId { get; set; }
}

internal static class PersonName
{
    /// <summary>"Nguyễn Minh Anh" -> "MA" (chữ cái đầu của hai từ cuối).</summary>
    public static string Initials(string fullName)
        => string.Concat(fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(2)
            .Select(w => char.ToUpperInvariant(w[0])));
}

public static class DoctorVmExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Giữ nguyên chữ tiếng Việt; Razor sẽ tự HTML-encode khi đưa vào attribute.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>JSON đặt vào data-doctor-open để booking.js mở modal thông tin bác sĩ.</summary>
    public static string ToModalJson(this DoctorVm d, string timeRange, string room, DateOnly date)
        => JsonSerializer.Serialize(new
        {
            name = d.FullName,
            title = d.Title,
            specialty = d.Specialty,
            experience = d.Experience,
            initials = d.Initials,
            time = timeRange,
            room,
            date = VietnameseFormat.ShortDate(date)
        }, JsonOptions);
}