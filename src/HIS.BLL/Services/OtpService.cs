using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

/// <summary>
/// Cấp mã OTP khi thêm hồ sơ: kiểm tần suất xin mã, chọn email nhận, sinh mã, ghi một dòng notifications
/// làm dấu vết (chỉ lưu email đã che, không lưu mã) rồi đẩy qua mọi kênh <see cref="IOtpDelivery"/> mà host
/// đã đăng ký. Mã chưa xác thực không được lưu ở đâu ngoài Session của trình duyệt nên không cần bảng mới.
/// </summary>
public class OtpService : IOtpService
{
    /// <summary>
    /// Tiêu đề dòng notifications ghi mỗi lần cấp mã. Cùng một lúc nó là khóa để đếm tần suất xin mã
    /// (không cần bảng mới) và là bộ lọc để quầy/trợ lý tra lại lịch sử cấp mã.
    /// </summary>
    public const string TraceTitle = "Mã xác thực hồ sơ";

    private readonly AppDbContext _context;
    private readonly OtpOptions _options;
    private readonly IReadOnlyList<IOtpDelivery> _deliveries;

    public OtpService(
        AppDbContext context,
        IOptions<OtpOptions> options,
        IEnumerable<IOtpDelivery> deliveries)
    {
        _context = context;
        _options = options.Value;
        _deliveries = deliveries.ToList();
    }

    public async Task<OtpIssueResult> IssueProfileOtpAsync(
        int accountId,
        int patientId,
        CancellationToken cancellationToken = default)
    {
        Patient? patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        if (patient is null
            || !await _context.AccountPatientProfiles.AnyAsync(
                l => l.AccountId == accountId && l.PatientId == patientId, cancellationToken))
        {
            return new OtpIssueResult(false, "Hồ sơ này không thuộc tài khoản của bạn.");
        }

        // Nhánh "đã từng khám": chỉ email đã lưu trong hồ sơ mới được dùng, form không có ô email.
        string? recipient = ProfileEmail.NormalizeUsable(patient.Email);

        // Chủ thể mang đi ghi dấu vết và đề trên email là MÃ BỆNH NHÂN chứ không phải họ tên:
        // người vừa gõ mã BN chưa chứng minh được quyền biết tên của hồ sơ đó (docs/DECISIONS.md #25).
        string subject = patient.Id.ToPatientCode();

        if (recipient is null)
        {
            return new OtpIssueResult(false,
                $"Hồ sơ {subject} chưa có email trong hệ thống nên không gửi được mã xác thực. " +
                "Liên kết vẫn chờ quầy đối chiếu giấy tờ để mở quyền đặt lịch, hotline 0808 0808.");
        }

        return await IssueAndSendAsync(accountId, subject, recipient, cancellationToken);
    }

    public async Task<OtpIssueResult> IssueNewProfileOtpAsync(
        int accountId,
        string email,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        string? recipient = ProfileEmail.NormalizeUsable(email);

        if (recipient is null)
        {
            return new OtpIssueResult(false,
                $"Email \"{email}\" không hợp lệ nên hệ thống không gửi được mã xác thực. " +
                "Vui lòng kiểm tra lại hoặc gọi hotline 0808 0808.");
        }

        string name = (fullName ?? string.Empty).Trim();
        return await IssueAndSendAsync(accountId, name.Length > 0 ? name : "hồ sơ mới", recipient, cancellationToken);
    }

