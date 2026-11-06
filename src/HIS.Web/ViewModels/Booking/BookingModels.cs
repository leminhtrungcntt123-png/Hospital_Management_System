using System.Text.Encodings.Web;
using System.Text.Json;
using HIS.BLL.DTOs;
using HIS.Web.Helpers;

namespace HIS.Web.ViewModels.Booking;

// Các model dữ liệu nhỏ dùng cho quy trình đặt lịch.
// Hậu tố "Vm" để không trùng tên với entity trong HIS.Domain.
// Dữ liệu luôn lấy từ HIS.BLL (BookingService) rồi map sang các model này.

/// <summary>
/// Ba đường vào đặt khám. Bệnh nhân chọn một luồng ở trang Index, mọi luồng đều hội tụ
/// về đúng một slotId ở bước "khung giờ" nên phần lưu lịch chỉ có một đường.
/// </summary>
public static class BookingModes
{
    public const string ByDepartment = "khoa";
    public const string ByDate = "ngay";
    public const string ByDoctor = "bacsi";

    /// <summary>Giá trị doctorId của thẻ "Bác sĩ bất kỳ" — chỉ hiện ở luồng theo chuyên khoa.</summary>
    public const int AnyDoctorId = 0;

    public static bool IsValid(string? mode) =>
        mode is ByDepartment or ByDate or ByDoctor;
}

public class DepartmentVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Tên icon Lucide, ví dụ "heart-pulse".
    public string Icon { get; set; } = "stethoscope";

    /// <summary>Số suất đặt online còn lại đang được xét (cả cửa sổ, hoặc riêng ngày đã chọn).</summary>
    public int SlotsAvailable { get; set; }
}

public class DoctorVm
{
    /// <summary>
    /// Màn "chọn bác sĩ" có hai dạng: thẻ "Bác sĩ bất kỳ" (Id = 0) và các bác sĩ thật.
    /// Thẻ bất kỳ không có hồ sơ bác sĩ nên modal không được trống tên.
    /// </summary>
    public const string AnyDoctorName = "Bác sĩ bất kỳ";

    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>Chuyên môn sâu (doctors.specialty).</summary>
    public string Specialty { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;

    /// <summary>Các ngày trực đang hiệu lực, dạng "T2 · T5 · T7".</summary>
    public string DutyDays { get; set; } = string.Empty;

    /// <summary>Số suất đặt online còn lại trong phạm vi đang xét.</summary>
    public int SlotsAvailable { get; set; }

    /// <summary>Ngày gần nhất người này còn suất đặt online; null khi hết lịch đặt được.</summary>
    public DateOnly? NextAvailableDate { get; set; }

    public string Initials => PersonName.Initials(FullName);

    public static DoctorVm From(BookingDoctorDto d) => new()
    {
        Id = d.Id,
        FullName = d.FullName,
        Specialty = d.Specialty,
        DepartmentName = d.DepartmentName,
        DutyDays = d.DutyDays,
        SlotsAvailable = d.SlotsAvailable,
        NextAvailableDate = d.NextAvailableDate
    };

    /// <summary>Bác sĩ của một khung giờ: chỉ có những cột DTO khung giờ mang theo.</summary>
    public static DoctorVm From(BookingSlotDto s) => new()
    {
        Id = s.DoctorId,
        FullName = s.DoctorName ?? AnyDoctorName,
        Specialty = s.DoctorTitle ?? string.Empty
    };
}

/// <summary>Bác sĩ kèm các khung giờ của người đó trong một ngày — màn "Bác sĩ & khung giờ".</summary>
public class DoctorSlotsVm
{
    public DoctorVm Doctor { get; set; } = new();
    public string Room { get; set; } = string.Empty;
    public List<SlotVm> Slots { get; set; } = new();

    public int SlotsLeft => Slots.Sum(s => s.SlotsLeft);
}

public class DayVm
{
    public DateOnly Date { get; set; }

    /// <summary>available | few | full | off | closed</summary>
    public string State { get; set; } = "off";
}

/// <summary>
/// Một khung giờ khám. SlotsLeft là số suất A (đặt online) còn lại, không phải tổng ghế
/// vì phần còn lại dành cho bệnh nhân đến quầy lấy số T.
/// </summary>
public class SlotVm
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>"Ca sáng" / "Ca chiều" — dùng để nhóm chip giờ trên màn hình.</summary>
    public string ShiftLabel { get; set; } = string.Empty;

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

    /// <summary>ACTIVE = liên kết đã xác nhận, được đặt lịch.</summary>
    public bool IsVerified { get; set; }

    /// <summary>Chỉ ACTIVE. PENDING còn chờ mã OTP nên chưa chứng minh được quyền dùng hồ sơ.</summary>
    public bool IsBookable { get; set; }

    /// <summary>PENDING: hồ sơ chưa xác thực xong nên chưa đặt lịch được.</summary>
    public bool NeedsApproval { get; set; }

    /// <summary>PENDING: FullName/DateOfBirth/Phone đã bị controller xóa trước khi xuống view.</summary>
    public bool IsMasked { get; set; }

    public string DisplayName => IsMasked ? "Hồ sơ chờ xác thực" : FullName;
    public string DisplayDateOfBirth => IsMasked
        ? "••/••/••••"
        : VietnameseFormat.ShortDate(DateOfBirth);

