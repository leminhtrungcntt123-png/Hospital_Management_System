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
}
