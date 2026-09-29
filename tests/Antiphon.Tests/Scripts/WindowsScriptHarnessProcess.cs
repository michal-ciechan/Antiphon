using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Tests.Scripts;

internal sealed class WindowsScriptHarnessProcess : IOwnedScriptProcess
{
    private const uint CreateSuspended = 0x00000004;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    internal const uint JobObjectLimitBreakawayOk = 0x00000800;
    internal const uint JobObjectLimitSilentBreakawayOk = 0x00001000;
    private const int JobObjectExtendedLimitInformation = 9;
    private const int JobObjectBasicAccountingInformation = 1;
    private const int ProcThreadAttributeHandleList = 0x00020002;
    private const uint WaitObject0 = 0;
    private const uint DuplicateSameAccess = 0x00000002;

    private readonly SafeFileHandle _job;
    private readonly SafeFileHandle _process;
    private readonly SafeFileHandle _thread;
    private readonly StreamReader _stdout;
    private readonly StreamReader _stderr;
    private readonly WindowsFaultInjection? _faults;
    private bool _terminated;
    public StreamReader Stdout => _stdout;
    public StreamReader Stderr => _stderr;
    internal int ProcessId { get; }
    internal IReadOnlyList<IntPtr> InheritedHandlesForTesting { get; private set; } = Array.Empty<IntPtr>();
    internal SafeFileHandle JobHandleForTesting => _job;

    internal uint QueryLimitFlagsForTesting() => QueryLimitFlagsForTesting(_job);

    internal uint QueryActiveProcessesForTesting() => QueryActiveProcessesForTesting(_job);

    internal static uint QueryLimitFlagsForTesting(SafeFileHandle job)
    {
        var limits = new JobExtendedLimitInformation();
        if (!QueryInformationJobObjectLimits(job, JobObjectExtendedLimitInformation, ref limits,
                Marshal.SizeOf<JobExtendedLimitInformation>(), IntPtr.Zero))
            throw NativeError("QueryInformationJobObject (limits)");
        return limits.Basic.LimitFlags;
    }

    internal static uint QueryActiveProcessesForTesting(SafeFileHandle job)
    {
        if (!QueryInformationJobObject(job, JobObjectBasicAccountingInformation,
                out var accounting, Marshal.SizeOf<JobBasicAccounting>(), IntPtr.Zero))
            throw NativeError("QueryInformationJobObject (accounting)");
        return accounting.ActiveProcesses;
    }

    internal WindowsScriptHarnessProcess(ScriptProcessRequest request) : this(request, null) { }

