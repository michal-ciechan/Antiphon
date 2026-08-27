using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>Periodic driver for the CARD-0091 parked-message cleanup sweep.</summary>
public sealed class ParkedMessageSweepHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ParkedMessageSweepSettings _settings;
    private readonly ILogger<ParkedMessageSweepHostedService> _logger;

    public ParkedMessageSweepHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<ParkedMessageSweepSettings> settings,
        ILogger<ParkedMessageSweepHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sweep = scope.ServiceProvider.GetRequiredService<ParkedMessageSweepService>();
                await sweep.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Parked-message sweep failed");
            }
        }
    }
}
