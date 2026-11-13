using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Pure nullable normalization. Host and process percentages have different units.</summary>
public static class HostLoadProjection
{
    public static HostLoadSample Create(HostSample read, HostLoadSource source, HostStatsSettings settings,
        HostAdmissionSnapshot authority)
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        double? Number(string path, double? value, double maximum = double.MaxValue, string missing = "missing")
        {
            if (value is null) { reasons[path] = missing; return null; }
            if (!double.IsFinite(value.Value) || value < 0 || value > maximum)
            { reasons[path] = "invalid"; return null; }
            return value;
        }
        long? Bytes(string path, long? value)
        {
            if (value is null || value < 0) { reasons[path] = value is null ? "missing" : "invalid"; return null; }
            return value;
        }
        var cores = read.Cores > 0 ? (int?)read.Cores : null;
        if (cores is null) reasons["hostLogicalCores"] = "missing";
        var cpu = Number("cpuPercent", read.CpuPercent, 100, read.CpuUnknownReason ?? "first-delta");
        var load1 = Number("load1", read.Load1, missing: read.LoadUnsupported ? "unsupported" : "missing");
        var load5 = Number("load5", read.Load5, missing: read.LoadUnsupported ? "unsupported" : "missing");
        var load15 = Number("load15", read.Load15, missing: read.LoadUnsupported ? "unsupported" : "missing");
        var total = Bytes("memory.totalBytes", read.MemoryTotalBytes);
        var available = Bytes("memory.availableBytes", read.MemoryAvailableBytes);
        if (available > total) { available = null; reasons["memory.availableBytes"] = "invalid"; }
        var swapTotal = Bytes("swap.totalBytes", read.SwapTotalBytes);
        var swapFree = Bytes("swap.freeBytes", read.SwapFreeBytes);
        if (swapFree > swapTotal) { swapFree = null; reasons["swap.freeBytes"] = "invalid"; }
        if (settings.PhysicalHostKey is null) reasons["physicalHostKey"] = "binding-unknown";
        var processes = read.Processes.Select(p => new HostLoadProcess(
            p.Name is "runner" or "session" or "pty-host" ? p.Name : "session",
            Guid.TryParse(p.SessionId, out var id) ? id : null,
            Number("processes.cpuPercent", p.CpuPercent),
            p.WorkingSetKnown ? Bytes("processes.workingSetBytes", p.WorkingSetBytes) : null)).ToArray();
        return new HostLoadSample
        {
            Source = source, SampledAt = read.At, IntervalSeconds = settings.IntervalMs / 1000,
            PhysicalHostKey = settings.PhysicalHostKey, BindingRevision = settings.BindingRevision,
            SourceEpoch = settings.SourceEpoch, MetricScope = cores is null ? "unknown" : "host",
            HostLogicalCores = cores, EffectiveCoreLimit = Number("effectiveCoreLimit", read.EffectiveCoreLimit),
            CpuPercent = cpu, BusyCoreEquivalent = cpu * cores / 100d,
            IoWaitPercent = Number("ioWaitPercent", read.IoWaitPercent, 100),
            Load1 = load1, Load5 = load5, Load15 = load15, LoadPerCore = load1 / cores,
            Memory = new(total, available, total - available),
            Swap = new(swapTotal, swapFree, swapTotal - swapFree, read.LoadUnsupported ? "commit" : "swap"),
            Volumes = read.Disks.Select(d => new HostLoadVolume(d.Path,
                Bytes("volumes.freeBytes", d.FreeBytes), Bytes("volumes.totalBytes", d.TotalBytes))).ToArray(),
            ProcessCount = read.ProcessCount is >= 0 ? read.ProcessCount : null,
            Processes = processes, TopConsumers = Top(read), UnknownReasons = reasons, Authority = authority,
        };
    }

    private static HostLoadTopConsumer[] Top(HostSample read) => [];
}
