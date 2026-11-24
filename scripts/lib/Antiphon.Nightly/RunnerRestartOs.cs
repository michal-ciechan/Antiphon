// CARD-0420: the production platform of the desktop session runner restart (New-RunnerRestartPlatform of the deleted
// scripts/session-runner-restart-health.ps1; unit 30 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md).
// The checkout's logs directory holds the desired state (session-runner.state), the PID files (session-runner.service.pid,
// session-runner.supervisor.pid) and the capture log (session-runner.log); the runner this checkout owns is its Debug
// executable, src/Antiphon.SessionRunner/bin/Debug/net9.0/Antiphon.SessionRunner.exe. HTTP goes to 127.0.0.1:17204
// (no proxy, no redirect). Files are the IFileSystem's, the wall clock and the sleep the host's, a tree stop the host's
// qualification seam (taskkill /PID /T /F), the logon task's start the host's launcher (schtasks /Run), and the process
// reads (identity, the listener's owner, the processes of a name) RunnerProcessSeams' (RunnerProcesses on this machine).
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The process reads of the restart platform: a process's identity, the listener's owner, the processes of a name.</summary>
    public sealed class RunnerProcessSeams
    {
        /// <summary>The live process with the id (its start and path), or null.</summary>
        public Func<int, RunnerProcessIdentity> Identity { get; init; } = RunnerProcesses.Identity;

        /// <summary>The ids of the running processes with the image name (without .exe).</summary>
        public Func<string, IReadOnlyList<int>> IdsByName { get; init; } = RunnerProcesses.IdsByName;

        /// <summary>The process listening on the IPv4 loopback or any-address port, or 0.</summary>
        public Func<int, int> ListeningOwner { get; init; } = RunnerProcesses.ListeningOwner;
    }

    /// <summary>The production IRunnerRestartPlatform: this machine, for the checkout at root.</summary>
    public sealed class RunnerRestartOs : IRunnerRestartPlatform
    {
        /// <summary>The canonical session runner port.</summary>
        public const int RunnerPort = 17204;

        /// <summary>The logon task the install-autostart command registers.</summary>
        public const string TaskName = "Antiphon Session Runner";

        private static readonly Regex Digits = new Regex(@"^\d+$", RegexOptions.CultureInvariant);

        private readonly NightlyHost _host;
        private readonly Stopwatch _clock;
        private readonly string _logDir;
        private readonly string _expectedExe;
        private readonly int _port;
        private readonly RunnerProcessSeams _processes;

        /// <summary>
        /// The platform of the checkout at root. commandClock is the command's monotonic clock, started at command entry
        /// (CommandOrigin 0). port replaces 17204 (tests); processes replaces this machine's process reads (tests).
        /// </summary>
        public RunnerRestartOs(string root, NightlyHost host, IFileSystem files, Stopwatch commandClock, int port = RunnerPort, RunnerProcessSeams processes = null)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("the checkout root is required", nameof(root));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            Files = files ?? throw new ArgumentNullException(nameof(files));
            _clock = commandClock ?? Stopwatch.StartNew();
            _port = port;
            _processes = processes ?? new RunnerProcessSeams();
            _logDir = Files.Path.Combine(root, "logs");
            _expectedExe = Files.Path.GetFullPath(Files.Path.Combine(root, "src", "Antiphon.SessionRunner", "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe"));
            LogPath = Files.Path.Combine(_logDir, "session-runner.log");
        }

        /// <inheritdoc />
        public IFileSystem Files { get; }

        /// <inheritdoc />
        public string LogPath { get; }

        /// <summary>This checkout's runner executable.</summary>
        public string ExpectedExecutable => _expectedExe;

        /// <inheritdoc />
        public double? CommandOrigin => 0;

        /// <inheritdoc />
        public double Now() => _clock.Elapsed.TotalMilliseconds;

        /// <inheritdoc />
        public string Utc() => _host.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public void Sleep(double milliseconds) => _host.Sleep(TimeSpan.FromMilliseconds(Math.Ceiling(milliseconds)));

        /// <inheritdoc />
        public RunnerProbeResult Probe(double budgetMilliseconds)
        {
            try
            {
                using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, budgetMilliseconds)));
                using var response = client.GetAsync(Url("/health"), HttpCompletionOption.ResponseHeadersRead, deadline.Token).GetAwaiter().GetResult();
                var status = (int)response.StatusCode;
                return new RunnerProbeResult(status, "http-" + status.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e) when (IsTransport(e))
            {
                return new RunnerProbeResult(0, "transport-error-or-timeout");
            }
        }

        /// <inheritdoc />
        public JsonNode ProbeBuild()
        {
            try
            {
                using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 65536 };
                using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
                using var response = client.GetAsync(Url("/capabilities"), HttpCompletionOption.ResponseContentRead, deadline.Token).GetAwaiter().GetResult();
                if ((int)response.StatusCode != 200) return null;
                return JsonNode.Parse(response.Content.ReadAsStringAsync(deadline.Token).GetAwaiter().GetResult());
            }
            catch (Exception e) when (IsTransport(e) || e is JsonException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public RunnerProcessIdentity Identity()
        {
            var owner = _processes.ListeningOwner(_port);
            return owner > 0 ? _processes.Identity(owner) : null;
        }

        /// <inheritdoc />
        public bool Verify(RunnerProcessIdentity identity) =>
            identity != null && string.Equals(identity.Path, _expectedExe, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_processes.Identity(identity.Pid)?.StartTimeUtc, identity.StartTimeUtc, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public RunnerCensus Census() => new RunnerCensus(ReadPid("session-runner.supervisor.pid"), ReadPid("session-runner.service.pid"));

        /// <inheritdoc />
        public RunnerProcessIdentity Process(int pid) => _processes.Identity(pid);

        /// <inheritdoc />
        public void Control(string operation, RunnerProcessIdentity identity)
        {
            switch (operation)
            {
                case RunnerRestartOperations.WriteState:
                    Files.Directory.CreateDirectory(_logDir);
                    Files.File.WriteAllText(Files.Path.Combine(_logDir, "session-runner.state"), "running" + Environment.NewLine, new UTF8Encoding(false));
                    break;
                case RunnerRestartOperations.StopService:
                    // The recorded wrapper and the runners at this checkout's Debug executable: a foreign listener (a worktree
                    // or Release runner) is left alone, even with --hard.
                    Stop(ReadPid("session-runner.service.pid"));
                    foreach (var pid in _processes.IdsByName("Antiphon.SessionRunner"))
                    {
                        var owned = _processes.Identity(pid);
                        if (owned != null && string.Equals(owned.Path, _expectedExe, StringComparison.OrdinalIgnoreCase)) Stop(owned);
                    }
                    break;
                case RunnerRestartOperations.StopSupervisor:
                    Stop(identity);
                    break;
                case RunnerRestartOperations.DeletePids:
                    foreach (var name in new[] { "session-runner.service.pid", "session-runner.supervisor.pid" })
                    {
                        try { Files.File.Delete(Files.Path.Combine(_logDir, name)); }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { /* Remove-Item -ErrorAction SilentlyContinue */ }
                    }
                    break;
                case RunnerRestartOperations.StartTask:
                    var run = _host.Launch(new NightlyLaunch("schtasks.exe", new[] { "/Run", "/TN", TaskName }, "", CaptureOutput: true));
                    if (run.ExitCode != 0)
                    {
                        var detail = (run.LogText ?? "").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
                        throw new InvalidOperationException("schtasks /Run /TN \"" + TaskName + "\" exited " + run.ExitCode.ToString(CultureInfo.InvariantCulture)
                            + (detail.Length > 0 ? ": " + detail : ""));
                    }
                    break;
                case RunnerRestartOperations.KillSessions:
                    if (!KillAll()) _host.Out.WriteLine("kill-all endpoint unavailable; checking detached pty-host stragglers.");
                    foreach (var pid in _processes.IdsByName("Antiphon.PtyHost")) Stop(_processes.Identity(pid));
                    break;
                default:
                    throw new InvalidOperationException("Unknown control operation " + operation);
            }
        }

        // POST /sessions/kill-all (15 s); false when it failed or answered anything but 2xx.
        private bool KillAll()
        {
            try
            {
                using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                using var response = client.PostAsync(Url("/sessions/kill-all"), null).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch (Exception e) when (IsTransport(e))
            {
                return false;
            }
        }

        // Stops the identified process's tree when it is still the same process; a changed identity refuses.
        private void Stop(RunnerProcessIdentity identity)
        {
            if (identity == null) return;
            var now = _processes.Identity(identity.Pid);
            if (now == null) return;
            if (!string.Equals(now.StartTimeUtc, identity.StartTimeUtc, StringComparison.OrdinalIgnoreCase) || !string.Equals(now.Path, identity.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Process identity changed before stop");
            var tree = _host.Qualification ?? throw new InvalidOperationException("this host binds no tree stop");
            var stopped = tree.StopTree(identity.Pid);
            if (!stopped.Ok && _processes.Identity(identity.Pid) != null) throw new InvalidOperationException("taskkill failed (" + stopped.Reason + ")");
        }

        // An unreadable or malformed PID is unknown ownership, not an absent supervisor: the restart fails before any control.
        // An absent file or logs directory is no PID (a fresh checkout need not have logs). This read creates nothing.
        private RunnerProcessIdentity ReadPid(string name)
        {
            string text;
            try
            {
                text = Files.File.ReadAllText(Files.Path.Combine(_logDir, name)).Trim();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            if (!Digits.IsMatch(text) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
                throw new InvalidOperationException("Invalid PID file: " + name);
            return _processes.Identity(pid);
        }

        private string Url(string path) => "http://127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + path;

        private static bool IsTransport(Exception e) =>
            e is HttpRequestException || e is OperationCanceledException || e is IOException || e is SocketException || e is InvalidOperationException;
    }

    /// <summary>This machine's process reads (Get-RunnerProcessIdentity and AntiphonRestartTcpOwner of the deleted script).</summary>
    public static class RunnerProcesses
    {
        private const int QueryLimited = 0x1000;
        private const uint StillActive = 259;

        /// <summary>
        /// The live process with the id: its start (round-trip UTC) and its image path (null when unreadable); null when
        /// there is no such process or its start cannot be read.
        /// </summary>
        public static RunnerProcessIdentity Identity(int pid)
        {
            if (pid <= 0) return null;
            if (!OperatingSystem.IsWindows()) return PortableIdentity(pid);
            var handle = OpenProcess(QueryLimited, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                if (!GetExitCodeProcess(handle, out var code) || code != StillActive) return null;
                if (!GetProcessTimes(handle, out var creation, out _, out _, out _) || creation <= 0) return null;
                var start = DateTime.FromFileTimeUtc(creation).ToString("o", CultureInfo.InvariantCulture);
                var name = new StringBuilder(32768);
                var size = name.Capacity;
                var path = QueryFullProcessImageNameW(handle, 0, name, ref size) ? name.ToString(0, size) : null;
                return new RunnerProcessIdentity(pid, start, path);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>The ids of the running processes with the image name (without .exe).</summary>
        public static IReadOnlyList<int> IdsByName(string name)
        {
            var ids = new List<int>();
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
                using (process) ids.Add(process.Id);
            return ids;
        }

        /// <summary>
        /// The owner of the IPv4 listener on the port bound to any address or 127.0.0.1 (GetExtendedTcpTable, owner-pid
        /// listeners), or 0. Windows only (0 elsewhere): a lookup without the slow CIM enumeration of Get-NetTCPConnection.
        /// </summary>
        public static int ListeningOwner(int port)
        {
            if (!OperatingSystem.IsWindows()) return 0;
            var size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
            if (size <= 0) return 0;
            var table = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0) != 0) return 0;
                var count = Marshal.ReadInt32(table);
                for (var i = 0; i < count; i++)
                {
                    var offset = 4 + i * 24;
                    var localPort = (Marshal.ReadByte(table, offset + 8) << 8) | Marshal.ReadByte(table, offset + 9);
                    var address = Marshal.ReadInt32(table, offset + 4);
                    if (Marshal.ReadInt32(table, offset) == 2 && localPort == port && (address == 0 || address == 16777343)) return Marshal.ReadInt32(table, offset + 20);
                }
                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }

        private static RunnerProcessIdentity PortableIdentity(int pid)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                var start = process.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                string path = null;
                try { path = process.MainModule?.FileName; }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is NotSupportedException) { }
                return new RunnerProcessIdentity(pid, start, path);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception || e is NotSupportedException)
            {
                return null;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
    }
}
