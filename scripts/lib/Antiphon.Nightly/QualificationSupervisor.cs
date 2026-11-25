// CARD-1667: the rc qualification supervisor. It replaced scripts/lib/qualification-supervisor.ps1 (the line and record
// writers, Save-QualificationOwnRunState, Get-QualificationSupervisionClassification, Get-QualificationSupervision) and the
// entries scripts/qualification/observe.ps1, start-supervisor.ps1 and supervise-rc.ps1, deleted 2026-11-23 (unit 7 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The CLI runs them as `antiphon-scripts
// qualification-observe`, `qualification-start-supervisor` and `qualification-supervise` (the detached child).
//
// The supervisor only observes and records: nothing here stops, signals or restarts a process or clears a lock, and its
// death does not end the run (last-run.json completedAt is the end signal). Every verdict is bound to the launch's run id
// (supervisor-identity.json): the state root is shared by every nightly and rc run, so another run's last-run.json or
// run.lock is never credited (Review b26811ff D-1); own-last-run.json, the supervisor's copy of last-run.json while it named
// the bound run id, stands in for it once the next run has overwritten it (Review 9f29e134 D-1). Records are read through
// RcQualificationRecords (FileShare.Delete: Review 230d1f5d A-1). Log lines are opened, appended, flushed to disk and closed
// one at a time (a 1> wrapper buffer lost its tail when the wrapper died: 2026-11-02, run 7f8e9fd1). Process observation is
// the host's process table; the detached start, the console and session and the heartbeat's process table are
// host.Qualification. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The supervisor's own files: the flushed log line and the replaced record.</summary>
    public static class QualificationFiles
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Write-QualificationSupervisorLine: "&lt;utc yyyy-MM-ddTHH:mm:ss.fffZ&gt; &lt;text&gt;\r\n", appended (FileShare.ReadWrite), flushed to
        /// disk and closed before the call returns.
        /// </summary>
        public static void WriteLine(IFileSystem files, string path, string text, DateTime utcNow)
        {
            var line = utcNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + " " + text + "\r\n";
            var bytes = Utf8NoBom.GetBytes(line);
            using var stream = files.FileStream.New(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        /// <summary>Write-QualificationSupervisorRecord: the text to path + ".tmp" (UTF-8, no byte-order mark), the old file deleted, the temp moved in.</summary>
        public static void WriteRecord(IFileSystem files, string path, string text)
        {
            var temp = path + ".tmp";
            files.File.WriteAllText(temp, text, Utf8NoBom);
            if (files.File.Exists(path)) files.File.Delete(path);
            files.File.Move(temp, path);
        }
    }

    /// <summary>A supervision verdict: completed, running, supervisor-lost-run-alive, run-lost, not-started, launch-failed or unknown, and why.</summary>
    public sealed record QualificationVerdict(string Classification, string Reason);

    /// <summary>Get-QualificationSupervisionClassification's inputs.</summary>
    public sealed class QualificationClassificationInput
    {
        public string ExpectedRunId { get; init; }
        public string StateStatus { get; init; } = "missing";
        public JsonElement? State { get; init; }
        public string LockStatus { get; init; } = "missing";
        public string LockRunId { get; init; }
        public string RunOwner { get; init; } = "unknown";
        public string Supervisor { get; init; } = "unknown";
        public string Heartbeat { get; init; } = "missing";
        public DateTime? HeartbeatUtc { get; init; }
        public DateTime NowUtc { get; init; }
        public string CommissionExit { get; init; }
        public double StaleMinutes { get; init; } = 5;
        public string CopyStatus { get; init; } = "missing";
        public JsonElement? Copy { get; init; }
        public DateTime? LaunchStartedUtc { get; init; }
    }

    /// <summary>The supervision classification, the observation (observe) and the supervisor's copy of its run state.</summary>
    public static class QualificationSupervision
    {
        /// <summary>Get-QualificationHostPlatform: Windows on a Windows host (the host probe), Unix otherwise.</summary>
        public static string Platform(NightlyHost host) => host.Probe.Platform() == "windows" ? "Windows" : "Unix";

        /// <summary>The host's qualification seams; InvalidOperationException for a host that binds none.</summary>
        public static IQualificationHost Seams(NightlyHost host) =>
            host.Qualification ?? throw new InvalidOperationException("the host binds no qualification seams (NightlyHost.Qualification)");

        /// <summary>The owner observation of a record on this host's process table (the platform's start-time tolerance).</summary>
        public static RcQualificationObservation Observe(JsonElement? record, NightlyHost host) =>
            RcQualificationOwner.Observe(record, host.Processes, RcQualificationOwner.ToleranceMilliseconds(host.Probe.Platform()));

        /// <summary>
        /// The classification. completedAt for the bound run id in last-run.json (else in the copy) is the end signal;
        /// commission-exit.txt and the heartbeat belong to the supervisor, which can die while nightly-run keeps going.
        /// completed needs the run id in last-run.json or the copy; running and supervisor-lost-run-alive need it in run.lock
        /// and in last-run.json or the copy. A run id seen in neither is not-started, or launch-failed once commission-exit.txt
        /// is written, unless last-run.json was superseded (a copy of ours exists, or it names a run that started at or after
        /// the launch child, or gives no start): unknown state-superseded, because "never seen" cannot be told from "seen
        /// then overwritten".
        /// </summary>
        public static QualificationVerdict Classify(QualificationClassificationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var expected = input.ExpectedRunId;
            if (string.IsNullOrWhiteSpace(expected)) return new QualificationVerdict("unknown", "run-id-unbound");
            var stateOurs = input.StateStatus == "ok" && input.State != null && RcQualificationRecords.Text(RcQualificationRecords.Member(input.State, "runId")) == expected;
            var lockOurs = input.LockStatus == "ok" && input.LockRunId == expected;
            var copyOurs = input.CopyStatus == "ok" && input.Copy != null && RcQualificationRecords.Text(RcQualificationRecords.Member(input.Copy, "runId")) == expected;
            if (stateOurs && RcQualificationRecords.Time(RcQualificationRecords.Member(input.State, "completedAt")) != null)
                return new QualificationVerdict("completed", "state-completedAt");
            if (!stateOurs && copyOurs && RcQualificationRecords.Time(RcQualificationRecords.Member(input.Copy, "completedAt")) != null)
                return new QualificationVerdict("completed", "state-copy-completedAt");
            if (input.StateStatus != "ok" && input.StateStatus != "missing") return new QualificationVerdict("unknown", "state-" + input.StateStatus);
            if (input.LockStatus != "ok" && input.LockStatus != "missing") return new QualificationVerdict("unknown", "lock-" + input.LockStatus);
            if (!stateOurs && !lockOurs)
            {
                var superseded = copyOurs;
                if (!superseded && input.StateStatus == "ok" && input.State != null && input.LaunchStartedUtc != null)
                {
                    var otherStart = RcQualificationRecords.Time(RcQualificationRecords.Member(input.State, "startedAt"));
                    superseded = otherStart == null || otherStart.Value >= input.LaunchStartedUtc.Value;
                }
                if (superseded) return new QualificationVerdict("unknown", "state-superseded");
                if (!string.IsNullOrEmpty(input.CommissionExit)) return new QualificationVerdict("launch-failed", "commission-exit-" + input.CommissionExit);
                return new QualificationVerdict("not-started", "run-id-not-seen");
            }
            // Our run started. Only our own run.lock says whether its nightly-run is still alive; a released (error path) or
            // foreign lock without completedAt is unknown, not run-lost.
            if (!lockOurs) return new QualificationVerdict("unknown", input.LockStatus == "ok" ? "run-lock-foreign" : "run-lock-missing");
            if (input.RunOwner == "absent" || input.RunOwner == "reused") return new QualificationVerdict("run-lost", "run-owner-" + input.RunOwner);
            if (input.RunOwner != "alive") return new QualificationVerdict("unknown", "run-owner-" + input.RunOwner);
            if (!stateOurs && !copyOurs) return new QualificationVerdict("not-started", "state-not-written");
            if (input.Supervisor != "alive") return new QualificationVerdict("supervisor-lost-run-alive", "supervisor-" + input.Supervisor);
            if (input.Heartbeat != "ok" || input.HeartbeatUtc == null) return new QualificationVerdict("supervisor-lost-run-alive", "heartbeat-" + input.Heartbeat);
            if ((input.NowUtc - input.HeartbeatUtc.Value).TotalMinutes >= input.StaleMinutes) return new QualificationVerdict("supervisor-lost-run-alive", "heartbeat-stale");
            return new QualificationVerdict("running", "heartbeat-fresh");
        }

        /// <summary>
        /// Get-QualificationSupervision: the observation of TaskRoot (supervisor-identity.json, supervisor-heartbeat.json,
        /// launch-child.json, own-last-run.json, commission-exit.txt) and StateRoot (run.lock, last-run.json). The run owner is
        /// probed BEFORE last-run.json is read: nightly-run writes completedAt, then releases run.lock and exits, so an absent
        /// owner read first cannot hide a completion. Result fields come only from our own run's state (or its copy).
        /// </summary>
        public static JsonObject Observation(string taskRoot, string stateRoot, double staleMinutes, NightlyHost host, IFileSystem files)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            RcQualificationRead ReadAt(string root, string name) => RcQualificationRecords.Read(files, files.Path.GetFullPath(files.Path.Combine(root, name)));
            RcQualificationObservation Probe(RcQualificationRead read) => Observe(read.Record, host);
            var lockRead = RcQualificationRecords.Read(files, files.Path.GetFullPath(NightlyRunLock.LockPath(stateRoot)));
            var runOwner = Probe(lockRead);
            var state = ReadAt(stateRoot, "last-run.json");
            var identity = ReadAt(taskRoot, "supervisor-identity.json");
            var supervisor = identity.Status == "missing" ? new RcQualificationObservation("missing", "no-identity") : Probe(identity);
            // The launch's run id, recorded by qualification-supervise from qualification-start-supervisor --run-id.
            string expected = null;
            if (identity.Ok && RcQualificationRecords.IsString(RcQualificationRecords.Member(identity.Record, "runId")))
                expected = RcQualificationRecords.Member(identity.Record, "runId").Value.GetString();
            var lockRunId = lockRead.Ok && RcQualificationRecords.Present(RcQualificationRecords.Member(lockRead.Record, "runId"))
                ? RcQualificationRecords.Text(RcQualificationRecords.Member(lockRead.Record, "runId")) : null;
            var stateRunId = state.Ok && RcQualificationRecords.Present(RcQualificationRecords.Member(state.Record, "runId"))
                ? RcQualificationRecords.Text(RcQualificationRecords.Member(state.Record, "runId")) : null;
            var runLock = lockRead.Status;
            if (lockRead.Ok) runLock = !string.IsNullOrWhiteSpace(expected) && lockRunId == expected ? "ours" : "foreign";
            var beat = ReadAt(taskRoot, "supervisor-heartbeat.json");
            DateTime? beatUtc = null;
            bool? hang = null;
            if (beat.Ok)
            {
                var stamp = RcQualificationRecords.Member(beat.Record, "observedAtUtc");
                if (!RcQualificationRecords.Present(stamp)) stamp = RcQualificationRecords.Member(beat.Record, "utc");
                beatUtc = RcQualificationRecords.Time(stamp);
                var hangValue = RcQualificationRecords.Member(beat.Record, "hangSuspected");
                if (RcQualificationRecords.IsBool(hangValue)) hang = hangValue.Value.GetBoolean();
            }
            var copy = ReadAt(taskRoot, "own-last-run.json");
            JsonElement? copyRecord = null;
            var stateCopy = copy.Status;
            if (copy.Ok)
            {
                stateCopy = "foreign";
                if (!string.IsNullOrWhiteSpace(expected) && RcQualificationRecords.Text(RcQualificationRecords.Member(copy.Record, "runId")) == expected)
                {
                    stateCopy = "ours";
                    copyRecord = copy.Record;
                }
            }
            // The launch child's start bounds which later last-run.json writer superseded ours.
            var launch = ReadAt(taskRoot, "launch-child.json");
            DateTime? launchStarted = null;
            if (launch.Ok && !string.IsNullOrWhiteSpace(expected) && RcQualificationRecords.Text(RcQualificationRecords.Member(launch.Record, "runId")) == expected)
                launchStarted = RcQualificationRecords.Time(RcQualificationRecords.Member(launch.Record, "processStartedAt"));
            var exitPath = files.Path.Combine(taskRoot, "commission-exit.txt");
            string commissionExit = null;
            if (files.File.Exists(exitPath))
            {
                try { commissionExit = files.File.ReadAllText(exitPath).Trim(); }
                catch (Exception) { commissionExit = "unreadable"; }
            }
            var now = host.UtcNow;
            var verdict = Classify(new QualificationClassificationInput
            {
                ExpectedRunId = expected,
                StateStatus = state.Status,
                State = state.Record,
                LockStatus = lockRead.Status,
                LockRunId = lockRunId,
                RunOwner = runOwner.Status,
                Supervisor = supervisor.Status,
                Heartbeat = beat.Status,
                HeartbeatUtc = beatUtc,
                NowUtc = now,
                CommissionExit = commissionExit,
                StaleMinutes = staleMinutes,
                CopyStatus = copy.Status,
                Copy = copyRecord,
                LaunchStartedUtc = launchStarted,
            });
            var ownState = copyRecord;
            if (!string.IsNullOrWhiteSpace(expected) && stateRunId == expected) ownState = state.Record;
            var fields = RcQualificationRecords.Select(ownState, "phase", "completedAt", "exitCode", "testsPassed", "coverageComplete");
            return new JsonObject
            {
                ["observedAtUtc"] = now.ToString("o", CultureInfo.InvariantCulture),
                ["classification"] = verdict.Classification,
                ["reason"] = verdict.Reason,
                ["runOwner"] = runOwner.Status,
                ["supervisor"] = supervisor.Status,
                ["heartbeat"] = beat.Status,
                ["heartbeatAgeMinutes"] = beatUtc == null ? null : JsonValue.Create(Math.Round((now - beatUtc.Value).TotalMinutes, 1)),
                ["hangSuspected"] = hang == null ? null : JsonValue.Create(hang.Value),
                ["state"] = state.Status,
                ["stateCopy"] = stateCopy,
                ["runId"] = expected,
                ["stateRunId"] = stateRunId,
                ["runLock"] = runLock,
                ["lockRunId"] = lockRunId,
                ["phase"] = fields["phase"]?.DeepClone(),
                ["completedAt"] = fields["completedAt"]?.DeepClone(),
                ["exitCode"] = fields["exitCode"]?.DeepClone(),
                ["testsPassed"] = fields["testsPassed"]?.DeepClone(),
                ["coverageComplete"] = fields["coverageComplete"]?.DeepClone(),
                ["commissionExit"] = commissionExit,
                ["launchStartedAt"] = launchStarted?.ToString("o", CultureInfo.InvariantCulture),
            };
        }

        /// <summary>
        /// Save-QualificationOwnRunState: while StateRoot/last-run.json names runId, its exact text (a byte-order mark
        /// dropped) is kept as TaskRoot/own-last-run.json; a copy with completedAt is never replaced by one without. Returns
        /// copied, unchanged, kept, foreign, missing or unreadable; it never writes the state root.
        /// </summary>
        public static string SaveOwnRunState(string taskRoot, string stateRoot, string runId, IFileSystem files)
        {
            var source = files.Path.GetFullPath(files.Path.Combine(stateRoot, "last-run.json"));
            var target = files.Path.GetFullPath(files.Path.Combine(taskRoot, "own-last-run.json"));
            if (!files.File.Exists(source)) return "missing";
            string text;
            JsonElement parsed;
            try
            {
                using (var stream = RcQualificationRecords.OpenSharedRead(files, source))
                {
                    if (stream.Length > RcQualificationRecords.MaxBytes) return "unreadable";
                    using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
                    text = reader.ReadToEnd().TrimStart((char)0xFEFF);
                }
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                parsed = document.RootElement.Clone();
            }
            catch (Exception)
            {
                return "unreadable";
            }
            if (parsed.ValueKind != JsonValueKind.Object || RcQualificationRecords.Text(RcQualificationRecords.Member(parsed, "runId")) != runId) return "foreign";
            var existing = RcQualificationRecords.Read(files, target);
            if (existing.Ok && RcQualificationRecords.Text(RcQualificationRecords.Member(existing.Record, "runId")) == runId)
            {
                var kept = RcQualificationRecords.Time(RcQualificationRecords.Member(existing.Record, "completedAt"));
                var incoming = RcQualificationRecords.Time(RcQualificationRecords.Member(parsed, "completedAt"));
                if (kept != null && incoming == null) return "kept";
            }
            if (files.File.Exists(target))
            {
                string current = null;
                try { current = files.File.ReadAllText(target); }
                catch (Exception) { }
                if (current == text) return "unchanged";
            }
            QualificationFiles.WriteRecord(files, target, text);
            return "copied";
        }
    }

    /// <summary>The qualification-observe command's options.</summary>
    public sealed class QualificationObserveOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "qualification-observe --task-root <dir> --state-root <dir> [--heartbeat-stale-minutes 5]";

        /// <summary>The supervisor's task directory.</summary>
        public string TaskRoot { get; init; } = "";

        /// <summary>The run's state root.</summary>
        public string StateRoot { get; init; } = "";

        /// <summary>A heartbeat this many minutes old (1 to 120) is stale.</summary>
        public double HeartbeatStaleMinutes { get; init; } = 5;

        /// <summary>The arguments after the command name.</summary>
        public static QualificationObserveOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--task-root", "--state-root", "--heartbeat-stale-minutes" }, Array.Empty<string>());
            return new QualificationObserveOptions
            {
                TaskRoot = flags.Required("--task-root"),
                StateRoot = flags.Required("--state-root"),
                HeartbeatStaleMinutes = QualificationFlags.Number(flags, "--heartbeat-stale-minutes", 5, 1, 120),
            };
        }
    }

    /// <summary>Range-checked numeric flags of the supervisor commands.</summary>
    public static class QualificationFlags
    {
        /// <summary>A whole-number flag in [minimum, maximum]; NightlyUsageException otherwise.</summary>
        public static int Whole(NightlyArgs flags, string flag, int fallback, int minimum, int maximum)
        {
            var value = flags.Int(flag, fallback);
            if (value < minimum || value > maximum)
                throw new NightlyUsageException(flag + " expects " + minimum.ToString(CultureInfo.InvariantCulture) + " to " + maximum.ToString(CultureInfo.InvariantCulture)
                    + ", not " + value.ToString(CultureInfo.InvariantCulture));
            return value;
        }

        /// <summary>A number flag (invariant culture) in [minimum, maximum]; NightlyUsageException otherwise.</summary>
        public static double Number(NightlyArgs flags, string flag, double fallback, double minimum, double maximum)
        {
            var text = flags.Value(flag);
            if (string.IsNullOrEmpty(text)) return fallback;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || value < minimum || value > maximum)
                throw new NightlyUsageException(flag + " expects a number from " + minimum.ToString(CultureInfo.InvariantCulture) + " to " + maximum.ToString(CultureInfo.InvariantCulture)
                    + ", not '" + text + "'");
            return value;
        }

        /// <summary>A run id flag (RcQualificationStatus.IdPattern); NightlyUsageException otherwise.</summary>
        public static string RunId(NightlyArgs flags, string flag)
        {
            var value = flags.Required(flag);
            if (!RcQualificationStatus.IdPattern.IsMatch(value)) throw new NightlyUsageException(flag + " must match " + RcQualificationStatus.IdPattern + ", not '" + value + "'");
            return value;
        }
    }

    /// <summary>The qualification-observe command (observe.ps1).</summary>
    public static class QualificationObserve
    {
        /// <summary>
        /// One JSON line on host.Out; it writes nothing and never stops a process or clears a lock. Exit 0 completed, running
        /// or supervisor-lost-run-alive; 3 run-lost (the bound run's nightly-run gone from its run.lock without completedAt:
        /// investigate); 2 not-started, launch-failed or unknown (state-superseded: read the run's LogRoot run directory
        /// before any relaunch).
        /// </summary>
        public static int Run(QualificationObserveOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var observation = QualificationSupervision.Observation(options.TaskRoot, options.StateRoot, options.HeartbeatStaleMinutes, host, files);
            host.Out.WriteLine(RcQualificationRecords.Compact(observation));
            return ExitCode((string)observation["classification"]);
        }

        /// <summary>The exit code of a classification.</summary>
        public static int ExitCode(string classification) => classification switch
        {
            "completed" => 0,
            "running" => 0,
            "supervisor-lost-run-alive" => 0,
            "run-lost" => 3,
            _ => 2,
        };
    }

    /// <summary>The flags the start-supervisor and supervise commands share.</summary>
    public abstract class QualificationSupervisorFlags
    {
        /// <summary>The supervisor's task directory (its identity, log, heartbeat and the launch child's records).</summary>
        public string TaskRoot { get; init; } = "";

        /// <summary>The launch record (launch-record.json): checkoutRoot, expectedSha, nightlyArguments, buildSlotLabel.</summary>
        public string LaunchRecord { get; init; } = "";

        /// <summary>The run's log root (the heartbeat counts its files).</summary>
        public string LogRoot { get; init; } = "";

        /// <summary>The run's state root (last-run.json, run.lock).</summary>
        public string StateRoot { get; init; } = "";

        /// <summary>The launch's run id: the -RunId in the launch record's nightlyArguments.</summary>
        public string RunId { get; init; } = "";

        /// <summary>Seconds between heartbeats, 1 to 300.</summary>
        public int PollSeconds { get; init; } = 30;

        /// <summary>Minutes between PROGRESS lines, 1 to 120.</summary>
        public int ProgressMinutes { get; init; } = 10;

        /// <summary>The idle budget of a chunk with no planned cost, 1 to 600 minutes.</summary>
        public double HangMinutes { get; init; } = 20;

        /// <summary>The PowerShell 7 that runs scripts/nightly-run.ps1 for the launch (under the launch's build slot).</summary>
        public string PowerShellPath { get; init; } = "pwsh";

        internal static readonly string[] SharedValued =
        {
            "--task-root", "--launch-record", "--log-root", "--state-root", "--run-id", "--poll-seconds", "--progress-minutes", "--hang-minutes", "--powershell-path",
        };

        /// <summary>The shared flags' arguments, as the starter hands them to the supervisor.</summary>
        public IEnumerable<string> SharedArguments() => new[]
        {
            "--task-root", TaskRoot, "--launch-record", LaunchRecord, "--log-root", LogRoot, "--state-root", StateRoot, "--run-id", RunId,
            "--poll-seconds", PollSeconds.ToString(CultureInfo.InvariantCulture), "--progress-minutes", ProgressMinutes.ToString(CultureInfo.InvariantCulture),
            "--hang-minutes", HangMinutes.ToString(CultureInfo.InvariantCulture), "--powershell-path", PowerShellPath,
        };
    }

    /// <summary>The qualification-start-supervisor command's options.</summary>
    public sealed class QualificationStartOptions : QualificationSupervisorFlags
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "qualification-start-supervisor --task-root <dir> --launch-record <json> --log-root <dir> --state-root <dir> --run-id <id>"
            + " [--poll-seconds 30] [--progress-minutes 10] [--hang-minutes 20] [--identity-timeout-seconds 60] [--powershell-path <pwsh>]";

        /// <summary>How long to wait for the supervisor's identity, 5 to 600 seconds.</summary>
        public int IdentityTimeoutSeconds { get; init; } = 60;

        /// <summary>The arguments after the command name. powerShellPath is the default --powershell-path (the CLI resolves pwsh).</summary>
        public static QualificationStartOptions Parse(IReadOnlyList<string> arguments, string powerShellPath)
        {
            var flags = NightlyArgs.Parse(arguments, SharedValued.Concat(new[] { "--identity-timeout-seconds" }), Array.Empty<string>());
            var pwsh = string.IsNullOrWhiteSpace(flags.Value("--powershell-path")) ? powerShellPath : flags.Value("--powershell-path");
            if (string.IsNullOrWhiteSpace(pwsh)) throw new NightlyUsageException("--powershell-path: pwsh is not on PATH; name the PowerShell 7 that runs the launch");
            return new QualificationStartOptions
            {
                TaskRoot = flags.Required("--task-root"),
                LaunchRecord = flags.Required("--launch-record"),
                LogRoot = flags.Required("--log-root"),
                StateRoot = flags.Required("--state-root"),
                RunId = QualificationFlags.RunId(flags, "--run-id"),
                PollSeconds = QualificationFlags.Whole(flags, "--poll-seconds", 30, 1, 300),
                ProgressMinutes = QualificationFlags.Whole(flags, "--progress-minutes", 10, 1, 120),
                HangMinutes = QualificationFlags.Number(flags, "--hang-minutes", 20, 1, 600),
                IdentityTimeoutSeconds = QualificationFlags.Whole(flags, "--identity-timeout-seconds", 60, 5, 600),
                PowerShellPath = pwsh,
            };
        }
    }

    /// <summary>The qualification-supervise command's options (the detached child the starter runs).</summary>
    public sealed class QualificationSuperviseOptions : QualificationSupervisorFlags
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "qualification-supervise --task-root <dir> --launch-record <json> --log-root <dir> --state-root <dir> --run-id <id>"
            + " [--poll-seconds 30] [--progress-minutes 10] [--hang-minutes 20] [--detach new-console|setsid|none] [--powershell-path <pwsh>]";

        /// <summary>How the starter detached this process: new-console, setsid or none.</summary>
        public string Detach { get; init; } = "none";

        /// <summary>The arguments after the command name.</summary>
        public static QualificationSuperviseOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, SharedValued.Concat(new[] { "--detach" }), Array.Empty<string>());
            var detach = flags.Value("--detach", "none");
            if (detach != "new-console" && detach != "setsid" && detach != "none") throw new NightlyUsageException("--detach expects new-console, setsid or none, not '" + detach + "'");
            return new QualificationSuperviseOptions
            {
                TaskRoot = flags.Required("--task-root"),
                LaunchRecord = flags.Required("--launch-record"),
                LogRoot = flags.Required("--log-root"),
                StateRoot = flags.Required("--state-root"),
                RunId = QualificationFlags.RunId(flags, "--run-id"),
                PollSeconds = QualificationFlags.Whole(flags, "--poll-seconds", 30, 1, 300),
                ProgressMinutes = QualificationFlags.Whole(flags, "--progress-minutes", 10, 1, 120),
                HangMinutes = QualificationFlags.Number(flags, "--hang-minutes", 20, 1, 600),
                Detach = detach,
                PowerShellPath = string.IsNullOrWhiteSpace(flags.Value("--powershell-path")) ? "pwsh" : flags.Value("--powershell-path"),
            };
        }
    }

    /// <summary>The qualification-start-supervisor command (start-supervisor.ps1).</summary>
    public static class QualificationStartSupervisor
    {
        /// <summary>The prefix of the starter's lines.</summary>
        public const string Prefix = "qualification-start-supervisor: ";

        /// <summary>
        /// Starts qualification-supervise detached (its own hidden console on Windows, setsid on Unix; its stdout and stderr
        /// in supervisor-stdout.log and supervisor-stderr.log), from a copy of this CLI in TaskRoot/cli so the long-lived
        /// supervisor never holds the checkout's build output. Waits for supervisor-identity.json, writes
        /// start-supervisor.json (also printed) and exits 0 when the supervisor is alive (or already finished: its
        /// commission-exit.txt is written), 1 otherwise. Refuses (exit 1) a missing TaskRoot or launch record, a TaskRoot that
        /// already has a supervisor identity, and a run id that StateRoot's last-run.json or run.lock, or TaskRoot's
        /// own-last-run.json, already names (a reused run id could credit an earlier run).
        /// </summary>
        public static int Run(QualificationStartOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var seams = QualificationSupervision.Seams(host);
            var platform = QualificationSupervision.Platform(host);
            var identityPath = files.Path.Combine(options.TaskRoot, "supervisor-identity.json");
            if (!files.Directory.Exists(options.TaskRoot)) return Refuse(host, "TaskRoot does not exist: " + options.TaskRoot);
            if (!files.File.Exists(options.LaunchRecord)) return Refuse(host, "the launch record does not exist: " + options.LaunchRecord);
            if (files.File.Exists(identityPath)) return Refuse(host, "refused, a supervisor identity already exists: " + identityPath);
            foreach (var seenPath in new[] { files.Path.Combine(options.StateRoot, "last-run.json"), NightlyRunLock.LockPath(options.StateRoot), files.Path.Combine(options.TaskRoot, "own-last-run.json") })
            {
                var seen = RcQualificationRecords.Read(files, files.Path.GetFullPath(seenPath));
                if (seen.Ok && string.Equals(RcQualificationRecords.Text(RcQualificationRecords.Member(seen.Record, "runId")), options.RunId, StringComparison.OrdinalIgnoreCase))
                    return Refuse(host, "refused, " + seen.Path + " already names run id " + options.RunId + "; bind a fresh --run-id");
            }
            var detach = platform == "Windows" ? "new-console" : "setsid";
            IReadOnlyList<string> cli;
            try
            {
                cli = seams.StageCli(files.Path.Combine(options.TaskRoot, "cli"));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Refuse(host, "refused, this CLI could not be copied into " + files.Path.Combine(options.TaskRoot, "cli") + ": " + e.Message);
            }
            var arguments = cli.Skip(1).Concat(new[] { "qualification-supervise" }).Concat(options.SharedArguments()).Concat(new[] { "--detach", detach }).ToArray();
            IQualificationChild started;
            try
            {
                started = seams.Start(new QualificationStart(cli[0], arguments, files.Path.Combine(options.TaskRoot, "supervisor-stdout.log"),
                    files.Path.Combine(options.TaskRoot, "supervisor-stderr.log")));
            }
            catch (Exception e)
            {
                return Refuse(host, "the supervisor did not start: " + e.Message);
            }
            using (started)
            {
                var deadline = host.UtcNow.AddSeconds(options.IdentityTimeoutSeconds);
                JsonElement? identity = null;
                while (host.UtcNow < deadline)
                {
                    var read = RcQualificationRecords.Read(files, files.Path.GetFullPath(identityPath));
                    if (read.Ok) { identity = read.Record; break; }
                    if (started.HasExited && started.ExitCode != 0) break;
                    host.Sleep(TimeSpan.FromMilliseconds(200));
                }
                var self = host.Processes.CurrentPid;
                var record = new JsonObject
                {
                    ["starterPid"] = self,
                    ["starterStartedAt"] = host.Processes.StartTimeUtc(self)?.ToString("o", CultureInfo.InvariantCulture),
                    ["startedPid"] = started.Pid,
                    ["runId"] = options.RunId,
                    ["platform"] = platform,
                    ["detach"] = detach,
                    ["supervisorPid"] = null,
                    ["supervisorStartedAt"] = null,
                    ["observation"] = "identity-timeout",
                };
                if (identity != null)
                {
                    var pid = RcQualificationRecords.Member(identity, "pid");
                    record["supervisorPid"] = RcQualificationRecords.Present(pid) ? JsonNode.Parse(pid.Value.GetRawText()) : null;
                    record["supervisorStartedAt"] = RcQualificationRecords.Text(RcQualificationRecords.Member(identity, "processStartedAt"));
                    record["observation"] = QualificationSupervision.Observe(identity, host).Status;
                }
                var json = RcQualificationRecords.Compact(record);
                QualificationFiles.WriteRecord(files, files.Path.Combine(options.TaskRoot, "start-supervisor.json"), json);
                host.Out.WriteLine(json);
                var finished = identity != null && files.File.Exists(files.Path.Combine(options.TaskRoot, "commission-exit.txt"));
                return (string)record["observation"] == "alive" || finished ? 0 : 1;
            }
        }

        private static int Refuse(NightlyHost host, string message)
        {
            host.Out.WriteLine(Prefix + message);
            return 1;
        }
    }

    /// <summary>The qualification-supervise command (supervise-rc.ps1): the detached supervisor of one launch.</summary>
    public static class QualificationSupervise
    {
        /// <summary>The variable that hands the launch its bound run id (qualification-launch refuses a record with another -RunId).</summary>
        public const string RunIdVariable = "ANTIPHON_QUALIFICATION_RUN_ID";
        internal const string HangMinutesVariable = "ANTIPHON_QUALIFICATION_HANG_MINUTES";

        /// <summary>
        /// Records its identity (supervisor-identity.json: pid, start, run id, CLI, launch record, platform, detach, session,
        /// console), starts `qualification-launch --config &lt;launch record&gt;` (its own console on Windows, this session on Unix;
        /// output in commission-console.log and commission-stderr.log; the run id in ANTIPHON_QUALIFICATION_RUN_ID), records
        /// launch-child.json, then every PollSeconds copies its run's last-run.json (SaveOwnRunState), writes the heartbeat
        /// with the active test-host sample when available, a HANG_SUSPECTED flag and a PROGRESS line every ProgressMinutes;
        /// at the child's exit it copies the state once more and
        /// writes EXIT &lt;code&gt; and commission-exit.txt. Lines go to supervisor.log one flushed line at a time. A failed poll
        /// logs HEARTBEAT-ERROR and the next poll retries. Exit 0, or 1 after SUPERVISOR-ERROR (the message on host.Error).
        /// It never stops, signals or restarts a process and never clears a lock.
        /// </summary>
        public static int Run(QualificationSuperviseOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var seams = QualificationSupervision.Seams(host);
            var log = files.Path.Combine(options.TaskRoot, "supervisor.log");
            var heartbeat = files.Path.Combine(options.TaskRoot, "supervisor-heartbeat.json");
            void Line(string text) => QualificationFiles.WriteLine(files, log, text, host.UtcNow);
            void Quietly(string text)
            {
                try { Line(text); }
                catch (Exception) { }
            }
            // Copy our run's last-run.json into TaskRoot; a failed copy never ends the supervisor.
            void SaveOwn()
            {
                try
                {
                    if (QualificationSupervision.SaveOwnRunState(options.TaskRoot, options.StateRoot, options.RunId, files) == "copied") Line("STATE-COPY own-last-run.json");
                }
                catch (Exception e)
                {
                    Quietly("STATE-COPY-ERROR " + e.Message);
                }
            }
            var platform = QualificationSupervision.Platform(host);
            var self = host.Processes.CurrentPid;
            var cli = seams.Cli;
            var session = seams.SessionId();
            var consoles = seams.ConsoleProcessIds() ?? Array.Empty<int>();
            var identity = new JsonObject
            {
                ["pid"] = self,
                ["processStartedAt"] = host.Processes.StartTimeUtc(self)?.ToString("o", CultureInfo.InvariantCulture),
                ["runId"] = options.RunId,
                ["cli"] = string.Join(" ", cli),
                ["launchRecord"] = options.LaunchRecord,
                ["platform"] = platform,
                ["detach"] = options.Detach,
                ["sessionId"] = session,
                ["consoleProcessIds"] = new JsonArray(consoles.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
            };
            QualificationFiles.WriteRecord(files, files.Path.Combine(options.TaskRoot, "supervisor-identity.json"), RcQualificationRecords.Compact(identity));
            Line("START supervisor pid=" + self.ToString(CultureInfo.InvariantCulture) + " run=" + options.RunId + " platform=" + platform + " detach=" + options.Detach
                + " session=" + (session?.ToString(CultureInfo.InvariantCulture) ?? "") + " console=[" + string.Join(",", consoles.Select(id => id.ToString(CultureInfo.InvariantCulture))) + "]");
            try
            {
                var arguments = cli.Skip(1).Concat(new[] { "qualification-launch", "--config", options.LaunchRecord, "--powershell-path", options.PowerShellPath }).ToArray();
                using var child = seams.Start(new QualificationStart(cli[0], arguments, files.Path.Combine(options.TaskRoot, "commission-console.log"),
                    files.Path.Combine(options.TaskRoot, "commission-stderr.log"), SameSession: true,
                    Environment: new Dictionary<string, string>
                    {
                        [RunIdVariable] = options.RunId,
                        [HangMinutesVariable] = options.HangMinutes.ToString(CultureInfo.InvariantCulture),
                    }));
                var childStart = child.StartTimeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                QualificationFiles.WriteRecord(files, files.Path.Combine(options.TaskRoot, "launch-child.json"),
                    RcQualificationRecords.Compact(new JsonObject { ["pid"] = child.Pid, ["processStartedAt"] = childStart, ["runId"] = options.RunId }));
                Line("LAUNCHED pid=" + child.Pid.ToString(CultureInfo.InvariantCulture) + " started=" + childStart + " record=" + options.LaunchRecord);
                QualificationHeartbeatMonitor monitor = null;
                var hangReported = false;
                var nextProgress = host.UtcNow.AddMinutes(options.ProgressMinutes);
                while (!child.WaitForExit(TimeSpan.FromSeconds(options.PollSeconds)))
                {
                    SaveOwn();
                    try
                    {
                        var observed = Files(files, options.LogRoot).Concat(Files(files, options.StateRoot))
                            .Where(file => file.Name != NightlyChunkIdleMonitor.HeartbeatFile
                                && file.Name != NightlyChunkIdleMonitor.HeartbeatFile + ".tmp").ToArray();
                        var bytes = observed.Sum(file => file.Length);
                        var trx = observed.Count(file => string.Equals(file.Extension, ".trx", StringComparison.OrdinalIgnoreCase));
                        var newest = observed.OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()?.Name ?? "";
                        var ownState = RcQualificationRecords.Read(files, files.Path.GetFullPath(files.Path.Combine(options.TaskRoot, "own-last-run.json")));
                        var runLog = RcQualificationRecords.Text(RcQualificationRecords.Member(ownState.Record, "logDir"));
                        var inTests = ownState.Ok && RcQualificationRecords.Text(RcQualificationRecords.Member(ownState.Record, "runId")) == options.RunId
                            && RcQualificationRecords.Text(RcQualificationRecords.Member(ownState.Record, "phase")) == "tests" && !string.IsNullOrWhiteSpace(runLog);
                        QualificationHeartbeatRecord beat;
                        bool fromTestHost = false;
                        if (inTests)
                        {
                            monitor = null;
                            beat = ReadTestHostHeartbeat(files.Path.Combine(runLog, NightlyChunkIdleMonitor.HeartbeatFile),
                                child.StartTimeUtc, host.UtcNow, options.PollSeconds);
                            fromTestHost = beat != null;
                            beat ??= new QualificationHeartbeatRecord { IdleBudgetMinutes = options.HangMinutes, IdleBudgetSource = "default-unknown-group" };
                            // Keep the run-wide progress fields. The idle clock and CPU are supplied by the chunk's owner.
                            beat.Trx = trx;
                            beat.Bytes = bytes;
                            beat.Newest = newest;
                            beat.ObservedAtUtc = host.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                            QualificationHeartbeat.Write(beat, heartbeat);
                        }
                        else
                        {
                            monitor ??= new QualificationHeartbeatMonitor(host.Clock, seams.ProcessTable, options.HangMinutes);
                            beat = monitor.Beat(heartbeat, child.Pid, new[] { self }, trx, bytes, newest);
                        }
                        if (beat.HangSuspected && !hangReported)
                        {
                            hangReported = true;
                            var line = "HANG_SUSPECTED idleMinutes=" + beat.IdleMinutes.ToString(CultureInfo.InvariantCulture) + " reason=" + beat.HangReason
                                + (fromTestHost ? " (test-host heartbeat from the job owner)" : " (observe only; nothing is stopped)");
                            QualificationFiles.WriteRecord(files, files.Path.Combine(options.TaskRoot, "hang-suspected.txt"), line);
                            Line(line);
                        }
                        if (!beat.HangSuspected) hangReported = false;
                        if (host.UtcNow >= nextProgress)
                        {
                            nextProgress = host.UtcNow.AddMinutes(options.ProgressMinutes);
                            Line("PROGRESS trx=" + trx.ToString(CultureInfo.InvariantCulture) + " bytes=" + bytes.ToString(CultureInfo.InvariantCulture) + " idleMinutes="
                                + beat.IdleMinutes.ToString(CultureInfo.InvariantCulture) + " cpuSeconds=" + beat.CpuSeconds.ToString(CultureInfo.InvariantCulture) + " newest=" + newest);
                        }
                    }
                    catch (Exception e)
                    {
                        // A failed observation must never end the supervisor; the next poll retries.
                        Quietly("HEARTBEAT-ERROR " + e.Message);
                    }
                }
                var exitCode = child.ExitCode;
                SaveOwn();
                Line("EXIT " + exitCode.ToString(CultureInfo.InvariantCulture));
                QualificationFiles.WriteRecord(files, files.Path.Combine(options.TaskRoot, "commission-exit.txt"), exitCode.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                Quietly("SUPERVISOR-ERROR " + e.Message);
                host.Error.WriteLine("qualification-supervise: " + e.Message);
                return 1;
            }
            finally
            {
                Quietly("SUPERVISOR-END");
            }
            return 0;
        }

        private static QualificationHeartbeatRecord ReadTestHostHeartbeat(string path, DateTime launchStart, DateTime now, int pollSeconds)
        {
            try
            {
                var beat = QualificationHeartbeat.Read(path);
                if (DateTime.TryParse(beat.ObservedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
                    && stamp >= launchStart && stamp <= now
                    && now - stamp <= TimeSpan.FromSeconds(pollSeconds) + TimeSpan.FromMilliseconds(2 * NightlyChunkIdleMonitor.PollMilliseconds)
                    && beat.HostCpu.Length == 1 && beat.HostCpu[0].Pid > 0)
                    return beat;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is FormatException) { }
            return null;
        }

        // Get-ChildItem -Recurse -File -Force -ErrorAction SilentlyContinue: every file (hidden ones too), inaccessible
        // directories and reparse points skipped; nothing when the root is missing.
        private static IEnumerable<IFileInfo> Files(IFileSystem files, string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !files.Directory.Exists(root)) return Array.Empty<IFileInfo>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return files.DirectoryInfo.New(root).EnumerateFiles("*", options).ToArray();
        }
    }
}
