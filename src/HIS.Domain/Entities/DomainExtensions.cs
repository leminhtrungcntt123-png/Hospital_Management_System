using System.Security.Cryptography;

namespace HIS.Domain;

/// <summary>
/// nhãn tiếng Việt của các enum danh mục. Tầng UI chỉ việc gọi ToLabel(),
/// không được so sánh chuỗi code trong view.
/// </summary>
public static class DomainLabels
{
    public static string ToLabel(this Gender gender) => gender switch
    {
        Gender.Male => "Nam",
        Gender.Female => "Nữ",
        _ => "Khác"
    };

    public static string ToLabel(this Gender? gender) =>
        gender is null ? string.Empty : gender.Value.ToLabel();

    public static string ToLabel(this ExamShift shift) => shift switch
    {
        ExamShift.Morning => "Ca sáng",
        _ => "Ca chiều"
    };

    public static string ToLabel(this ExamShift? shift) =>
        shift is null ? "Cả ngày" : shift.Value.ToLabel();

    public static string ToLabel(this SlotStatus status) => status switch
    {
        SlotStatus.Available => "Còn chỗ",
        SlotStatus.Full => "Đã đầy",
        _ => "Không mở"
    };

    public static string ToLabel(this BookingChannel channel) => channel switch
    {
        BookingChannel.Web => "Đặt qua web",
        _ => "Tại quầy"
    };

    public static string ToLabel(this VisitType visitType) => visitType switch
    {
        VisitType.Scheduled => "Đặt lịch",
        _ => "Bốc số trực tiếp"
    };

    public static string ToLabel(this ScheduleExceptionType type) => type switch
    {
        ScheduleExceptionType.Leave => "Nghỉ phép",
        ScheduleExceptionType.Training => "Đào tạo",
        ScheduleExceptionType.Surgery => "Phẫu thuật",
        _ => "Đổi ca"
    };

    public static string ToLabel(this ScheduleExceptionStatus status) => status switch
    {
        ScheduleExceptionStatus.Approved => "Đã duyệt",
        ScheduleExceptionStatus.Rejected => "Từ chối",
        _ => "Chờ duyệt"
    };

    public static string ToLabel(this PatientRelationship relationship) => relationship switch
    {
        PatientRelationship.Self => "Bản thân",
        PatientRelationship.Spouse => "Vợ / chồng",
        PatientRelationship.Child => "Con",
        PatientRelationship.Parent => "Bố / mẹ",
        PatientRelationship.Sibling => "Anh chị em",
        _ => "Khác"
    };

    public static string ToLabel(this LinkStatus status) => status switch
    {
        LinkStatus.Active => "Đã xác nhận",
        LinkStatus.Revoked => "Đã thu hồi",
        _ => "Chờ quầy xác nhận"
    };

    public static string ToLabel(this InvoiceCategory category) => category switch
    {
        InvoiceCategory.Examination => "Khám bệnh",
        InvoiceCategory.Medication => "Thuốc",
        _ => "Dịch vụ"
    };

    public static string ToLabel(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => "Đã thu đủ",
        InvoiceStatus.PartiallyPaid => "Thu một phần",
        _ => "Chưa thu"
    };

    public static string ToLabel(this PaymentType type) => type switch
    {
        PaymentType.Deposit => "Tạm ứng",
        PaymentType.Refund => "Hoàn trả",
        _ => "Thanh toán"
    };

    public static string ToLabel(this PaymentMethod method) => method switch
    {
        PaymentMethod.BankTransfer => "Chuyển khoản",
        _ => "Tiền mặt"
    };

    public static string ToLabel(this LabOrderStatus status) => status switch
    {
        LabOrderStatus.Completed => "Đã có kết quả",
        _ => "Chờ kết quả"
    };

    public static string ToLabel(this ReferralType type) => type switch
    {
        ReferralType.OutOfNetwork => "Trái tuyến",
        _ => "Đúng tuyến"
    };
}

/// <summary>
/// Chuyển tên enum PascalCase sang mã UPPER_SNAKE_CASE đúng như cột VARCHAR trong CSDL
/// (PartiallyPaid -> PARTIALLY_PAID). DAL dùng để ghi, BLL/DTO dùng khi cần chuỗi mã.
/// </summary>
public static class EnumCodes
{
    private static class Map<TEnum> where TEnum : struct, Enum
    {
        public static readonly Dictionary<string, TEnum> ByCode =
            Enum.GetValues<TEnum>().ToDictionary(value => value.ToDatabaseCode());
    }

    public static string ToDatabaseCode<TEnum>(this TEnum value)
        where TEnum : struct, Enum => ToUpperSnakeCase(value.ToString());

    /// <summary>Đọc chuỗi mã UPPER_SNAKE từ form/DTO thành enum; null nếu không nhận ra.</summary>
    public static TEnum? ParseDatabaseCode<TEnum>(string? code)
        where TEnum : struct, Enum
    {
        var key = code?.Trim().ToUpperInvariant();
        return key is not null && Map<TEnum>.ByCode.TryGetValue(key, out var value) ? value : null;
    }

