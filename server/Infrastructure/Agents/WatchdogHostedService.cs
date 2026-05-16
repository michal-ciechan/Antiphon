using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents;

public sealed class WatchdogHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WatchdogSettings _settings;
    private readonly ILogger<WatchdogHostedService> _logger;

    public WatchdogHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<WatchdogSettings> settings,
        ILogger<WatchdogHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(100, _settings.ScanIntervalMs));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var watchdog = scope.ServiceProvider.GetRequiredService<WatchdogService>();
                await watchdog.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watchdog scan failed");
            }
        }
    }
}
