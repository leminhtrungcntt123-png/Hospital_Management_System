using System.Threading.Channels;
using HIS.BLL.Services;

namespace HIS.Web.Workers;

public sealed class PasswordResetQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordResetQueue> logger) : BackgroundService
{
    private readonly Channel<string> _requests = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait
    });

    public void Enqueue(string identifier)
    {
        if (!_requests.Writer.TryWrite(identifier))
        {
            logger.LogWarning("Password reset queue is full.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var identifier in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
                await auth.SendPasswordResetOtpAsync(identifier, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning("Password reset delivery failed: {Type}", error.GetType().Name);
            }
        }
    }
}
