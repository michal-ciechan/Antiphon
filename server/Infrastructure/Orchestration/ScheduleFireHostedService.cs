using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Drains <see cref="ScheduleFireQueue"/> and runs each claimed fire (CARD-0057 D4). A fire that
/// throws is logged and DROPPED, never retried: the sweep already advanced NextFireAt.
/// </summary>
public sealed class ScheduleFireHostedService : BackgroundService
{
    private readonly ScheduleFireQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScheduleSettings _settings;
    private readonly ILogger<ScheduleFireHostedService> _logger;

    public ScheduleFireHostedService(
        ScheduleFireQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<ScheduleSettings> settings,
        ILogger<ScheduleFireHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Schedules are disabled; the fire worker will not run.");
            return;
        }

        try
        {
            await foreach (var claim in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var schedules = scope.ServiceProvider.GetRequiredService<ScheduleService>();
                    await schedules.FireAsync(claim, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex, "Schedule fire {ScheduleId} #{FireNumber} failed",
                        claim.ScheduleId, claim.FireNumber);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }
}
