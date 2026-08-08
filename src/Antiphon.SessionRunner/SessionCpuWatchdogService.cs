using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// Kills sessions whose process is idle-spinning: the transcript says the turn ended, yet the
/// child (claude.exe) keeps burning a core. Live incident 2026-08-08 — a finished CARD worktree
/// session sat at its prompt with one JS thread pegged at ~110% of a core for 80+ CPU-minutes
/// (stuck render/SSE retry loop), transcript not growing, doing nothing. A watchdog kill uses the
/// <see cref="RunnerExitReasons.CpuSpinKilled"/> exit reason, which the server maps to a CLEAN
/// stop (session and agent Stopped, not Failed) so the next message simply resumes the session
/// by id, exactly like any other stopped session.
///
/// The kill decision is deliberately triple-gated, in order of cheapness:
/// 1. the session has been up for at least the min-uptime grace (startup / --resume history
///    loading is legitimately hot);
/// 2. the transcript PROVES idle — at least one completed turn and no activity since
///    (<see cref="TranscriptWorkingState.IsProvenIdle"/>; no transcript → no judgement, never kill);
/// 3. CPU stayed at/above the hot threshold for the WHOLE sustained window
///    (<see cref="CpuSpinDetector"/>; any cool or missing sample resets the window).
/// </summary>
public sealed class SessionCpuWatchdogService : BackgroundService
{
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(5);

    private readonly SessionRunnerRuntime _runtime;
    private readonly IProcessCpuProbe _probe;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionCpuWatchdogService> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _minUptime;
    private readonly double _hotCpuPercent;
    private readonly TimeSpan _sustainedDuration;
    private readonly CpuSpinDetector _detector;

    public SessionCpuWatchdogService(
        SessionRunnerRuntime runtime,
        IProcessCpuProbe probe,
        IOptions<SessionRunnerSettings> settings,
        TimeProvider timeProvider,
        ILogger<SessionCpuWatchdogService> logger)
    {
        _runtime = runtime;
        _probe = probe;
        _timeProvider = timeProvider;
        _logger = logger;
        var s = settings.Value;
        _enabled = s.CpuWatchdogEnabled;
        _interval = TimeSpan.FromMilliseconds(Math.Max(1_000, s.CpuWatchdogIntervalMs));
        _minUptime = TimeSpan.FromSeconds(Math.Max(0, s.CpuWatchdogMinUptimeSeconds));
        _hotCpuPercent = Math.Max(1, s.CpuWatchdogHotCpuPercent);
        _sustainedDuration = TimeSpan.FromSeconds(Math.Max(1, s.CpuWatchdogSustainedSeconds));
        _detector = new CpuSpinDetector(_hotCpuPercent, _sustainedDuration);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
            return;

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "CPU spin watchdog sweep failed");
            }
        }
    }

    /// <summary>One full sweep over all sessions. Internal so tests can drive it without the timer.</summary>
    internal async Task SweepOnceAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var watched = new HashSet<Guid>();

        foreach (var session in _runtime.List())
        {
            ct.ThrowIfCancellationRequested();
            if (session.Status != "Running" || session.Pid is not int pid)
                continue;
            watched.Add(session.SessionId);

            if (now - session.StartedAt < _minUptime)
            {
                _detector.Forget(session.SessionId);
                continue;
            }

            IReadOnlyList<RunnerTranscriptEvent> entries;
            try
            {
                entries = _runtime.GetTranscript(session.SessionId).Entries;
            }
            catch (KeyNotFoundException)
            {
                _detector.Forget(session.SessionId);
                continue; // removed between List() and here
            }

            if (!TranscriptWorkingState.IsProvenIdle(entries))
            {
                _detector.Forget(session.SessionId);
                continue;
            }

            if (_probe.TryGetTotalCpuTime(pid, session.StartedAt) is not { } cpuTime)
            {
                _detector.Forget(session.SessionId);
                continue;
            }

            if (!_detector.Observe(session.SessionId, cpuTime, now))
                continue;

            _logger.LogWarning(
                "CPU spin watchdog killing session {SessionId} (pid {Pid}): idle per transcript yet burning "
                + ">= {HotPercent:F0}% of a core for {SustainedSeconds:F0}s straight. Marking it "
                + "stopped; the session stays resumable by id.",
                session.SessionId, pid, _hotCpuPercent, _sustainedDuration.TotalSeconds);

            try
            {
                var after = await _runtime.KillAsync(
                    session.SessionId, KillTimeout, ct, RunnerExitReasons.CpuSpinKilled);
                if (after.Status != "Exited")
                {
                    _logger.LogWarning(
                        "CPU spin watchdog kill of session {SessionId} did not confirm exit (status {Status}); "
                        + "the liveness sweep is the backstop",
                        session.SessionId, after.Status);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "CPU spin watchdog failed to kill session {SessionId}", session.SessionId);
            }

            _detector.Forget(session.SessionId);
        }

        _detector.ForgetAllExcept(watched);
    }
}
