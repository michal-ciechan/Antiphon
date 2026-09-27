using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Checkpoints;

/// <summary>
/// Starts one row with <see cref="Process.Start(ProcessStartInfo)"/> so argument
/// quoting and standard-handle inheritance stay with the runtime, then assigns
/// that process to a kill-on-close job. Assignment is best-effort: a child
/// spawned before the assignment, or by a package alias such as the MSIX pwsh
/// stub, can escape the job. Timeout and cancel still sweep the process list.
/// </summary>
internal sealed class WindowsJobProcessHandle : IProcessHandle
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitClass = 9;
    private const uint ProcessTerminate = 0x0001;
    private const uint Th32CsSnapProcess = 0x00000002;

    private readonly Process _process;
    private SafeFileHandle? _job;
    private int _exitCode = 1;
    private int _pid;
    private int _disposed;

    public WindowsJobProcessHandle(ProcessStartInfo start) =>
        _process = new Process { StartInfo = start, EnableRaisingEvents = true };

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
            {
                return true;
            }
        }
    }

    public int ExitCode
    {
        get
        {
            try { return _process.ExitCode; }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
            {
                return _exitCode;
            }
        }
    }

    public bool Start()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows driver jobs require Windows.");
        if (!_process.Start())
            return false;
        try
        {
            _pid = _process.Id;
            TryAssignJob();
            _process.Exited += OnExited;
            if (_process.HasExited)
                OnExited(_process, EventArgs.Empty);
            return true;
        }
        catch
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
            {
            }
            throw;
        }
    }

    public void BeginRead(Action<string?> stdout, Action<string?> stderr)
    {
        _process.OutputDataReceived += (_, e) => stdout(e.Data);
        _process.ErrorDataReceived += (_, e) => stderr(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Kill(bool entireProcessTree)
    {
        _ = entireProcessTree;
        TerminateDescendants();
        try
        {
            if (_job is not null && !_job.IsInvalid && !_job.IsClosed)
                TerminateJobObject(_job, 1);
        }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
        }
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try { _process.Exited -= OnExited; }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
        try { _job?.Dispose(); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
        try { _process.Dispose(); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        try
        {
            _exitCode = _process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
        }
    }

    private void TryAssignJob()
    {
        SafeFileHandle? job = null;
        try
        {
            job = CreateKillOnCloseJob();
            if (!AssignProcessToJobObject(job, _process.SafeHandle))
            {
                job.Dispose();
                return;
            }

            _job = job;
            job = null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            job?.Dispose();
        }
    }

    private void TerminateDescendants()
    {
        if (_pid <= 0)
            return;
        foreach (var pid in DescendantPids(_pid))
        {
            var handle = OpenProcess(ProcessTerminate, false, pid);
            if (handle == IntPtr.Zero)
                continue;
            try { TerminateProcess(handle, 1); }
            finally { CloseHandle(handle); }
        }
    }

    private static List<int> DescendantPids(int root)
    {
        var entries = new List<(int Pid, int Parent)>();
        var snapshot = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        try
        {
            var entry = new ProcessEntry { dwSize = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry))
            {
                do { entries.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID)); }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var known = new HashSet<int> { root };
        var descendants = new List<int>();
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (pid, parent) in entries)
            {
                if (pid == root || !known.Contains(parent) || !known.Add(pid))
                    continue;
                descendants.Add(pid);
                grew = true;
            }
        }

        return descendants;
    }

    private static SafeFileHandle CreateKillOnCloseJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObjectW failed");
        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
        };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitClass, ref limits, Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "SetInformationJobObject failed");
        }

        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
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
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref JobObjectExtendedLimitInformation information, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeHandle job, SafeHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
