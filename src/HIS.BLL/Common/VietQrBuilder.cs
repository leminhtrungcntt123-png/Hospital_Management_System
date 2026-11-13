using System.Text;
using System.Text.RegularExpressions;

namespace HIS.BLL.Common;

/// <summary>
/// Trình tạo mã VietQR theo chuẩn EMVCo QR Code (QRIBFTTA / Napas 247).
/// Tương thích tham chiếu từ WebBanSach (Utils.cs).
/// </summary>
public static class VietQrBuilder
{
    private static readonly Dictionary<string, string> BankBins = new(StringComparer.OrdinalIgnoreCase)
    {
        // Big 4
        ["ICB"] = "970415",
        ["VIETINBANK"] = "970415",
        ["VCB"] = "970436",
        ["VIETCOMBANK"] = "970436",
        ["BIDV"] = "970418",
        ["VBA"] = "970405",
        ["AGRIBANK"] = "970405",

        // Ngân hàng TMCP lớn
        ["MB"] = "970422",
        ["MBBANK"] = "970422",
        ["TCB"] = "970407",
        ["TECHCOMBANK"] = "970407",
        ["ACB"] = "970416",
        ["VPB"] = "970432",
        ["VPBANK"] = "970432",
        ["TPB"] = "970423",
        ["TPBANK"] = "970423",
        ["STB"] = "970403",
        ["SACOMBANK"] = "970403",
        ["HDB"] = "970437",
        ["HDBANK"] = "970437",
        ["OCB"] = "970448",
        ["VIB"] = "970441",
        ["SHB"] = "970443",
        ["MSB"] = "970426",
        ["EIB"] = "970431",
        ["EXIMBANK"] = "970431",
        ["VCCB"] = "970454",
        ["VIETCAPITALBANK"] = "970454",
        ["BVBank"] = "970454",
        ["SCB"] = "970429",
        ["LPB"] = "970449",
        ["LPBANK"] = "970449",
        ["SEAB"] = "970440",
        ["SEABANK"] = "970440",
        ["BAB"] = "970409",
        ["BACABANK"] = "970409",
        ["NCB"] = "970419",
        ["ABB"] = "970425",
        ["ABBANK"] = "970425",
        ["VAB"] = "970427",
        ["VIETABANK"] = "970427",
        ["NAB"] = "970428",
        ["NAMABANK"] = "970428",
        ["PGB"] = "970430",
        ["PGBANK"] = "970430",
        ["VIETBANK"] = "970433",
        ["BVB"] = "970438",
        ["BAOVIETBANK"] = "970438",
        ["KLB"] = "970452",
        ["KIENLONGBANK"] = "970452",
        ["SGICB"] = "970400",
        ["SAIGONBANK"] = "970400",

        // Ngân hàng số & Fintech
        ["CAKE"] = "546034",
        ["UBANK"] = "546035",
        ["TIMO"] = "963388",
        ["VIETTELMONEY"] = "971005",
        ["VTLMONEY"] = "971005",
        ["VNPTMONEY"] = "971011",
        ["VIKKI"] = "970406",
        ["PVCOMBANK"] = "970412",
        ["PVCB"] = "970412"
    };

    /// <summary>
    /// Tìm mã BIN 6 chữ số theo tên hoặc mã viết tắt của ngân hàng.
    /// Nếu truyền thẳng BIN 6 chữ số thì trả về nguyên bản.
    /// </summary>
    public static string GetBankBin(string bankCodeOrBin)
    {
        if (string.IsNullOrWhiteSpace(bankCodeOrBin))
            return string.Empty;

        var trimmed = bankCodeOrBin.Trim();
        if (trimmed.Length == 6 && trimmed.All(char.IsDigit))
            return trimmed;

        return BankBins.TryGetValue(trimmed, out var bin) ? bin : string.Empty;
    }

