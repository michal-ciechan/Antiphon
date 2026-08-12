using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Porta.Pty;

namespace Antiphon.Agents.Pty;

/// <summary>
/// A pty whose pseudoconsole comes from the SHIPPED <c>conpty.dll</c> instead of kernel32's, behind
/// the same <see cref="IPtySession"/> the default backend is adapted to, so <see cref="PtyAgentRunner"/>
/// and everything downstream of it (read loop, audit, screen, job object, exit plumbing) is untouched.
///
/// <para><b>Why our own host rather than a Porta.Pty fork.</b> Porta reaches
/// <c>CreatePseudoConsole</c> through Vanara's <c>kernel32.dll</c> import — a P/Invoke bound to a
/// module name, with no seam to redirect: <c>NativeLibrary.SetDllImportResolver</c> is per-assembly
/// and would have to redirect every OTHER kernel32 import in the same assembly too, which
/// conpty.dll does not export. So the choice is a fork or a host. A fork means owning a
/// cross-platform package (Linux/macOS native shims included) and re-vendoring it on every upstream
/// bump, to change one function resolution on Windows. A host means ~200 lines that we already had
/// written and measured (<c>tests/Antiphon.Agents.Pty.Tests/ConPtyHost.cs</c>, CARD-0030). This file
/// is that host, narrowed to the production shape and made a drop-in for the interface, so the flag
/// is a one-line branch and the OFF path is byte-identical to what shipped before.</para>
///
/// <para>The spawn deliberately mirrors <c>Porta.Pty.Windows.PtyProvider</c> step for step —
/// environment merge, PATH resolution, argument quoting, <c>bInheritHandles=false</c>,
/// kill-on-close job object, unbuffered <see cref="FileStream"/>s over the pipe handles, teardown
/// order — so that the ONLY difference between the two backends is which module provides
/// <c>CreatePseudoConsole</c>. Anything else that differs is a bug in this file.</para>
/// </summary>
internal sealed class ModernConPtyConnection : IPtySession
{
    private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    private static readonly object ModuleGate = new();
    private static readonly Dictionary<string, ConPtyModule> Modules = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConPtyModule _module;
    private readonly IntPtr _hPC;
    private readonly SafeFileHandle _inWrite;
    private readonly SafeFileHandle _outRead;
    private readonly SafeFileHandle _job;
    private readonly IntPtr _attrList;
    private readonly IntPtr _hProcess;
    private readonly IntPtr _hThread;
    private readonly Process _process;
    private readonly object _disposeGate = new();
    private bool _disposed;

    public event Action<int>? Exited;

    public Stream ReaderStream { get; }

    public Stream WriterStream { get; }

    public int Pid { get; }

    /// <summary>The conpty.dll this connection's pseudoconsole came from (for logs and assertions).</summary>
    public string ConPtyDllPath => _module.Path;

    private ModernConPtyConnection(
        ConPtyModule module,
        IntPtr hPC,
        SafeFileHandle inWrite,
        SafeFileHandle outRead,
        SafeFileHandle job,
        IntPtr attrList,
        IntPtr hProcess,
        IntPtr hThread,
        int pid)
    {
        _module = module;
        _hPC = hPC;
        _inWrite = inWrite;
        _outRead = outRead;
        _job = job;
        _attrList = attrList;
        _hProcess = hProcess;
        _hThread = hThread;
        Pid = pid;

        // bufferSize 0 = unbuffered, as Porta does: a write must reach the pipe as ONE WriteFile.
        // The single write is the measured-good shape for a paste (a paced 86 KB delivery read
        // NOTHING once in the CARD-0030 sweep), so nothing may re-chunk it on the way out.
        ReaderStream = new FileStream(
            new SafeFileHandle(outRead.DangerousGetHandle(), ownsHandle: false),
            FileAccess.Read, bufferSize: 0, isAsync: false);
        WriterStream = new FileStream(
            new SafeFileHandle(inWrite.DangerousGetHandle(), ownsHandle: false),
            FileAccess.Write, bufferSize: 0, isAsync: false);

        _process = Process.GetProcessById(pid);
        _process.Exited += OnProcessExited;
        _process.EnableRaisingEvents = true;
    }

