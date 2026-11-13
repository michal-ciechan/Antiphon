using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1884. CPU deltas are keyed by pid and start ticks. A recycled pid has no previous counter.
/// A scan closer than five seconds returns the previous ranking.
/// </summary>
internal sealed class HostTopConsumerTracker
{
    private readonly Dictionary<(int Pid, long StartTicks), long> _cpu = new();
    private long _lastScan = long.MinValue;
    private IReadOnlyList<HostTopConsumerSample> _published = [];

    public IReadOnlyList<HostTopConsumerSample> Sample(IHostProcessTable? table, TimeProvider time)
    {
        if (table is null) return [];
        var now = time.GetTimestamp();
        if (_lastScan != long.MinValue && time.GetElapsedTime(_lastScan, now) < HostTopConsumerLimits.MinScanInterval)
            return _published;
        IReadOnlyList<HostProcessObservation> rows;
        try
        {
            var retain = new List<int>(_cpu.Count);
            foreach (var key in _cpu.Keys) retain.Add(key.Pid);
            rows = table.Read(HostTopConsumerLimits.ScanCap, retain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _published;
        }

        var elapsed = _lastScan == long.MinValue ? TimeSpan.Zero : time.GetElapsedTime(_lastScan, now);
        _lastScan = now;
        _published = Rank(rows, elapsed);
        return _published;
    }

    private IReadOnlyList<HostTopConsumerSample> Rank(IReadOnlyList<HostProcessObservation> rows, TimeSpan elapsed)
    {
        var seen = new HashSet<(int Pid, long StartTicks)>();
        var ranked = new List<HostTopConsumerSample>();
        foreach (var row in rows)
        {
            var key = (row.Pid, row.StartTicks);
            if (!seen.Add(key)) continue;
            double? cores = null;
            if (elapsed.TotalSeconds > 0 && row.TicksPerSecond > 0 && _cpu.TryGetValue(key, out var previous))
            {
                var delta = row.CpuTicks - previous;
                if (delta >= 0)
                {
                    var value = delta / (double)row.TicksPerSecond / elapsed.TotalSeconds;
                    if (double.IsFinite(value) && value >= 0) cores = value;
                }
            }

            _cpu[key] = row.CpuTicks;
            if (cores is null) continue;
            if (!HostConsumerRules.IsName(row.Name) || !HostConsumerRules.IsRole(row.Role)) continue;
            ranked.Add(new HostTopConsumerSample(row.Pid, row.StartTicks, row.Name, row.Role, cores, row.RssBytes));
        }

        foreach (var key in _cpu.Keys.ToArray())
        {
            if (!seen.Contains(key)) _cpu.Remove(key);
        }

        ranked.Sort(static (left, right) =>
        {
            var byCpu = right.CpuCores!.Value.CompareTo(left.CpuCores!.Value);
            return byCpu != 0 ? byCpu : left.Pid.CompareTo(right.Pid);
        });
        if (ranked.Count > HostTopConsumerLimits.TopCount)
            ranked.RemoveRange(HostTopConsumerLimits.TopCount, ranked.Count - HostTopConsumerLimits.TopCount);
        return ranked;
    }
}