    /// <summary>
    /// Tạo chuỗi VietQR EMVCo với số tiền và nội dung chuyển khoản.
    /// </summary>
    /// <param name="bankCodeOrBin">Mã viết tắt (VD: "ICB", "VCB") hoặc mã BIN (6 số)</param>
    /// <param name="accountNumber">Số tài khoản thụ hưởng</param>
    /// <param name="amount">Số tiền thanh toán (làm tròn nguyên VND)</param>
    /// <param name="message">Nội dung chuyển khoản (tối đa ~25 ký tự để hiển thị tốt nhất trên app ngân hàng)</param>
    public static string GeneratePayload(string bankCodeOrBin, string accountNumber, decimal amount, string message)
    {
        var bankBin = GetBankBin(bankCodeOrBin);
        if (string.IsNullOrEmpty(bankBin))
            throw new ArgumentException($"Không tìm thấy mã BIN hợp lệ cho ngân hàng: {bankCodeOrBin}", nameof(bankCodeOrBin));

        var cleanAcc = (accountNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanAcc))
            throw new ArgumentException("Số tài khoản không được để trống.", nameof(accountNumber));

        var cleanMsg = SanitizeMessage(message);
        var amountStr = Math.Max(0, Math.Round(amount, 0, MidpointRounding.AwayFromZero)).ToString("F0");

        // Sub-field 01 trong Field 38: [00: BIN] [01: AccountNumber]
        var path12 = new StringBuilder()
            .Append("00").Append(bankBin.Length.ToString("D2")).Append(bankBin)
            .Append("01").Append(cleanAcc.Length.ToString("D2")).Append(cleanAcc)
            .ToString();

        // Field 38 (Merchant Account Info - VietQR Napas)
        // [00: GUID Napas = A000000727] [01: Beneficiary Info] [02: Service Code = QRIBFTTA]
        var path11 = new StringBuilder()
            .Append("0010A000000727")
            .Append("01").Append(path12.Length.ToString("D2")).Append(path12)
            .Append("0208QRIBFTTA")
            .ToString();

        var field38 = new StringBuilder()
            .Append("38").Append(path11.Length.ToString("D2")).Append(path11)
            .ToString();

        // Field 62 (Additional Data) -> Sub-field 08: Purpose of transaction / nội dung
        var field62 = string.Empty;
        if (!string.IsNullOrEmpty(cleanMsg))
        {
            var sub08 = new StringBuilder()
                .Append("08").Append(cleanMsg.Length.ToString("D2")).Append(cleanMsg)
                .ToString();
            field62 = new StringBuilder()
                .Append("62").Append(sub08.Length.ToString("D2")).Append(sub08)
                .ToString();
        }

        // Field 53 (704 = VND), Field 54 (Amount), Field 58 (VN)
        var path2 = new StringBuilder()
            .Append("5303704");

        if (amount > 0)
        {
            path2.Append("54").Append(amountStr.Length.ToString("D2")).Append(amountStr);
        }

        path2.Append("5802VN")
             .Append(field62);

        // Ghép toàn bộ chuỗi trước CRC
        // 00: 000201 (Format indicator)
        // 01: 010212 (Dynamic QR, 11 = Static)
        var sb = new StringBuilder()
            .Append("000201")
            .Append("010212")
            .Append(field38)
            .Append(path2)
            .Append("6304");

        var crc = ComputeCrc16(sb.ToString());
        sb.Append(crc);

        return sb.ToString();
    }

    /// <summary>
    /// Tính toán CRC16-CCITT (0xFFFF, Poly 0x1021) theo ISO/IEC 13239
    /// </summary>
    public static string ComputeCrc16(string text)
    {
        ushort crc = 0xFFFF;
        const ushort polynomial = 0x1021;
        var bytes = Encoding.UTF8.GetBytes(text);

        foreach (var b in bytes)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ polynomial);
                else
                    crc = (ushort)(crc << 1);
            }
        }

        return crc.ToString("X4");
    }

    /// <summary>
    /// Chuẩn hóa nội dung chuyển khoản: loại bỏ dấu tiếng Việt, chỉ giữ chữ số/chữ cái và dấu cách, giới hạn độ dài.
    /// </summary>
    public static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        // Bỏ dấu tiếng Việt
        var text = message.Normalize(NormalizationForm.FormD);
        var chars = text.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray();
        var clean = new string(chars).Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');

        // Chỉ giữ chữ cái, số, dấu cách
        clean = Regex.Replace(clean, @"[^a-zA-Z0-9\s]", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        if (clean.Length > 25)
            clean = clean[..25].Trim();

        return clean;
    }
}

