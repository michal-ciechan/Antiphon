using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Checkpoints;

/// <summary>
/// Starts one driver process inside a kill-on-close job so a grandchild
/// whose parent has already exited still dies on cancel or timeout.
/// </summary>
internal sealed class WindowsJobProcessHandle : IProcessHandle
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitClass = 9;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint StillActive = 259;
    private const uint ProcessTerminate = 0x0001;
    private const uint Th32CsSnapProcess = 0x00000002;

    private readonly ProcessStartInfo _start;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SafeFileHandle? _job;
    private SafeFileHandle? _process;
    private StreamReader? _stdout;
    private StreamReader? _stderr;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private ManualResetEvent? _signal;
    private RegisteredWaitHandle? _wait;
    private int _exitCode = 1;
    private int _pid;

    public WindowsJobProcessHandle(ProcessStartInfo start) => _start = start;

    public bool HasExited => _exited.Task.IsCompleted;
    public int ExitCode => _exitCode;

    public bool Start()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows driver jobs require Windows.");
        _job = CreateKillOnCloseJob();
        var security = new SecurityAttributes { nLength = Marshal.SizeOf<SecurityAttributes>(), bInheritHandle = 1 };
        if (!CreatePipe(out var outRead, out var outWrite, ref security, 0)
            || !CreatePipe(out var errRead, out var errWrite, ref security, 0)
            || !CreatePipe(out var inRead, out var inWrite, ref security, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed");
        }

        try
        {
            ClearInherit(outRead);
            ClearInherit(errRead);
            ClearInherit(inWrite);
            var startup = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                dwFlags = StartfUseStdHandles,
                hStdInput = inRead,
                hStdOutput = outWrite,
                hStdError = errWrite,
            };
            var environment = AllocEnvironment(_start);
            try
            {
                var directory = string.IsNullOrWhiteSpace(_start.WorkingDirectory) ? null : _start.WorkingDirectory;
                if (!CreateProcessW(null, CommandLine(_start), IntPtr.Zero, IntPtr.Zero, true,
                        CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow, environment, directory,
                        ref startup, out var info))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed");
                }

                try
                {
                    using var process = new SafeFileHandle(info.hProcess, ownsHandle: false);
                    if (!AssignProcessToJobObject(_job, process))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed");
                    if (ResumeThread(info.hThread) == uint.MaxValue)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed");
                }
                finally
                {
                    CloseHandle(info.hThread);
                }

                _pid = info.dwProcessId;
                _process = new SafeFileHandle(info.hProcess, ownsHandle: true);
                _signal = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(_process.DangerousGetHandle(), ownsHandle: false) };
                _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) =>
                {
                    if (GetExitCodeProcess(_process, out var code) && code != StillActive)
                        _exitCode = (int)code;
                    _exited.TrySetResult();
                }, null, -1, true);
            }
            finally
            {
                Marshal.FreeHGlobal(environment);
            }

            _stdout = Reader(outRead);
            _stderr = Reader(errRead);
            outRead = IntPtr.Zero;
            errRead = IntPtr.Zero;
            return true;
        }
        finally
        {
            CloseIfSet(inRead);
            CloseIfSet(inWrite);
            CloseIfSet(outWrite);
            CloseIfSet(errWrite);
            CloseIfSet(outRead);
            CloseIfSet(errRead);
        }
    }

    public void BeginRead(Action<string?> stdout, Action<string?> stderr)
    {
        _stdoutTask = Pump(_stdout, stdout);
        _stderrTask = Pump(_stderr, stderr);
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _exited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_stdoutTask is not null)
            await _stdoutTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_stderrTask is not null)
            await _stderrTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Kill(bool entireProcessTree)
    {
        _ = entireProcessTree;
        TerminateDescendants();
        if (_job is not null && !_job.IsInvalid && !_job.IsClosed)
            TerminateJobObject(_job, 1);
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

    public void Dispose()
    {
        _wait?.Unregister(null);
        _signal?.Dispose();
        try { _stdout?.Dispose(); } catch (IOException) { }
        try { _stderr?.Dispose(); } catch (IOException) { }
        _job?.Dispose();
        _process?.Dispose();
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

    private static void ClearInherit(IntPtr handle)
    {
        if (!SetHandleInformation(handle, HandleFlagInherit, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation failed");
    }

    private static StreamReader Reader(IntPtr handle) =>
        new(new FileStream(new SafeFileHandle(handle, ownsHandle: true), FileAccess.Read, 4096, isAsync: false));

    private static Task Pump(StreamReader? reader, Action<string?> onLine)
    {
        if (reader is null)
            return Task.CompletedTask;
        return Task.Run(() =>
        {
            while (true)
            {
                var line = reader.ReadLine();
                onLine(line);
                if (line is null)
                    return;
            }
        });
    }

    private static string CommandLine(ProcessStartInfo start)
    {
        var command = new StringBuilder();
        command.Append(Quote(start.FileName));
        foreach (var argument in start.ArgumentList)
        {
            command.Append(' ');
            command.Append(Quote(argument));
        }

        return command.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";
        if (value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static IntPtr AllocEnvironment(ProcessStartInfo start)
    {
        var block = new StringBuilder();
        foreach (var pair in start.Environment)
        {
            block.Append(pair.Key);
            block.Append('=');
            block.Append(pair.Value);
            block.Append('\0');
        }

        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    private static void CloseIfSet(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
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
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint exitCode);

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
