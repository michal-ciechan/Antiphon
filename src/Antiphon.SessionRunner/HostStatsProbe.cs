namespace Antiphon.SessionRunner;

/// <summary>CARD-0718. One disk on a configured volume. Bytes, not a percentage.</summary>
public readonly record struct HostDiskSample(string Path, long FreeBytes, long TotalBytes);

/// <summary>CARD-0718. One process the sampler could name. A null CPU is "cannot say".</summary>
public readonly record struct HostProcessSample(string Name, string? SessionId, double? CpuPercent, long WorkingSetBytes);

/// <summary>
/// CARD-0718 D-1. One host reading. Null metrics are absent (the first CPU sample, load on Windows),
/// never a zero standing in for "unknown".
/// </summary>
public readonly record struct HostSample(
    DateTimeOffset At,
    double? CpuPercent,
    int Cores,
    double? Load1,
    double? Load5,
    double? Load15,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes,
    long? SwapTotalBytes,
    long? SwapFreeBytes,
    IReadOnlyList<HostDiskSample> Disks,
    int? ProcessCount,
    IReadOnlyList<HostProcessSample> Processes);

/// <summary>CARD-0718. The platform read. Null when this OS has no arm or the read faults.</summary>
public interface IHostStatsProbe
{
    HostSample? Read();
}

/// <summary>
/// CARD-0718 D-1. Picks the Linux or Windows arm once. Also the build-slot memory probe, so the
/// floor and the Hosts page cannot disagree.
/// </summary>
public sealed class SystemHostStatsProbe : IHostStatsProbe, IHostMemoryProbe
{
    private readonly IHostStatsProbe? _arm;

    public SystemHostStatsProbe(HostStatsSettings settings)
    {
        _arm = SelectArm(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), settings.Volumes ?? []);
    }

    public HostSample? Read() => _arm?.Read();

    public long? AvailableBytes => (_arm as IHostMemoryProbe)?.AvailableBytes;

    /// <summary>
    /// Pure platform switch. Constructing either arm does no OS call; the public constructors only
    /// store delegates.
    /// </summary>
    internal static IHostStatsProbe? SelectArm(bool isWindows, bool isLinux)
        => SelectArm(isWindows, isLinux, []);

    internal static IHostStatsProbe? SelectArm(bool isWindows, bool isLinux, IReadOnlyList<string> volumes)
    {
        if (isWindows && !isLinux)
            return new WindowsHostStatsProbe(volumes);
        if (isLinux && !isWindows)
            return new LinuxHostStatsProbe(volumes);
        return null;
    }
}

internal static class HostStatsVolumes
{
    public static (long Free, long Total)? Stat(string path)
    {
        try
        {
            var info = new DriveInfo(path);
            if (!info.IsReady)
                return null;
            return (info.AvailableFreeSpace, info.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
