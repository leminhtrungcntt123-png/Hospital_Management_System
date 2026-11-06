using System.Text;
using QRCoder;
using SkiaSharp;

namespace HIS.Web.Helpers
{
    public class Utils
    {
        /// <summary>
        /// Tạo VietQR code theo chuẩn EMV QR Code
        /// </summary>
        /// <param name="bankCode">Mã ngân hàng (VD: "OCB", "VCB")</param>
        /// <param name="accountNumber">Số tài khoản</param>
        /// <param name="amount">Số tiền</param>
        /// <param name="message">Nội dung chuyển khoản</param>
        /// <returns>Chuỗi VietQR code</returns>
        public static string GenerateQRCode(string bankCode, string accountNumber, string amount, string message)
        {
            // Tìm BIN code của ngân hàng
            string bankId = GetBankBin(bankCode);
            if (string.IsNullOrEmpty(bankId))
            {
                throw new ArgumentException($"Không tìm thấy mã ngân hàng cho: {bankCode}");
            }

            // Tạo path12 (Bank ID + Account)
            var path12StringBuilder = new StringBuilderBank()
                .Append("00")
                .Append(bankId.Length.ToString("D2"))
                .Append(bankId)
                .Append("01")
                .Append(accountNumber.Length.ToString("D2"))
                .Append(accountNumber);

            // Tạo path11 (Merchant Account Information)
            var path11StringBuilder = new StringBuilderBank()
                .Append("0010A000000727")
                .Append("01")
                .Append(path12StringBuilder.Length.ToString("D2"))
                .Append(path12StringBuilder.ToString())
                .Append("0208QRIBFTTA");

            // Tạo path1 (Field 38)
            var path1StringBuilder = new StringBuilderBank()
                .Append("38")
                .Append(path11StringBuilder.Length.ToString("D2"))
                .Append(path11StringBuilder.ToString());

            // Tạo path21 (Additional Data)
            var path21StringBuilder = new StringBuilderBank()
                .Append("08")
                .Append(message.Length.ToString("D2"))
                .Append(message);

            // Tạo path2 (Currency + Amount + Country + Additional Data)
            var path2StringBuilder = new StringBuilderBank()
                .Append("5303704") // Currency: VND
                .Append("54")
                .Append(amount.Length.ToString("D2"))
                .Append(amount)
                .Append("5802VN") // Country: Vietnam
                .Append("62")
                .Append(path21StringBuilder.Length.ToString("D2"))
                .Append(path21StringBuilder.ToString());

            // Tạo chuỗi QR chưa có CRC
            var stringBuilder = new StringBuilderBank()
                .Append("000201") // Payload Format Indicator
                .Append("010212") // Point of Initiation Method
                .Append(path1StringBuilder.ToString())
                .Append(path2StringBuilder.ToString())
                .Append("6304");

            // Tính CRC và hoàn thiện QR code
            string qrCodeContent = stringBuilder
                .Append(GenerateCheckSum(stringBuilder.ToString()).ToUpper())
                .ToString();

            return qrCodeContent;
        }

        /// <summary>
        /// Tính toán CRC16 checksum theo chuẩn ISO/IEC 13239
        /// </summary>
        private static string GenerateCheckSum(string text)
        {
            ushort crc = 0xFFFF;
            ushort polynomial = 0x1021;
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            foreach (byte b in bytes)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                    {
                        crc = (ushort)((crc << 1) ^ polynomial);
                    }
                    else
                    {
                        crc = (ushort)(crc << 1);
                    }
                }
            }

