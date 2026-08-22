using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Periodic driver for <see cref="SubscriptionUsageMonitorService"/>. Own hosted service —
/// the poll types into live terminals and must not stall the supervisor tick.
/// Registration is unconditional; the <c>Enabled</c> guard lives here.
/// </summary>
public sealed class SubscriptionUsageMonitorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SubscriptionUsageMonitoringSettings _settings;
    private readonly ILogger<SubscriptionUsageMonitorHostedService> _logger;

    public SubscriptionUsageMonitorHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SubscriptionUsageMonitoringSettings> settings,
        ILogger<SubscriptionUsageMonitorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Subscription usage monitoring disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<SubscriptionUsageMonitorService>()
                    .SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Subscription usage sweep failed");
            }
        }
    }
}
