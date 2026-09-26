using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-0718. The runner's snapshot the phone-home dispatcher returns. The store is the source.</summary>
public interface IHostStatsSource
{
    RunnerHostStatsDto Snapshot();

    RunnerHostSeriesDto Series(string metric, string window);
}

/// <summary>
/// CARD-0718 D-2. A fixed ring of host samples. Rollups and series are computed from the ring on
/// read; an empty window is null, never zero. Capacity is <c>RetentionMinutes * 60 / IntervalSeconds</c>.
/// </summary>
public sealed class HostStatsStore : IHostStatsSource
{
    private readonly object _gate = new();
    private readonly HostSample[] _slots;
    private readonly int _capacity;
    private readonly int _intervalSeconds;
    private readonly int _retentionMinutes;
    private readonly TimeProvider _time;
    private readonly BuildSlotBroker? _broker;
    private int _next;
    private int _count;

    public HostStatsStore(HostStatsSettings settings, TimeProvider time, BuildSlotBroker? broker = null)
    {
        if (settings.IntervalMs < 1000)
            throw new ArgumentOutOfRangeException(nameof(settings), "IntervalMs must be at least 1000.");
        if (settings.RetentionMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(settings), "RetentionMinutes must be at least 1.");
        _intervalSeconds = settings.IntervalMs / 1000;
        _retentionMinutes = settings.RetentionMinutes;
        _capacity = Math.Max(1, _retentionMinutes * 60 / _intervalSeconds);
        _slots = new HostSample[_capacity];
        _time = time;
        _broker = broker;
    }

    /// <summary>The same object <see cref="Add"/> and the reads lock, so a test can hold it.</summary>
    internal object Gate => _gate;

    public int IntervalSeconds => _intervalSeconds;

    public int RetentionMinutes => _retentionMinutes;

    public void Add(HostSample sample)
    {
        lock (_gate)
        {
            _slots[_next] = sample;
            _next = (_next + 1) % _capacity;
            if (_count < _capacity)
                _count++;
        }
    }

    public HostSample? Latest()
    {
        lock (_gate)
        {
            if (_count == 0)
                return null;
            return _slots[(_next - 1 + _capacity) % _capacity];
        }
    }

    public IReadOnlyDictionary<string, RunnerHostWindowRollups> Rollups(DateTimeOffset now)
    {
        lock (_gate)
            return RollupsOf(Copy(), now);
    }

    public IReadOnlyList<RunnerHostSeriesPoint> Series(string metric, string window, DateTimeOffset now)
    {
        var span = WindowSpan(window);
        var selector = MetricSelector(metric);
        lock (_gate)
        {
            var cutoff = now - span;
            var points = new List<RunnerHostSeriesPoint>();
            foreach (var sample in Copy())
            {
                if (sample.At < cutoff)
                    continue;
                if (selector(sample) is double value)
                    points.Add(new RunnerHostSeriesPoint(sample.At, value));
            }

            return points.ToArray();
        }
    }

    public RunnerHostStatsDto Snapshot(DateTimeOffset now)
    {
        var slots = ReadBuildSlots();
        lock (_gate)
        {
            var samples = Copy();
            HostSample? latest = samples.Count == 0 ? null : samples[^1];
            return new RunnerHostStatsDto(
                latest?.At,
                _intervalSeconds,
                _retentionMinutes,
                latest?.Cores,
                latest is { } value ? ToCurrent(value) : null,
                RollupsOf(samples, now),
                slots);
        }
    }

    public RunnerHostStatsDto Snapshot() => Snapshot(_time.GetUtcNow());

    public RunnerHostSeriesDto Series(string metric, string window)
        => new(metric, window, _intervalSeconds, Series(metric, window, _time.GetUtcNow()));

    internal static bool KnownQuery(string? metric, string? window)
        => metric is "cpu" or "load" or "memory"
           && window is "1m" or "5m" or "15m" or "30m";

