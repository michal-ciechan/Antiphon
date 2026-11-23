// CARD-1667, CARD-1507: the operating-system seams of the rc qualification supervisor set that NightlyHost's launcher and
// process table do not cover: a detached start with the child's output in its own files, the Windows tree stop, a live
// process's command line, this process's console and session, the process table the heartbeat observes, and the CLI that
// a detached child runs. It replaced New-QualificationDetachedStartParameters, ConvertTo-QualificationCommandLine,
// Get-QualificationConsoleProcessIds, Get-QualificationSessionId (scripts/lib/qualification-supervisor.ps1),
// Invoke-QualificationWindowsTreeStop and Get-QualificationStopCommandLine (scripts/qualification/qualification-stop.ps1),
// deleted 2026-11-23 (unit 7 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md).
//
// NightlyHost.Qualification carries one; NightlyHost.Production binds QualificationOs, a test binds its own fake, so no test
// starts or stops a real process through it. A detached child (QualificationDetach.Plan) is, on Windows, started with its
// own hidden console (CREATE_NEW_CONSOLE, SW_HIDE), so a teardown of the starter's console cannot reach it (launch-rc.ps1
// survived run 7f8e9fd1 this way); on Unix under setsid (a new session and process group) unless it shares the session.
// Its stdin is the null device and its stdout and stderr are its own files: the starter holds no pipe or buffer for it, so
// the starter's death never blocks or kills its writes. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Nightly
{
    /// <summary>
    /// One detached child: the file and its arguments (separate argv entries), its stdout and stderr files (created or
    /// truncated), whether it shares this process's session (Unix: no setsid; Windows always gets its own console), and the
    /// variables added to this process's environment for it (null: none).
    /// </summary>
    public sealed record QualificationStart(
        string FileName,
        IReadOnlyList<string> Arguments,
        string StandardOutput,
        string StandardError,
        bool SameSession = false,
        IReadOnlyDictionary<string, string> Environment = null);

    /// <summary>A started child: its id and start, and its exit once it has exited.</summary>
    public interface IQualificationChild : IDisposable
    {
        /// <summary>The child's process id.</summary>
        int Pid { get; }

        /// <summary>The child's start time in UTC.</summary>
        DateTime StartTimeUtc { get; }

        /// <summary>Waits up to the timeout; true once the child has exited.</summary>
        bool WaitForExit(TimeSpan timeout);

        /// <summary>The child has exited.</summary>
        bool HasExited { get; }

        /// <summary>The child's exit code (valid once it has exited).</summary>
        int ExitCode { get; }
    }

    /// <summary>One tree stop: whether it succeeded, why (stopped, taskkill-exit-N, taskkill-timeout, taskkill-start-failed, refused-pid, windows-only) and the command.</summary>
    public sealed record QualificationTreeStop(bool Ok, string Reason, string Command)
    {
        /// <summary>The recorded command of a tree stop: taskkill.exe /PID &lt;pid&gt; /T /F.</summary>
        public static string CommandFor(int pid) => "taskkill.exe /PID " + pid.ToString(CultureInfo.InvariantCulture) + " /T /F";
    }

    /// <summary>The qualification supervisor set's operating-system seams.</summary>
    public interface IQualificationHost
    {
        /// <summary>The argv that starts this CLI (the executable, or the dotnet host, exec and the assembly).</summary>
        IReadOnlyList<string> Cli { get; }

        /// <summary>Copies this CLI's build output into the directory and returns the argv that starts the copy.</summary>
        IReadOnlyList<string> StageCli(string directory);

        /// <summary>Starts a detached child (QualificationDetach.Plan). Throws when it cannot start.</summary>
        IQualificationChild Start(QualificationStart start);

        /// <summary>Invoke-QualificationWindowsTreeStop: taskkill /PID &lt;pid&gt; /T /F and its outcome.</summary>
        QualificationTreeStop StopTree(int pid);

        /// <summary>Get-QualificationStopCommandLine: the live process's command line; null when it cannot be read.</summary>
        string CommandLine(int pid);

        /// <summary>Get-QualificationConsoleProcessIds: the processes attached to this process's console (Windows); empty elsewhere.</summary>
        IReadOnlyList<int> ConsoleProcessIds();

        /// <summary>Get-QualificationSessionId: this process's session id (Unix); null elsewhere or when unreadable.</summary>
        int? SessionId();

        /// <summary>The process table the supervisor's heartbeat observes.</summary>
        IQualificationProcessTable ProcessTable { get; }
    }

    /// <summary>What a detached start runs: the file and argv (Unix), or the file and command line (Windows), and how it detaches.</summary>
    public sealed class QualificationDetachPlan
    {
        /// <summary>The file started.</summary>
        public string FileName { get; init; }

        /// <summary>Its argv entries after the file (Unix).</summary>
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

        /// <summary>The Windows command line (the file quoted when it has a space, then CommandLine of the arguments); null on Unix.</summary>
        public string CommandLine { get; init; }

        /// <summary>Windows: the child gets its own console, hidden (never shares the starter's).</summary>
        public bool NewHiddenConsole { get; init; }

        /// <summary>Unix: the child runs under setsid (its own session and process group).</summary>
        public bool Setsid { get; init; }

        /// <summary>The child's stdout file.</summary>
        public string StandardOutput { get; init; }

        /// <summary>The child's stderr file.</summary>
        public string StandardError { get; init; }
    }

    /// <summary>The detached start's plan and Windows command-line quoting.</summary>
    public static class QualificationDetach
    {
        /// <summary>The Unix redirection: stdin the null device, stdout and stderr the two files, then exec the rest of argv.</summary>
        public const string UnixScript = "out=$1; err=$2; shift 2; exec \"$@\" </dev/null >\"$out\" 2>\"$err\"";

        /// <summary>
        /// ConvertTo-QualificationCommandLine: the arguments joined for CommandLineToArgvW (and .NET on Unix). A non-empty
        /// argument without whitespace or a quote stays as is; another is quoted, a quote escaped with a backslash and every
        /// backslash before a quote or the closing quote doubled.
        /// </summary>
        public static string CommandLine(IEnumerable<string> arguments)
        {
            var parts = new List<string>();
            foreach (var argument in arguments ?? Array.Empty<string>())
            {
                var text = argument ?? "";
                if (text.Length > 0 && text.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '\v', '\f', '"' }) < 0)
                {
                    parts.Add(text);
                    continue;
                }
                var builder = new StringBuilder("\"");
                var slashes = 0;
                foreach (var ch in text)
                {
                    if (ch == '\\') { slashes++; continue; }
                    if (ch == '"')
                    {
                        builder.Append('\\', 2 * slashes + 1).Append('"');
                        slashes = 0;
                        continue;
                    }
                    if (slashes > 0) { builder.Append('\\', slashes); slashes = 0; }
                    builder.Append(ch);
                }
                if (slashes > 0) builder.Append('\\', 2 * slashes);
                parts.Add(builder.Append('"').ToString());
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// New-QualificationDetachedStartParameters: Windows starts the file itself with its own hidden console (never the
        /// starter's: no NoNewWindow); Unix starts /bin/sh with UnixScript, the two files, then setsid (unless SameSession),
        /// the file and its arguments. platform is Windows or Unix.
        /// </summary>
        public static QualificationDetachPlan Plan(QualificationStart start, string platform)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (string.IsNullOrWhiteSpace(start.FileName)) throw new ArgumentException("FileName is required", nameof(start));
            if (string.IsNullOrWhiteSpace(start.StandardOutput) || string.IsNullOrWhiteSpace(start.StandardError)) throw new ArgumentException("both output files are required", nameof(start));
            var arguments = (start.Arguments ?? Array.Empty<string>()).Select(a => a ?? "").ToArray();
            if (platform == "Windows")
            {
                var file = start.FileName.IndexOfAny(new[] { ' ', '\t' }) >= 0 ? "\"" + start.FileName + "\"" : start.FileName;
                var tail = CommandLine(arguments);
                return new QualificationDetachPlan
                {
                    FileName = start.FileName,
                    Arguments = arguments,
                    CommandLine = tail.Length == 0 ? file : file + " " + tail,
                    NewHiddenConsole = true,
                    StandardOutput = start.StandardOutput,
                    StandardError = start.StandardError,
                };
            }
            if (platform != "Unix") throw new ArgumentException("platform is Windows or Unix: " + platform, nameof(platform));
            var argv = new List<string> { "-c", UnixScript, "sh", start.StandardOutput, start.StandardError };
            if (!start.SameSession) argv.Add("setsid");
            argv.Add(start.FileName);
            argv.AddRange(arguments);
            return new QualificationDetachPlan
            {
                FileName = "/bin/sh",
                Arguments = argv,
                Setsid = !start.SameSession,
                StandardOutput = start.StandardOutput,
                StandardError = start.StandardError,
            };
        }
    }

    /// <summary>The production seams: this machine.</summary>
    public sealed class QualificationOs : IQualificationHost
    {
        private static readonly string Platform = OperatingSystem.IsWindows() ? "Windows" : "Unix";

        /// <inheritdoc />
        public IQualificationProcessTable ProcessTable { get; } = new QualificationProcessTable();

        /// <inheritdoc />
        public IReadOnlyList<string> Cli => Command(AppContext.BaseDirectory);

        // The dotnet host runs the entry assembly in the directory; an apphost runs its own file there.
        private static IReadOnlyList<string> Command(string directory)
        {
            var process = Environment.ProcessPath ?? "dotnet";
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(entry))
                return new[] { process, "exec", Path.Combine(directory, Path.GetFileName(entry)) };
            return new[] { Path.Combine(directory, Path.GetFileName(process)) };
        }

        /// <inheritdoc />
        public IReadOnlyList<string> StageCli(string directory)
        {
            var source = Path.GetFullPath(AppContext.BaseDirectory);
            var target = Path.GetFullPath(directory);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var copy = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(file, copy, true);
            }
            return Command(target);
        }

        /// <inheritdoc />
        public IQualificationChild Start(QualificationStart start)
        {
            var plan = QualificationDetach.Plan(start, Platform);
            if (OperatingSystem.IsWindows()) return WindowsChild.Start(plan, start.Environment);
            var info = new ProcessStartInfo(plan.FileName) { UseShellExecute = false };
            foreach (var argument in plan.Arguments) info.ArgumentList.Add(argument);
            foreach (var pair in start.Environment ?? new Dictionary<string, string>()) info.Environment[pair.Key] = pair.Value;
            var process = Process.Start(info) ?? throw new InvalidOperationException("the child did not start: " + start.FileName);
            return new UnixChild(process);
        }

        /// <inheritdoc />
        public QualificationTreeStop StopTree(int pid)
        {
            var command = QualificationTreeStop.CommandFor(pid);
            if (!OperatingSystem.IsWindows()) return new QualificationTreeStop(false, "windows-only", command);
            if (pid <= 0 || pid == Environment.ProcessId) return new QualificationTreeStop(false, "refused-pid", command);
            var info = new ProcessStartInfo("taskkill.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "/PID", pid.ToString(CultureInfo.InvariantCulture), "/T", "/F" }) info.ArgumentList.Add(argument);
            Process process;
            try
            {
                process = Process.Start(info);
                if (process == null) return new QualificationTreeStop(false, "taskkill-start-failed", command);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return new QualificationTreeStop(false, "taskkill-start-failed", command);
            }
            using (process)
            {
                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch (Exception e) when (e is InvalidOperationException || e is Win32Exception) { }
                    return new QualificationTreeStop(false, "taskkill-timeout", command);
                }
                process.WaitForExit();
                return process.ExitCode == 0
                    ? new QualificationTreeStop(true, "stopped", command)
                    : new QualificationTreeStop(false, "taskkill-exit-" + process.ExitCode.ToString(CultureInfo.InvariantCulture), command);
            }
        }

        /// <inheritdoc />
        public string CommandLine(int pid)
        {
            if (pid <= 0) return null;
            try
            {
                if (OperatingSystem.IsWindows()) return WindowsCommandLine(pid);
                var path = "/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/cmdline";
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0) return null;
                var text = new UTF8Encoding(false).GetString(bytes).TrimEnd('\0').Replace('\0', ' ');
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Win32Exception)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<int> ConsoleProcessIds()
        {
            if (!OperatingSystem.IsWindows()) return Array.Empty<int>();
            try
            {
                var list = new uint[256];
                var count = GetConsoleProcessList(list, 256);
                return list.Take((int)Math.Min(count, 256u)).Select(id => (int)id).ToArray();
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return Array.Empty<int>();
            }
        }

        /// <inheritdoc />
        public int? SessionId()
        {
            if (OperatingSystem.IsWindows()) return null;
            try
            {
                // Fields after the parenthesised command: state ppid pgrp session ...
                var stat = File.ReadAllText("/proc/self/stat");
                var rest = stat.Substring(stat.LastIndexOf(')') + 2).Split(' ');
                return int.Parse(rest[3], NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is ArgumentException || e is IndexOutOfRangeException)
            {
                return null;
            }
        }

        // ProcessCommandLineInformation (60): the command line the process's parameters hold, as Win32_Process reads it.
        private static string WindowsCommandLine(int pid)
        {
            var handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                NtQueryInformationProcess(handle, 60, IntPtr.Zero, 0, out var needed);
                if (needed <= 0) return null;
                var buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    if (NtQueryInformationProcess(handle, 60, buffer, needed, out _) != 0) return null;
                    var text = Marshal.PtrToStructure<UnicodeString>(buffer);
                    if (text.Buffer == IntPtr.Zero || text.Length == 0) return null;
                    return Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private sealed class UnixChild : IQualificationChild
        {
            private readonly Process _process;

            public UnixChild(Process process)
            {
                _process = process;
                Pid = process.Id;
                try { StartTimeUtc = process.StartTime.ToUniversalTime(); }
                catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException) { StartTimeUtc = DateTime.UtcNow; }
            }

            public int Pid { get; }

            public DateTime StartTimeUtc { get; }

            public bool WaitForExit(TimeSpan timeout) => _process.WaitForExit((int)Math.Min(int.MaxValue, Math.Max(0, timeout.TotalMilliseconds)));

            public bool HasExited => _process.HasExited;

            public int ExitCode => _process.ExitCode;

            public void Dispose() => _process.Dispose();
        }

        private sealed class WindowsChild : IQualificationChild
        {
            private IntPtr _process;

            private WindowsChild(IntPtr process, int pid, DateTime started)
            {
                _process = process;
                Pid = pid;
                StartTimeUtc = started;
            }

            public int Pid { get; }

            public DateTime StartTimeUtc { get; }

            public bool WaitForExit(TimeSpan timeout)
            {
                var milliseconds = (uint)Math.Min(uint.MaxValue - 1, Math.Max(0, timeout.TotalMilliseconds));
                return WaitForSingleObject(_process, milliseconds) == 0;
            }

            public bool HasExited => WaitForSingleObject(_process, 0) == 0;

            public int ExitCode => GetExitCodeProcess(_process, out var code) ? unchecked((int)code) : throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess");

            public void Dispose()
            {
                if (_process != IntPtr.Zero) CloseHandle(_process);
                _process = IntPtr.Zero;
            }

            // CreateProcess with the three handles alone inherited (PROC_THREAD_ATTRIBUTE_HANDLE_LIST), its own hidden
            // console, and this process's environment with the additions.
            public static IQualificationChild Start(QualificationDetachPlan plan, IReadOnlyDictionary<string, string> additions)
            {
                using var input = File.OpenHandle("NUL", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var output = File.OpenHandle(plan.StandardOutput, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var error = File.OpenHandle(plan.StandardError, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                foreach (var handle in new[] { input, output, error })
                    Check(SetHandleInformation(handle.DangerousGetHandle(), 1, 1), "SetHandleInformation");
                IntPtr attributes = IntPtr.Zero, handles = IntPtr.Zero, block = IntPtr.Zero;
                try
                {
                    var size = IntPtr.Zero;
                    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                    attributes = Marshal.AllocHGlobal(size);
                    Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref size), "InitializeProcThreadAttributeList");
                    handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
                    Marshal.WriteIntPtr(handles, 0, input.DangerousGetHandle());
                    Marshal.WriteIntPtr(handles, IntPtr.Size, output.DangerousGetHandle());
                    Marshal.WriteIntPtr(handles, IntPtr.Size * 2, error.DangerousGetHandle());
                    Check(UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x20002, handles, (IntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute");
                    var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) variables[(string)entry.Key] = (string)entry.Value;
                    foreach (var pair in additions ?? new Dictionary<string, string>())
                    {
                        if (string.IsNullOrEmpty(pair.Value)) variables.Remove(pair.Key);
                        else variables[pair.Key] = pair.Value;
                    }
                    block = Marshal.StringToHGlobalUni(string.Join("\0", variables.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0");
                    var startup = new StartupInfoEx();
                    startup.Startup.cb = Marshal.SizeOf<StartupInfoEx>();
                    startup.Startup.flags = 0x100 | 0x1; // STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW
                    startup.Startup.show = 0; // SW_HIDE
                    startup.Startup.stdin = input.DangerousGetHandle();
                    startup.Startup.stdout = output.DangerousGetHandle();
                    startup.Startup.stderr = error.DangerousGetHandle();
                    startup.Attributes = attributes;
                    var application = Path.IsPathRooted(plan.FileName) ? plan.FileName : null;
                    // CREATE_NEW_CONSOLE | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT
                    Check(CreateProcessW(application, new StringBuilder(plan.CommandLine), IntPtr.Zero, IntPtr.Zero, true, 0x10 | 0x400 | 0x80000,
                        block, null, ref startup, out var info), "CreateProcess " + plan.FileName);
                    CloseHandle(info.Thread);
                    var started = GetProcessTimes(info.Process, out var creation, out _, out _, out _) ? DateTime.FromFileTimeUtc(creation) : DateTime.UtcNow;
                    return new WindowsChild(info.Process, (int)info.Pid, started);
                }
                finally
                {
                    if (attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
                    if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
                    if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
                }
            }
        }

        private static void Check(bool ok, string operation)
        {
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
        }

        [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public int cb; public IntPtr reserved, desktop, title; public int x, y, cx, cy, charsX, charsY, fill, flags; public short show, reservedBytes; public IntPtr reserved2, stdin, stdout, stderr; }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetConsoleProcessList(uint[] list, uint count);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
        [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returned);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
