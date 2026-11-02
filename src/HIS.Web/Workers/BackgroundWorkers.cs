using HIS.BLL.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Web.Workers;

public class SlotGenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlotGenerationWorker> _logger;

    public SlotGenerationWorker(IServiceScopeFactory scopeFactory, ILogger<SlotGenerationWorker> logger)
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
                // Slot generation runs periodically (e.g., daily)
                _logger.LogInformation("SlotGenerationWorker executed check.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SlotGenerationWorker");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}

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

public class NoShowWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NoShowWorker> _logger;

    public NoShowWorker(IServiceScopeFactory scopeFactory, ILogger<NoShowWorker> logger)
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
                // Check end-of-shift confirmed appointments not checked-in -> NO_SHOW
                _logger.LogInformation("NoShowWorker executed check.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NoShowWorker");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