    internal WindowsScriptHarnessProcess(ScriptProcessRequest request, WindowsFaultInjection? faults)
    {
        _faults = faults;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // App Execution Alias stubs live only under the per-user alias directory and are reparse
        // points; a real MSIX-packaged executable (e.g. Store-installed pwsh.exe) legitimately
        // lives under the machine-wide "Program Files\WindowsApps\<package>\" tree and must not
        // be rejected by a bare "\WindowsApps\" substring match.
        var aliasDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        if (!Path.IsPathFullyQualified(request.Executable) ||
            string.Equals(Path.GetDirectoryName(request.Executable), aliasDirectory, StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(request.Executable) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.");
        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job.IsInvalid) { _job.Dispose(); throw NativeError("CreateJobObjectW"); }
        if (faults is not null)
        {
            // Duplicate the job handle so a test can inspect accounting after a thrown
            // constructor disposes this instance's own copy during unwind.
            var self = GetCurrentProcess();
            if (!DuplicateHandle(self, _job, self, out var jobCopy, 0, false, DuplicateSameAccess))
                throw NativeError("DuplicateHandle (job)");
            faults.JobHandleCopy = jobCopy;
        }
        SafeFileHandle? stdoutRead = null, stdoutWrite = null, stderrRead = null, stderrWrite = null,
            stdinRead = null, stdinWrite = null;
        try
        {
        var limits = new JobExtendedLimitInformation
        {
            Basic = new JobBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose }
        };
        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<JobExtendedLimitInformation>()))
            throw NativeError("SetInformationJobObject");

        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        if (!CreatePipe(out stdoutRead, out stdoutWrite, ref security, 0) ||
            !CreatePipe(out stderrRead, out stderrWrite, ref security, 0) ||
            !CreatePipe(out stdinRead, out stdinWrite, ref security, 0))
            throw NativeError("CreatePipe");
        using (stdoutWrite)
        using (stderrWrite)
        using (stdinRead)
        using (stdinWrite)
        {
            if (!SetHandleInformation(stdoutRead, HandleFlagInherit, 0) ||
                !SetHandleInformation(stderrRead, HandleFlagInherit, 0) ||
                !SetHandleInformation(stdinWrite, HandleFlagInherit, 0))
                throw NativeError("SetHandleInformation");

            IntPtr attributes = IntPtr.Zero;
            IntPtr handles = IntPtr.Zero;
            try
            {
                nint size = 0;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                attributes = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
                    throw NativeError("InitializeProcThreadAttributeList");
                handles = Marshal.AllocHGlobal(3 * IntPtr.Size);
                Marshal.WriteIntPtr(handles, 0, stdinRead.DangerousGetHandle());
                Marshal.WriteIntPtr(handles, IntPtr.Size, stdoutWrite.DangerousGetHandle());
                Marshal.WriteIntPtr(handles, 2 * IntPtr.Size, stderrWrite.DangerousGetHandle());
                InheritedHandlesForTesting = [stdinRead.DangerousGetHandle(), stdoutWrite.DangerousGetHandle(),
                    stderrWrite.DangerousGetHandle()];
                if (!UpdateProcThreadAttribute(attributes, 0, ProcThreadAttributeHandleList,
                        handles, (nint)(3 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                    throw NativeError("UpdateProcThreadAttribute");

                var startup = new StartupInfoEx
                {
                    StartupInfo = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfoEx>(), Flags = StartfUseStdHandles,
                        StdInput = stdinRead.DangerousGetHandle(),
                        StdOutput = stdoutWrite.DangerousGetHandle(),
                        StdError = stderrWrite.DangerousGetHandle()
                    }, AttributeList = attributes
                };
                var argv = new List<string> { request.Executable, "-NoProfile", "-NonInteractive", "-File", request.Script,
                    "-Case", request.CaseName, "-ResultsDirectory", request.ResultsDirectory };
                if (request.AdditionalArguments is not null) argv.AddRange(request.AdditionalArguments);
                var command = new StringBuilder(string.Join(" ", argv.Select(QuoteArgument)));
                if (!CreateProcessW(request.Executable, command, IntPtr.Zero, IntPtr.Zero, true,
                        CreateSuspended | ExtendedStartupInfoPresent, IntPtr.Zero,
                        Directory.GetCurrentDirectory(), ref startup, out var information))
                    throw NativeError("CreateProcessW");
                _process = new SafeFileHandle(information.Process, ownsHandle: true);
                _thread = new SafeFileHandle(information.Thread, ownsHandle: true);
                ProcessId = information.ProcessId;
                faults?.RootProcessIds.Add(information.ProcessId);
                faults?.Trace.Add("Assign:attempt");
                var assigned = faults?.FailAssign == true ? false : AssignProcessToJobObject(_job, _process);
                if (!assigned) throw NativeError("AssignProcessToJobObject");
                faults?.Trace.Add("Assign:success");
                faults?.OnAssigned?.Invoke();
                faults?.Trace.Add("Resume:attempt");
                var resumed = faults?.FailResume == true ? uint.MaxValue : ResumeThread(_thread);
                if (resumed == uint.MaxValue) throw NativeError("ResumeThread");
                faults?.Trace.Add("Resume:success");
            }
            finally
            {
                if (attributes != IntPtr.Zero)
                {
                    DeleteProcThreadAttributeList(attributes);
                    Marshal.FreeHGlobal(attributes);
                }
                if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            }
        }
        // CreatePipe hands back a synchronous (non-overlapped) handle; isAsync:true is rejected
        // by FileStream's handle validation, so ReadAsync falls back to thread-pool reads here.
        _stdout = new StreamReader(new FileStream(stdoutRead, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
        _stderr = new StreamReader(new FileStream(stderrRead, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
        }
        catch
        {
            // A failed assignment or resume must never release a suspended root.
            // Terminate the private job and the root before closing its handles.
            try { TerminateJobObject(_job, 1); } catch { }
            if (_process is not null)
            {
                try { TerminateProcess(_process, 1); WaitForSingleObject(_process, 2000); } catch { }
            }
            _stdout?.Dispose();
            _stderr?.Dispose();
            _thread?.Dispose();
            _process?.Dispose();
            stdoutRead?.Dispose(); stdoutWrite?.Dispose();
            stderrRead?.Dispose(); stderrWrite?.Dispose();
            stdinRead?.Dispose(); stdinWrite?.Dispose();
            _job.Dispose();
            throw;
        }
    }

    public async Task<int> StartAndWaitForRootAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var wait = WaitForSingleObject(_process, 0);
            if (wait == WaitObject0) break;
            if (wait == uint.MaxValue) throw NativeError("WaitForSingleObject");
            await Task.Delay(25, cancellationToken);
        }
        if (!GetExitCodeProcess(_process, out var exitCode)) throw NativeError("GetExitCodeProcess");
        return unchecked((int)exitCode);
    }

    public Task TerminateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_terminated) return Task.CompletedTask;
        _terminated = true;
        if (_faults?.SkipTerminateKill == true) return Task.CompletedTask;
        if (!TerminateJobObject(_job, 1)) throw NativeError("TerminateJobObject");
        return Task.CompletedTask;
    }

