using System.Security.Cryptography;
using System.Text;

namespace HIS.BLL.Security;

/// <summary>
/// Thử thách OTP đặt lại mật khẩu với cơ chế khóa luồng và so sánh thời gian không đổi (constant-time)
/// để chống race condition và timing attack khi có nhiều yêu cầu xác thực đồng thời.
/// </summary>
public sealed class PasswordResetChallenge
{
    private readonly object _sync = new();
    private readonly byte[] _code;
    private readonly DateTime _expiresAt;
    private bool _consumed;

    public int AccountId { get; }
    public int Attempts { get; private set; }
    public DateTime ExpiresAt => _expiresAt;
    public string MaskedEmail { get; }
    public string? DemoOtp { get; }

    public PasswordResetChallenge(int accountId, string otp, DateTime expiresAt, string maskedEmail, string? demoOtp = null)
    {
        AccountId = accountId;
        _code = Encoding.UTF8.GetBytes(otp);
        _expiresAt = expiresAt;
        MaskedEmail = maskedEmail;
        DemoOtp = demoOtp;
    }

    public PasswordResetChallenge(string otp, DateTime expiresAt, string maskedEmail, string? demoOtp = null)
        : this(0, otp, expiresAt, maskedEmail, demoOtp)
    {
    }

    public enum VerifyOutcome
    {
        Success,
        Expired,
        MaxAttemptsExceeded,
        Mismatch,
        AlreadyConsumed
    }

    public VerifyOutcome TryVerify(string candidate, DateTime now, int maxAttempts, out int remainingAttempts)
    {
        lock (_sync)
        {
            if (_consumed)
            {
                remainingAttempts = 0;
                return VerifyOutcome.AlreadyConsumed;
            }

            if (now > _expiresAt)
            {
                remainingAttempts = 0;
                return VerifyOutcome.Expired;
            }

            if (Attempts >= maxAttempts)
            {
                remainingAttempts = 0;
                return VerifyOutcome.MaxAttemptsExceeded;
            }

            Attempts++;
            remainingAttempts = Math.Max(0, maxAttempts - Attempts);

            byte[] candidateBytes = Encoding.UTF8.GetBytes(candidate ?? string.Empty);
            if (!CryptographicOperations.FixedTimeEquals(_code, candidateBytes))
            {
                return VerifyOutcome.Mismatch;
            }

            _consumed = true;
            return VerifyOutcome.Success;
        }
    }
}

