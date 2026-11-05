using System.ComponentModel.DataAnnotations;
using HIS.BLL.Interfaces;
using HIS.Web.ViewModels.Booking;

namespace HIS.Web.ViewModels.HoSo;

// Màn "Hồ sơ của tôi": danh sách hồ sơ đang liên kết với tài khoản đang đăng nhập.
public class HoSoIndexViewModel
{
    public List<HoSoItemVm> Profiles { get; set; } = new();
}

public class HoSoItemVm
{
    public int PatientId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string RelationshipCode { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string GenderLabel { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    // ACTIVE / PENDING / REVOKED (khớp account_patient_profiles.status).
    public string StatusCode { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;

    // PENDING: hồ sơ chỉ dùng để đặt lịch, quầy duyệt giấy tờ rồi mới khám.
    public bool NeedsApproval { get; set; }

    // Theo quyết định #10: hồ sơ người khác chỉ hiện bệnh án khi người bệnh đồng ý.
    public bool CanViewRecords { get; set; }

    public int AppointmentCount { get; set; }

    public string Initials => PersonName.Initials(FullName);
}

// Màn chi tiết một hồ sơ: thông tin định danh + lịch sử khám riêng của hồ sơ đó.
public class HoSoChiTietViewModel
{
    public HoSoItemVm Profile { get; set; } = new();
    public List<AppointmentVm> History { get; set; } = new();
}

// Dữ liệu form "Tạo hồ sơ" (màn Hồ sơ): MỘT màn hình, HAI nhánh.
// Branch = "seen"  -> người này đã từng khám, chỉ nhập mã bệnh nhân để xin liên kết hồ sơ có sẵn.
// Branch = "new"   -> chưa từng khám, khai đầy đủ nhân thân.
// Form không có ô email: mã OTP lấy theo email tài khoản (nhánh new) hoặc email đã lưu trong hồ sơ (nhánh seen).
// Không dùng [Required]/[RegularExpression] vì trường của nhánh kia luôn rỗng khi submit;
// HoSoController kiểm tra theo từng nhánh rồi mới gọi BLL.
public class ThemHoSoRequest
{
    public const string SeenBranch = "seen";
    public const string NewBranch = "new";

    public string? Branch { get; set; }

    // Nhánh "đã từng khám": CHỈ mã bệnh nhân (BN######). Không cho dò hồ sơ bằng CCCD/SĐT/thẻ BHYT.
    [StringLength(20, ErrorMessage = "Mã bệnh nhân không được vượt quá 20 ký tự.")]
    public string? Keyword { get; set; }

    [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự.")]
    public string? FullName { get; set; }

    // Định dạng DD/MM/YYYY.
    public string? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? Relationship { get; set; }

    public string? Phone { get; set; }

    /// <summary>Email nhận mã xác thực, bắt buộc ở nhánh "chưa từng khám" và được lưu vào patients.email.</summary>
    [StringLength(200, ErrorMessage = "Email không được vượt quá 200 ký tự.")]
    public string? Email { get; set; }

    // CCCD/SĐT định danh; để trống với trẻ nhỏ chưa có thì phải có CCCD người thân.
    [StringLength(12, ErrorMessage = "CCCD không được vượt quá 12 ký tự.")]
    public string? NationalId { get; set; }

    [StringLength(12, ErrorMessage = "CCCD người thân không được vượt quá 12 ký tự.")]
    public string? GuardianNationalId { get; set; }

    // Chọn từ danh sách tỉnh/thành hoặc tự nhập địa chỉ đầy đủ.
    [StringLength(200, ErrorMessage = "Địa chỉ không được vượt quá 200 ký tự.")]
    public string? Address { get; set; }

    [StringLength(20, ErrorMessage = "Số thẻ BHYT không được vượt quá 20 ký tự.")]
    public string? InsuranceCardNumber { get; set; }

    public bool IsSeen => Branch == SeenBranch;
}

/// <summary>Mã OTP đang chờ người dùng nhập, giữ trong Session nên không cần bảng mới.</summary>
public sealed class PendingOtp
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public int Attempts { get; set; }
    public bool RevealCode { get; set; }

    /// <summary>
    /// Phiếu nhánh "chưa từng khám" còn chờ mã: DB chưa có hồ sơ nào, chỉ tạo khi nhập đúng mã.
    /// null = nhánh "đã từng khám" (liên kết PENDING đã có sẵn, nhập đúng mã thì chuyển ACTIVE).
    /// </summary>
    public ThemHoSoRequest? Draft { get; set; }
}

// Màn "Nhập mã xác thực" sau khi thêm hồ sơ.
public class XacOtpViewModel
{
    public string PatientName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int AttemptsLeft { get; set; }

    /// <summary>Số giây còn phải chờ trước khi xin mã mới (0 = xin được ngay).</summary>
    public int ResendWaitSeconds { get; set; }

    /// <summary>Chỉ khác rỗng khi Otp:RevealCodeOnScreen = true (demo chưa có kênh gửi thật).</summary>
    public string? RevealedCode { get; set; }
}

public sealed class RelationshipOptionVm
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
