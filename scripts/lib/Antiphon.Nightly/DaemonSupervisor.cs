// CARD-0043, CARD-0420: the long-lived supervisor of one Antiphon service (unit 30 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). It replaced the body of scripts/run-daemon.ps1, which
// stays as a thin launcher because the AppHost starts it by path (DaemonProcessService: a detached pwsh -File) and the
// logon task's scripts/autostart-session-runner.ps1 calls it. The CLI runs it as
//
//   antiphon-scripts run-daemon --name <n> --work-dir <dir> --exe <path> [--exe-args <text>] --log-file <path>
//       --service-pid-file <path> --state-file <path> [--build-project-dir <dir>] [--log-max-mb 20]
//       [--log-retain-days 5] [--log-retain-count 10] [--supervisor-pid <pid>]
//
// Each iteration reads the desired state (logs/<name>.state: "stopped" ends supervision; a missing, empty or unreadable
// file is "running"), rotates the capture log, takes a fresh startup attempt and, with --build-project-dir, builds the
// project in Debug before the launch (under a build slot in this process, BuildSlot, stamped -p:SourceRevisionId=<HEAD> when
// HEAD reads as 40 hex; a failed build retries after 5 s), so a soft restart picks up new code, then launches the BUILT executable
// directly (never `dotnet run`, whose kill-on-close job would capture the detached pty-hosts). The service runs under
// `cmd.exe /s /c "<exe> <args> >> "<log>" 2>&1"`, so the Aspire log tailer reads its output from the log; its wrapper's
// PID is the service PID file while it runs. When the service exits and the desired state is still "running", the next
// iteration starts after 3 s.
//
// Rotation (CARD-0043) happens here, at (re)launch, and nowhere else: the '>>' redirection holds the log open for the whole
// service lifetime. An oversized log (over --log-max-mb) is renamed <base>.<yyyyMMdd-HHmmss><ext>; rolls are pruned by age
// (--log-retain-days) and count (--log-retain-count). Diagnostics never alter supervision: the startup milestones
// (ANTIPHON_STARTUP records, which restart-session-runner reads) and every log write swallow their own failures.
//
// The supervisor the records name is --supervisor-pid (default: this process): the process the AppHost or the logon task
// recorded in logs/<name>.supervisor.pid, which the launcher passes as its own PID. The session runner receives it as
// ANTIPHON_STARTUP_SUPERVISOR_PID/_START with the attempt (ANTIPHON_STARTUP_ATTEMPT). When it is another process and it is
// gone (its start time changed or it no longer runs), supervision ends at the next check, as it did when that process was
// the supervisor itself. Exit 0. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The run-daemon command's options (run-daemon.ps1's parameters, and the supervisor its launcher names).</summary>
    public sealed class DaemonSupervisorOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "run-daemon --name <n> --work-dir <dir> --exe <path> [--exe-args <text>] --log-file <path> --service-pid-file <path> --state-file <path>"
            + " [--build-project-dir <dir>] [--log-max-mb 20] [--log-retain-days 5] [--log-retain-count 10] [--supervisor-pid <pid>]";

        /// <summary>The service's name (session-runner gets the startup correlation variables).</summary>
        public string Name { get; init; } = "";

        /// <summary>The service's working directory.</summary>
        public string WorkDir { get; init; } = "";

        /// <summary>The service's executable (or command).</summary>
        public string Exe { get; init; } = "";

        /// <summary>The service's arguments, one whitespace-separated string (PowerShell -File drops extra values from a [string[]]).</summary>
        public string ExeArgs { get; init; } = "";

        /// <summary>The capture log.</summary>
        public string LogFile { get; init; } = "";

        /// <summary>The service wrapper's PID file.</summary>
        public string ServicePidFile { get; init; } = "";

        /// <summary>The desired-state file.</summary>
        public string StateFile { get; init; } = "";

        /// <summary>When set, the project built (Debug) before each launch; empty: no build.</summary>
        public string BuildProjectDir { get; init; } = "";

        /// <summary>The capture log's cap, in MB; 0 or less: no rotation.</summary>
        public int LogMaxMb { get; init; } = 20;

        /// <summary>A roll older than this many days is pruned.</summary>
        public int LogRetainDays { get; init; } = 5;

        /// <summary>At most this many rolls are kept.</summary>
        public int LogRetainCount { get; init; } = 10;

        /// <summary>The supervisor the records name; null: this process.</summary>
        public int? SupervisorPid { get; init; }

        /// <summary>The service's arguments, split at whitespace.</summary>
        public IReadOnlyList<string> ExeArgList => (ExeArgs ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// The flags after the command name. A missing name, working directory, executable, log, PID or state file, a
        /// non-integer number or a supervisor PID below 1 is a NightlyUsageException.
        /// </summary>
        public static DaemonSupervisorOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[]
            {
                "--name", "--work-dir", "--exe", "--exe-args", "--log-file", "--service-pid-file", "--state-file", "--build-project-dir",
                "--log-max-mb", "--log-retain-days", "--log-retain-count", "--supervisor-pid",
            }, Array.Empty<string>());
            int? supervisor = null;
            if (flags.Has("--supervisor-pid"))
            {
                supervisor = flags.Int("--supervisor-pid", 0);
                if (supervisor <= 0) throw new NightlyUsageException("--supervisor-pid expects a process id above 0");
            }
            return new DaemonSupervisorOptions
            {
                Name = flags.Required("--name"),
                WorkDir = flags.Required("--work-dir"),
                Exe = flags.Required("--exe"),
                ExeArgs = flags.Value("--exe-args", ""),
                LogFile = flags.Required("--log-file"),
                ServicePidFile = flags.Required("--service-pid-file"),
                StateFile = flags.Required("--state-file"),
                BuildProjectDir = flags.Value("--build-project-dir", ""),
                LogMaxMb = flags.Int("--log-max-mb", 20),
                LogRetainDays = flags.Int("--log-retain-days", 5),
                LogRetainCount = flags.Int("--log-retain-count", 10),
                SupervisorPid = supervisor,
            };
        }
    }

    /// <summary>One service start: the file, its raw command line, the working directory and the variables added to this process's environment.</summary>
    public sealed record DaemonServiceStart(string FileName, string Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string> Environment);

    /// <summary>A started service wrapper.</summary>
    public interface IDaemonService : IDisposable
    {
        /// <summary>The wrapper's process id.</summary>
        int Pid { get; }

        /// <summary>The wrapper's start time in UTC (null when unreadable).</summary>
        DateTime? StartTimeUtc { get; }

        /// <summary>Waits for the wrapper's exit and returns its exit code.</summary>
        int WaitForExit();
    }

    /// <summary>Starts a service wrapper; throws when it cannot start.</summary>
    public delegate IDaemonService DaemonServiceStarter(DaemonServiceStart start);

    /// <summary>The supervisor loop (run-daemon.ps1's body).</summary>
    public static class DaemonSupervisor
    {
        private static readonly Regex Sha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>
        /// Supervises the service until the desired state stops it or the named supervisor is gone; returns 0. The build runs
        /// under buildSlot (label daemon-build; its BUILD SLOT lines and output captured into the log), HEAD on host's git, the
        /// waits on host.Sleep, the clock is host's, files are the IFileSystem's and the service starts through start.
        /// </summary>
        public static int Run(DaemonSupervisorOptions options, NightlyHost host, IFileSystem files, DaemonServiceStarter start, BuildSlotRunner buildSlot)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (buildSlot == null) throw new ArgumentNullException(nameof(buildSlot));
            return new Loop(options, host, files, start, buildSlot).Run();
        }

        /// <summary>The production service start: Process.Start of the raw command line, no shell, no window, this process's streams.</summary>
        public static IDaemonService StartService(DaemonServiceStart start)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            var info = new ProcessStartInfo(start.FileName)
            {
                Arguments = start.Arguments ?? "",
                WorkingDirectory = start.WorkingDirectory ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var pair in start.Environment ?? new Dictionary<string, string>()) info.Environment[pair.Key] = pair.Value;
            var process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start returned null");
            return new ProcessService(process);
        }

        private sealed class ProcessService : IDaemonService
        {
            private readonly Process _process;

            public ProcessService(Process process)
            {
                _process = process;
                Pid = process.Id;
                try { StartTimeUtc = process.StartTime.ToUniversalTime(); }
                catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException) { StartTimeUtc = null; }
            }

            public int Pid { get; }

            public DateTime? StartTimeUtc { get; }

            public int WaitForExit()
            {
                _process.WaitForExit();
                return _process.ExitCode;
            }

            public void Dispose() => _process.Dispose();
        }

        private sealed class Loop
        {
            private readonly DaemonSupervisorOptions _options;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly DaemonServiceStarter _start;
            private readonly BuildSlotRunner _buildSlot;
            private readonly int _supervisorPid;
            private readonly DateTime? _supervisorStart;
            private readonly string _producerStart;
            private readonly bool _watchSupervisor;
            private string _attempt;

            public Loop(DaemonSupervisorOptions options, NightlyHost host, IFileSystem files, DaemonServiceStarter start, BuildSlotRunner buildSlot)
            {
                _options = options;
                _host = host;
                _files = files;
                _start = start;
                _buildSlot = buildSlot;
                _supervisorPid = options.SupervisorPid ?? host.Processes.CurrentPid;
                _supervisorStart = host.Processes.StartTimeUtc(_supervisorPid);
                _producerStart = _supervisorStart?.ToString("o", CultureInfo.InvariantCulture);
                _watchSupervisor = _supervisorPid != host.Processes.CurrentPid;
            }

            public int Run()
            {
                foreach (var file in new[] { _options.LogFile, _options.ServicePidFile, _options.StateFile })
                {
                    var directory = _files.Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(directory) && !_files.Directory.Exists(directory)) _files.Directory.CreateDirectory(directory);
                }
                WriteLog("Supervisor started (PID " + Pid(_supervisorPid)
                    + (_watchSupervisor ? ", run-daemon PID " + Pid(_host.Processes.CurrentPid) : "") + ")");
                while (true)
                {
                    var desired = DesiredState();
                    if (desired == "stopped")
                    {
                        WriteStartup("supervisor-stopping");
                        WriteLog("Desired state is stopped - exiting.");
                        DeleteServicePid();
                        return 0;
                    }
                    if (SupervisorGone()) return StopForGoneSupervisor();
                    RotateLog();
                    _attempt = Guid.NewGuid().ToString("N");
                    if (!string.IsNullOrEmpty(_options.BuildProjectDir) && !Build()) continue;
                    Launch();
                    desired = DesiredState();
                    if (desired != "running")
                    {
                        WriteStartup("supervisor-stopping");
                        WriteLog("Desired state is '" + desired + "' - stopping.");
                        return 0;
                    }
                    if (SupervisorGone()) return StopForGoneSupervisor();
                    WriteLog("Restarting in 3 s...");
                    WriteStartup("retry-scheduled", 3000);
                    _host.Sleep(TimeSpan.FromSeconds(3));
                }
            }

            // Builds before the launch so a soft restart (kill the service; the loop relaunches) picks up new code. The old
            // service is dead here, so its executable is unlocked and the build can overwrite it. False: retry after 5 s.
            private bool Build()
            {
                WriteStartup("build-start");
                var began = _host.Clock.GetTimestamp();
                WriteLog("Building " + _options.BuildProjectDir + " (Debug)...");
                // A deployed daemon stamps HEAD; an unreadable HEAD builds unstamped ("unknown"), which check-daemon-build and
                // the runner status buildVersion then report.
                var arguments = new List<string> { "build", _options.BuildProjectDir, "-c", "Debug", "--nologo" };
                string revision;
                try
                {
                    var head = _host.RunGit(_options.BuildProjectDir, "rev-parse", "HEAD");
                    revision = head.ExitCode == 0 ? head.Output.Trim().ToLowerInvariant() : "";
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
                {
                    // git that cannot start is an unreadable HEAD, as `& git ... 2>$null` was.
                    revision = "";
                }
                if (Sha.IsMatch(revision)) arguments.Add("-p:SourceRevisionId=" + revision);
                else WriteLog("WARNING: git rev-parse HEAD failed in " + _options.BuildProjectDir + "; building unstamped.");
                var command = new List<string> { "dotnet" };
                command.AddRange(arguments);
                var capture = new StringWriter(CultureInfo.InvariantCulture);
                // The slot's own lines and the build's output, in arrival order; a build that cannot start is its exit 1 line.
                var code = _buildSlot(new BuildSlotOptions { Label = "daemon-build", Command = command }, capture);
                var output = capture.ToString();
                WriteStartup("build-complete", _host.Clock.GetElapsedTime(began).TotalMilliseconds, code);
                var lines = output.Replace("\r\n", "\n").Split('\n');
                var count = lines.Length;
                while (count > 0 && lines[count - 1].Length == 0) count--;
                for (var i = 0; i < count; i++) AppendLine(lines[i]);
                if (code != 0)
                {
                    WriteStartup("retry-scheduled", 5000, code);
                    WriteLog("[ERR] Build failed (exit " + code.ToString(CultureInfo.InvariantCulture) + "). Retrying in 5 s...");
                    _host.Sleep(TimeSpan.FromSeconds(5));
                    return false;
                }
                WriteLog("Build succeeded.");
                return true;
            }

            private void Launch()
            {
                var argumentList = _options.ExeArgList;
                WriteLog("Starting " + _options.Exe + " " + string.Join(" ", argumentList));
                var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (_options.Name == "session-runner")
                {
                    environment["ANTIPHON_STARTUP_ATTEMPT"] = _attempt;
                    environment["ANTIPHON_STARTUP_SUPERVISOR_PID"] = Pid(_supervisorPid);
                    if (_producerStart != null) environment["ANTIPHON_STARTUP_SUPERVISOR_START"] = _producerStart;
                }
                // cmd.exe /s /c handles .cmd shims (npm, npx) and the >> redirection in a detached process: /s strips the
                // outer quotes, and stdout and stderr are appended to the log the Aspire tailer reads.
                var inner = argumentList.Count > 0 ? _options.Exe + " " + string.Join(" ", argumentList) : _options.Exe;
                var commandLine = "/s /c \"" + inner + " >> \"" + _options.LogFile + "\" 2>&1\"";
                IDaemonService service = null;
                try
                {
                    WriteStartup("launch-requested");
                    service = _start(new DaemonServiceStart("cmd.exe", commandLine, _options.WorkDir, environment));
                    WriteStartup("wrapper-started", wrapper: service);
                    try
                    {
                        _files.File.WriteAllText(_options.ServicePidFile, Pid(service.Pid) + Environment.NewLine, Utf8);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        // Set-Content's failure did not stop the script: the service runs on.
                        WriteLog("[WRN] Service PID file not written: " + e.Message);
                    }
                    WriteLog("Service PID " + Pid(service.Pid));
                    var code = service.WaitForExit();
                    WriteStartup("service-exit", exitCode: code);
                    WriteLog("Exited (code " + code.ToString(CultureInfo.InvariantCulture) + ")");
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException
                    || e is ArgumentException || e is PlatformNotSupportedException)
                {
                    WriteStartup("launch-error", exitCode: 1);
                    WriteLog("[ERR] Failed to start: " + e.Message);
                }
                finally
                {
                    DeleteServicePid();
                    service?.Dispose();
                }
            }

            // Rolls the log aside when it is over the cap, then prunes old rolls. Only safe between service runs: while a
            // service is up, cmd.exe holds the log for the '>>' redirection. A locked or vanished log never stops the start.
            private void RotateLog()
            {
                if (_options.LogMaxMb <= 0) return;
                var directory = _files.Path.GetDirectoryName(_options.LogFile) ?? "";
                var stem = _files.Path.GetFileNameWithoutExtension(_options.LogFile);
                var extension = _files.Path.GetExtension(_options.LogFile);
                try
                {
                    var info = _files.FileInfo.New(_options.LogFile);
                    if (info.Exists && info.Length > _options.LogMaxMb * 1024L * 1024L)
                    {
                        var stamp = _host.Clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                        var rolled = _files.Path.Combine(directory, stem + "." + stamp + extension);
                        _files.File.Move(_options.LogFile, rolled, true);
                        WriteLog("Rotated log at " + (info.Length / 1048576.0).ToString("N1", CultureInfo.InvariantCulture) + " MB -> " + _files.Path.GetFileName(rolled));
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    WriteLog("[WRN] Log rotation skipped: " + e.Message);
                    return;
                }
                try
                {
                    var roll = new Regex("^" + Regex.Escape(stem) + @"\.\d{8}-\d{6}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    var rolls = _files.Directory.EnumerateFiles(string.IsNullOrEmpty(directory) ? "." : directory, stem + ".*" + extension)
                        .Select(path => _files.FileInfo.New(path))
                        .Where(file => roll.IsMatch(_files.Path.GetFileNameWithoutExtension(file.Name)))
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .ToArray();
                    var cutoff = _host.UtcNow.AddDays(-_options.LogRetainDays);
                    for (var i = 0; i < rolls.Length; i++)
                    {
                        if (i < _options.LogRetainCount && rolls[i].LastWriteTimeUtc >= cutoff) continue;
                        try { rolls[i].Delete(); }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { /* Remove-Item -ErrorAction SilentlyContinue */ }
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    WriteLog("[WRN] Log prune skipped: " + e.Message);
                }
            }

            // Get-DesiredState: the trimmed, lower-cased state; a missing, empty or unreadable file is "running".
            private string DesiredState()
            {
                try
                {
                    var text = _files.File.ReadAllText(_options.StateFile);
                    return text.Length == 0 ? "running" : text.Trim().ToLowerInvariant();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return "running";
                }
            }

            private bool SupervisorGone()
            {
                if (!_watchSupervisor) return false;
                return _supervisorStart is DateTime started
                    ? _host.Processes.StartTimeUtc(_supervisorPid) != started
                    : !_host.Processes.IsAlive(_supervisorPid);
            }

            private int StopForGoneSupervisor()
            {
                WriteStartup("supervisor-stopping");
                WriteLog("Supervisor PID " + Pid(_supervisorPid) + " is gone - stopping.");
                DeleteServicePid();
                return 0;
            }

            private void DeleteServicePid()
            {
                try { _files.File.Delete(_options.ServicePidFile); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { /* Remove-Item -ErrorAction SilentlyContinue */ }
            }

            // One "HH:mm:ss [name] message" line, appended with explicit sharing so a milestone append never denies cmd's
            // stdout open during a launch. Diagnostics must never turn a healthy launch into a failure.
            private void WriteLog(string message) =>
                AppendLine(_host.Clock.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " [" + _options.Name + "] " + message);

            private void AppendLine(string line)
            {
                try
                {
                    using var stream = _files.FileStream.New(_options.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    var bytes = Utf8.GetBytes(line + Environment.NewLine);
                    stream.Write(bytes, 0, bytes.Length);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                {
                    // Optional diagnostics: never alter supervision.
                }
            }

            // These records are optional diagnostics; they must not alter supervision.
            private void WriteStartup(string name, double? durationMs = null, int? exitCode = null, IDaemonService wrapper = null)
            {
                var record = new JsonObject
                {
                    ["version"] = 1,
                    ["event"] = name,
                    ["utc"] = _host.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["producer"] = "supervisor",
                    ["producerPid"] = _supervisorPid,
                    ["producerStartTimeUtc"] = _producerStart,
                    ["attemptId"] = _attempt,
                    ["elapsedMs"] = durationMs,
                    ["exitCode"] = exitCode,
                    ["wrapperPid"] = wrapper?.Pid,
                    ["wrapperStartTimeUtc"] = wrapper?.StartTimeUtc?.ToString("o", CultureInfo.InvariantCulture),
                };
                WriteLog("ANTIPHON_STARTUP " + record.ToJsonString(Compact));
            }

            private static string Pid(int pid) => pid.ToString(CultureInfo.InvariantCulture);
        }
    }
}