    public static ModernConPtyConnection Spawn(string conptyDllPath, PtyOptions options)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The modern ConPTY backend is Windows-only.");

        var module = LoadModule(conptyDllPath);
        var environment = MergeEnvironment(options.Environment);
        var app = ResolveAppPath(options.App, options.Cwd, environment);
        var commandLine = BuildCommandLine(app, options.CommandLine, options.VerbatimCommandLine);

        // Kill-on-close job, created BEFORE anything else so every failure path can dispose it (and
        // with it any process that did get created). Porta does the same; without it a crashed host
        // leaves the child and its OpenConsole behind.
        var job = CreateKillOnCloseJob();
        SafeFileHandle? inRead = null, inWrite = null, outRead = null, outWrite = null;
        var attrList = IntPtr.Zero;
        var hPC = IntPtr.Zero;
        var envBlock = IntPtr.Zero;

        try
        {
            if (!CreatePipe(out inRead, out inWrite, IntPtr.Zero, 0))
                throw LastError("CreatePipe (input) failed");
            if (!CreatePipe(out outRead, out outWrite, IntPtr.Zero, 0))
                throw LastError("CreatePipe (output) failed");

            var hr = module.Create(
                new COORD { X = (short)options.Cols, Y = (short)options.Rows },
                inRead.DangerousGetHandle(),
                outWrite.DangerousGetHandle(),
                0,
                out hPC);
            if (hr != 0)
                throw new InvalidOperationException(
                    $"CreatePseudoConsole from {conptyDllPath} failed: 0x{hr:X8}");

            // The pseudoconsole owns its ends now; holding them open here breaks EOF on teardown.
            inRead.Dispose();
            inRead = null;
            outWrite.Dispose();
            outWrite = null;

            attrList = CreatePseudoConsoleAttributeList(hPC);

            var startupInfo = new STARTUPINFOEX();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            // STARTF_USESTDHANDLES with all three handles left NULL, exactly as Porta does — and it
            // is load-bearing, not cargo. Without it the child LOSES the pseudoconsole attach to the
            // parent's own std handles whenever the parent's stdio is redirected (every daemon and
            // every test host here): it comes up on the parent's pipes with isTTY false while the
            // pty sits empty, which reads as total input loss and is not. Measured on the first cut
            // of this file; it is also CARD-0030's failure mode 1.
            startupInfo.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            startupInfo.lpAttributeList = attrList;

            envBlock = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(environment));

            // bInheritHandles false, as Porta has it: the pseudoconsole duplicates the pipe ends it
            // was given into the console host itself, so nothing here needs to be inheritable.
            var created = CreateProcessW(
                null,
                new StringBuilder(commandLine),
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                envBlock,
                options.Cwd,
                ref startupInfo,
                out var processInfo);
            if (!created)
                throw LastError($"Could not start terminal process {commandLine}");

            if (!AssignProcessToJobObject(job, processInfo.hProcess))
                throw LastError("AssignProcessToJobObject failed");

