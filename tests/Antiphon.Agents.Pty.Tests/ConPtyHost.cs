using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// A ConPTY host we own end to end, so the things the production path fixes can become variables:
/// <b>which conhost implements the pseudoconsole</b>, the input pipe's buffer size, the pseudoconsole
/// creation flags and the window size.
///
/// <para>Why it exists (CARD-0030): <c>CreatePseudoConsole</c> in kernel32 launches
/// <c>%SystemRoot%\System32\conhost.exe</c> — on this machine version 10.0.19041.1, a 2020 build.
/// Windows Terminal does NOT use it; it ships its own <c>OpenConsole.exe</c> (1.24) and drives that.
/// Every "the terminal does X" measurement therefore has a hidden variable, and the redistributable
/// <c>conpty.dll</c> (shipped by pty4j/VS Code next to a modern OpenConsole.exe) is the knob that
/// controls it: it exports the same three entry points and spawns the OpenConsole.exe sitting beside
/// it instead of the inbox conhost.</para>
///
/// Windows-only; every call site is already gated on that.
/// </summary>
internal sealed class ConPtyHost : IAsyncDisposable
{
    private static readonly object SpawnGate = new();

    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    private readonly IntPtr _hPC;
    private readonly ClosePseudoConsoleFn _close;
    private readonly SafeFileHandle _inWrite;
    private readonly SafeFileHandle _outRead;
    private readonly FileStream _writer;
    private readonly FileStream _reader;
    private readonly StringBuilder _output = new();
    private readonly TerminalScreen _screen;
    private DateTime _lastDataAt = DateTime.UtcNow;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readTask;
    private readonly IntPtr _attrList;
    private readonly PROCESS_INFORMATION _pi;

    public string Backend { get; }

    /// <summary>Set at Start(); the numbers that say whether the attach could have worked at all.</summary>
    public static string Diagnostics { get; private set; } = "";

    public int Pid => _pi.dwProcessId;

    private ConPtyHost(
        string backend,
        int cols,
        int rows,
        IntPtr hPC,
        ClosePseudoConsoleFn close,
        SafeFileHandle inWrite,
        SafeFileHandle outRead,
        IntPtr attrList,
        PROCESS_INFORMATION pi)
    {
        Backend = backend;
        _screen = new TerminalScreen(cols, rows);
        _hPC = hPC;
        _close = close;
        _inWrite = inWrite;
        _outRead = outRead;
        _attrList = attrList;
        _pi = pi;
        _writer = new FileStream(inWrite, FileAccess.Write, bufferSize: 1, isAsync: false);
        _reader = new FileStream(outRead, FileAccess.Read, bufferSize: 4096, isAsync: false);
        _readTask = Task.Run(ReadLoop);
    }

