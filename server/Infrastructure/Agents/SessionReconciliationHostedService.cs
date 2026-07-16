using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>Periodic driver for <see cref="SessionReconciliationService"/> — see that class for why.</summary>
public sealed class SessionReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionReconciliationSettings _settings;
    private readonly ILogger<SessionReconciliationHostedService> _logger;

    public SessionReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SessionReconciliationSettings> settings,
        ILogger<SessionReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(1_000, _settings.IntervalMs));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<SessionReconciliationService>();
                await reconciler.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session reconciliation scan failed");
            }
        }
    }
}
