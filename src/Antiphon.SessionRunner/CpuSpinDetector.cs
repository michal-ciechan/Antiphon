namespace Antiphon.SessionRunner;

/// <summary>
/// Per-session CPU-burn tracker for the spin watchdog. Fed one cumulative-CPU-time sample per
/// sweep, it computes each interval's CPU usage as a percentage of one core and reports when a
/// session has stayed at or above the hot threshold for the full sustained window. Any cool
/// interval, missing sample (<see cref="Forget"/>) or CPU-time regression (PID recycled) resets
/// the window — a kill verdict therefore always means "continuously hot for the whole window".
/// Not thread-safe; owned and driven by a single sweep loop.
/// </summary>
public sealed class CpuSpinDetector
{
    private readonly double _hotCpuPercent;
    private readonly TimeSpan _sustainedDuration;
    private readonly Dictionary<Guid, Tracker> _trackers = new();

    public CpuSpinDetector(double hotCpuPercent, TimeSpan sustainedDuration)
    {
        _hotCpuPercent = hotCpuPercent;
        _sustainedDuration = sustainedDuration;
    }

    /// <summary>
    /// Records one sample; true when the session has been continuously hot for at least the
    /// sustained window. The first sample of a session only establishes the baseline.
    /// </summary>
    public bool Observe(Guid sessionId, TimeSpan cpuTime, DateTime nowUtc)
    {
        if (!_trackers.TryGetValue(sessionId, out var previous))
        {
            _trackers[sessionId] = new Tracker(cpuTime, nowUtc, HotSinceUtc: null);
            return false;
        }

        var wallSeconds = (nowUtc - previous.SampledAtUtc).TotalSeconds;
        var cpuSeconds = (cpuTime - previous.CpuTime).TotalSeconds;
        if (wallSeconds <= 0 || cpuSeconds < 0)
        {
            // Clock went nowhere, or CPU time went backwards (a different process under a recycled
            // PID) — this sample proves nothing; start over from it.
            _trackers[sessionId] = new Tracker(cpuTime, nowUtc, HotSinceUtc: null);
            return false;
        }

        var percentOfOneCore = cpuSeconds / wallSeconds * 100.0;
        var hotSince = percentOfOneCore >= _hotCpuPercent
            ? previous.HotSinceUtc ?? previous.SampledAtUtc
            : (DateTime?)null;
        _trackers[sessionId] = new Tracker(cpuTime, nowUtc, hotSince);

        return hotSince is { } since && nowUtc - since >= _sustainedDuration;
    }

    /// <summary>Drops a session's window — the next sample starts a fresh baseline.</summary>
    public void Forget(Guid sessionId) => _trackers.Remove(sessionId);

    /// <summary>Prunes state for sessions no longer being watched (exited / removed).</summary>
    public void ForgetAllExcept(IReadOnlySet<Guid> watched)
    {
        var stale = _trackers.Keys.Where(id => !watched.Contains(id)).ToList();
        foreach (var id in stale)
            _trackers.Remove(id);
    }

    private readonly record struct Tracker(TimeSpan CpuTime, DateTime SampledAtUtc, DateTime? HotSinceUtc);
}
