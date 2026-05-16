using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Agents.Pty;

internal sealed class WindowsJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const int JobObjectBasicProcessIdList = 3;
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MemoryKillExitCode = 137;

    private readonly SafeFileHandle _jobHandle;
    private readonly ulong _memoryLimitBytes;
    private int _memoryLimitReached;

    private WindowsJobObject(SafeFileHandle jobHandle, ulong memoryLimitBytes)
    {
        _jobHandle = jobHandle;
        _memoryLimitBytes = memoryLimitBytes;
    }

    public bool MemoryLimitReached => Volatile.Read(ref _memoryLimitReached) == 1;

    public static WindowsJobObject AssignMemoryLimitedJob(int pid, int memoryLimitMb)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows JobObject memory limits require Windows.");
        if (pid <= 0)
            throw new ArgumentOutOfRangeException(nameof(pid));
        if (memoryLimitMb <= 0)
            throw new ArgumentOutOfRangeException(nameof(memoryLimitMb));

        var memoryLimitBytes = checked((ulong)memoryLimitMb * 1024UL * 1024UL);
        var hardLimitBytes = checked(memoryLimitBytes + Math.Max(64UL * 1024UL * 1024UL, memoryLimitBytes / 4UL));
        var jobHandle = CreateJobObjectW(IntPtr.Zero, null);
        if (jobHandle.IsInvalid)
            throw LastWin32Exception("CreateJobObjectW failed.");

        try
        {
            var limitInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                        | JobObjectLimitProcessMemory
                        | JobObjectLimitJobMemory
                },
                ProcessMemoryLimit = new UIntPtr(hardLimitBytes),
                JobMemoryLimit = new UIntPtr(hardLimitBytes)
            };

            if (!SetInformationJobObject(
                    jobHandle,
                    JobObjectExtendedLimitInformation,
                    ref limitInfo,
                    Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            {
                throw LastWin32Exception("SetInformationJobObject failed.");
            }

            using var processHandle = OpenProcess(
                ProcessTerminate | ProcessSetQuota | ProcessQueryLimitedInformation,
                false,
                pid);
            if (processHandle.IsInvalid)
                throw LastWin32Exception($"OpenProcess failed for PID {pid}.");

            if (!AssignProcessToJobObject(jobHandle, processHandle))
                throw LastWin32Exception($"AssignProcessToJobObject failed for PID {pid}.");

            return new WindowsJobObject(jobHandle, memoryLimitBytes);
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }
    }

    public async Task MonitorMemoryLimitAsync(
        Task processExited,
        Action onMemoryLimitReached,
        CancellationToken ct)
    {
        while (!processExited.IsCompleted && !ct.IsCancellationRequested)
        {
            if (HasReachedMemoryLimit())
            {
                onMemoryLimitReached();
                TryTerminate();
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public bool HasReachedMemoryLimit()
    {
        if (MemoryLimitReached)
            return true;

        if (TryGetCurrentPrivateBytes(out var currentPrivateBytes) && currentPrivateBytes >= _memoryLimitBytes)
        {
            Volatile.Write(ref _memoryLimitReached, 1);
            return true;
        }

        if (!TryQueryExtendedInfo(out var info))
            return false;

        var peakProcessMemory = info.PeakProcessMemoryUsed.ToUInt64();
        var peakJobMemory = info.PeakJobMemoryUsed.ToUInt64();
        if (peakProcessMemory >= _memoryLimitBytes || peakJobMemory >= _memoryLimitBytes)
        {
            Volatile.Write(ref _memoryLimitReached, 1);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        _jobHandle.Dispose();
    }

    private bool TryQueryExtendedInfo(out JOBOBJECT_EXTENDED_LIMIT_INFORMATION info)
    {
        if (!OperatingSystem.IsWindows() || _jobHandle.IsInvalid || _jobHandle.IsClosed)
        {
            info = default;
            return false;
        }

        return QueryInformationJobObject(
            _jobHandle,
            JobObjectExtendedLimitInformation,
            out info,
            Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(),
            out _);
    }

    private bool TryGetCurrentPrivateBytes(out ulong privateBytes)
    {
        privateBytes = 0;
        if (!TryGetAssignedProcessIds(out var processIds) || processIds.Length == 0)
            return false;

        foreach (var processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                privateBytes += (ulong)Math.Max(0, process.PrivateMemorySize64);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // The process exited between the job query and process lookup.
            }
        }

        return true;
    }

    private bool TryGetAssignedProcessIds(out int[] processIds)
    {
        processIds = [];
        if (!OperatingSystem.IsWindows() || _jobHandle.IsInvalid || _jobHandle.IsClosed)
            return false;

        var processIdCapacity = 256;
        var bufferSize = 8 + processIdCapacity * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (!QueryInformationJobObjectBuffer(
                    _jobHandle,
                    JobObjectBasicProcessIdList,
                    buffer,
                    bufferSize,
                    out _))
            {
                return false;
            }

            var countInList = Marshal.ReadInt32(buffer, 4);
            if (countInList <= 0)
                return true;

            var ids = new List<int>(countInList);
            var offset = 8;
            for (var i = 0; i < countInList; i++)
            {
                var rawProcessId = IntPtr.Size == 8
                    ? Marshal.ReadInt64(buffer, offset + i * IntPtr.Size)
                    : Marshal.ReadInt32(buffer, offset + i * IntPtr.Size);
                if (rawProcessId > 0 && rawProcessId <= int.MaxValue)
                    ids.Add((int)rawProcessId);
            }

            processIds = ids.ToArray();
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void TryTerminate()
    {
        if (!OperatingSystem.IsWindows() || _jobHandle.IsInvalid || _jobHandle.IsClosed)
            return;

        _ = TerminateJobObject(_jobHandle, MemoryKillExitCode);
    }

    private static Win32Exception LastWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle hJob,
        int jobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(
        SafeFileHandle hJob,
        int jobObjectInfoClass,
        out JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        int cbJobObjectInfoLength,
        out int lpReturnLength);

    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryInformationJobObjectBuffer(
        SafeFileHandle hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        int cbJobObjectInfoLength,
        out int lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenProcess(
        uint dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle hJob, SafeFileHandle hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle hJob, uint uExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
