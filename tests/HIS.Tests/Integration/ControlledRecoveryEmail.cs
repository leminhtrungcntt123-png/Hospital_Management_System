using System.Text.RegularExpressions;
using HIS.BLL.Services;

namespace HIS.Tests.Integration;

public sealed class ControlledRecoveryEmail : IEmailSender
{
    private int _calls;
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string LastOtp { get; private set; } = "";

    public async Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            throw new InvalidOperationException("Synthetic SMTP failure with private details");
        }
        LastOtp = Regex.Match(body, @"(?<!\d)\d{6}(?!\d)").Value;
    }
}
