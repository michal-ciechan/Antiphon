using System.Globalization;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0718 D-1. Linux host figures from <c>/proc/stat</c>, <c>/proc/meminfo</c>, <c>/proc/loadavg</c>
/// and <see cref="DriveInfo"/>. Inside the server2 container these are the host's numbers (no cgroup
/// cap). CPU is the busy delta over the first eight jiffy columns; guest is already inside user/nice.
/// The first read has no delta and reports no CPU.
/// </summary>
public sealed class LinuxHostStatsProbe : IHostStatsProbe, IHostMemoryProbe
{
    private readonly Func<string, IReadOnlyList<string>> _readLines;
    private readonly Func<string, (long Free, long Total)?> _statVolume;
    private readonly IReadOnlyList<string> _volumes;
    private ProcStatTotals? _previous;

    public LinuxHostStatsProbe(IReadOnlyList<string> volumes)
        : this(static path => File.ReadAllLines(path), HostStatsVolumes.Stat, volumes ?? [])
    {
    }

    internal LinuxHostStatsProbe(
        Func<string, IReadOnlyList<string>> readLines,
        Func<string, (long Free, long Total)?> statVolume,
        IReadOnlyList<string> volumes)
    {
        _readLines = readLines;
        _statVolume = statVolume;
        _volumes = volumes;
    }

    public long? AvailableBytes
    {
        get
        {
            try
            {
                return ParseMemInfo(_readLines("/proc/meminfo")).AvailableBytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public HostSample? Read()
    {
        try
        {
            var statLine = FirstCpuLine(_readLines("/proc/stat"));
            var memory = ParseMemInfo(_readLines("/proc/meminfo"));
            var loadLine = _readLines("/proc/loadavg").FirstOrDefault();
            var load = loadLine is null ? null : ParseLoadAvg(loadLine);
            double? cpu = null;
            if (statLine is not null && ParseProcStat(statLine) is { } current)
            {
                if (_previous is { } previous)
                    cpu = CpuPercent(previous, current);
                _previous = current;
            }

            return new HostSample(
                DateTimeOffset.UtcNow,
                cpu,
                Environment.ProcessorCount,
                load?.Load1,
                load?.Load5,
                load?.Load15,
                memory.TotalBytes,
                memory.AvailableBytes,
                memory.SwapTotalBytes,
                memory.SwapFreeBytes,
                ReadDisks(),
                load?.ProcessCount,
                []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal readonly record struct ProcStatTotals(long Idle, long Total);

    internal readonly record struct MemInfo(long? TotalBytes, long? AvailableBytes, long? SwapTotalBytes, long? SwapFreeBytes);

    internal readonly record struct LoadAvg(double Load1, double Load5, double Load15, int ProcessCount);

    /// <summary>
    /// Idle is columns 4+5 (idle + iowait). Total is the first eight columns. Guest columns are excluded.
    /// An 8-column line parses; a shorter line is null rather than an index throw.
    /// </summary>
    internal static ProcStatTotals? ParseProcStat(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;
        var index = string.Equals(parts[0], "cpu", StringComparison.Ordinal) ? 1 : 0;
        if (parts.Length - index < 8)
            return null;
        Span<long> cols = stackalloc long[8];
        for (var i = 0; i < 8; i++)
        {
            if (!long.TryParse(parts[index + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out cols[i]))
                return null;
        }

        long total = 0;
        for (var i = 0; i < 8; i++)
            total += cols[i];
        return new ProcStatTotals(cols[3] + cols[4], total);
    }

    internal static double? CpuPercent(ProcStatTotals previous, ProcStatTotals current)
    {
        var dTotal = current.Total - previous.Total;
        var dIdle = current.Idle - previous.Idle;
        if (dTotal <= 0 || dIdle < 0 || dIdle > dTotal)
            return null;
        return (1.0 - (double)dIdle / dTotal) * 100.0;
    }

    internal static MemInfo ParseMemInfo(IEnumerable<string> lines)
    {
        long? total = null, available = null, swapTotal = null, swapFree = null;
        foreach (var line in lines)
        {
            if (TryKb(line, "MemTotal:", out var memTotal))
                total = memTotal * 1024;
            else if (TryKb(line, "MemAvailable:", out var memAvailable))
                available = memAvailable * 1024;
            else if (TryKb(line, "SwapTotal:", out var swapTotalKb))
                swapTotal = swapTotalKb * 1024;
            else if (TryKb(line, "SwapFree:", out var swapFreeKb))
                swapFree = swapFreeKb * 1024;
        }

        return new MemInfo(total, available, swapTotal, swapFree);
    }

    internal static LoadAvg? ParseLoadAvg(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var load1)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var load5)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var load15))
            return null;
        var pair = parts[3].Split('/');
        if (pair.Length != 2 || !int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processes))
            return null;
        return new LoadAvg(load1, load5, load15, processes);
    }

    private static bool TryKb(string line, string label, out long kb)
    {
        kb = 0;
        if (!line.StartsWith(label, StringComparison.Ordinal))
            return false;
        var rest = line.AsSpan(label.Length).Trim();
        var end = 0;
        while (end < rest.Length && char.IsDigit(rest[end]))
            end++;
        return end > 0 && long.TryParse(rest[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out kb);
    }

    private static string? FirstCpuLine(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("cpu ", StringComparison.Ordinal) || line == "cpu")
                return line;
        }

        return lines.Count > 0 ? lines[0] : null;
    }

    private List<HostDiskSample> ReadDisks()
    {
        var disks = new List<HostDiskSample>(_volumes.Count);
        foreach (var volume in _volumes)
        {
            if (string.IsNullOrWhiteSpace(volume))
                continue;
            try
            {
                if (_statVolume(volume) is { } stat)
                    disks.Add(new HostDiskSample(volume, stat.Free, stat.Total));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        return disks;
    }
}
