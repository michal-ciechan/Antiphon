namespace Antiphon.SessionRunner;

/// <summary>
/// Periodically asks the runtime to sweep "Running" sessions whose OS process has vanished.
/// See <see cref="SessionRunnerRuntime.SweepVanishedSessions"/> for the why.
/// </summary>
public sealed class SessionLivenessSweepService : BackgroundService
{
    private readonly SessionRunnerRuntime _runtime;
    private readonly IProcessLivenessProbe _probe;
    private readonly TimeSpan _interval;
    private readonly ILogger<SessionLivenessSweepService> _logger;

    public SessionLivenessSweepService(
        SessionRunnerRuntime runtime,
        IProcessLivenessProbe probe,
        Microsoft.Extensions.Options.IOptions<SessionRunnerSettings> settings,
        ILogger<SessionLivenessSweepService> logger)
    {
        _runtime = runtime;
        _probe = probe;
        _interval = TimeSpan.FromMilliseconds(Math.Max(1_000, settings.Value.LivenessSweepIntervalMs));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _runtime.SweepVanishedSessions(_probe);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session liveness sweep failed");
            }
        }
    }
}
