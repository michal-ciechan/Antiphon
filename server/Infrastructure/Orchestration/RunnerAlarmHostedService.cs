using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

public sealed class RunnerAlarmHostedService(
    IServiceScopeFactory scopes,
    AlarmWakeQueue wake,
    RunnerAlarmState state,
    IOptions<AlarmSettings> settings,
    TimeProvider clock,
    ILogger<RunnerAlarmHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled)
            return;
        var nextSweep = clock.GetUtcNow();
        var forceSweep = true;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = clock.GetUtcNow();
                var signal = wake.Take();
                var sweep = forceSweep || signal.Sweep || now >= nextSweep;
                await using var scope = scopes.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<RunnerAlarmCoordinator>();
                if (sweep)
                {
                    await coordinator.EvaluateJournalsAsync(null, now, stoppingToken);
                    nextSweep = now.AddMinutes(settings.Value.SweepMinutes);
                    forceSweep = false;
                }
                else if (signal.Fenced.Length > 0)
                {
                    await coordinator.EvaluateJournalsAsync(signal.Fenced, now, stoppingToken);
                }

                await coordinator.EvaluateRunnersAsync(now, stoppingToken);
                failures = 0;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Runner alarm pass failed");
                wake.RequestSweep();
                if (++failures > 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, failures)), clock, stoppingToken);
            }

            await wake.WaitAsync(NextDue(nextSweep), clock, stoppingToken);
        }
    }

    private DateTimeOffset NextDue(DateTimeOffset nextSweep)
    {
        var grace = TimeSpan.FromSeconds(settings.Value.RunnerGraceSeconds);
        var due = nextSweep;
        foreach (var episode in state.Current.Episodes)
        {
            if (episode.RaisedAt is not null)
                continue;
            var raiseAt = episode.DownSince + grace;
            if (raiseAt < due)
                due = raiseAt;
        }

        return due;
    }
}
