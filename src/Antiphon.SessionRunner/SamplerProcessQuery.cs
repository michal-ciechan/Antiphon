using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1757. Reads one process's CPU time and working set without a whole-system snapshot.
/// Windows opens the pid once with <c>PROCESS_QUERY_LIMITED_INFORMATION</c> and reads both
/// figures from that handle. Linux keeps one <c>/proc</c> read through <see cref="Process"/>.
/// <see cref="WorkingSet"/> reuses the working set from the immediately preceding
/// <see cref="TryGetTotalCpuTime"/> for the same pid on this instance. Nothing is kept
/// for a later sample, so the extra staleness of that reuse is zero sample intervals.
/// </summary>
internal sealed class SamplerProcessQuery : IProcessCpuProbe
{
    // Same window as SystemProcessCpuProbe: a later start time is a recycled pid.
    private static readonly TimeSpan PidReuseTolerance = TimeSpan.FromMinutes(2);
    private readonly object _gate = new();
    private int _cachedPid;
    private long? _cachedWorkingSet;
    private bool _cacheFresh;
    private int _queries;

    internal int Queries => Volatile.Read(ref _queries);

    public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt)
    {
        var reading = Read(pid, startedAt);
        lock (_gate)
        {
            _cachedPid = pid;
            _cachedWorkingSet = reading.WorkingSet;
            _cacheFresh = true;
        }

        return reading.Cpu;
    }

    public long? WorkingSet(int pid)
    {
        lock (_gate)
        {
            if (_cacheFresh && _cachedPid == pid)
            {
                _cacheFresh = false;
                return _cachedWorkingSet;
            }
        }

        return Read(pid, startedAt: default).WorkingSet;
    }

    private Reading Read(int pid, DateTime startedAt)
    {
        Interlocked.Increment(ref _queries);
        if (pid <= 0)
            return Reading.Missing;

        try
        {
            if (OperatingSystem.IsWindows())
                return ReadWindows(pid, startedAt);
            if (OperatingSystem.IsLinux())
                return ReadLinux(pid, startedAt);
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or Win32Exception
            or IOException
            or UnauthorizedAccessException)
        {
            return Reading.Missing;
        }

        return Reading.Missing;
    }

    private static Reading ReadLinux(int pid, DateTime startedAt)
    {
        using var process = Process.GetProcessById(pid);
        if (process.HasExited)
            return Reading.Missing;

        TimeSpan? cpu = null;
        if (process.StartTime.ToUniversalTime() <= startedAt.ToUniversalTime() + PidReuseTolerance)
            cpu = process.TotalProcessorTime;
        return new Reading(cpu, process.WorkingSet64);
    }

    private static Reading ReadWindows(int pid, DateTime startedAt)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return Reading.Missing;

        try
        {
            if (!GetExitCodeProcess(handle, out var exitCode) || exitCode != StillActive)
                return Reading.Missing;

            long? workingSet = null;
            var counters = new ProcessMemoryCounters
            {
                cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>(),
            };
            if (K32GetProcessMemoryInfo(handle, ref counters, counters.cb))
                workingSet = (long)counters.WorkingSetSize;

            TimeSpan? cpu = null;
            if (GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
            {
                var startedUtc = DateTime.FromFileTimeUtc(creation);
                if (startedUtc <= startedAt.ToUniversalTime() + PidReuseTolerance)
                {
                    cpu = pid == Environment.ProcessId
                        ? Environment.CpuUsage.TotalTime
                        : new TimeSpan(kernel + user);
                }
            }

            return new Reading(cpu, workingSet);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private readonly record struct Reading(TimeSpan? Cpu, long? WorkingSet)
    {
        public static Reading Missing => new(null, null);
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int StillActive = 259;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr handle, out int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr handle,
        out long creation,
        out long exit,
        out long kernel,
        out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetProcessMemoryInfo(
        IntPtr handle,
        ref ProcessMemoryCounters counters,
        uint size);
}
