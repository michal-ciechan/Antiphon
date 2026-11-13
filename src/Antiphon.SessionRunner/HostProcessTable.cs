using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

internal readonly record struct HostProcessObservation(
    int Pid,
    long StartTicks,
    long CpuTicks,
    long TicksPerSecond,
    long? RssBytes,
    string Name,
    string Role);

internal interface IHostProcessTable
{
    IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids);
}

internal static class HostTopConsumerLimits
{
    public const int TopCount = 8;
    public const int ScanCap = 512;
    public static readonly TimeSpan MinScanInterval = TimeSpan.FromSeconds(5);
}

internal static class HostProcessTables
{
    public static IHostProcessTable Create() => Select(OperatingSystem.IsWindows(), OperatingSystem.IsLinux());

    public static IHostProcessTable Select(bool isWindows, bool isLinux) => EmptyHostProcessTable.Instance;
}

internal sealed class EmptyHostProcessTable : IHostProcessTable
{
    public static readonly EmptyHostProcessTable Instance = new();

    public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids) => [];
}

internal static class HostProcessSelection
{
    public static List<int> Choose(IReadOnlyList<int> pids, IReadOnlyCollection<int> retain, int max) => [];
}

internal static class HostConsumerClassifier
{
    public static (string Name, string Role) Classify(string comm, IReadOnlyList<string> argBasenames)
        => ("unknown", "other");
}

internal static class LinuxProcessStat
{
    public static HostProcessObservation? Parse(int pid, string stat, ReadOnlySpan<byte> cmdline, int pageSize, long ticksPerSecond)
        => null;
}

internal sealed class LinuxHostProcessTable : IHostProcessTable
{
    public LinuxHostProcessTable(string root)
    {
    }

    internal LinuxHostProcessTable(string root, int pageSize, long ticksPerSecond)
    {
    }

    public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids) => [];
}

internal sealed class WindowsHostProcessTable : IHostProcessTable
{
    public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids)
    {
        if (!OperatingSystem.IsWindows()) return [];
        return [];
    }
}
