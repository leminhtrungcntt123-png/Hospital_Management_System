using HIS.BLL.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Web.Workers;

public class HoldExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldExpirationWorker> _logger;

    public HoldExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<HoldExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var slotService = scope.ServiceProvider.GetRequiredService<ISlotGenerationService>();
                await slotService.ExpirePendingHoldsAsync(stoppingToken);
            }
            catch (NotImplementedException)
            {
                // Stub placeholder
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HoldExpirationWorker");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

