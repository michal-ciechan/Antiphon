using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// 5 s claim sweep for <see cref="ScheduleService"/> (CARD-0057 D4). Claims due rows and hands
/// them to <see cref="ScheduleFireQueue"/>; it never runs a fire.
/// </summary>
public sealed class ScheduleSweepHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScheduleFireQueue _queue;
    private readonly ScheduleSettings _settings;
    private readonly ILogger<ScheduleSweepHostedService> _logger;

    public ScheduleSweepHostedService(
        IServiceScopeFactory scopeFactory,
        ScheduleFireQueue queue,
        IOptions<ScheduleSettings> settings,
        ILogger<ScheduleSweepHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Schedules are disabled; the sweep will not run.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.SweepSeconds));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var schedules = scope.ServiceProvider.GetRequiredService<ScheduleService>();
                var claimed = await schedules.ClaimDueAsync(stoppingToken);
                if (claimed > 0)
                    _logger.LogDebug("Claimed {Count} due schedule(s); {Pending} waiting", claimed, _queue.PendingCount);
                await schedules.PruneFiresAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Schedule sweep failed");
            }
        }
    }
}
