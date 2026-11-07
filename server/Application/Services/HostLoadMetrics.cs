using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public static class HostLoadMetrics
{
    public static IReadOnlyDictionary<string, double?> Values(HostLoadSample sample)
    {
        var values = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["cpuPercent"] = sample.CpuPercent, ["busyCoreEquivalent"] = sample.BusyCoreEquivalent,
            ["ioWaitPercent"] = sample.IoWaitPercent, ["load1"] = sample.Load1, ["load5"] = sample.Load5,
            ["load15"] = sample.Load15, ["loadPerCore"] = sample.LoadPerCore,
            ["memory.totalBytes"] = sample.Memory.TotalBytes, ["memory.availableBytes"] = sample.Memory.AvailableBytes,
            ["memory.usedBytes"] = sample.Memory.UsedBytes, ["swap.totalBytes"] = sample.Swap.TotalBytes,
            ["swap.freeBytes"] = sample.Swap.FreeBytes, ["swap.usedBytes"] = sample.Swap.UsedBytes,
            ["processCount"] = sample.ProcessCount, ["authority.occupied"] = sample.Authority.Occupied,
            ["authority.activeCommands"] = sample.Authority.ActiveCommands,
        };
        for (var i = 0; i < sample.Volumes.Count; i++)
        { values["volumes." + i + ".freeBytes"] = sample.Volumes[i].FreeBytes; values["volumes." + i + ".totalBytes"] = sample.Volumes[i].TotalBytes; }
        return values;
    }
    public static double BinWidth(string metric) => metric.Contains("Bytes", StringComparison.Ordinal) ? 1024 * 1024
        : metric.Contains("load", StringComparison.Ordinal) ? 0.01
        : metric is "cpuPercent" or "ioWaitPercent" or "busyCoreEquivalent" ? 0.1 : 1;
    public static DateTimeOffset Hour(DateTimeOffset at) => new(at.UtcDateTime.Year, at.UtcDateTime.Month, at.UtcDateTime.Day, at.UtcDateTime.Hour, 0, 0, TimeSpan.Zero);
    public static string Identity(HostLoadSource source) => source.RunnerStoreId.ToString("N") + ":" + source.BootId.ToString("N") + ":" + source.Sequence;
}