    /// <summary>
    /// The redistributable ConPTY (conpty.dll + a sibling OpenConsole.exe) if one can be found on
    /// this machine, else null. pty4j (JetBrains IDEs) and node-pty (VS Code) both ship the pair.
    /// </summary>
    public static string? FindRedistConPty()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, @"Programs\Rider\lib\pty4j\win\x86-64"),
            Path.Combine(local, @"Programs\WebStorm\lib\pty4j\win\x86-64"),
            Path.Combine(local, @"Programs\DataGrip\lib\pty4j\win\x86-64"),
            Path.Combine(local, @"Programs\Microsoft VS Code\resources\app\node_modules\node-pty\build\Release"),
        };
        foreach (var dir in candidates)
        {
            var dll = Path.Combine(dir, "conpty.dll");
            if (File.Exists(dll) && File.Exists(Path.Combine(dir, "OpenConsole.exe"))) return dll;
        }
        return null;
    }

    /// <summary>Windows Terminal's own OpenConsole.exe, the one a real WT paste goes through.</summary>
    public static string? FindWindowsTerminalOpenConsole()
    {
        try
        {
            var root = @"C:\Program Files\WindowsApps";
            if (!Directory.Exists(root)) return null;
            return Directory.EnumerateDirectories(root, "Microsoft.WindowsTerminal_*_x64__*")
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "OpenConsole.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }

    /// <summary>
    /// Stages <paramref name="conptyDll"/> next to a chosen OpenConsole.exe in a private directory,
    /// so the conhost that serves the pseudoconsole is a deliberate choice rather than whatever the
    /// IDE happened to ship.
    /// </summary>
    public static string StageConPty(string conptyDll, string openConsoleExe, string intoDir)
    {
        Directory.CreateDirectory(intoDir);
        var dll = Path.Combine(intoDir, "conpty.dll");
        var exe = Path.Combine(intoDir, "OpenConsole.exe");
        File.Copy(conptyDll, dll, overwrite: true);
        File.Copy(openConsoleExe, exe, overwrite: true);
        return dll;
    }

    /// <param name="conptyDllPath">null = the inbox kernel32/conhost.exe pseudoconsole.</param>
    /// <param name="inputPipeBytes">CreatePipe nSize for the pty's INPUT pipe; 0 = system default.</param>
    public static ConPtyHost Start(
        string app,
        string[] args,
        string? cwd = null,
        IDictionary<string, string>? env = null,
        short cols = 120,
        short rows = 30,
        string? conptyDllPath = null,
        int inputPipeBytes = 0,
        uint flags = 0,
        bool inheritHandles = false)
    {
        var backend = conptyDllPath is null ? "kernel32/conhost" : Path.GetFileName(Path.GetDirectoryName(conptyDllPath)!) + "/OpenConsole";

        CreatePseudoConsoleFn create;
        ClosePseudoConsoleFn close;
        if (conptyDllPath is null)
        {
            create = Kernel32CreatePseudoConsole;
            close = Kernel32ClosePseudoConsole;
        }
        else
        {
            var module = LoadLibrary(conptyDllPath);
            if (module == IntPtr.Zero)
                throw new InvalidOperationException($"LoadLibrary failed for {conptyDllPath}: {Marshal.GetLastWin32Error()}");
            create = Marshal.GetDelegateForFunctionPointer<CreatePseudoConsoleFn>(
                GetProcAddress(module, "CreatePseudoConsole"));
            close = Marshal.GetDelegateForFunctionPointer<ClosePseudoConsoleFn>(
                GetProcAddress(module, "ClosePseudoConsole"));
        }

        // Two anonymous pipes: what we write goes into the pseudoconsole's input; what the child
        // renders comes back out. nSize is the knob CARD-0030 asks about (card item 4).
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, inputPipeBytes))
            throw new InvalidOperationException("CreatePipe(in) failed: " + Marshal.GetLastWin32Error());
        if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0))
            throw new InvalidOperationException("CreatePipe(out) failed: " + Marshal.GetLastWin32Error());

        var hr = create(new COORD { X = cols, Y = rows }, inRead.DangerousGetHandle(),
            outWrite.DangerousGetHandle(), flags, out var hPC);
        if (hr != 0) throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");

        // The pty owns its ends now.
        inRead.Dispose();
        outWrite.Dispose();

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var attrList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref size))
            throw new InvalidOperationException("InitializeProcThreadAttributeList failed: " + Marshal.GetLastWin32Error());
        if (!UpdateProcThreadAttribute(attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("UpdateProcThreadAttribute failed: " + Marshal.GetLastWin32Error());

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        si.lpAttributeList = attrList;
        Diagnostics = $"sizeof(STARTUPINFOEX)={si.StartupInfo.cb} attrListBytes={size} hPC=0x{hPC:X} "
                      + $"parentConsole=0x{GetConsoleWindow():X}";

        var commandLine = new StringBuilder(Quote(app));
        foreach (var a in args) commandLine.Append(' ').Append(Quote(a));

        // REQUIRED, and the reason the first attempts at this host produced a child with no console
        // at all: a parent whose own std handles are redirected (every test host and every daemon
        // here) leaks those handle VALUES into the child, where they beat the pseudoconsole attach —
        // the child comes up on the parent's pipes with isTTY false while the pty sits empty.
        //
        // Blank OUR std handles for the duration of the call rather than declaring the child's as
        // NULL: STARTF_USESTDHANDLES + NULL also fixes the attach, but it leaves claude.exe (Bun)
        // with a stdin it never reads from — it renders its whole TUI through the pty and ignores
        // every byte written to it, which reads exactly like total input loss and is not.
        // The blanking is process-global, so it is serialised: anything else spawning a process in
        // this window would get our blanked handles too.
        bool ok;
        int lastErr;
        PROCESS_INFORMATION pi;
        var envBlock = BuildEnvironmentBlock(env);
        var flagsCp = EXTENDED_STARTUPINFO_PRESENT | (envBlock == IntPtr.Zero ? 0 : CREATE_UNICODE_ENVIRONMENT);
        lock (SpawnGate)
        {
            var savedIn = GetStdHandle(STD_INPUT_HANDLE);
            var savedOut = GetStdHandle(STD_OUTPUT_HANDLE);
            var savedErr = GetStdHandle(STD_ERROR_HANDLE);
            SetStdHandle(STD_INPUT_HANDLE, IntPtr.Zero);
            SetStdHandle(STD_OUTPUT_HANDLE, IntPtr.Zero);
            SetStdHandle(STD_ERROR_HANDLE, IntPtr.Zero);
            try
            {
                ok = CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles,
                    flagsCp, envBlock, cwd ?? Environment.CurrentDirectory, ref si, out pi);
                lastErr = Marshal.GetLastWin32Error();
            }
            finally
            {
                SetStdHandle(STD_INPUT_HANDLE, savedIn);
                SetStdHandle(STD_OUTPUT_HANDLE, savedOut);
                SetStdHandle(STD_ERROR_HANDLE, savedErr);
            }
        }
        Diagnostics += $" flags=0x{flagsCp:X} createProcess={ok} err={lastErr}";
        if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
        if (!ok) throw new InvalidOperationException("CreateProcess failed: " + Marshal.GetLastWin32Error());

        return new ConPtyHost(backend, cols, rows, hPC, close, inWrite, outRead, attrList, pi);
    }

    private static string Quote(string s) => s.Contains(' ') && !s.StartsWith('"') ? '"' + s + '"' : s;

    private static IntPtr BuildEnvironmentBlock(IDictionary<string, string>? overrides)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            merged[(string)e.Key] = (string?)e.Value ?? "";
        if (overrides is not null)
            foreach (var kv in overrides) merged[kv.Key] = kv.Value;

        var sb = new StringBuilder();
        foreach (var kv in merged.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
        sb.Append('\0');
        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    private void ReadLoop()
    {
        var buf = new byte[4096];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var n = _reader.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                var text = Encoding.UTF8.GetString(buf, 0, n);
                lock (_gate)
                {
                    _output.Append(text);
                    _screen.Feed(text);
                    _lastDataAt = DateTime.UtcNow;
                }
            }
        }
        catch { /* pipe closed on teardown */ }
    }

    public string OutputText { get { lock (_gate) return _output.ToString(); } }

    /// <summary>The rendered screen — what a human would see, not the byte stream.</summary>
    public string ScreenText { get { lock (_gate) return _screen.GetScreenText(); } }

    /// <summary>Waits for the RENDERED screen to satisfy a predicate.</summary>
    public async Task<bool> WaitForScreenAsync(Func<string, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(ScreenText)) return true;
            await Task.Delay(100);
        }
        return false;
    }

    /// <summary>Waits until the child has emitted nothing for <paramref name="quiet"/>.</summary>
    public async Task<bool> WaitForQuietAsync(TimeSpan quiet, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            DateTime last;
            lock (_gate) last = _lastDataAt;
            if (DateTime.UtcNow - last >= quiet) return true;
            await Task.Delay(100);
        }
        return false;
    }

    public void ClearOutput() { lock (_gate) _output.Clear(); }

    /// <summary>One WriteFile of the whole payload — the shape a terminal's paste has.</summary>
    public void Write(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        _writer.Write(bytes, 0, bytes.Length);
        _writer.Flush();
    }

    public async Task<bool> WaitForAsync(string needle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (OutputText.Contains(needle, StringComparison.Ordinal)) return true;
            await Task.Delay(50);
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        try { _close(_hPC); } catch { }
        try { _writer.Dispose(); } catch { }
        try { _reader.Dispose(); } catch { }
        try { if (_attrList != IntPtr.Zero) { DeleteProcThreadAttributeList(_attrList); Marshal.FreeHGlobal(_attrList); } } catch { }
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(_pi.dwProcessId);
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch { }
        try { CloseHandle(_pi.hThread); CloseHandle(_pi.hProcess); } catch { }
        await Task.CompletedTask;
    }

    // ---- interop ------------------------------------------------------------------------

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreatePseudoConsoleFn(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClosePseudoConsoleFn(IntPtr hPC);

    [DllImport("kernel32.dll", EntryPoint = "CreatePseudoConsole", SetLastError = true)]
    private static extern int Kernel32CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", EntryPoint = "ClosePseudoConsole", SetLastError = true)]
    private static extern void Kernel32ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

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
}
