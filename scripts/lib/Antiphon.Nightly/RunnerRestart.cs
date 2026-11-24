// CARD-0420, CARD-0604: the desktop session runner's restart and its health wait (unit 30 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). It replaced scripts/restart-session-runner.ps1 and its
// library scripts/session-runner-restart-health.ps1 (Invoke-RunnerRestart, New-RunnerMilestoneReader and
// Read-RunnerMilestones; deleted 2026-11-24). The CLI runs it as
//
//   antiphon-scripts restart-session-runner [--hard] [--kill-sessions] [--wait-only] [--timeout-sec 180]
//
// The supervisor delay, the incremental build, the launch and the complete adoption precede HTTP; detached sessions
// survive. A restart writes the desired state, stops the recorded service wrapper and the runners at this checkout's Debug
// executable path (never an arbitrary port 17204 listener: another build path is left alone even with --hard) and, with
// --hard or when no supervisor is recorded, stops the supervisor, deletes the PID files and starts the logon task.
// --wait-only makes no desired-state write, process stop, PID deletion or Scheduled Task call. Then it waits, at most
// --timeout-sec seconds (positive; default 180), for a completed HTTP 200 from /health whose port owner is this checkout's
// runner, reading the startup milestones (ANTIPHON_STARTUP records) from logs/session-runner.log meanwhile. The last stdout
// line is "RUNNER RESTART RESULT: <json>". Exit 0 healthy; 2 wait-expired; 1 action-failed or wait-failed.
//
// Every observation of the machine is IRunnerRestartPlatform's: RunnerRestartOs on this Windows machine, a fake in tests.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
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
    /// <summary>A process as the restart identifies it: its id, its start (round-trip UTC text) and its executable path (null when unreadable).</summary>
    public sealed record RunnerProcessIdentity(int Pid, string StartTimeUtc, string Path)
    {
        /// <summary>The result record's form: {"pid","startTimeUtc","path"}.</summary>
        public JsonObject ToJson() => new JsonObject { ["pid"] = Pid, ["startTimeUtc"] = StartTimeUtc, ["path"] = Path };
    }

    /// <summary>One /health probe: the HTTP status (0 when no answer arrived in time) and its result text.</summary>
    public sealed record RunnerProbeResult(int Status, string Result);

    /// <summary>The recorded supervisor and service wrapper: null when there is no PID file or no process with its id.</summary>
    public sealed record RunnerCensus(RunnerProcessIdentity Supervisor, RunnerProcessIdentity Wrapper);

    /// <summary>
    /// Everything the restart observes or changes (New-RunnerRestartPlatform's closures). A member may throw: before the
    /// wait that is an action failure, during it a wait failure; either way the result line is still written.
    /// </summary>
    public interface IRunnerRestartPlatform
    {
        /// <summary>The monotonic clock's value at command entry, when the clock started before the restart (production: 0); null: the restart's own first reading.</summary>
        double? CommandOrigin { get; }

        /// <summary>The monotonic clock, in milliseconds.</summary>
        double Now();

        /// <summary>The wall clock, round-trip UTC text.</summary>
        string Utc();

        /// <summary>Waits the milliseconds.</summary>
        void Sleep(double milliseconds);

        /// <summary>GET /health within the budget (milliseconds).</summary>
        RunnerProbeResult Probe(double budgetMilliseconds);

        /// <summary>GET /capabilities: the parsed answer, or null when there is none.</summary>
        JsonNode ProbeBuild();

        /// <summary>The process that listens on the runner port, or null.</summary>
        RunnerProcessIdentity Identity();

        /// <summary>True when the identity is this checkout's runner and still the same process.</summary>
        bool Verify(RunnerProcessIdentity identity);

        /// <summary>The recorded supervisor and wrapper. Throws for a PID file that cannot be read or is not a positive integer.</summary>
        RunnerCensus Census();

        /// <summary>The live process with the id, or null.</summary>
        RunnerProcessIdentity Process(int pid);

        /// <summary>One control operation (RunnerRestartOperations); identity is the supervisor for stop-supervisor, else null.</summary>
        void Control(string operation, RunnerProcessIdentity identity);

        /// <summary>The runner's capture log, where the startup milestones are read.</summary>
        string LogPath { get; }

        /// <summary>The file system the milestone reader reads LogPath through.</summary>
        IFileSystem Files { get; }
    }

    /// <summary>The control operations, in the order a restart runs them.</summary>
    public static class RunnerRestartOperations
    {
        /// <summary>Desired state running (logs/session-runner.state).</summary>
        public const string WriteState = "write-state";

        /// <summary>POST /sessions/kill-all, then stop every Antiphon.PtyHost (--kill-sessions).</summary>
        public const string KillSessions = "kill-sessions";

        /// <summary>Stop the recorded wrapper and every runner at this checkout's Debug executable path.</summary>
        public const string StopService = "stop-service";

        /// <summary>Stop the recorded supervisor (--hard).</summary>
        public const string StopSupervisor = "stop-supervisor";

        /// <summary>Delete the service and supervisor PID files.</summary>
        public const string DeletePids = "delete-pids";

        /// <summary>Start the logon task "Antiphon Session Runner".</summary>
        public const string StartTask = "start-task";
    }

    /// <summary>The restart-session-runner command's options (--hard --kill-sessions --wait-only --timeout-sec).</summary>
    public sealed class RunnerRestartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "restart-session-runner [--hard] [--kill-sessions] [--wait-only] [--timeout-sec 180]";

        /// <summary>Refresh the supervisor for a planned deployment (required for the first restart after SHA stamp activation).</summary>
        public bool Hard { get; init; }

        /// <summary>Also kill detached sessions (human-only).</summary>
        public bool KillSessions { get; init; }

        /// <summary>Observe only: no desired-state write, process stop, PID deletion or Scheduled Task call.</summary>
        public bool WaitOnly { get; init; }

        /// <summary>The observation budget after the restart actions, in seconds; must be positive.</summary>
        public int TimeoutSec { get; init; } = 180;

        /// <summary>The checkout whose runner restarts (its logs and Debug executable), and whose toolchain floor is read.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI. A non-integer --timeout-sec is a NightlyUsageException.</summary>
        public static RunnerRestartOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--timeout-sec" }, new[] { "--hard", "--kill-sessions", "--wait-only" });
            return new RunnerRestartOptions
            {
                Hard = flags.Switch("--hard"),
                KillSessions = flags.Switch("--kill-sessions"),
                WaitOnly = flags.Switch("--wait-only"),
                TimeoutSec = flags.Int("--timeout-sec", 180),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>One restart's record: the final JSON line's fields, in its order.</summary>
    public sealed class RunnerRestartResult
    {
        /// <summary>healthy, wait-expired, action-failed or wait-failed.</summary>
        public string Outcome { get; set; } = "action-failed";

        /// <summary>restart or wait-only.</summary>
        public string Mode { get; set; } = "restart";

        /// <summary>0 healthy, 2 wait-expired, 1 otherwise.</summary>
        public int ExitCode { get; set; } = 1;

        /// <summary>The budget asked for.</summary>
        public int TimeoutSec { get; set; }

        /// <summary>The wait's length (null when the clock failed).</summary>
        public double? WaitElapsedMs { get; set; } = 0;

        /// <summary>The command's length (null when the clock failed).</summary>
        public double? CommandElapsedMs { get; set; } = 0;

        /// <summary>When the result was taken.</summary>
        public string ObservedAtUtc { get; set; }

        /// <summary>The last trusted milestone's event, or unknown.</summary>
        public string LastObservedPhase { get; set; } = "unknown";

        /// <summary>That milestone's age.</summary>
        public double? PhaseAgeMs { get; set; }

        /// <summary>The current startup attempt.</summary>
        public string AttemptId { get; set; }

        /// <summary>The recorded (or observed) supervisor.</summary>
        public RunnerProcessIdentity Supervisor { get; set; }

        /// <summary>The recorded service wrapper.</summary>
        public RunnerProcessIdentity Wrapper { get; set; }

        /// <summary>The verified (or observed) runner.</summary>
        public RunnerProcessIdentity Runner { get; set; }

        /// <summary>When the healthy 200 was observed.</summary>
        public string FirstHealth200ObservedAtUtc { get; set; }

        /// <summary>The last probe's result.</summary>
        public string LastProbeResult { get; set; }

        /// <summary>The current attempt's non-zero exit code.</summary>
        public int? ErrorCode { get; set; }

        /// <summary>The operation that failed (health-wait for a wait failure).</summary>
        public string Operation { get; set; }

        /// <summary>The failure's message.</summary>
        public string Error { get; set; }

        /// <summary>When the command started.</summary>
        public string CommandEnteredAtUtc { get; set; }

        /// <summary>Whether the identified supervisor is still the same live process (null: unknown).</summary>
        public bool? SupervisorAlive { get; set; }

        /// <summary>Whether the identified runner is still the same live process (null: unknown).</summary>
        public bool? RunnerAlive { get; set; }

        /// <summary>The last observed port owner.</summary>
        public RunnerProcessIdentity PortOwner { get; set; }

        /// <summary>When the relaunch was requested (restart mode).</summary>
        public string RelaunchRequestedAtUtc { get; set; }

        /// <summary>When the wait started.</summary>
        public string WaitStartedAtUtc { get; set; }

        /// <summary>The poll interval's cap.</summary>
        public int PollingIntervalMs { get; set; } = 2000;

        /// <summary>The record as a JSON object, in the result line's key order.</summary>
        public JsonObject ToJson() => new JsonObject
        {
            ["outcome"] = Outcome,
            ["mode"] = Mode,
            ["exitCode"] = ExitCode,
            ["timeoutSec"] = TimeoutSec,
            ["waitElapsedMs"] = WaitElapsedMs,
            ["commandElapsedMs"] = CommandElapsedMs,
            ["observedAtUtc"] = ObservedAtUtc,
            ["lastObservedPhase"] = LastObservedPhase,
            ["phaseAgeMs"] = PhaseAgeMs,
            ["attemptId"] = AttemptId,
            ["supervisor"] = Supervisor?.ToJson(),
            ["wrapper"] = Wrapper?.ToJson(),
            ["runner"] = Runner?.ToJson(),
            ["firstHealth200ObservedAtUtc"] = FirstHealth200ObservedAtUtc,
            ["lastProbeResult"] = LastProbeResult,
            ["errorCode"] = ErrorCode,
            ["operation"] = Operation,
            ["error"] = Error,
            ["commandEnteredAtUtc"] = CommandEnteredAtUtc,
            ["supervisorAlive"] = SupervisorAlive,
            ["runnerAlive"] = RunnerAlive,
            ["portOwner"] = PortOwner?.ToJson(),
            ["relaunchRequestedAtUtc"] = RelaunchRequestedAtUtc,
            ["waitStartedAtUtc"] = WaitStartedAtUtc,
            ["pollingIntervalMs"] = PollingIntervalMs,
        };

        /// <summary>The compact JSON text of the result line.</summary>
        public string ToJsonText() => ToJson().ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>One trusted startup milestone (an ANTIPHON_STARTUP record).</summary>
    public sealed record RunnerMilestone(string Event, DateTime Utc, string Producer, string AttemptId, int? ExitCode);

    /// <summary>
    /// New-RunnerMilestoneReader and Read-RunnerMilestones: bounded byte reads of the capture log that keep an incomplete
    /// UTF-8 line (and its JSON) until its newline arrives. A record counts only when its producer is the live process it
    /// names (same start), is this checkout's runner or the recorded supervisor, belongs to the current attempt (a new
    /// attempt starts at build-start, launch-requested or managed-entry; retired attempts never return) and is not older
    /// than the last one. A truncated, replaced or rotated log, a gap over 256 KiB or a line over 8 KiB clears the phase.
    /// </summary>
    public sealed class RunnerMilestoneReader
    {
        /// <summary>The most bytes one read takes, and the tail kept after a reset.</summary>
        public const int WindowBytes = 262144;

        /// <summary>The longest line read.</summary>
        public const int LineBytes = 8192;

        private const int AnchorBytes = 128;
        private const int RetiredKept = 32;
        private const string Marker = "ANTIPHON_STARTUP ";
        private static readonly string[] AttemptStarts = { "build-start", "launch-requested", "managed-entry" };

        private readonly IFileSystem _files;
        private readonly List<string> _retired = new List<string>();
        private long _offset;
        private long? _creation;
        private byte[] _pending = Array.Empty<byte>();
        private bool _initial = true;
        private bool _discardFirst;
        private string _anchor;
        private DateTime? _lastUtc;

        /// <summary>A reader of the log at path.</summary>
        public RunnerMilestoneReader(string path, IFileSystem files)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            _files = files ?? throw new ArgumentNullException(nameof(files));
        }

        /// <summary>The log read.</summary>
        public string Path { get; }

        /// <summary>The last trusted milestone, or null.</summary>
        public RunnerMilestone Phase { get; private set; }

        /// <summary>The current attempt.</summary>
        public string Attempt { get; private set; }

        /// <summary>The current attempt's last non-zero exit code.</summary>
        public int? ErrorCode { get; private set; }

        /// <summary>The runner a trusted record named.</summary>
        public RunnerProcessIdentity RunnerIdentity { get; private set; }

        /// <summary>The supervisor a trusted record named.</summary>
        public RunnerProcessIdentity SupervisorIdentity { get; private set; }

        /// <summary>Reads what the log gained since the last read; records older than notBeforeUtc are ignored. Never throws.</summary>
        public void Read(IRunnerRestartPlatform platform, DateTime notBeforeUtc)
        {
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            Stream stream = null;
            try
            {
                var info = _files.FileInfo.New(Path);
                if (!info.Exists) throw new FileNotFoundException("the startup log is absent", Path);
                stream = _files.File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var creation = info.CreationTimeUtc.Ticks;
                var reset = _initial || _creation != creation || stream.Length < _offset;
                if (!reset && _anchor != null)
                {
                    var size = (int)Math.Min(AnchorBytes, _offset);
                    stream.Seek(_offset - size, SeekOrigin.Begin);
                    var anchor = new byte[size];
                    var read = stream.ReadAtLeast(anchor, size, throwOnEndOfStream: false);
                    reset = read != size || Convert.ToBase64String(anchor) != _anchor;
                }
                if (reset || stream.Length - _offset > WindowBytes)
                {
                    Phase = null;
                    Attempt = null;
                    ErrorCode = null;
                    _pending = Array.Empty<byte>();
                    _lastUtc = null;
                    _retired.Clear();
                    _offset = Math.Max(0, stream.Length - WindowBytes);
                    _discardFirst = _offset > 0;
                }
                _initial = false;
                _creation = creation;
                stream.Seek(_offset, SeekOrigin.Begin);
                var bytes = new byte[(int)Math.Min(WindowBytes, stream.Length - _offset)];
                var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
                _offset += count;
                if (count == 0) return;
                var tail = (int)Math.Min(AnchorBytes, _offset);
                stream.Seek(_offset - tail, SeekOrigin.Begin);
                var mark = new byte[tail];
                stream.ReadAtLeast(mark, tail, throwOnEndOfStream: false);
                _anchor = Convert.ToBase64String(mark);
                var all = new byte[_pending.Length + count];
                Buffer.BlockCopy(_pending, 0, all, 0, _pending.Length);
                Buffer.BlockCopy(bytes, 0, all, _pending.Length, count);
                var start = 0;
                for (var i = 0; i < all.Length; i++)
                {
                    if (all[i] != 10) continue;
                    var length = i - start;
                    if (_discardFirst)
                    {
                        _discardFirst = false;
                        start = i + 1;
                        continue;
                    }
                    if (length > LineBytes)
                    {
                        Phase = null;
                        start = i + 1;
                        continue;
                    }
                    var line = Encoding.UTF8.GetString(all, start, length);
                    start = i + 1;
                    var marker = line.IndexOf(Marker, StringComparison.Ordinal);
                    if (marker < 0) continue;
                    Consider(line.Substring(marker + Marker.Length), platform, notBeforeUtc);
                }
                _pending = start < all.Length ? all.AsSpan(start).ToArray() : Array.Empty<byte>();
                if (_pending.Length > LineBytes)
                {
                    _pending = Array.Empty<byte>();
                    _discardFirst = true;
                    Phase = null;
                }
            }
            catch (Exception)
            {
                // Any failure, the log's or the platform's, is an unknown phase; the next read starts over. The reader never
                // fails the wait.
                Phase = null;
                _pending = Array.Empty<byte>();
                _initial = true;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private void Consider(string text, IRunnerRestartPlatform platform, DateTime notBeforeUtc)
        {
            JsonObject record;
            try
            {
                record = JsonNode.Parse(text) as JsonObject;
            }
            catch (JsonException)
            {
                Phase = null;
                return;
            }
            if (record == null || !IsOne(record["version"]))
            {
                Phase = null;
                return;
            }
            var current = platform.Process(Int(record["producerPid"]) ?? 0);
            if (current == null) return;
            if (!RunnerRestart.TryUtc(current.StartTimeUtc, out var currentStart) || !RunnerRestart.TryUtc(Str(record["producerStartTimeUtc"]), out var recordStart)
                || currentStart != recordStart) return;
            var attempt = Str(record["attemptId"]);
            if (string.IsNullOrEmpty(attempt) || _retired.Contains(attempt, StringComparer.OrdinalIgnoreCase)) return;
            if (!RunnerRestart.TryUtc(Str(record["utc"]), out var utc) || utc < notBeforeUtc) return;
            var supervisor = platform.Census()?.Supervisor;
            var producer = Str(record["producer"]);
            var trusted = (Same(producer, "supervisor") && supervisor != null && supervisor.Pid == current.Pid && Same(supervisor.StartTimeUtc, current.StartTimeUtc))
                || (Same(producer, "runner") && platform.Verify(current));
            if (!trusted) return;
            if (_lastUtc != null && utc < _lastUtc) return;
            if (Same(producer, "runner")) RunnerIdentity = current;
            else SupervisorIdentity = current;
            var name = Str(record["event"]);
            if (!Same(Attempt, attempt))
            {
                if (!AttemptStarts.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrEmpty(Attempt))
                {
                    _retired.Add(Attempt);
                    if (_retired.Count > RetiredKept) _retired.RemoveRange(0, _retired.Count - RetiredKept);
                }
                Attempt = attempt;
                ErrorCode = null;
            }
            _lastUtc = utc;
            var exitCode = Int(record["exitCode"]);
            Phase = new RunnerMilestone(name, utc, producer, attempt, exitCode);
            if (exitCode != null && exitCode != 0) ErrorCode = exitCode;
        }

        // PowerShell's -eq on strings: ordinal, ignoring case; two nulls are equal.
        private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase) && (left == null) == (right == null);

        private static bool IsOne(JsonNode node)
        {
            if (node is not JsonValue value) return false;
            if (value.TryGetValue<string>(out var text)) return text == "1";
            return Int(node) == 1;
        }

        private static int? Int(JsonNode node)
        {
            if (node is not JsonValue value) return null;
            if (value.TryGetValue<int>(out var number)) return number;
            if (value.TryGetValue<long>(out var wide) && wide >= int.MinValue && wide <= int.MaxValue) return (int)wide;
            if (value.TryGetValue<double>(out var real) && real == Math.Floor(real) && real >= int.MinValue && real <= int.MaxValue) return (int)real;
            return null;
        }

        private static string Str(JsonNode node)
        {
            if (node is not JsonValue value) return node?.ToJsonString();
            return value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
        }
    }

    /// <summary>A restart refused before any control operation (the argument check).</summary>
    public sealed class RunnerRestartRefusedException : Exception
    {
        /// <summary>The refusal and why.</summary>
        public RunnerRestartRefusedException(string message) : base(message) { }
    }

    /// <summary>The restart (Invoke-RunnerRestart) and the command around it.</summary>
    public static class RunnerRestart
    {
        /// <summary>The prefix of the final stdout line.</summary>
        public const string ResultPrefix = "RUNNER RESTART RESULT: ";

        /// <summary>The command an expired wait names to keep observing without another restart.</summary>
        public const string ContinueCommand = "scripts/antiphon restart-session-runner --wait-only --timeout-sec 180";

        private const double PollMs = 2000;
        private const double ProbeCapMs = 5000;
        private const double ProgressMs = 15000;
        private static readonly Regex FullSha = new Regex(@"\A[0-9a-f]{40}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The restart-session-runner command: the toolchain floor warnings (CARD-1698: read-only setup, warn and never refuse),
        /// the restart, then "RUNNER RESTART RESULT: &lt;json&gt;" as the last line on host.Out. Returns the result's exit code.
        /// </summary>
        public static int Run(RunnerRestartOptions options, NightlyHost host, IRunnerRestartPlatform platform)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            var entered = host.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            ToolchainFloor.Run(new ToolchainFloorOptions { RepoRoot = options.RepoRoot }, host);
            var result = Restart(options, platform, host.Out, entered);
            host.Out.WriteLine(ResultPrefix + result.ToJsonText());
            return result.ExitCode;
        }

        /// <summary>
        /// Invoke-RunnerRestart: the arguments are checked, the census read and (unless --wait-only) the control operations
        /// run, then the health wait. Progress goes to output. A failure of the platform before the wait is action-failed
        /// (operation names the step), during it wait-failed (operation health-wait); either way a result is returned.
        /// commandEnteredAtUtc null: the platform's wall clock now.
        /// </summary>
        public static RunnerRestartResult Restart(RunnerRestartOptions options, IRunnerRestartPlatform platform, TextWriter output, string commandEnteredAtUtc = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            if (output == null) throw new ArgumentNullException(nameof(output));
            var entry = platform.Now();
            if (platform.CommandOrigin is double origin) entry = origin;
            if (string.IsNullOrEmpty(commandEnteredAtUtc)) commandEnteredAtUtc = platform.Utc();
            var result = new RunnerRestartResult
            {
                Mode = options.WaitOnly ? "wait-only" : "restart",
                TimeoutSec = options.TimeoutSec,
                CommandEnteredAtUtc = commandEnteredAtUtc,
            };
            double? waitStart = null;
            try
            {
                result.Operation = "validate-arguments";
                if (options.TimeoutSec <= 0 || (options.WaitOnly && (options.Hard || options.KillSessions)))
                    throw new RunnerRestartRefusedException("--timeout-sec must be positive; --wait-only cannot be combined with --hard or --kill-sessions");
                var census = platform.Census();
                result.Supervisor = census?.Supervisor;
                result.Wrapper = census?.Wrapper;
                var reader = new RunnerMilestoneReader(platform.LogPath, platform.Files);
                var notBefore = options.WaitOnly ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) : ParseUtc(platform.Utc());
                if (!options.WaitOnly)
                {
                    Control(platform, result, RunnerRestartOperations.WriteState, null);
                    if (options.KillSessions) Control(platform, result, RunnerRestartOperations.KillSessions, null);
                    Control(platform, result, RunnerRestartOperations.StopService, null);
                    if (options.Hard || census?.Supervisor == null)
                    {
                        if (options.Hard && census?.Supervisor != null) Control(platform, result, RunnerRestartOperations.StopSupervisor, census.Supervisor);
                        Control(platform, result, RunnerRestartOperations.DeletePids, null);
                        Control(platform, result, RunnerRestartOperations.StartTask, null);
                    }
                    result.RelaunchRequestedAtUtc = platform.Utc();
                }
                result.Operation = null;
                waitStart = platform.Now();
                var started = waitStart.Value;
                result.WaitStartedAtUtc = platform.Utc();
                output.WriteLine("Health wait started at " + result.WaitStartedAtUtc + "; budget " + Text(options.TimeoutSec) + "s, poll interval up to 2000 ms.");
                var budget = options.TimeoutSec * 1000.0;
                var lastMessage = started;
                var lastPhase = "unknown";
                result.Outcome = "wait-expired";
                result.ExitCode = 2;
                while (true)
                {
                    var remaining = budget - (platform.Now() - started);
                    if (remaining <= 0) break;
                    reader.Read(platform, notBefore);
                    var phase = reader.Phase?.Event ?? "unknown";
                    var now = platform.Now();
                    if (phase != lastPhase)
                    {
                        output.WriteLine("Last observed phase: " + phase + " at " + platform.Utc());
                        lastPhase = phase;
                        lastMessage = now;
                    }
                    else if (now - lastMessage >= ProgressMs)
                    {
                        output.WriteLine("Continuing health wait; last observed phase: " + phase);
                        lastMessage = now;
                    }
                    remaining = budget - (platform.Now() - started);
                    if (remaining <= 0) break;
                    var probe = platform.Probe(Math.Min(ProbeCapMs, remaining));
                    result.LastProbeResult = probe.Result;
                    var completed = platform.Now();
                    var observed = platform.Utc();
                    if (probe.Status == 200)
                    {
                        var identity = platform.Identity();
                        result.PortOwner = identity;
                        var verified = platform.Verify(identity);
                        completed = platform.Now();
                        if (verified) result.Runner = identity;
                        if (verified && completed - started <= budget)
                        {
                            result.Runner = identity;
                            result.FirstHealth200ObservedAtUtc = observed;
                            result.Outcome = "healthy";
                            result.ExitCode = 0;
                            break;
                        }
                        result.LastProbeResult = verified ? "http-200-late" : "http-200-identity-unconfirmed";
                    }
                    remaining = budget - (platform.Now() - started);
                    if (remaining > 0) platform.Sleep(Math.Min(PollMs, remaining));
                }
                result.WaitElapsedMs = Math.Round(platform.Now() - started, 3);
                result.LastObservedPhase = reader.Phase?.Event ?? "unknown";
                if (reader.Phase != null) result.PhaseAgeMs = Math.Max(0, (ParseUtc(platform.Utc()) - reader.Phase.Utc).TotalMilliseconds);
                result.AttemptId = reader.Attempt;
                result.ErrorCode = reader.ErrorCode;
                try
                {
                    var after = platform.Census();
                    result.Supervisor = after?.Supervisor;
                    result.Wrapper = after?.Wrapper;
                }
                catch (Exception)
                {
                    // The census after the wait is advisory: the wait's outcome stands.
                }
                result.Supervisor ??= reader.SupervisorIdentity;
                result.Runner ??= reader.RunnerIdentity;
                result.SupervisorAlive = Alive(platform, result.Supervisor);
                result.RunnerAlive = Alive(platform, result.Runner);
                if (result.Outcome == "healthy")
                {
                    output.WriteLine("healthy: first completed HTTP 200 at " + result.FirstHealth200ObservedAtUtc + "; wait " + Text(result.WaitElapsedMs) + " ms.");
                }
                else
                {
                    try { result.PortOwner = platform.Identity(); }
                    catch (Exception) { /* The last observation stands. */ }
                    output.WriteLine("Health wait expired; runner readiness is unconfirmed. Startup may still be in progress.");
                    output.WriteLine("wait-expired: the command stopped waiting and has not stopped background startup.");
                    output.WriteLine("Last observed phase: " + result.LastObservedPhase + "; age ms: " + Text(result.PhaseAgeMs) + "; probe: " + result.LastProbeResult + ".");
                    output.WriteLine("Identified supervisor alive: " + Text(result.SupervisorAlive) + "; runner alive: " + Text(result.RunnerAlive) + " (blank means unknown).");
                    if (result.PortOwner != null)
                    {
                        output.WriteLine("Last observed port 17204 owner: PID " + Text(result.PortOwner.Pid) + "; path: " + result.PortOwner.Path + ".");
                        output.WriteLine("A runner at another executable path is not stopped, even with --hard; establish its ownership before stopping it.");
                    }
                    output.WriteLine("Continue: " + ContinueCommand);
                    output.WriteLine("Inspect: Get-Content '" + platform.LogPath + "' -Tail 30; also dated logs under %TEMP%\\antiphon-logs\\session-runner-*.log.");
                }
            }
            catch (Exception e)
            {
                // Clock or observation failures must not suppress the stable final result line.
                result.Error = e.Message;
                if (waitStart is double begun)
                {
                    result.Outcome = "wait-failed";
                    result.ExitCode = 1;
                    result.Operation = "health-wait";
                    try { result.WaitElapsedMs = Math.Round(platform.Now() - begun, 3); }
                    catch (Exception) { result.WaitElapsedMs = null; }
                    output.WriteLine("wait-failed: observation failed: " + result.Error + ". Background startup has not been stopped.");
                }
                else
                {
                    output.WriteLine("action-failed: " + result.Operation + ": " + result.Error);
                }
            }
            try { result.ObservedAtUtc = platform.Utc(); }
            catch (Exception) { /* Left null. */ }
            try { result.CommandElapsedMs = Math.Round(platform.Now() - entry, 3); }
            catch (Exception) { result.CommandElapsedMs = null; }
            if (result.Outcome == "healthy" && !options.Hard) ShaStampHint(platform, result, output);
            return result;
        }

        private static void Control(IRunnerRestartPlatform platform, RunnerRestartResult result, string operation, RunnerProcessIdentity identity)
        {
            result.Operation = operation;
            platform.Control(operation, identity);
        }

        // Whether the identified process is still the same live process; null when there is none or the check failed.
        private static bool? Alive(IRunnerRestartPlatform platform, RunnerProcessIdentity identity)
        {
            if (identity == null) return null;
            try
            {
                var live = platform.Process(identity.Pid);
                return live != null && string.Equals(live.StartTimeUtc, identity.StartTimeUtc, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The deploy build of the run-daemon command is expected to carry a full SHA. An unavailable advisory leaves the
        // observed result intact.
        private static void ShaStampHint(IRunnerRestartPlatform platform, RunnerRestartResult result, TextWriter output)
        {
            try
            {
                if (platform.ProbeBuild()?["build"] is not JsonObject build) return;
                var sha = build["commitSha"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : build["commitSha"]?.ToJsonString() ?? "";
                if (FullSha.IsMatch(sha)) return;
                var owner = platform.Identity();
                if (owner != null && result.Runner != null && owner.Pid == result.Runner.Pid && string.Equals(owner.StartTimeUtc, result.Runner.StartTimeUtc, StringComparison.OrdinalIgnoreCase))
                    output.WriteLine("WARNING: first restart after the SHA stamp activation: re-run with --hard");
            }
            catch (Exception)
            {
                // Advisory only.
            }
        }

        /// <summary>A round-trip (or any invariant) date text as UTC; text without a zone is local time, as [DateTime] read it.</summary>
        public static bool TryUtc(string text, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)) return false;
            utc = parsed.UtcDateTime;
            return true;
        }

        private static DateTime ParseUtc(string text)
        {
            if (!TryUtc(text, out var utc)) throw new FormatException("not a date: " + text);
            return utc;
        }

        // Write-Host's text of a value: empty for null, True/False, invariant numbers.
        private static string Text(object value) => value switch
        {
            null => "",
            bool flag => flag ? "True" : "False",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }
}
