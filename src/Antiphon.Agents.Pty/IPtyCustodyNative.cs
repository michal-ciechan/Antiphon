using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Antiphon.Agents.Pty;

/// <summary>Win32 I/O seam for the custody checks, not a policy or a substitute job.</summary>
internal interface IPtyCustodyNative
{
    bool IsInJob(IntPtr process, SafeFileHandle job);
    uint QueryLimitFlags(SafeFileHandle job);
    uint QueryActiveProcesses(SafeFileHandle job);
    uint Resume(IntPtr thread);
    PtyTerminationObservation Terminate(SafeFileHandle job);
}

public sealed record PtyTerminationObservation(bool Succeeded, int ErrorCode);

internal sealed class WindowsPtyCustodyNative : IPtyCustodyNative
{
    public bool IsInJob(IntPtr process, SafeFileHandle job)
    {
        if (!IsProcessInJob(process, job, out var contained)) ThrowLastError("IsProcessInJob");
        return contained;
    }

    public uint QueryLimitFlags(SafeFileHandle job)
    {
        if (!QueryLimits(job, 9, out var limits, Marshal.SizeOf<ExtendedLimits>(), IntPtr.Zero))
            ThrowLastError("QueryInformationJobObject limits");
        return limits.Basic.LimitFlags;
    }

    public uint QueryActiveProcesses(SafeFileHandle job)
    {
        if (!QueryAccounting(job, 1, out var accounting, Marshal.SizeOf<Accounting>(), IntPtr.Zero))
            ThrowLastError("QueryInformationJobObject accounting");
        return accounting.ActiveProcesses;
    }

    public uint Resume(IntPtr thread) => ResumeThread(thread);

    public PtyTerminationObservation Terminate(SafeFileHandle job)
    {
        var ok = TerminateJobObject(job, 1);
        return new(ok, ok ? 0 : Marshal.GetLastWin32Error());
    }

    private static void ThrowLastError(string operation)
    {
        var code = Marshal.GetLastWin32Error();
        throw new System.ComponentModel.Win32Exception(code, operation + " failed");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, SafeFileHandle job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint code);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryLimits(SafeFileHandle job, int kind, out ExtendedLimits info, int size, IntPtr length);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryAccounting(SafeFileHandle job, int kind, out Accounting info, int size, IntPtr length);

    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
}
