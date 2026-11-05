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
/// Cấp mã OTP khi thêm hồ sơ: chọn email nhận, sinh mã, ghi một dòng notifications làm dấu vết
/// rồi đẩy qua mọi kênh <see cref="IOtpDelivery"/> mà host đã đăng ký.
/// Mã chưa xác thực không được lưu ở đâu ngoài Session của trình duyệt nên không cần bảng mới.
/// </summary>
public class OtpService : IOtpService
{
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

        if (recipient is null)
        {
            return new OtpIssueResult(false,
                $"Hồ sơ {patient.FullName} chưa có email trong hệ thống nên không gửi được mã xác thực. " +
                "Hồ sơ vẫn chờ quầy đối chiếu giấy tờ khi bạn tới khám, hotline 0808 0808.");
        }

        return await IssueAndSendAsync(accountId, patient.FullName, recipient, cancellationToken);
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

    /// <summary>Phần chung của hai nhánh: sinh mã, ghi dấu vết notifications, đẩy qua các kênh đã đăng ký.</summary>
    private async Task<OtpIssueResult> IssueAndSendAsync(
        int accountId,
        string patientName,
        string recipient,
        CancellationToken cancellationToken)
    {
        string code = NextCode();
        DateTimeOffset expiresAt = DateTimeOffset.Now.AddMinutes(_options.LifetimeMinutes);

        _context.Notifications.Add(new Notification
        {
            AccountId = accountId,
            Title = "Mã xác thực hồ sơ",
            Content = $"Mã {code} xác thực hồ sơ {patientName} gửi tới {recipient}, " +
                      $"có hiệu lực đến {expiresAt:HH:mm} ngày {expiresAt:dd/MM/yyyy}.",
            IsRead = false
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
        string message = delivered
            ? $"Đã gửi mã xác thực {_options.CodeLength} số tới {recipient} qua {channels}. Mã hết hạn lúc {expiresAt:HH:mm}."
            : _deliveries.Count == 0
                ? "Chưa cấu hình kênh gửi mã OTP."
                : $"Chưa gửi được mã. {string.Join(" | ", failures)}";

        return new OtpIssueResult(
            delivered, message, recipient, code, expiresAt, _options.RevealCodeOnScreen, patientName);
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
