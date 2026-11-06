using System.Security.Cryptography;
using System.Text;

namespace HIS.BLL.Common;

internal sealed class PasswordResetChallenge(string otp, DateTime expiresAt)
{
    private readonly object _sync = new();
    private readonly byte[] _code = Encoding.UTF8.GetBytes(otp);
    private bool _consumed;

    internal int Attempts { get; private set; }

    internal bool TryConsume(string candidate, DateTime now)
    {
        lock (_sync)
        {
            if (_consumed || now >= expiresAt || Attempts >= 5)
            {
                return false;
            }

            Attempts++;
            if (!CryptographicOperations.FixedTimeEquals(_code, Encoding.UTF8.GetBytes(candidate)))
            {
                return false;
            }

            _consumed = true;
            return true;
        }
    }
}
