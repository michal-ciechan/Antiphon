using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

public sealed class StaleWorkingTranscriptHostedService(
    StaleWorkingTranscriptRecovery recovery, IOptions<StaleWorkingTranscriptSettings> options,
    IOptions<SessionRunnerSettings> runner, TimeProvider clock,
    ILogger<StaleWorkingTranscriptHostedService> logger) : BackgroundService
{
    private readonly TaskCompletionSource _firstPass = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task FirstPass => _firstPass.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var settings = options.Value;
            if (!settings.Enabled || !runner.Value.Enabled)
            {
                logger.LogWarning("Stale Working periodic recovery bound disabled: feature {FeatureEnabled}, transport {TransportEnabled}", settings.Enabled, runner.Value.Enabled);
                _firstPass.TrySetResult();
                return;
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await recovery.TickAsync(stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { logger.LogWarning(ex, "Stale Working scheduler pass failed; continuing at configured poll"); }
                finally { _firstPass.TrySetResult(); }
                // This delay follows the whole pass. Completed probe slots are
                // replenished on the next pass, so the backlog allowance includes
                // poll cadence and pass/commit duration (T62), not timeout alone.
                await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), clock, stoppingToken);
            }
        }
        finally { await recovery.StopAsync(CancellationToken.None); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await recovery.StopAsync(CancellationToken.None);
    }
}
