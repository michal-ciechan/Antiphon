using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Drains due alert digests to their sinks (see AlertDigestFlusher) and prunes old alerts.</summary>
public sealed class AlertDigestFlushHostedService : BackgroundService
{
    private static readonly TimeSpan PrunePeriod = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertsSettings _settings;
    private readonly ILogger<AlertDigestFlushHostedService> _logger;
    private DateTime _lastPruneUtc = DateTime.MinValue;

    public AlertDigestFlushHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AlertsSettings> settings,
        ILogger<AlertDigestFlushHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.RoutingEnabled)
        {
            _logger.LogInformation("Alert routing disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _settings.FlushTickSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AlertDigestFlusher>().FlushDueAsync(stoppingToken);

                    if (DateTime.UtcNow - _lastPruneUtc >= PrunePeriod)
                    {
                        _lastPruneUtc = DateTime.UtcNow;
                        var db = scope.ServiceProvider
                            .GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
                        var cutoff = DateTime.UtcNow.AddDays(-_settings.AlertRetentionDays);
                        var pruned = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                            .ExecuteDeleteAsync(db.Alerts.Where(a => a.CreatedAt < cutoff), stoppingToken);
                        if (pruned > 0)
                            _logger.LogInformation("Pruned {Count} alert(s) past retention", pruned);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Alert digest flush failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
