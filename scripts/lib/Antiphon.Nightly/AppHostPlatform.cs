// The machine the desktop AppHost commands observe and change (unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-
// csharp.md): IAppHostPlatform, and AppHostOs, its production implementation on this Windows machine. It replaced the seams of
// scripts/apphost-common.ps1 (Get-AppHostPortOwners, Stop-AppHostProcessId, Stop-AppHostProcessTree, Get-AppHostStrayProcesses,
// Start-AppHostDevLaunch, the health, version and dashboard probes, Invoke-AppHostGracefulStop's HTTP) and the cmdlets the
// watchdog, its state observer and the maintenance helper called (Get-NetTCPConnection, Get-Process, Get-CimInstance
// Win32_Process, Get-ScheduledTask, Enable-/Disable-ScheduledTask, the Event Log, Invoke-WebRequest).
//
// AppHostOs: the listeners are GetExtendedTcpTable's owner-pid listener rows (IPv4 and IPv6), a process census the toolhelp
// snapshot with each process's creation time and command line (QualificationOs), a tree stop the host's qualification seam
// (taskkill /PID /T /F), a detached child this CLI's run copy (AppHostCli.RunCopy, unit 37) started by the host's
// QualificationOs.Start (its own hidden console, output in its files), the
// Scheduled Tasks the Task Scheduler's COM service (root folder), the Event Log advapi32's ReportEvent under the
// Antiphon-Watchdog source. Tests bind a fake (tests/Antiphon.Tests/Scripts/AppHostWorld.cs). Plain C#: no PowerShell types.
// ASCII only.
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
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>One HTTP probe (Invoke-WebRequest): the status when the server answered, the body of a 2xx, and the error text otherwise.</summary>
    public sealed record AppHostHttpAnswer(int? StatusCode, string Body, string Error);

    /// <summary>One bounded command: whether it timed out, its exit code and its output (stdout and stderr together).</summary>
    public sealed record AppHostCommandResult(bool TimedOut, int ExitCode, string Output);

    /// <summary>One process of a census or a name lookup: its id, image name, command line (null when unreadable) and start (UTC; null when unreadable).</summary>
    public sealed record AppHostProcessRow(int Pid, string Name, string CommandLine, DateTime? CreatedUtc);

    /// <summary>A Scheduled Task's state as the observer records it: Enabled, Disabled, Missing or Unknown, and the detail.</summary>
    public sealed record AppHostTaskInfo(string State, string Detail)
    {
        /// <summary>The task exists and its state was read.</summary>
        public bool Registered => State == "Enabled" || State == "Disabled";
    }

    /// <summary>A started child (the dev-aspire launch, the AppHost wrapper): its id, whether it has exited, its exit code, and a forced stop.</summary>
    public interface IAppHostChild
    {
        /// <summary>The child's process id.</summary>
        int Pid { get; }

        /// <summary>The child has exited.</summary>
        bool HasExited { get; }

        /// <summary>The exit code once it has exited, else null.</summary>
        int? ExitCode { get; }

        /// <summary>Stop-AppHostLaunchChild: stops the child when it is still running.</summary>
        void Stop();
    }

    /// <summary>Everything the AppHost commands observe or change outside their files' contents. A member may throw where noted.</summary>
    public interface IAppHostPlatform
    {
        /// <summary>The file system.</summary>
        IFileSystem Files { get; }

        /// <summary>This is a Windows host.</summary>
        bool IsWindows { get; }

        /// <summary>This process's id (the pid a lock records).</summary>
        int CurrentPid { get; }

        /// <summary>The phone-home configuration whose presence refuses a launch.</summary>
        string PhoneHomeConfigPath { get; }

        /// <summary>Get-Process -Id: Alive, Dead (no such process) or Unknown.</summary>
        AppHostHolder Holder(int pid);

        /// <summary>Get-NetTCPConnection -LocalPort -State Listen: the owners of the port's listeners (IPv4 and IPv6), each once.</summary>
        IReadOnlyList<int> PortOwners(int port);

        /// <summary>Every listening TCP port, each once. Throws when the listeners cannot be read.</summary>
        IReadOnlyList<int> ListeningPorts();

        /// <summary>Get-Process -Name: the running processes with one of the image names (without .exe).</summary>
        IReadOnlyList<AppHostProcessRow> Processes(IReadOnlyCollection<string> names);

        /// <summary>Get-CimInstance Win32_Process: every process with its command line and creation time. Throws when it cannot be read.</summary>
        IReadOnlyList<AppHostProcessRow> Census();

        /// <summary>This process and the processes that started it (the launcher chain), which a census never counts as a launch.</summary>
        IReadOnlyCollection<int> OwnProcessChain();

        /// <summary>Stop-Process -Force (best effort).</summary>
        void StopProcess(int pid);

        /// <summary>taskkill /T /F /PID (best effort).</summary>
        void StopTree(int pid);

        /// <summary>
        /// Starts this CLI detached with the arguments (the command first; dev-aspire, run-apphost), from its run copy under the
        /// checkout's bin-launcher, its stdout and stderr the two files. Throws when it cannot start.
        /// </summary>
        IAppHostChild StartCli(IReadOnlyList<string> arguments, string output, string error);

        /// <summary>One GET within the timeout.</summary>
        AppHostHttpAnswer Get(string url, int timeoutSec);

        /// <summary>One JSON POST within the timeout (the SignalR negotiate).</summary>
        AppHostHttpAnswer Post(string url, string body, int timeoutSec);

        /// <summary>The graceful stop of the local server (POST /api/operator/shutdown with the operator token).</summary>
        AppHostGracefulStopResult GracefulStop(string reason, int timeoutSec);

        /// <summary>One webhook POST (JSON body, Idempotency-Key, no redirect): true for a 2xx answer, false otherwise.</summary>
        bool PostWebhook(Uri uri, string idempotencyKey, string body, int timeoutSec);

        /// <summary>Runs a command and waits at most timeoutSec (0: no limit); a command that cannot start answers exit -1 with the reason.</summary>
        AppHostCommandResult Run(string file, IReadOnlyList<string> arguments, int timeoutSec);

        /// <summary>Get-ScheduledTask -TaskName: the task's state.</summary>
        AppHostTaskInfo Task(string name);

        /// <summary>The named task is Running; null when the task inventory cannot be read.</summary>
        bool? TaskRunning(string name);

        /// <summary>Enable-ScheduledTask or Disable-ScheduledTask. Throws when it fails.</summary>
        void SetTaskEnabled(string name, bool enabled);

        /// <summary>One Application Event Log entry under Antiphon-Watchdog (event 1527); false when it cannot be written.</summary>
        bool WriteEvent(string message, bool information);

        /// <summary>The advisory daemon build check (check-daemon-build) a launch runs first, its lines on the host's output.</summary>
        void CheckDaemonBuild(NightlyHost host);
    }

    /// <summary>The production IAppHostPlatform: this machine, for the checkout at root.</summary>
    public sealed class AppHostOs : IAppHostPlatform
    {
        private readonly NightlyHost _host;
        private readonly string _root;
        private readonly string _operatorTokenPath;

        /// <summary>The platform of the checkout at root; operatorTokenPath is the token file the graceful stop sends.</summary>
        public AppHostOs(NightlyHost host, IFileSystem files, string root, string operatorTokenPath)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            Files = files ?? throw new ArgumentNullException(nameof(files));
            _root = root;
            _operatorTokenPath = operatorTokenPath;
        }

        /// <inheritdoc />
        public IFileSystem Files { get; }

        /// <inheritdoc />
        public bool IsWindows => OperatingSystem.IsWindows();

        /// <inheritdoc />
        public int CurrentPid => Environment.ProcessId;

        /// <inheritdoc />
        public string PhoneHomeConfigPath => WindowsPhoneHome.DefaultConfigPath;

        /// <inheritdoc />
        public AppHostHolder Holder(int pid)
        {
            if (pid <= 0) return AppHostHolder.Dead;
            try
            {
                using var process = Process.GetProcessById(pid);
                return AppHostHolder.Alive;
            }
            catch (ArgumentException) { return AppHostHolder.Dead; }
            catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException) { return AppHostHolder.Unknown; }
        }

        /// <inheritdoc />
        public IReadOnlyList<int> PortOwners(int port) =>
            AppHostTcp.Listeners().Where(row => row.Port == port && row.Pid > 0).Select(row => row.Pid).Distinct().ToArray();

        /// <inheritdoc />
        public IReadOnlyList<int> ListeningPorts()
        {
            if (OperatingSystem.IsWindows()) return AppHostTcp.Listeners().Select(row => row.Port).Distinct().OrderBy(port => port).ToArray();
            if (OperatingSystem.IsLinux()) return AppHostTcp.LinuxListeningPorts();
            throw new PlatformNotSupportedException("listening ports are read on Windows and Linux");
        }

        /// <inheritdoc />
        public IReadOnlyList<AppHostProcessRow> Processes(IReadOnlyCollection<string> names)
        {
            var rows = new List<AppHostProcessRow>();
            foreach (var name in names)
                foreach (var process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        DateTime? started = null;
                        try { started = process.StartTime.ToUniversalTime(); }
                        catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is NotSupportedException) { }
                        rows.Add(new AppHostProcessRow(process.Id, process.ProcessName, null, started));
                    }
            return rows;
        }

        /// <inheritdoc />
        public IReadOnlyList<AppHostProcessRow> Census()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("the process census reads Windows processes");
            return AppHostWin32.Census();
        }

        /// <inheritdoc />
        public IReadOnlyCollection<int> OwnProcessChain()
        {
            var chain = new HashSet<int> { Environment.ProcessId };
            var pid = Environment.ProcessId;
            for (var hop = 0; hop < 32; hop++)
            {
                int parent;
                try { parent = NightlyProcesses.ParentProcessId(pid); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is Win32Exception || e is FormatException) { break; }
                if (parent <= 0 || !chain.Add(parent)) break;
                pid = parent;
            }
            return chain;
        }

        /// <inheritdoc />
        public void StopProcess(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception || e is NotSupportedException) { }
        }

        /// <inheritdoc />
        public void StopTree(int pid)
        {
            var tree = _host.Qualification ?? throw new InvalidOperationException("this host binds no tree stop");
            tree.StopTree(pid);
        }

        /// <inheritdoc />
        public IAppHostChild StartCli(IReadOnlyList<string> arguments, string output, string error)
        {
            var qualification = _host.Qualification ?? throw new InvalidOperationException("this host binds no detached start");
            var cli = AppHostCli.RunCopy(_root, qualification.Cli);
            var argv = new List<string>(cli.Skip(1));
            argv.AddRange(arguments ?? Array.Empty<string>());
            return new DetachedChild(qualification.Start(new QualificationStart(cli[0], argv, output, error)));
        }

        /// <inheritdoc />
        public AppHostHttpAnswer Get(string url, int timeoutSec) => AppHostHttp.Get(url, timeoutSec);

        /// <inheritdoc />
        public AppHostHttpAnswer Post(string url, string body, int timeoutSec)
        {
            try
            {
                var answer = AppHostHttp.Post(url, null, body, timeoutSec);
                return answer.Status >= 200 && answer.Status < 300
                    ? new AppHostHttpAnswer(answer.Status, answer.Body, null)
                    : new AppHostHttpAnswer(answer.Status, null, "Response status code does not indicate success: " + answer.Status.ToString(CultureInfo.InvariantCulture) + ".");
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is IOException || e is InvalidOperationException || e is UriFormatException)
            {
                return new AppHostHttpAnswer(null, null, e.Message);
            }
        }

        /// <inheritdoc />
        public AppHostGracefulStopResult GracefulStop(string reason, int timeoutSec) =>
            AppHostGracefulStop.Run(Files, _operatorTokenPath, AppHostGracefulStop.DefaultBaseUrl, reason, timeoutSec, AppHostHttp.Post);

        /// <inheritdoc />
        public bool PostWebhook(Uri uri, string idempotencyKey, string body, int timeoutSec) => AppHostHttp.Webhook(uri, idempotencyKey, body, timeoutSec);

        /// <inheritdoc />
        public AppHostCommandResult Run(string file, IReadOnlyList<string> arguments, int timeoutSec)
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            Process process;
            try { process = Process.Start(info); }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException) { return new AppHostCommandResult(false, -1, e.Message); }
            if (process == null) return new AppHostCommandResult(false, -1, "the command did not start: " + file);
            using (process)
            {
                var output = new StringBuilder();
                var gate = new object();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (gate) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (gate) output.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutSec > 0 ? timeoutSec * 1000 : Timeout.Infinite))
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException) { }
                    lock (gate) return new AppHostCommandResult(true, -1, output.ToString());
                }
                process.WaitForExit();
                lock (gate) return new AppHostCommandResult(false, process.ExitCode, output.ToString());
            }
        }

        /// <inheritdoc />
        public AppHostTaskInfo Task(string name)
        {
            if (!OperatingSystem.IsWindows()) return new AppHostTaskInfo("Unknown", "the Task Scheduler is a Windows service");
            return AppHostTasks.Read(name);
        }

        /// <inheritdoc />
        public bool? TaskRunning(string name)
        {
            if (!OperatingSystem.IsWindows()) return null;
            return AppHostTasks.Running(name);
        }

        /// <inheritdoc />
        public void SetTaskEnabled(string name, bool enabled)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("the Task Scheduler is a Windows service");
            AppHostTasks.SetEnabled(name, enabled);
        }

        /// <inheritdoc />
        public bool WriteEvent(string message, bool information)
        {
            if (!OperatingSystem.IsWindows()) return false;
            return AppHostEventLog.Write(message, information);
        }

        /// <inheritdoc />
        public void CheckDaemonBuild(NightlyHost host) =>
            DaemonBuildCheck.Run(DaemonBuildCheckOptions.Parse(Array.Empty<string>(), _root), host ?? _host, NightlyReport.HttpTransport(timeout: DaemonBuildCheck.Deadline));

        private sealed class DetachedChild : IAppHostChild
        {
            private readonly IQualificationChild _child;

            public DetachedChild(IQualificationChild child) => _child = child;

            public int Pid => _child.Pid;

            public bool HasExited
            {
                get
                {
                    try { return _child.HasExited; }
                    catch (Win32Exception) { return true; }
                }
            }

            public int? ExitCode
            {
                get
                {
                    try { return _child.HasExited ? _child.ExitCode : null; }
                    catch (Win32Exception) { return null; }
                }
            }

            public void Stop()
            {
                if (HasExited) return;
                try
                {
                    using var process = Process.GetProcessById(_child.Pid);
                    process.Kill();
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception || e is NotSupportedException) { }
            }
        }
    }

    /// <summary>The AppHost commands' HTTP: Invoke-WebRequest's GET, the shutdown POST and the outage webhook.</summary>
    public static class AppHostHttp
    {
        /// <summary>One GET (redirects followed, no proxy for the local stack): the status, the 2xx body, the error text otherwise.</summary>
        public static AppHostHttpAnswer Get(string url, int timeoutSec)
        {
            try
            {
                using var handler = new SocketsHttpHandler { UseProxy = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSec)) };
                using var response = client.GetAsync(url).GetAwaiter().GetResult();
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode) return new AppHostHttpAnswer(status, response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), null);
                return new AppHostHttpAnswer(status, null, "Response status code does not indicate success: " + status.ToString(CultureInfo.InvariantCulture)
                    + " (" + response.ReasonPhrase + ").");
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is IOException || e is InvalidOperationException || e is UriFormatException)
            {
                return new AppHostHttpAnswer(null, null, e.Message);
            }
        }

        /// <summary>One JSON POST with the headers within the timeout: the status and body. A transport failure throws.</summary>
        public static NightlyReportHttpResponse Post(string uri, IReadOnlyDictionary<string, string> headers, string body, int timeoutSec)
        {
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSec)) };
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            foreach (var header in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = new StringContent(body ?? "", Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            return new NightlyReportHttpResponse((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
        }

        /// <summary>The outage webhook: POST without redirects; true only for a 2xx answer. Neither the URL nor the answer is printed.</summary>
        public static bool Webhook(Uri uri, string idempotencyKey, string body, int timeoutSec)
        {
            try
            {
                using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSec)) };
                using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                request.Content = new StringContent(body ?? "", Encoding.UTF8);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is IOException || e is InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>The TCP listeners: GetExtendedTcpTable's owner-pid listener tables (Windows), /proc/net/tcp and tcp6 (Linux).</summary>
    public static class AppHostTcp
    {
        /// <summary>Every IPv4 and IPv6 listener (port, owner); empty off Windows.</summary>
        public static IReadOnlyList<(int Port, int Pid)> Listeners()
        {
            var rows = new List<(int Port, int Pid)>();
            if (!OperatingSystem.IsWindows()) return rows;
            Read(2, 24, 8, 20, rows);
            Read(23, 56, 20, 52, rows);
            return rows;
        }

        // TCP_TABLE_OWNER_PID_LISTENER (3) of one family: dwNumEntries, then rows of rowSize bytes with the local port (network
        // byte order in the low two bytes) at portOffset and the owning pid at pidOffset.
        private static void Read(int family, int rowSize, int portOffset, int pidOffset, List<(int Port, int Pid)> rows)
        {
            var size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
            if (size <= 0) return;
            var table = Marshal.AllocHGlobal(size);
            try
            {
                var status = GetExtendedTcpTable(table, ref size, false, family, 3, 0);
                if (status != 0) throw new Win32Exception((int)status, "GetExtendedTcpTable");
                var count = Marshal.ReadInt32(table);
                for (var i = 0; i < count; i++)
                {
                    var offset = 4 + i * rowSize;
                    var port = (Marshal.ReadByte(table, offset + portOffset) << 8) | Marshal.ReadByte(table, offset + portOffset + 1);
                    rows.Add((port, Marshal.ReadInt32(table, offset + pidOffset)));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }

        /// <summary>The Linux listeners (state 0A) of /proc/net/tcp and /proc/net/tcp6, each port once, ascending.</summary>
        public static IReadOnlyList<int> LinuxListeningPorts()
        {
            var ports = new SortedSet<int>();
            foreach (var path in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
            {
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path).Skip(1))
                {
                    var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 4 || fields[3] != "0A") continue;
                    var local = fields[1];
                    var colon = local.LastIndexOf(':');
                    if (colon < 0) continue;
                    if (int.TryParse(local.Substring(colon + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var port)) ports.Add(port);
                }
            }
            return ports.ToArray();
        }

        [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
    }

    /// <summary>The Win32_Process fields the launch activity reads, without WMI: toolhelp snapshot, creation time, command line.</summary>
    internal static class AppHostWin32
    {
        private const uint SnapProcess = 0x2;
        private const int QueryLimited = 0x1000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
        }

        public static IReadOnlyList<AppHostProcessRow> Census()
        {
            var commandLines = new QualificationOs();
            var rows = new List<AppHostProcessRow>();
            var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateToolhelp32Snapshot");
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
                for (var more = First(snapshot, ref entry); more; more = Next(snapshot, ref entry))
                {
                    var pid = (int)entry.ProcessId;
                    if (pid > 0)
                    {
                        DateTime? created = null;
                        var handle = OpenProcess(QueryLimited, false, pid);
                        if (handle != IntPtr.Zero)
                        {
                            try
                            {
                                if (GetProcessTimes(handle, out var creation, out _, out _, out _) && creation > 0) created = DateTime.FromFileTimeUtc(creation);
                            }
                            finally
                            {
                                CloseHandle(handle);
                            }
                        }
                        rows.Add(new AppHostProcessRow(pid, entry.ExeFile ?? "", commandLines.CommandLine(pid), created));
                    }
                    entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }
            return rows;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool First(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Next(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }

    /// <summary>
    /// The Task Scheduler (Schedule.Service, late bound): a root-folder task's state (TASK_STATE: 1 Disabled, 2 Queued, 3 Ready,
    /// 4 Running), its Running test, and Enabled. A task that does not exist is ERROR_FILE_NOT_FOUND.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class AppHostTasks
    {
        private const int NotFound = unchecked((int)0x80070002);
        private static readonly string[] States = { "Unknown", "Disabled", "Queued", "Ready", "Running" };

        /// <summary>The task's state: Enabled (State=Ready and the others), Disabled, Missing, or Unknown with the failure.</summary>
        public static AppHostTaskInfo Read(string name)
        {
            try
            {
                var task = Find(name);
                if (task == null) return new AppHostTaskInfo("Missing", "No scheduled task named '" + name + "' is registered");
                var state = StateName(Convert.ToInt32(Get(task, "State"), CultureInfo.InvariantCulture));
                return new AppHostTaskInfo(state == "Disabled" ? "Disabled" : "Enabled", "State=" + state);
            }
            catch (Exception e) when (e is COMException || e is TargetInvocationException || e is InvalidCastException || e is FormatException || e is MissingMethodException || e is UnauthorizedAccessException)
            {
                return new AppHostTaskInfo("Unknown", Message(e));
            }
        }

        /// <summary>True when the task is Running, false when it is not or does not exist, null when the inventory cannot be read.</summary>
        public static bool? Running(string name)
        {
            try
            {
                var task = Find(name);
                return task != null && Convert.ToInt32(Get(task, "State"), CultureInfo.InvariantCulture) == 4;
            }
            catch (Exception e) when (e is COMException || e is TargetInvocationException || e is InvalidCastException || e is FormatException || e is MissingMethodException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Sets the registered task's Enabled flag. Throws when the task does not exist or the change is refused.</summary>
        public static void SetEnabled(string name, bool enabled)
        {
            object task;
            try { task = Find(name); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw new InvalidOperationException(Message(e), e.InnerException); }
            if (task == null) throw new InvalidOperationException("No scheduled task named '" + name + "' is registered");
            try { task.GetType().InvokeMember("Enabled", BindingFlags.SetProperty, null, task, new object[] { enabled }, CultureInfo.InvariantCulture); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw new InvalidOperationException(Message(e), e.InnerException); }
        }

        /// <summary>The state name of a TASK_STATE value (Get-ScheduledTask's State).</summary>
        public static string StateName(int state) => state >= 0 && state < States.Length ? States[state] : "Unknown";

        private static object Find(string name)
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true);
            var service = Activator.CreateInstance(type);
            Invoke(service, "Connect");
            var folder = Invoke(service, "GetFolder", "\\");
            // ERROR_FILE_NOT_FOUND surfaces as a FileNotFoundException (the interop's mapping of 0x80070002), not a COMException.
            try { return Invoke(folder, "GetTask", name); }
            catch (TargetInvocationException e) when (e.InnerException != null && e.InnerException.HResult == NotFound) { return null; }
            catch (Exception e) when ((e is COMException || e is FileNotFoundException) && e.HResult == NotFound) { return null; }
        }

        private static object Invoke(object target, string name, params object[] arguments) =>
            target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments, CultureInfo.InvariantCulture);

        private static object Get(object target, string name) =>
            target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);

        private static string Message(Exception e) => (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e).Message;
    }

    /// <summary>
    /// Write-AppHostWindowsEvent: the Application log under the Antiphon-Watchdog source (created when missing, which needs
    /// elevation: a denial is a sink failure, never a watchdog failure), Warning (or Information for a recovery), event 1527.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class AppHostEventLog
    {
        private const string Source = "Antiphon-Watchdog";
        private const string Logs = @"SYSTEM\CurrentControlSet\Services\EventLog";

        /// <summary>Writes one entry; false when the source cannot be created, belongs to another log, or the write fails.</summary>
        public static bool Write(string message, bool information)
        {
            try
            {
                var log = SourceLog();
                if (log == null)
                {
                    using var application = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Logs + @"\Application", writable: true);
                    if (application == null) return false;
                    using var created = application.CreateSubKey(Source);
                    created.SetValue("EventMessageFile", Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "EventLogMessages.dll"));
                    log = "Application";
                }
                if (!string.Equals(log, "Application", StringComparison.OrdinalIgnoreCase)) return false;
                var handle = RegisterEventSourceW(null, Source);
                if (handle == IntPtr.Zero) return false;
                try
                {
                    return ReportEventW(handle, (ushort)(information ? 0x0004 : 0x0002), 0, 1527, IntPtr.Zero, 1, 0, new[] { message }, IntPtr.Zero);
                }
                finally
                {
                    DeregisterEventSource(handle);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException || e is ObjectDisposedException)
            {
                return false;
            }
        }

        // The log whose key holds the source (EventLog.LogNameFromSourceName), or null.
        private static string SourceLog()
        {
            using var logs = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Logs);
            if (logs == null) return null;
            foreach (var name in logs.GetSubKeyNames())
            {
                try
                {
                    using var log = logs.OpenSubKey(name);
                    using var source = log?.OpenSubKey(Source);
                    if (source != null) return name;
                }
                catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException) { }
            }
            return null;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr RegisterEventSourceW(string server, string source);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ReportEventW(IntPtr log, ushort type, ushort category, uint eventId, IntPtr sid, ushort strings, uint dataSize, string[] messages, IntPtr data);
        [DllImport("advapi32.dll")] private static extern bool DeregisterEventSource(IntPtr log);
    }
}