    public static string ToUpperSnakeCase(string pascalCase)
    {
        var builder = new System.Text.StringBuilder(pascalCase.Length + 4);

        for (int i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(pascalCase[i - 1]))
            {
                builder.Append('_');
            }
            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }
}

/// <summary>
/// Mức ưu tiên của một lượt khám, khớp ck_appointments_priority_level (0..2).
/// Cấp cứu không phải một mức ưu tiên: ca cấp cứu không bốc số mà vào thẳng phòng khám.
/// </summary>
public static class PriorityLevels
{
    public const int Normal = 0;

    /// <summary>Hệ thống tự tính từ ngày sinh: trẻ dưới 6 tuổi hoặc người từ 70 tuổi.</summary>
    public const int AgePriority = 1;

    /// <summary>Chỉ nhân viên quầy được gán; được chen lên trước tối đa MaxOvertakeAhead người đang chờ.</summary>
    public const int StaffPriority = 2;

    public const int Max = StaffPriority;

    public static string ToPriorityVietnamese(this int priorityLevel) => priorityLevel switch
    {
        AgePriority => "Trẻ em / người cao tuổi",
        StaffPriority => "Nhân viên chỉ định ưu tiên",
        _ => "Bình thường"
    };
}

/// <summary>
/// Tính tuổi theo ngày sinh, dùng để quyết định mức ưu tiên 1.
/// </summary>
public static class PatientAge
{
    public static int YearsOn(DateOnly dateOfBirth, DateOnly on)
    {
        int years = on.Year - dateOfBirth.Year;

        if (dateOfBirth > on.AddYears(-years))
        {
            years--;
        }

        return years;
    }

    public static bool NeedsAgePriority(DateOnly dateOfBirth, DateOnly on, int childUnderAge, int seniorFromAge)
    {
        int age = YearsOn(dateOfBirth, on);
        return age < childUnderAge || age >= seniorFromAge;
    }
}

public static class PatientExtensions
{
    // Extension method: Gọi trực tiếp từ bất kỳ số int nào (ví dụ: patient.Id.ToPatientCode())
    public static string ToPatientCode(this int patientId) => $"BN{patientId:D6}";
}

/// <summary>
/// Số thứ tự hàng đợi: A = đặt online, T = đến trực tiếp.
/// Số là vị trí cố định trong ca của phòng, không phải bộ đếm tăng dần.
/// </summary>
public static class QueueNumberPrefix
{
    public const char Web = 'A';
    public const char OnSite = 'T';

    public static char For(BookingChannel channel) =>
        channel == BookingChannel.Web ? Web : OnSite;

    public static string Format(BookingChannel channel, int positionInShift) =>
        $"{For(channel)}{positionInShift:D2}";
}

/// <summary>
/// Chia suất và tính số hiển thị cho một ca của phòng.
/// Số hiển thị chạy liên tục qua các khung giờ: khung 07:00-08:00 (capacity 5) chiếm 1..5,
/// khung 08:00-09:00 chiếm 6..10, nên màn gọi số không bao giờ gặp hai người cùng một số.
/// </summary>
public static class QueuePositions
{
    /// <summary>Suất A = làm tròn(capacity * tỉ lệ online%). Capacity 5 với 60% ra 3A + 2T; capacity 4 ra 2A + 2T.</summary>
    public static int OnlineQuota(int capacity, int onlineRatioPct)
    {
        if (capacity <= 0)
        {
            return 0;
        }

        int ratio = Math.Clamp(onlineRatioPct, 0, 100);
        int quota = (int)Math.Round(capacity * ratio / 100.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(quota, 0, capacity);
    }

    /// <summary>Vị trí trong khung giờ: A xếp trước (1..onlineQuota), T xếp sau (onlineQuota+1..capacity).</summary>
    public static bool IsOnlinePosition(int slotPosition, int onlineQuota) => slotPosition <= onlineQuota;

    /// <summary>Số hiển thị trong ca = tổng capacity các khung giờ trước + vị trí trong khung giờ.</summary>
    public static int ShiftNumber(int capacityBefore, int slotPosition) => capacityBefore + slotPosition;
}

/// <summary>
/// Xác định ca khám theo giờ, dùng để nhóm các khung giờ của cùng một phòng trong ngày.
/// Ca sáng kết thúc trước 12:00, khớp cách sinh slot hiện tại; ca tối thuộc duty_shifts.
/// </summary>
public static class ExamShifts
{
    public const int MorningEndsAtHour = 12;

    public static ExamShift For(DateTime time) =>
        time.Hour < MorningEndsAtHour ? ExamShift.Morning : ExamShift.Afternoon;
}

/// <summary>
/// Sinh mã lịch hẹn không đoán được theo khuôn HIS-{yyyyMMdd}-{6 ký tự base32}.
/// Chuỗi base32 dùng bảng chữ Crockford (bỏ I, L, O, U) để đọc/ghi lại không nhầm.
/// Caller phải thử lại khi vi phạm unique; spec cho phép tối đa 5 lần.
/// </summary>
public static class AppointmentCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int CodeLength = 6;

    public static string Generate(DateOnly day)
    {
        Span<char> suffix = stackalloc char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"HIS-{day:yyyyMMdd}-{new string(suffix)}";
    }
}
