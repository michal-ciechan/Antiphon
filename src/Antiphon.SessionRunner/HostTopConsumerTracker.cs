using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1884. CPU deltas are keyed by pid and start ticks. A recycled pid has no previous counter.
/// A scan closer than five seconds returns the previous ranking.
/// </summary>
internal readonly record struct HostTopConsumerView(
    IReadOnlyList<HostTopConsumerSample> Rows, double? AgeSeconds, bool Stale, bool Truncated);

internal sealed class HostTopConsumerTracker
{
    private sealed class Published
    {
        public static readonly Published Empty = new();
        public IReadOnlyList<HostTopConsumerSample> Rows { get; init; } = [];
        public long ScannedAt { get; init; } = long.MinValue;
        public bool Truncated { get; init; }
    }

    private readonly Dictionary<(int Pid, long StartTicks), long> _cpu = new();
    private long _lastScan = long.MinValue;
    private Published _published = Published.Empty;

    /// <summary>The latest ranking. This does not read the process table.</summary>
    public HostTopConsumerView View(TimeProvider time, TimeSpan cadence)
    {
        var snap = Volatile.Read(ref _published);
        if (snap.ScannedAt == long.MinValue)
            return new(snap.Rows, null, false, snap.Truncated);
        var age = time.GetElapsedTime(snap.ScannedAt, time.GetTimestamp());
        var stale = cadence > TimeSpan.Zero && age.Ticks > cadence.Ticks * 2;
        return new(snap.Rows, age.TotalSeconds, stale, snap.Truncated);
    }

    public IReadOnlyList<HostTopConsumerSample> Sample(IHostProcessTable? table, TimeProvider time, TimeSpan? minimumInterval = null)
    {
        if (table is null) return [];
        var minimum = minimumInterval ?? HostTopConsumerLimits.MinScanInterval;
        var now = time.GetTimestamp();
        var current = Volatile.Read(ref _published);
        if (_lastScan != long.MinValue && time.GetElapsedTime(_lastScan, now) < minimum)
            return current.Rows;
        IReadOnlyList<HostProcessObservation> rows;
        bool truncated;
        try
        {
            rows = table.Read(HostTopConsumerLimits.ScanCap, RetainPids());
            truncated = table.LastReadTruncated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return current.Rows;
        }

        return Observe(rows, truncated, time);
    }

    /// <summary>Pids from the previous ranking. The next read keeps them ahead of the cap.</summary>
    public List<int> RetainPids()
    {
        var retain = new List<int>(_cpu.Count);
        foreach (var key in _cpu.Keys)
            retain.Add(key.Pid);
        return retain;
    }

    /// <summary>Ranks a read the caller already finished. A discarded late read never commits.</summary>
    public IReadOnlyList<HostTopConsumerSample> Observe(IReadOnlyList<HostProcessObservation> rows, bool truncated, TimeProvider time)
        => Observe(rows, truncated, time, publish => { publish(); return true; });

    /// <summary>
    /// Ranks outside the caller's lock. <paramref name="commit"/> publishes under that lock and
    /// returns false when the epoch is already stale, leaving the previous baseline in place.
    /// </summary>
    internal IReadOnlyList<HostTopConsumerSample> Observe(
        IReadOnlyList<HostProcessObservation> rows, bool truncated, TimeProvider time, Func<Action, bool> commit)
    {
        var now = time.GetTimestamp();
        var elapsed = _lastScan == long.MinValue ? TimeSpan.Zero : time.GetElapsedTime(_lastScan, now);
        var pending = Prepare(rows, elapsed);
        var published = false;
        commit(() =>
        {
            _cpu.Clear();
            foreach (var pair in pending.Cpu)
                _cpu[pair.Key] = pair.Value;
            _lastScan = now;
            Volatile.Write(ref _published, new Published { Rows = pending.Ranked, ScannedAt = now, Truncated = truncated });
            published = true;
        });
        return published ? pending.Ranked : Volatile.Read(ref _published).Rows;
    }

    private PendingRank Prepare(IReadOnlyList<HostProcessObservation> rows, TimeSpan elapsed)
    {
        var cpu = new Dictionary<(int Pid, long StartTicks), long>(_cpu);
        var seen = new HashSet<(int Pid, long StartTicks)>();
        var ranked = new List<HostTopConsumerSample>();
        foreach (var row in rows)
        {
            var key = (row.Pid, row.StartTicks);
            if (!seen.Add(key)) continue;
            double? cores = null;
            if (elapsed.TotalSeconds > 0 && row.TicksPerSecond > 0 && cpu.TryGetValue(key, out var previous))
            {
                var delta = row.CpuTicks - previous;
                if (delta >= 0)
                {
                    var value = delta / (double)row.TicksPerSecond / elapsed.TotalSeconds;
                    if (double.IsFinite(value) && value >= 0) cores = value;
                }
            }

            cpu[key] = row.CpuTicks;
            if (cores is null) continue;
            if (!HostConsumerRules.IsName(row.Name) || !HostConsumerRules.IsRole(row.Role)) continue;
            ranked.Add(new HostTopConsumerSample(row.Pid, row.StartTicks, row.Name, row.Role, cores, row.RssBytes));
        }

        foreach (var key in cpu.Keys.ToArray())
        {
            if (!seen.Contains(key)) cpu.Remove(key);
        }

        ranked.Sort(static (left, right) =>
        {
            var byCpu = right.CpuCores!.Value.CompareTo(left.CpuCores!.Value);
            return byCpu != 0 ? byCpu : left.Pid.CompareTo(right.Pid);
        });
        if (ranked.Count > HostTopConsumerLimits.TopCount)
            ranked.RemoveRange(HostTopConsumerLimits.TopCount, ranked.Count - HostTopConsumerLimits.TopCount);
        return new PendingRank(ranked, cpu);
    }

    private sealed record PendingRank(List<HostTopConsumerSample> Ranked, Dictionary<(int Pid, long StartTicks), long> Cpu);
}
