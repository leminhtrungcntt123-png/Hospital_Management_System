using HIS.BLL.Options;
using HIS.BLL.Services;
using Xunit;

namespace HIS.Tests;

/// <summary>
/// Test thuần, không chạm CSDL: so sánh mã OTP và kiểm tra/chuẩn hóa email trước khi đem đi gửi.
/// </summary>
public class OtpCodeTests
{
    [Theory]
    [InlineData("123456", "123456", true)]
    [InlineData("123456", "  123 456 ", true)]
    [InlineData("123456", "12345a6", true)]
    [InlineData("123456", "1234567", false)]
    [InlineData("123456", "12345", false)]
    [InlineData("123456", "", false)]
    [InlineData("123456", null, false)]
    [InlineData(null, "123456", false)]
    public void So_sanh_ma_chi_chap_nhan_dung_du_so_chu(string? expected, string? input, bool ok)
        => Assert.Equal(ok, OtpCodes.Matches(expected, input));

    [Theory]
    [InlineData("  Nguyen.A@Gmail.COM  ", "nguyen.a@gmail.com")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Email_chuan_hoa_ve_chu_thuong_va_loi_khoang_trang(string? input, string? expected)
        => Assert.Equal(expected, ProfileEmail.Normalize(input));

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("nguyen.a@gmail.com", true)]
    [InlineData("khong co a mui", false)]
    [InlineData("hai@admin@gmail.com", false)]
    [InlineData("", false)]
    public void Email_chi_nhan_mot_dia_chi_duy_nhat(string email, bool usable)
        => Assert.Equal(usable, ProfileEmail.IsUsable(email));

    [Fact]
    public void Email_dai_hon_cot_thi_boi_tuc()
        => Assert.False(ProfileEmail.IsUsable(new string('a', 196) + "@x.vn"));

    /// <summary>
    /// Định dạng dùng cho dấu vết notifications và màn "Nhập mã xác thực": giữ chữ cái đầu + domain
    /// để người nhận còn nhận ra địa chỉ của mình, nhưng dump DB không còn email đầy đủ.
    /// </summary>
    [Theory]
    [InlineData("minhchi1009@gmail.com", "m***@gmail.com")]
    [InlineData("  Nguyen.A@Gmail.COM  ", "n***@gmail.com")]
    [InlineData("a@b.co", "a***@b.co")]
    [InlineData("khong.phai.email", "k***")]
    [InlineData("x", "***")]
    [InlineData(null, "(trống)")]
    [InlineData("   ", "(trống)")]
    public void Mask_email_chi_giu_chu_dau_va_domain(string? input, string expected)
        => Assert.Equal(expected, ProfileEmail.Mask(input));

    // Luật SĐT của hồ sơ giờ chỉ có MỘT chỗ (PhoneNumbers) nên Web không còn tự viết regex riêng.
    [Theory]
    [InlineData("0912345678", true)]
    [InlineData("01234567890", true)]
    [InlineData("091234567", false)]
    [InlineData("091234567890", false)]
    [InlineData("1912345678", false)]
    [InlineData("091234567a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void So_dien_tho_ho_so_chi_nhan_10_hoac_11_so_bat_dau_bang_0(string? phone, bool ok)
        => Assert.Equal(ok, PhoneNumbers.IsUsable(phone));

    [Fact]
    public void So_dien_tho_chuan_hoa_bo_khoang_trang_dau_cham_va_gach()
        => Assert.Equal("0912345678", PhoneNumbers.Normalize(" 09 12.34-56 78 "));

    /// <summary>
    /// Cờ in mã ra màn hình là của bản demo Development nên MẶC ĐỊNH phải tắt: máy nào thiếu
    /// appsettings.Development.json (hoặc appsettings.Otp.local.json) cũng không được hiển thị mã,
    /// nếu không ai biết mã bệnh nhân cũng tự đọc mã và xác thực hồ sơ của người khác.
    /// </summary>
    [Fact]
    public void Mac_dinh_khong_in_ma_ra_man_hinh()
        => Assert.False(new OtpOptions().RevealCodeOnScreen);
}
