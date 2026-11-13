using HIS.BLL.Common;
using QRCoder;
using Xunit;

namespace HIS.Tests.Unit;

public class VietQrTests
{
    [Fact]
    public void GetBankBin_KnownCodes_ReturnsCorrectBin()
    {
        Assert.Equal("970415", VietQrBuilder.GetBankBin("ICB"));
        Assert.Equal("970415", VietQrBuilder.GetBankBin("VIETINBANK"));
        Assert.Equal("970436", VietQrBuilder.GetBankBin("VCB"));
        Assert.Equal("970436", VietQrBuilder.GetBankBin("VIETCOMBANK"));
        Assert.Equal("970422", VietQrBuilder.GetBankBin("MB"));
        Assert.Equal("970407", VietQrBuilder.GetBankBin("TCB"));
        Assert.Equal("970418", VietQrBuilder.GetBankBin("BIDV"));
        Assert.Equal("970405", VietQrBuilder.GetBankBin("AGRIBANK"));
        // Direct BIN
        Assert.Equal("970415", VietQrBuilder.GetBankBin("970415"));
    }

    [Fact]
    public void SanitizeMessage_RemovesVietnameseAccentsAndSpecialCharacters()
    {
        var input = "Thanh toán viện phí: HD-000123!";
        var clean = VietQrBuilder.SanitizeMessage(input);
        Assert.Equal("Thanh toan vien phi HD 00", clean[..Math.Min(25, clean.Length)]);
    }

    [Fact]
    public void GeneratePayload_ValidParameters_ProducesValidEmvQrString()
    {
        var payload = VietQrBuilder.GeneratePayload("VIETINBANK", "119002953584", 150000m, "HIS HD0001");

        Assert.NotNull(payload);
        Assert.StartsWith("000201010212", payload);
        Assert.Contains("0010A000000727", payload); // Napas GUID
        Assert.Contains("970415", payload);          // VietinBank BIN
        Assert.Contains("119002953584", payload);    // Account
        Assert.Contains("5303704", payload);         // VND
        Assert.Contains("5406150000", payload);      // Amount 150000
        Assert.Contains("5802VN", payload);          // VN
        Assert.Contains("6304", payload);            // CRC indicator
        Assert.True(payload.Length > 80);
    }

    [Fact]
    public void QRCoder_GeneratesValidPngBytes()
    {
        var payload = VietQrBuilder.GeneratePayload("ICB", "119002953584", 250000m, "HIS APP01");
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        // PNG magic header: 0x89, 'P', 'N', 'G'
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }
}