    public async Task ConfirmDeadAsync(CancellationToken cancellationToken)
    {
        if (_faults?.FailAccountingQuery == true) throw NativeError("QueryInformationJobObject (injected)");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!QueryInformationJobObject(_job, JobObjectBasicAccountingInformation,
                    out var accounting, Marshal.SizeOf<JobBasicAccounting>(), IntPtr.Zero))
                throw NativeError("QueryInformationJobObject");
            if (accounting.ActiveProcesses == 0) return;
            await Task.Delay(25, cancellationToken);
        }
    }

    public void CloseStreams()
    {
        _stdout.Dispose();
        _stderr.Dispose();
    }

    public void Dispose()
    {
        _stdout.Dispose();
        _stderr.Dispose();
        _thread.Dispose();
        _process.Dispose();
        _job.Dispose();
    }

    private static string QuoteArgument(string arg)
    {
        if (arg.Length != 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"')) return arg;
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"')
            {
                result.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }

    private static Win32Exception NativeError(string operation) =>
        new(Marshal.GetLastWin32Error(), operation + " failed");

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes
    { public int Length; public IntPtr SecurityDescriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public string? Reserved; public string? Desktop; public string? Title;
        public int X; public int Y; public int XSize; public int YSize; public int XCountChars;
        public int YCountChars; public int FillAttribute; public uint Flags; public short ShowWindow;
        public short Reserved2; public IntPtr Reserved2Ptr; public IntPtr StdInput; public IntPtr StdOutput;
        public IntPtr StdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx
    { public StartupInfo StartupInfo; public IntPtr AttributeList; }
    // Raw handles, not SafeFileHandle: the CLR interop marshaler cannot construct a SafeHandle
    // from a struct field populated by native code (out-parameter struct fields are unsupported).
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation
    { public IntPtr Process; public IntPtr Thread; public int ProcessId; public int ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct JobBasicLimitInformation
    {
        public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    {
        public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount;
        public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)] private struct JobExtendedLimitInformation
    {
        public JobBasicLimitInformation Basic; public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct JobBasicAccounting
    {
        public long TotalUserTime; public long TotalKernelTime; public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime; public uint TotalPageFaultCount; public uint TotalProcesses;
        public uint ActiveProcesses; public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read,
        out SafeFileHandle write, ref SecurityAttributes attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(
        SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(
        IntPtr list, int count, int flags, ref nint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(
        IntPtr list, uint flags, int attribute, IntPtr value, nint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment,
        string currentDirectory, ref StartupInfoEx startup, out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(
        SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint exitCode);
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(
        SafeFileHandle job, int informationClass, ref JobExtendedLimitInformation information, int length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(
        SafeFileHandle job, int informationClass, out JobBasicAccounting accounting, int length, IntPtr returned);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryInformationJobObjectLimits(
        SafeFileHandle job, int informationClass, ref JobExtendedLimitInformation information, int length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(
        IntPtr sourceProcess, SafeFileHandle sourceHandle, IntPtr targetProcess, out SafeFileHandle targetHandle,
        uint desiredAccess, bool inheritHandle, uint options);
}

/// <summary>
/// Test-only fault-injection and observation seam for WindowsScriptHarnessProcess. Each flag
/// forces the corresponding guard's native call to fail without disturbing any other native
/// operation, and each observation is populated from the real construction it instruments.
/// </summary>
internal sealed class WindowsFaultInjection
{
    internal bool FailAssign;
    internal bool FailResume;
    internal bool SkipTerminateKill;
    internal bool FailAccountingQuery;
    internal Action? OnAssigned;
    internal readonly List<string> Trace = [];
    internal readonly List<int> RootProcessIds = [];
    internal SafeFileHandle? JobHandleCopy;
}
