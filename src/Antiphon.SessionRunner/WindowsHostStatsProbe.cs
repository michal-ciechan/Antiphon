using System.Runtime.InteropServices;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0718 D-1. Windows host figures from <c>GetSystemTimes</c> and <c>GlobalMemoryStatusEx</c>.
/// Kernel time includes idle, so busy is <c>(kernel - idle) + user</c> over <c>kernel + user</c>.
/// A failed call is null, not zero. Load and process count are absent in Round 1 (no PDH, no
/// process sweep). The first read has no delta and reports no CPU.
/// </summary>
public sealed class WindowsHostStatsProbe : IHostStatsProbe, IHostMemoryProbe
{
    private readonly Func<(long Idle, long Kernel, long User)?> _readSystemTimes;
    private readonly Func<(ulong TotalPhys, ulong AvailPhys, ulong TotalPageFile, ulong AvailPageFile)?> _readMemoryStatus;
    private readonly Func<string, (long Free, long Total)?> _statVolume;
    private readonly IReadOnlyList<string> _volumes;
    private (long Idle, long Kernel, long User)? _previous;

    public WindowsHostStatsProbe(IReadOnlyList<string> volumes)
        : this(ReadSystemTimes, ReadMemoryStatus, HostStatsVolumes.Stat, volumes ?? [])
    {
    }

    internal WindowsHostStatsProbe(
        Func<(long Idle, long Kernel, long User)?> readSystemTimes,
        Func<(ulong TotalPhys, ulong AvailPhys, ulong TotalPageFile, ulong AvailPageFile)?> readMemoryStatus,
        Func<string, (long Free, long Total)?> statVolume,
        IReadOnlyList<string> volumes)
    {
        _readSystemTimes = readSystemTimes;
        _readMemoryStatus = readMemoryStatus;
        _statVolume = statVolume;
        _volumes = volumes;
    }

    public long? AvailableBytes
    {
        get
        {
            try
            {
                return _readMemoryStatus() is { } memory ? (long)memory.AvailPhys : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
            {
                return null;
            }
        }
    }

    public HostSample? Read()
    {
        try
        {
            var memory = _readMemoryStatus();
            var times = _readSystemTimes();
            double? cpu = null;
            if (times is { } current)
            {
                if (_previous is { } previous)
                    cpu = CpuPercent(previous.Idle, previous.Kernel, previous.User, current.Idle, current.Kernel, current.User);
                _previous = current;
            }

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

            return new HostSample(
                DateTimeOffset.UtcNow,
                cpu,
                Environment.ProcessorCount,
                null,
                null,
                null,
                memory is { } mem ? (long)mem.TotalPhys : null,
                memory is { } memAvail ? (long)memAvail.AvailPhys : null,
                memory is { } swap ? (long)swap.TotalPageFile : null,
                memory is { } swapFree ? (long)swapFree.AvailPageFile : null,
                disks,
                null,
                []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>
    /// Kernel includes idle. 40% for idle 0→600, kernel 0→800, user 0→200.
    /// Adding idle into the denominator again (and not subtracting it from kernel) is the rejected formula.
    /// </summary>
    internal static double? CpuPercent(long idle0, long kernel0, long user0, long idle1, long kernel1, long user1)
    {
        var dIdle = idle1 - idle0;
        var dKernel = kernel1 - kernel0;
        var dUser = user1 - user0;
        if (dIdle < 0 || dKernel < 0 || dUser < 0)
            return null;
        var total = dKernel + dUser;
        var busy = dKernel - dIdle + dUser;
        if (total <= 0 || busy < 0 || busy > total)
            return null;
        return busy * 100.0 / total;
    }

    private static (long Idle, long Kernel, long User)? ReadSystemTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return null;
        return (idle, kernel, user);
    }

    private static (ulong TotalPhys, ulong AvailPhys, ulong TotalPageFile, ulong AvailPageFile)? ReadMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            return null;
        return (status.TotalPhys, status.AvailPhys, status.TotalPageFile, status.AvailPageFile);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