    public string Initials => IsMasked ? "?" : PersonName.Initials(FullName);
}

/// <summary>Những gì người dùng đã chọn, lưu trong Session giữa các bước.</summary>
public class BookingState
{
    /// <summary>Một ba <see cref="BookingModes"/>; null khi chưa bấm vào lối vào nào.</summary>
    public string? Mode { get; set; }

    public int? DepartmentId { get; set; }

    /// <summary>Bác sĩ đã chọn; null nghĩa là "bác sĩ bất kỳ" (chỉ luồng theo chuyên khoa cho phép).</summary>
    public int? DoctorId { get; set; }

    /// <summary>Đã đi qua màn chọn bác sĩ hay chưa — phân biệt "chưa chọn" với "chọn bác sĩ bất kỳ".</summary>
    public bool DoctorChosen { get; set; }

    public DateOnly? Date { get; set; }

    /// <summary>Khung giờ đã chốt; chính là suất A sẽ bị giữ khi xác nhận.</summary>
    public int? SlotId { get; set; }

    public int? PatientId { get; set; }

    /// <summary>Hai khai báo bảo hiểm ở bước xác nhận; giữ trong Session để lùi bước rồi quay lại vẫn còn.</summary>
    public bool HasHealthInsurance { get; set; }
    public bool HasPrivateInsurance { get; set; }
}

/// <summary>
/// Nhãn khai báo bảo hiểm dùng chung cho bước xác nhận và hai màn lịch sử,
/// để cách diễn đạt "có BHYT / có bảo hiểm tư nhân" không lệch nhau giữa các trang.
/// </summary>
public static class InsuranceDeclarations
{
    public static string Summary(bool hasHealthInsurance, bool hasPrivateInsurance)
        => string.Join(" · ", new[]
        {
            hasHealthInsurance ? "BHYT" : null,
            hasPrivateInsurance ? "Bảo hiểm tư nhân" : null
        }.OfType<string>());
}

/// <summary>
/// Icon Lucide cho thẻ chuyên khoa. Bảng departments không có cột icon nên đây chỉ là
/// ánh xạ hiển thị theo tên khoa; khoa lạ vẫn có icon mặc định, không bao giờ trống.
/// </summary>
internal static class DepartmentIcons
{
    private static readonly (string Keyword, string Icon)[] ByKeyword =
    {
        ("tim mạch", "heart-pulse"),
        ("hô hấp", "wind"),
        ("tiêu hóa", "soup"),
        ("thận", "droplets"),
        ("da liễu", "hand"),
        ("mắt", "eye"),
        ("tai mũi họng", "ear"),
        ("răng", "smile"),
        ("nhi", "baby"),
        ("xét nghiệm", "flask-conical"),
        ("ngoại", "syringe"),
        ("nội", "stethoscope"),
        ("ung bướu", "activity"),
        ("sản", "flower-2"),
        ("cấp cứu", "ambulance"),
    };

    public static string For(string departmentName)
    {
        string name = departmentName.ToLowerInvariant();
        foreach (var (keyword, icon) in ByKeyword)
        {
            if (name.Contains(keyword)) return icon;
        }
        return "stethoscope";
    }
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

internal static class AppointmentVmMapper
{
    /// <summary>
    /// Map DTO lịch hẹn (BLL) sang ViewModel. Màn "Lịch khám" và màn "Hồ sơ" dùng chung để
    /// cách hiển thị ngày/giờ/số thứ tự của một lượt khám không lệch nhau giữa hai trang.
    /// </summary>
    public static AppointmentVm ToVm(this BookingAppointmentDto a) => new()
    {
        Id = a.Id,
        Code = a.Code,
        Status = a.Status,
        StatusLabel = a.StatusLabel,
        IsCancellable = a.IsCancellable,
        PatientName = a.PatientName,
        DepartmentName = a.DepartmentName,
        DateLabel = a.SlotStartTime is { } start ? VietnameseFormat.FullDate(DateOnly.FromDateTime(start)) : "Chưa xác định",
        TimeLabel = a.SlotStartTime is { } s && a.SlotEndTime is { } e ? $"{s:HH:mm} – {e:HH:mm}" : string.Empty,
        DoctorName = a.DoctorName,
        Room = a.RoomName,
        QueueNumber = a.QueueNumber,
        InsuranceSummary = InsuranceDeclarations.Summary(a.HasHealthInsurance, a.HasPrivateInsurance)
    };
}

public static class DoctorVmExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Giữ nguyên chữ tiếng Việt; Razor sẽ tự HTML-encode khi đưa vào attribute.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>JSON đặt vào data-doctor-open để booking.js mở modal thông tin bác sĩ.</summary>
    public static string ToModalJson(this DoctorVm d, string? timeRange = null, string? room = null, DateOnly? date = null, string? department = null)
        => JsonSerializer.Serialize(new
        {
            name = d.Id == BookingModes.AnyDoctorId ? DoctorVm.AnyDoctorName : d.FullName,
            specialty = d.Specialty,
            department = department ?? d.DepartmentName,
            duty = d.DutyDays,
            initials = d.Initials,
            time = timeRange ?? "Theo lịch trực",
            room = room ?? "Bệnh viện phân công",
            date = date is { } day ? VietnameseFormat.ShortDate(day) : string.Empty
        }, JsonOptions);
}