            return crc.ToString("X4");
        }

        /// <summary>
        /// Lấy BIN code của ngân hàng dựa trên mã ngân hàng
        /// </summary>
        private static string GetBankBin(string bankCode)
        {
            return bankCode.ToUpper() switch
            {
                // Ngân hàng lớn
                "ICB" or "VIETINBANK" => "970415",
                "VCB" or "VIETCOMBANK" => "970436",
                "BIDV" => "970418",
                "VBA" or "AGRIBANK" => "970405",
                "OCB" => "970448",
                "MB" or "MBBANK" => "970422",
                "TCB" or "TECHCOMBANK" => "970407",
                "ACB" => "970416",
                "VPB" or "VPBANK" => "970432",
                "TPB" or "TPBANK" => "970423",
                "STB" or "SACOMBANK" => "970403",
                "HDB" or "HDBANK" => "970437",
                "VCCB" or "VIETCAPITALBANK" => "970454",
                "SCB" => "970429",
                "VIB" => "970441",
                "SHB" => "970443",
                "EIB" or "EXIMBANK" => "970431",
                "MSB" => "970426",

                // Ngân hàng số
                "CAKE" => "546034",
                "UBANK" => "546035",
                "TIMO" => "963388",

                // Ví điện tử/Fintech
                "VTLMONEY" or "VIETTELMONEY" => "971005",
                "VNPTMONEY" => "971011",

                // Ngân hàng khác
                "SGICB" or "SAIGONBANK" => "970400",
                "BAB" or "BACABANK" => "970409",
                "PVDB" or "PVCOMBANK_PAY" => "971133",
                "PVCB" or "PVCOMBANK" => "970412",
                "MBV" => "970414",
                "NCB" => "970419",
                "SHBVN" or "SHINHANBANK" => "970424",
                "ABB" or "ABBANK" => "970425",
                "VAB" or "VIETABANK" => "970427",
                "NAB" or "NAMABANK" => "970428",
                "PGB" or "PGBANK" => "970430",
                "VIETBANK" => "970433",
                "BVB" or "BAOVIETBANK" => "970438",
                "SEAB" or "SEABANK" => "970440",
                "COOPBANK" => "970446",
                "LPB" or "LPBANK" => "970449",
                "KLB" or "KIENLONGBANK" => "970452",

                // Ngân hàng nước ngoài
                "KBANK" => "668888",
                "MAFC" => "977777",
                "KEBHANAHN" => "970467",
                "KEBHANAHCM" => "970466",
                "CITIBANK" => "533948",
                "VBSP" => "999888",
                "CBB" or "CBBANK" => "970444",
                "CIMB" => "422589",
                "DBS" => "796500",
                "VIKKI" => "970406",
                "PBVN" or "PUBLICBANK" => "970439",
                "KBHCM" or "KOOKMIN_HCM" => "970463",
                "KBHN" or "KOOKMIN_HN" => "970462",
                "WVN" or "WOORI" => "970457",
                "VRB" => "970421",
                "GPB" or "GPBANK" => "970408",
                "HLBVN" or "HONGLEONG" => "970442",
                "HSBC" => "458761",
                "IBK_HN" => "970455",
                "IBK_HCM" => "970456",
                "NHB_HN" or "NONGHYUP" => "801011",
                "UOB" => "970458",
                "IVB" or "INDOVINABANK" => "970434",
                "SCVN" or "STANDARDCHARTERED" => "970410",

                _ => string.Empty
            };
        }

        /// <summary>
        /// Class chứa các mã BIN của các ngân hàng (Cập nhật từ VietQR API - 64 ngân hàng)
        /// </summary>
        public static class BankCodes
        {
            // Big 4 Banks
            public const string VIETINBANK = "970415";     // ICB
            public const string VIETCOMBANK = "970436";    // VCB
            public const string BIDV = "970418";           // BIDV
            public const string AGRIBANK = "970405";       // VBA

            // Top Commercial Banks
            public const string OCB = "970448";            // OCB
            public const string MBBANK = "970422";         // MB
            public const string TECHCOMBANK = "970407";    // TCB
            public const string ACB = "970416";            // ACB
            public const string VPBANK = "970432";         // VPB
            public const string TPBANK = "970423";         // TPB
            public const string SACOMBANK = "970403";      // STB
            public const string HDBANK = "970437";         // HDB
            public const string VIETCAPITALBANK = "970454"; // VCCB
            public const string SCB = "970429";            // SCB
            public const string VIB = "970441";            // VIB
            public const string SHB = "970443";            // SHB
            public const string EXIMBANK = "970431";       // EIB
            public const string MSB = "970426";            // MSB

            // Digital Banks
            public const string CAKE = "546034";           // CAKE by VPBank
            public const string UBANK = "546035";          // Ubank by VPBank
            public const string TIMO = "963388";           // Timo by Ban Viet Bank

            // Fintech/E-Wallets
            public const string VIETTELMONEY = "971005";   // VTLMONEY
            public const string VNPTMONEY = "971011";      // VNPTMONEY

            // Other Banks
            public const string SAIGONBANK = "970400";     // SGICB
            public const string BACABANK = "970409";       // BAB
            public const string PVCOMBANK_PAY = "971133";  // PVDB
            public const string PVCOMBANK = "970412";      // PVCB
            public const string MBV = "970414";            // MBV
            public const string NCB = "970419";            // NCB
            public const string SHINHANBANK = "970424";    // SHBVN
            public const string ABBANK = "970425";         // ABB
            public const string VIETABANK = "970427";      // VAB
            public const string NAMABANK = "970428";       // NAB
            public const string PGBANK = "970430";         // PGB
            public const string VIETBANK = "970433";       // VIETBANK
            public const string BAOVIETBANK = "970438";    // BVB
            public const string SEABANK = "970440";        // SEAB
            public const string COOPBANK = "970446";       // COOPBANK
            public const string LPBANK = "970449";         // LPB
            public const string KIENLONGBANK = "970452";   // KLB

            // Foreign Banks
            public const string KBANK = "668888";          // KBank (Thai)
            public const string MAFC = "977777";           // Mirae Asset
            public const string KEBHANAHN = "970467";      // KEB Hana HN
            public const string KEBHANAHCM = "970466";     // KEB Hana HCM
            public const string CITIBANK = "533948";       // Citibank
            public const string VBSP = "999888";           // Vietnam Social Policy Bank
            public const string CBBANK = "970444";         // CBBank
            public const string CIMB = "422589";           // CIMB
            public const string DBS = "796500";            // DBS
            public const string VIKKI = "970406";          // Vikki
            public const string PUBLICBANK = "970439";     // PBVN
            public const string KOOKMIN_HCM = "970463";    // KBHCM
            public const string KOOKMIN_HN = "970462";     // KBHN
            public const string WOORI = "970457";          // WVN
            public const string VRB = "970421";            // VRB
            public const string GPBANK = "970408";         // GPB
            public const string HONGLEONG = "970442";      // HLBVN
            public const string HSBC = "458761";           // HSBC
            public const string IBK_HN = "970455";         // IBK Hanoi
            public const string IBK_HCM = "970456";        // IBK HCM
            public const string NONGHYUP = "801011";       // NHB HN
            public const string UOB = "970458";            // UOB
            public const string INDOVINABANK = "970434";   // IVB
            public const string STANDARDCHARTERED = "970410"; // SCVN
        }

        // Helper class tương tự StringBuilder trong TypeScript
        public class StringBuilderBank
        {
            private readonly System.Text.StringBuilder _builder;

            public StringBuilderBank()
            {
                _builder = new System.Text.StringBuilder();
            }

            public StringBuilderBank Append(string value)
            {
                _builder.Append(value);
                return this;
            }

            public int Length => _builder.Length;

            public override string ToString()
            {
                return _builder.ToString();
            }
        }

        /// <summary>
        /// Tạo ảnh QR code từ mã VietQR và lưu vào file (Cross-platform với SkiaSharp)
        /// </summary>
        /// <param name="qrCodeText">Mã QR code text (từ Utils.GenerateQRCode)</param>
        /// <param name="filePath">Đường dẫn file output (jpg/png)</param>
        /// <param name="size">Kích thước QR code (mặc định 300x300)</param>
        /// <param name="qrColor">Màu QR code (mặc định #005f69)</param>
        /// <param name="backgroundColor">Màu nền (mặc định trắng)</param>
        public static void GenerateQRCodeImage(
            string qrCodeText,
            string filePath,
            int size = 300,
            string qrColor = "#005f69",
            string backgroundColor = "#FFFFFF")
        {
            // Tạo QR code với QRCoder
            using QRCodeGenerator qrGenerator = new QRCodeGenerator();
            using QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeText, QRCodeGenerator.ECCLevel.M);
            using PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);

            // Lấy byte array của QR code
            byte[] qrCodeBytes = qrCode.GetGraphic(20);

            // Load QR code bằng SkiaSharp và thay đổi màu
            using var originalBitmap = SKBitmap.Decode(qrCodeBytes)
                ?? throw new InvalidOperationException("Không đọc được ảnh QR vừa tạo.");
            using var surface = SKSurface.Create(new SKImageInfo(size, size));

            var canvas = surface.Canvas;
            canvas.Clear(SKColor.Parse(backgroundColor));

            // Thay đổi màu QR code
            ChangeQRCodeColors(canvas, originalBitmap, size, qrColor, backgroundColor);

            // Tạo thư mục nếu chưa tồn tại
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Lưu ảnh
            using var image = surface.Snapshot();
            using var data = image.Encode(GetSkiaImageFormat(filePath), 100);
            using var stream = File.OpenWrite(filePath);
            data.SaveTo(stream);
        }

        /// <summary>
        /// Tạo ảnh QR code và trả về byte array
        /// </summary>
        /// <param name="qrCodeText">Mã QR code text</param>
        /// <param name="size">Kích thước QR code</param>
        /// <param name="qrColor">Màu QR code</param>
        /// <param name="backgroundColor">Màu nền</param>
        /// <param name="format">Format ảnh (PNG/JPEG)</param>
        /// <returns>Byte array của ảnh</returns>
        public static byte[] GenerateQRCodeImageBytes(
            string qrCodeText,
            int size = 300,
            string qrColor = "#005f69",
            string backgroundColor = "#FFFFFF",
            string format = "PNG")
        {
            // Tạo QR code với QRCoder
            using QRCodeGenerator qrGenerator = new QRCodeGenerator();
            using QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeText, QRCodeGenerator.ECCLevel.M);
            using PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);

            // Lấy byte array của QR code
            byte[] qrCodeBytes = qrCode.GetGraphic(20);

            // Load QR code bằng SkiaSharp và thay đổi màu
            using var originalBitmap = SKBitmap.Decode(qrCodeBytes)
                ?? throw new InvalidOperationException("Không đọc được ảnh QR vừa tạo.");
            using var surface = SKSurface.Create(new SKImageInfo(size, size));

            var canvas = surface.Canvas;
            canvas.Clear(SKColor.Parse(backgroundColor));

            // Thay đổi màu QR code
            ChangeQRCodeColors(canvas, originalBitmap, size, qrColor, backgroundColor);

            // Trả về byte array
            using var image = surface.Snapshot();
            var skFormat = format.ToUpper() == "JPEG" || format.ToUpper() == "JPG"
                ? SKEncodedImageFormat.Jpeg
                : SKEncodedImageFormat.Png;

            using var data = image.Encode(skFormat, 100);
            return data.ToArray();
        }

        /// <summary>
        /// Tạo ảnh QR code với logo ở giữa..
        /// </summary>
        /// <param name="qrCodeText">Mã QR code text</param>
        /// <param name="filePath">Đường dẫn file output</param>
        /// <param name="logoPath">Đường dẫn logo (tùy chọn)</param>
        /// <param name="size">Kích thước QR code</param>
        /// <param name="qrColor">Màu QR code</param>
        /// <param name="backgroundColor">Màu nền</param>
        public static void GenerateQRCodeImageWithLogo(
            string qrCodeText,
            string filePath,
            string? logoPath = null,
            int size = 300,
            string qrColor = "#005f69",
            string backgroundColor = "#FFFFFF")
        {
            // Tạo QR code với error correction cao để có thể thêm logo
            using QRCodeGenerator qrGenerator = new QRCodeGenerator();
            using QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrCodeText, QRCodeGenerator.ECCLevel.H);
            using PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);

            // Lấy byte array của QR code
            byte[] qrCodeBytes = qrCode.GetGraphic(20);

            // Load QR code bằng SkiaSharp
            using var originalBitmap = SKBitmap.Decode(qrCodeBytes)
                ?? throw new InvalidOperationException("Không đọc được ảnh QR vừa tạo.");
            using var surface = SKSurface.Create(new SKImageInfo(size, size));

            var canvas = surface.Canvas;
            canvas.Clear(SKColor.Parse(backgroundColor));

            // Thay đổi màu QR code
            ChangeQRCodeColors(canvas, originalBitmap, size, qrColor, backgroundColor);

            // Thêm logo nếu có
            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
            {
                using var logoStream = File.OpenRead(logoPath);
                using var logoBitmap = SKBitmap.Decode(logoStream);

                // Logo size khoảng 15% của QR code
                int logoSize = size / 7;
                int logoX = (size - logoSize) / 2;
                int logoY = (size - logoSize) / 2;

                // Vẽ nền trắng cho logo
                var backgroundRect = new SKRect(logoX - 5, logoY - 5, logoX + logoSize + 5, logoY + logoSize + 5);
                using var backgroundPaint = new SKPaint();
                backgroundPaint.Color = SKColor.Parse(backgroundColor);
                canvas.DrawRect(backgroundRect, backgroundPaint);

                // Vẽ logo
                var logoRect = new SKRect(logoX, logoY, logoX + logoSize, logoY + logoSize);
                using var logoPaint = new SKPaint();
                logoPaint.IsAntialias = true;
                canvas.DrawBitmap(logoBitmap, logoRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), logoPaint);
            }

            // Tạo thư mục nếu chưa tồn tại
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Lưu ảnh
            using var image = surface.Snapshot();
            using var data = image.Encode(GetSkiaImageFormat(filePath), 100);
            using var stream = File.OpenWrite(filePath);
            data.SaveTo(stream);
        }

        /// <summary>
        /// Thay đổi màu của QR code
        /// </summary>
        private static void ChangeQRCodeColors(SKCanvas canvas, SKBitmap originalBitmap, int targetSize, string qrColor, string backgroundColor)
        {
            SKColor newForeground = SKColor.Parse(qrColor);
            SKColor newBackground = SKColor.Parse(backgroundColor);

            // Resize bitmap về kích thước mong muốn
            using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(targetSize, targetSize), SKSamplingOptions.Default)
                ?? throw new InvalidOperationException("Không resize được ảnh QR.");

            // Tạo bitmap mới với màu tùy chỉnh
            using var customBitmap = new SKBitmap(targetSize, targetSize);
            var pixels = customBitmap.Pixels;
            var originalPixels = resizedBitmap.Pixels;

            for (int i = 0; i < pixels.Length; i++)
            {
                // Nếu pixel gốc là đen (hoặc gần đen) thì đổi thành màu QR
                // Nếu pixel gốc là trắng (hoặc gần trắng) thì đổi thành màu nền
                var originalPixel = originalPixels[i];
                var gray = (originalPixel.Red + originalPixel.Green + originalPixel.Blue) / 3;

                pixels[i] = gray < 128 ? newForeground : newBackground;
            }

            customBitmap.Pixels = pixels;

            // Vẽ bitmap đã tùy chỉnh
            canvas.DrawBitmap(customBitmap, 0, 0, SKSamplingOptions.Default);
        }

        /// <summary>
        /// Xác định SKEncodedImageFormat dựa trên file extension
        /// </summary>
        private static SKEncodedImageFormat GetSkiaImageFormat(string filePath)
        {
            string? extension = Path.GetExtension(filePath)?.ToLower();
            return extension switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".bmp" => SKEncodedImageFormat.Bmp,
                ".gif" => SKEncodedImageFormat.Gif,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png // Default to PNG
            };
        }
    }
}