    /// <summary>
    /// Phần chung của hai nhánh: chặn xin mã dày đặc, sinh mã, ghi một dòng notifications KHÔNG chứa mã
    /// (chỉ để lại email đã che + mốc giờ) rồi đẩy mã qua các kênh <see cref="IOtpDelivery"/> đã đăng ký.
    /// </summary>
    private async Task<OtpIssueResult> IssueAndSendAsync(
        int accountId,
        string patientName,
        string recipient,
        CancellationToken cancellationToken)
    {
        // Đồng hồ của ứng dụng: cột created_at là "timestamp without time zone" và Postgres điền DEFAULT now()
        // theo MÚI GIỜ CỦA SERVER. Nếu không tự gán, máy nào chạy DB UTC sẽ ghi giờ lệch 7 tiếng so với
        // DateTime.Now mà IssueQuotaMessageAsync dùng để đếm — hạn mức phát mã thành ra không bao giờ kích hoạt.
        DateTime now = DateTime.Now;

        string? tooOften = await IssueQuotaMessageAsync(accountId, now, cancellationToken);
        if (tooOften is not null)
        {
            return new OtpIssueResult(false, tooOften);
        }

        string code = NextCode();
        DateTimeOffset expiresAt = DateTimeOffset.Now.AddMinutes(_options.LifetimeMinutes);

        // Dấu vết chỉ để đối chiếu "đã gửi chưa, gửi lúc nào", không phải nơi cất mã:
        // ai dump bảng notifications cũng không đọc trộm được mã còn hiệu lực.
        _context.Notifications.Add(new Notification
        {
            AccountId = accountId,
            Title = TraceTitle,
            Content = $"Đã gửi mã {_options.CodeLength} số xác thực hồ sơ {patientName} tới " +
                      $"{ProfileEmail.Mask(recipient)} lúc {now:HH:mm} ngày {now:dd/MM/yyyy}; " +
                      $"mã hết hạn lúc {expiresAt:HH:mm}. Mã không nằm trong thông báo này.",
            IsRead = false,
            CreatedAt = now
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            return new OtpIssueResult(false, "Không ghi được yêu cầu cấp mã. Vui lòng thử lại.");
        }

        var failures = new List<string>();
        foreach (IOtpDelivery delivery in _deliveries)
        {
            try
            {
                delivery.Send(new OtpRecipient(patientName, recipient), code, expiresAt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{delivery.Name}: {ex.Message}");
            }
        }

        bool delivered = _deliveries.Count > 0 && failures.Count == 0;
        string channels = string.Join(" / ", _deliveries.Select(d => d.Name));
        // Email cũng phải đi qua Mask: message này có thể được render ở màn hình khác (kể cả khi người dùng
        // chưa chứng minh quyền sở hữu hộp thư), nên không được mang địa chỉ thô.
        string message = delivered
            ? $"Đã gửi mã xác thực {_options.CodeLength} số tới {ProfileEmail.Mask(recipient)} qua {channels}. Mã hết hạn lúc {expiresAt:HH:mm}."
            : _deliveries.Count == 0
                ? "Chưa cấu hình kênh gửi mã OTP."
                : $"Chưa gửi được mã. {string.Join(" | ", failures)}";

        return new OtpIssueResult(
            delivered, message, recipient, code, expiresAt, _options.RevealCodeOnScreen, patientName);
    }

    /// <summary>
    /// Đếm số lần cấp mã của chính tài khoản này trong cửa sổ đang cấu hình, dựa trên dấu vết notifications
    /// vừa ghi ở trên nên không phải thêm bảng. Trả lời tiếng Việt khi vượt ngưỡng, null khi còn xin được.
    /// </summary>
    private async Task<string?> IssueQuotaMessageAsync(int accountId, DateTime now, CancellationToken cancellationToken)
    {
        DateTime since = now.AddMinutes(-_options.IssueWindowMinutes);

        List<DateTime> recent = await _context.Notifications
            .Where(n => n.AccountId == accountId && n.Title == TraceTitle && n.CreatedAt >= since)
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        if (recent.Count < _options.MaxIssuesPerWindow)
        {
            return null;
        }

        // Dòng cũ nhất trong cửa sổ cho biết thời điểm được xin lại (giờ của ứng dụng, vì CreatedAt do code gán).
        int waitMinutes = (int)Math.Ceiling(
            (recent[0].AddMinutes(_options.IssueWindowMinutes) - now).TotalMinutes);
        if (waitMinutes < 1)
        {
            waitMinutes = 1;
        }

        return $"Bạn đã xin mã xác thực {_options.MaxIssuesPerWindow} lần trong " +
               $"{_options.IssueWindowMinutes} phút gần đây. Vui lòng quay lại sau khoảng {waitMinutes} phút " +
               "hoặc gọi hotline 0808 0808 để được hỗ trợ.";
    }

    private string NextCode()
    {
        int upperBound = 1;
        for (int i = 0; i < _options.CodeLength; i++)
        {
            upperBound *= 10;
        }

        return RandomNumberGenerator.GetInt32(0, upperBound)
            .ToString($"D{_options.CodeLength}", CultureInfo.InvariantCulture);
    }
}

/// <summary>So sánh mã người dùng nhập với mã trong Session, không lộ độ dài qua thời gian thực hiện.</summary>
public static class OtpCodes
{
    public static bool Matches(string? expected, string? input)
    {
        string candidate = new((input ?? string.Empty).Where(char.IsDigit).ToArray());
        if (expected is null || candidate.Length != expected.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(candidate));
    }
}