    private RunnerHostBuildSlotsDto? ReadBuildSlots()
    {
        if (_broker is null)
            return null;
        var listing = _broker.List();
        return new RunnerHostBuildSlotsDto(listing.Occupied, listing.Budget, listing.Waiters.Count, listing.Memory.AvailableMb);
    }

    private List<HostSample> Copy()
    {
        var list = new List<HostSample>(_count);
        var start = _count < _capacity ? 0 : _next;
        for (var i = 0; i < _count; i++)
            list.Add(_slots[(start + i) % _capacity]);
        return list;
    }

    private static Dictionary<string, RunnerHostWindowRollups> RollupsOf(IReadOnlyList<HostSample> samples, DateTimeOffset now)
        => new()
        {
            ["1m"] = Window(samples, now, TimeSpan.FromMinutes(1)),
            ["5m"] = Window(samples, now, TimeSpan.FromMinutes(5)),
            ["15m"] = Window(samples, now, TimeSpan.FromMinutes(15)),
            ["30m"] = Window(samples, now, TimeSpan.FromMinutes(30)),
        };

    private static RunnerHostWindowRollups Window(IReadOnlyList<HostSample> samples, DateTimeOffset now, TimeSpan span)
    {
        var cutoff = now - span;
        var held = new List<HostSample>();
        foreach (var sample in samples)
        {
            if (sample.At >= cutoff)
                held.Add(sample);
        }

        return new RunnerHostWindowRollups(
            Roll(held, static sample => sample.CpuPercent),
            Roll(held, static sample => sample.Load1),
            Roll(held, MemoryUsed));
    }

    private static RunnerHostRollupDto? Roll(List<HostSample> held, Func<HostSample, double?> selector)
    {
        if (held.Count == 0)
            return null;
        double sum = 0;
        var count = 0;
        double max = 0;
        var any = false;
        foreach (var sample in held)
        {
            if (selector(sample) is not double value)
                continue;
            sum += value;
            count++;
            if (!any || value > max)
            {
                max = value;
                any = true;
            }
        }

        if (count == 0)
            return null;
        return new RunnerHostRollupDto(sum / count, max);
    }

    private static TimeSpan WindowSpan(string window) => window switch
    {
        "1m" => TimeSpan.FromMinutes(1),
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        _ => throw new ArgumentException($"Unknown host stats window '{window}'.", nameof(window)),
    };

    private static Func<HostSample, double?> MetricSelector(string metric) => metric switch
    {
        "cpu" => static sample => sample.CpuPercent,
        "load" => static sample => sample.Load1,
        "memory" => MemoryUsed,
        _ => throw new ArgumentException($"Unknown host stats metric '{metric}'.", nameof(metric)),
    };

    private static long? MemoryUsedBytes(HostSample sample)
        => sample.MemoryTotalBytes is long total && sample.MemoryAvailableBytes is long available
            ? Math.Max(0L, total - available)
            : null;

    private static double? MemoryUsed(HostSample sample)
        => MemoryUsedBytes(sample) is long bytes ? bytes : null;

    private static RunnerHostCurrentDto ToCurrent(HostSample sample)
    {
        long? swapUsed = sample.SwapTotalBytes is long swapTotal && sample.SwapFreeBytes is long swapFree
            ? Math.Max(0, swapTotal - swapFree)
            : null;
        var disks = new List<RunnerHostDiskDto>(sample.Disks.Count);
        foreach (var disk in sample.Disks)
            disks.Add(new RunnerHostDiskDto(disk.Path, disk.FreeBytes, disk.TotalBytes));
        var processes = new List<RunnerHostProcessDto>(sample.Processes.Count);
        foreach (var process in sample.Processes)
            processes.Add(new RunnerHostProcessDto(process.Name, process.SessionId, process.CpuPercent, process.WorkingSetBytes));
        return new RunnerHostCurrentDto(
            sample.CpuPercent,
            sample.Load1,
            sample.Load5,
            sample.Load15,
            MemoryUsedBytes(sample),
            sample.MemoryAvailableBytes,
            sample.MemoryTotalBytes,
            swapUsed,
            sample.SwapTotalBytes,
            disks,
            sample.ProcessCount,
            processes);
    }
}
