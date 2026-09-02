using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Periodic driver for <see cref="CardTaskFileService.SyncAllAsync"/> (CARD-0004). A 60 s tick
/// and the manual endpoint are the only v1 triggers; there is no enqueue from <c>CardService</c>.
/// <see cref="CardFileSyncSettings.IntervalSeconds"/> of 0 is manual-only: the tick never starts
/// and <c>POST /api/boards/{id}/card-files/sync</c> stays available.
/// </summary>
public sealed class CardTaskFileSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CardFileSyncSettings _settings;
    private readonly ILogger<CardTaskFileSyncHostedService> _logger;

    public CardTaskFileSyncHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<CardFileSyncSettings> settings,
        ILogger<CardTaskFileSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        // 0 is a documented arm, not a floor: PeriodicTimer rejects TimeSpan.Zero, and the
        // endpoint must keep working when the operator wants manual-only.
        if (_settings.IntervalSeconds <= 0)
        {
            _logger.LogInformation(
                "Card file sync tick disabled (IntervalSeconds={Interval}); endpoint remains available",
                _settings.IntervalSeconds);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<CardTaskFileService>();
                await sync.SyncAllAsync(dryRun: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Card file sync sweep failed");
            }
        }
    }
}