            return new ModernConPtyConnection(
                module, hPC, inWrite!, outRead!, job, attrList,
                processInfo.hProcess, processInfo.hThread, processInfo.dwProcessId);
        }
        catch
        {
            if (hPC != IntPtr.Zero) module.Close(hPC);
            inRead?.Dispose();
            inWrite?.Dispose();
            outRead?.Dispose();
            outWrite?.Dispose();
            if (attrList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }

            job.Dispose();
            throw;
        }
        finally
        {
            if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
        }
    }

    public void Kill() => _process.Kill();

    public void Resize(int cols, int rows)
    {
        lock (_disposeGate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModernConPtyConnection));
            var hr = _module.Resize(_hPC, new COORD { X = (short)cols, Y = (short)rows });
            if (hr != 0)
                throw new InvalidOperationException($"Could not resize pseudo console: 0x{hr:X8}");
        }
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _process.Exited -= OnProcessExited;

        // Documented ConPTY teardown order, matching Porta's: pseudoconsole (tells the console host
        // to shut down), then the pipes (lets pending I/O complete), then process/thread handles,
        // then the job last so anything still alive is terminated.
        try { _module.Close(_hPC); } catch { /* teardown */ }
        try { _inWrite.Dispose(); } catch { /* teardown */ }
        try { _outRead.Dispose(); } catch { /* teardown */ }
        try { CloseHandle(_hThread); } catch { /* teardown */ }
        try { CloseHandle(_hProcess); } catch { /* teardown */ }
        if (_attrList != IntPtr.Zero)
        {
            try
            {
                DeleteProcThreadAttributeList(_attrList);
                Marshal.FreeHGlobal(_attrList);
            }
            catch { /* teardown */ }
        }

        try { _job.Dispose(); } catch { /* teardown */ }
        try { ReaderStream.Dispose(); } catch { /* teardown */ }
        try { WriterStream.Dispose(); } catch { /* teardown */ }
        try { _process.Dispose(); } catch { /* teardown */ }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;
        Exited?.Invoke(_process.ExitCode);
    }

    // ---- spawn helpers (ports of Porta.Pty.Windows, kept faithful on purpose) ----------------

    /// <summary>
    /// Full process environment, overlaid with the caller's. An EMPTY value removes the variable,
    /// which is Porta's contract — callers rely on it to unset things for the child.
    /// </summary>
    internal static Dictionary<string, string> MergeEnvironment(IDictionary<string, string>? overrides)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            merged[(string)entry.Key] = entry.Value?.ToString() ?? string.Empty;

        if (overrides is not null)
        {
            foreach (var kv in overrides)
            {
                if (string.IsNullOrEmpty(kv.Value))
                    merged.Remove(kv.Key);
                else
                    merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }

    /// <summary>Sorted, null-separated, double-null-terminated — the block CreateProcess wants.</summary>
    internal static string BuildEnvironmentBlock(IDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var key in environment.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            builder.Append(key).Append('=').Append(environment[key]).Append('\0');
        builder.Append('\0');
        return builder.ToString();
    }

    /// <summary>
    /// Quote each argument, join with spaces, prefix the (quoted-if-spaced) app — exactly
    /// <c>WindowsArguments.Format</c> plus Porta's app-quoting rule, so a command line that launches
    /// today launches identically on the new backend.
    /// </summary>
    internal static string BuildCommandLine(string app, string[]? args, bool verbatim)
    {
        var arguments = verbatim
            ? string.Join(" ", args ?? [])
            : string.Join(" ", (args ?? []).Select(a =>
                string.IsNullOrEmpty(a) ? string.Empty : "\"" + a.Replace("\"", "\"\"") + "\""));

        var quoteApp = app.Contains(' ') && !app.StartsWith('"') && !app.EndsWith('"');
        var builder = new StringBuilder(app.Length + arguments.Length + 4);
        if (quoteApp) builder.Append('"').Append(app).Append('"');
        else builder.Append(app);
        if (!string.IsNullOrWhiteSpace(arguments)) builder.Append(' ').Append(arguments);
        return builder.ToString();
    }

    /// <summary>
    /// Porta's <c>GetAppOnPath</c>: absolute wins, relative-with-directory is resolved against cwd,
    /// a bare name is searched on the child's PATH with .com/.exe probing, and WoW64's
    /// Sysnative/System32 swap is honoured. Getting this wrong would only show up as "some agents
    /// launch, some do not", so it is ported rather than approximated.
    /// </summary>
    internal static string ResolveAppPath(string app, string cwd, IDictionary<string, string> environment)
    {
        var isWow64 = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") is not null;
        var windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        var sysnative = Path.Combine(windir, "Sysnative");
        var sysnativeSlash = sysnative + Path.DirectorySeparatorChar;
        var system32 = Path.Combine(windir, "System32");
        var system32Slash = system32 + Path.DirectorySeparatorChar;

        try
        {
            if (Path.IsPathRooted(app))
            {
                if (isWow64)
                {
                    if (app.StartsWith(system32Slash, StringComparison.OrdinalIgnoreCase))
                    {
                        var redirected = Path.Combine(sysnative, app[system32Slash.Length..]);
                        if (File.Exists(redirected)) return redirected;
                    }
                }
                else if (app.StartsWith(sysnativeSlash, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(system32, app[sysnativeSlash.Length..]);
                }

                return app;
            }

            if (Path.GetDirectoryName(app) != string.Empty)
                return Path.Combine(cwd, app);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"Invalid terminal app path '{app}'");
        }
        catch (PathTooLongException)
        {
            throw new ArgumentException($"Terminal app path '{app}' is too long");
        }

        var pathValue = environment.TryGetValue("PATH", out var p) && !string.IsNullOrWhiteSpace(p)
            ? p
            : Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return Path.Combine(cwd, app);

        var paths = new List<string>(pathValue!.Split([';'], StringSplitOptions.RemoveEmptyEntries));
        if (isWow64)
        {
            var indexOfSystem32 = paths.FindIndex(e =>
                string.Equals(e, system32, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e, system32Slash, StringComparison.OrdinalIgnoreCase));
            var indexOfSysnative = paths.FindIndex(e =>
                string.Equals(e, sysnative, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e, sysnativeSlash, StringComparison.OrdinalIgnoreCase));
            if (indexOfSystem32 >= 0 && indexOfSysnative == -1)
                paths.Insert(indexOfSystem32, sysnative);
        }

        foreach (var entry in paths)
        {
            bool rooted;
            try { rooted = Path.IsPathRooted(entry); }
            catch (ArgumentException) { continue; }

            var full = rooted ? Path.Combine(entry, app) : Path.Combine(cwd, entry, app);
            if (File.Exists(full)) return full;
            if (File.Exists(full + ".com")) return full + ".com";
            if (File.Exists(full + ".exe")) return full + ".exe";
        }

        return Path.Combine(cwd, app);
    }

    private static IntPtr CreatePseudoConsoleAttributeList(IntPtr hPC)
    {
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        try
        {
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
                throw LastError("InitializeProcThreadAttributeList failed");
            if (!UpdateProcThreadAttribute(
                    list, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw LastError("UpdateProcThreadAttribute failed");
            return list;
        }
        catch
        {
            Marshal.FreeHGlobal(list);
            throw;
        }
    }

    private static SafeFileHandle CreateKillOnCloseJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) throw LastError("CreateJobObjectW failed");
        try
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = 0x00002000, // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                },
            };
            if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                throw LastError("SetInformationJobObject failed");
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    private static ConPtyModule LoadModule(string dllPath)
    {
        lock (ModuleGate)
        {
            if (Modules.TryGetValue(dllPath, out var cached)) return cached;

            // LoadLibraryEx with WITH_ALTERED_SEARCH_PATH so the DLL's own directory is searched for
            // its dependencies, and so it resolves the OpenConsole.exe sitting beside it.
            var handle = LoadLibraryExW(dllPath, IntPtr.Zero, 0x00000008);
            if (handle == IntPtr.Zero) throw LastError($"LoadLibrary failed for {dllPath}");

            var module = new ConPtyModule(
                dllPath,
                GetDelegate<CreatePseudoConsoleFn>(handle, "CreatePseudoConsole", dllPath),
                GetDelegate<ClosePseudoConsoleFn>(handle, "ClosePseudoConsole", dllPath),
                GetDelegate<ResizePseudoConsoleFn>(handle, "ResizePseudoConsole", dllPath));
            Modules[dllPath] = module;
            return module;
        }
    }

    private static T GetDelegate<T>(IntPtr module, string name, string dllPath) where T : Delegate
    {
        var address = GetProcAddress(module, name);
        if (address == IntPtr.Zero)
            throw new InvalidOperationException($"{dllPath} does not export {name}");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    /// <summary>Captures the error code ONCE — formatting a Win32Exception can itself clobber it.</summary>
    private static Win32Exception LastError(string message)
    {
        var code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"{message}: {new Win32Exception(code).Message}");
    }

    private sealed record ConPtyModule(
        string Path,
        CreatePseudoConsoleFn Create,
        ClosePseudoConsoleFn Close,
        ResizePseudoConsoleFn Resize);

    // ---- interop ------------------------------------------------------------------------------

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreatePseudoConsoleFn(COORD size, IntPtr hInput, IntPtr hOutput, uint flags, out IntPtr phPC);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClosePseudoConsoleFn(IntPtr hPC);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ResizePseudoConsoleFn(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue,
        IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle hJob, int jobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
}
