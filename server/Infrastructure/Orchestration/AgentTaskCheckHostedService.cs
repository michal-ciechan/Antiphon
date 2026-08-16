using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Drains <see cref="AgentTaskCheckQueue"/> and runs each claimed check-in (CARD-0047 §1.1).
///
/// <para>It exists so <c>AgentTaskDispatcher.TickAsync</c> never has to wait for one. The tick is
/// serial and fires every 5 s; a check reads a workspace and (from slice 4) makes a model call, so
/// running it inline would stall every dispatch behind it.</para>
///
/// <para>A check that throws is logged and DROPPED, never retried: the sweep already advanced the
/// task's schedule before handing the id over, so the next check comes round on its own. Retrying
/// here would be the loop that re-arm-before-run exists to prevent.</para>
/// </summary>
public sealed class AgentTaskCheckHostedService : BackgroundService
{
    private readonly AgentTaskCheckQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly ILogger<AgentTaskCheckHostedService> _logger;

    public AgentTaskCheckHostedService(
        AgentTaskCheckQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<AgentTaskCheckHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.CheckEnabled)
        {
            _logger.LogInformation("Delegate check-ins are disabled; the check worker will not run.");
            return;
        }

        try
        {
            await foreach (var taskId in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var checks = scope.ServiceProvider.GetRequiredService<AgentTaskCheckService>();
                    var outcome = await checks.RunCheckAsync(taskId, stoppingToken);
                    _logger.LogDebug("Check on task {TaskId}: {Outcome}", taskId, outcome);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Dropped on purpose — the schedule has already moved on.
                    _logger.LogWarning(ex, "Check on task {TaskId} failed", taskId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }
}
