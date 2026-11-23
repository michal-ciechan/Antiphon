// CARD-1507: stop one recorded Windows rc qualification by its supervisor tree, then its driver. It replaced
// scripts/qualification/qualification-stop.ps1 (Invoke-QualificationRecordedStop, Get-QualificationStopDryRunText and their
// helpers) and its entry scripts/qualification/stop-supervisor.ps1, deleted 2026-11-23 (unit 7 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The operator runs it as `antiphon-scripts qualification-stop`.
// Stopping the live qualification run is the operator's decision (human-only, 2026-10-31): agents never aim it at a live run.
//
// Without --confirm it prints a dry run of the recorded pids and identities and the planned lock action, kills nothing and
// exits 2. With --confirm it reads TaskRoot/supervisor-identity.json and launch-child.json (both must name the run id),
// re-observes each recorded pid and start (and its command line when the record has one), runs taskkill /PID <supervisor>
// /T /F, and a second taskkill /PID <driver> /T /F only when that driver outlived the supervisor tree; a driver already
// absent after the tree kill was ended as a descendant and is not a failed stop. A reused or unknown identity after a kill
// stays incomplete and the lock is kept. CoordinationRoot/native-run.lock moves into TaskRoot/stop-evidence only when its
// owner pid is dead, its run id matches and its bytes are unchanged. The stop reason is appended to TaskRoot/supervisor.log
// with quotes and backslashes escaped; a log write failure still returns the JSON result. It does not edit last-run.json
// or remove run.lock. A host other than Windows exits 5 before any kill or file change. Process observation is the host's
// process table, the tree stop and the command line host.Qualification. Exit codes: 0 stopped, 2 dry run or refused,
// 3 incomplete, 4 lock retained, 5 not Windows. Plain C#: no PowerShell types. ASCII only.
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
    /// <summary>The qualification-stop command's options.</summary>
    public sealed class QualificationStopOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "qualification-stop --task-root <dir> --coordination-root <dir> --run-id <id> --reason <text> [--evidence-root <dir>] [--confirm]";

        /// <summary>The supervisor's task directory.</summary>
        public string TaskRoot { get; init; } = "";

        /// <summary>The coordination root (native-run.lock).</summary>
        public string CoordinationRoot { get; init; } = "";

        /// <summary>The run id both records must name.</summary>
        public string RunId { get; init; } = "";

        /// <summary>The stop reason: printable ASCII, 1 to 200 characters, not blank.</summary>
        public string Reason { get; init; } = "";

        /// <summary>Where the moved lock goes; empty: TaskRoot/stop-evidence.</summary>
        public string EvidenceRoot { get; init; } = "";

        /// <summary>Stop (without it: the dry run).</summary>
        public bool Confirm { get; init; }

        /// <summary>How long the census waits for the recorded pids to be gone (the CLI: 30 s); 0 checks once.</summary>
        public int CensusMilliseconds { get; init; } = 30000;

        /// <summary>The arguments after the command name. The run id and the reason are checked by the stop (exit 2, refused).</summary>
        public static QualificationStopOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--task-root", "--coordination-root", "--run-id", "--reason", "--evidence-root" }, new[] { "--confirm" });
            return new QualificationStopOptions
            {
                TaskRoot = flags.Required("--task-root"),
                CoordinationRoot = flags.Required("--coordination-root"),
                RunId = flags.Value("--run-id", ""),
                Reason = flags.Value("--reason", ""),
                EvidenceRoot = flags.Value("--evidence-root", ""),
                Confirm = flags.Switch("--confirm"),
            };
        }
    }

    /// <summary>One kill of a stop: the phase (supervisor or driver), the pid and the tree stop's outcome.</summary>
    public sealed record QualificationKill(string Phase, int Pid, bool Ok, string Reason, string Command);

    /// <summary>A stop's result (New-QualificationStopResult).</summary>
    public sealed class QualificationStopResult
    {
        public int ExitCode { get; init; }
        public string Outcome { get; init; }
        public string Detail { get; init; }
        public IReadOnlyList<QualificationKill> Kills { get; init; } = Array.Empty<QualificationKill>();
        public string LockAction { get; init; }
        public string RetainedLockPath { get; init; }
        public string LogPath { get; init; }

        /// <summary>The JSON line the command prints.</summary>
        public JsonObject Json() => new JsonObject
        {
            ["exitCode"] = ExitCode,
            ["outcome"] = Outcome,
            ["detail"] = Detail,
            ["kills"] = new JsonArray(Kills.Select(kill => (JsonNode)new JsonObject
            {
                ["phase"] = kill.Phase, ["pid"] = kill.Pid, ["ok"] = kill.Ok, ["reason"] = kill.Reason, ["command"] = kill.Command,
            }).ToArray()),
            ["lockAction"] = LockAction,
            ["retainedLockPath"] = RetainedLockPath,
            ["logPath"] = LogPath,
        };
    }

    /// <summary>The recorded stop (qualification-stop.ps1, stop-supervisor.ps1).</summary>
    public static class QualificationStop
    {
        /// <summary>The command: the dry run (exit 2) without --confirm, else the stop's JSON line and its exit code.</summary>
        public static int Run(QualificationStopOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!options.Confirm)
            {
                host.Out.WriteLine(DryRunText(options, files));
                return 2;
            }
            QualificationStopResult result;
            try
            {
                result = Stop(options, host, files);
            }
            catch (Exception)
            {
                result = Result(3, "incomplete", "stop-fault", null, "not-attempted", null, null);
            }
            host.Out.WriteLine(RcQualificationRecords.Compact(result.Json()));
            return result.ExitCode;
        }

        /// <summary>Test-QualificationStopReason: 1 to 200 printable ASCII characters, not blank.</summary>
        public static bool ValidReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 200) return false;
            return reason.All(ch => ch >= 32 && ch <= 126);
        }

        /// <summary>Format-QualificationStopReason: backslashes and quotes escaped with a backslash.</summary>
        public static string FormatReason(string reason)
        {
            var builder = new StringBuilder();
            foreach (var ch in reason ?? "")
            {
                if (ch == '\\' || ch == '"') builder.Append('\\');
                builder.Append(ch);
            }
            return builder.ToString();
        }

        /// <summary>Get-QualificationStopDryRunText: the planned kills and lock action; nothing is read but the records.</summary>
        public static string DryRunText(QualificationStopOptions options, IFileSystem files)
        {
            var lines = new List<string> { "qualification-stop: dry-run; --confirm is required; no process was stopped" };
            var taskFull = FullOrNull(files, options.TaskRoot);
            var coordFull = FullOrNull(files, options.CoordinationRoot);
            if (taskFull == null || !files.Directory.Exists(taskFull))
            {
                lines.Add("planned kill phase=supervisor status=task-root");
                lines.Add("planned kill phase=driver status=task-root");
            }
            else
            {
                lines.Add(DryRunIdentity(files, "supervisor", files.Path.Combine(taskFull, "supervisor-identity.json")));
                lines.Add(DryRunIdentity(files, "driver", files.Path.Combine(taskFull, "launch-child.json")));
            }
            if (coordFull == null || !files.Directory.Exists(coordFull))
            {
                lines.Add("planned lock action=coordination-root");
            }
            else
            {
                var lockRead = RcQualificationRecords.Read(files, files.Path.Combine(coordFull, "native-run.lock"));
                if (lockRead.Status == "missing") lines.Add("planned lock action=absent");
                else if (!lockRead.Ok) lines.Add("planned lock action=retained-" + lockRead.Status);
                else
                    lines.Add("planned lock ownerPid=" + RcQualificationRecords.Text(RcQualificationRecords.Member(lockRead.Record, "pid")) + " runId="
                        + RcQualificationRecords.Text(RcQualificationRecords.Member(lockRead.Record, "runId")) + " action=move-if-owner-dead");
            }
            if (!string.IsNullOrWhiteSpace(options.RunId)) lines.Add("planned runId=" + options.RunId);
            return string.Join(Environment.NewLine, lines);
        }

        private static string DryRunIdentity(IFileSystem files, string phase, string path)
        {
            var read = RcQualificationRecords.Read(files, path);
            if (!read.Ok) return "planned kill phase=" + phase + " status=" + read.Status;
            var commandLine = RcQualificationRecords.Text(RcQualificationRecords.Member(read.Record, "commandLine"));
            return "planned kill phase=" + phase + " pid=" + RcQualificationRecords.Text(RcQualificationRecords.Member(read.Record, "pid"))
                + " processStartedAt=" + RcQualificationRecords.Text(RcQualificationRecords.Member(read.Record, "processStartedAt"))
                + " commandLine=" + (string.IsNullOrWhiteSpace(commandLine) ? "absent" : "recorded");
        }

        private static string FullOrNull(IFileSystem files, string path)
        {
            try { return files.Path.GetFullPath(path); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return null; }
        }

        /// <summary>Invoke-QualificationRecordedStop (see the file header).</summary>
        public static QualificationStopResult Stop(QualificationStopOptions options, NightlyHost host, IFileSystem files)
        {
            if (!RcQualificationStatus.IdPattern.IsMatch(options.RunId ?? "")) return Refused("run-id");
            if (!ValidReason(options.Reason)) return Refused("reason");
            if (QualificationSupervision.Platform(host) != "Windows") return Result(5, "refused", "windows-only", null, "not-attempted", null, null);
            var seams = QualificationSupervision.Seams(host);
            var taskFull = files.Path.GetFullPath(options.TaskRoot);
            var coordFull = files.Path.GetFullPath(options.CoordinationRoot);
            if (!files.Directory.Exists(taskFull)) return Refused("task-root");
            if (!files.Directory.Exists(coordFull)) return Refused("coordination-root");
            var logPath = files.Path.Combine(taskFull, "supervisor.log");
            var lockPath = files.Path.Combine(coordFull, "native-run.lock");

            var identity = RcQualificationRecords.Read(files, files.Path.Combine(taskFull, "supervisor-identity.json"));
            if (!identity.Ok) return Refused("supervisor-" + identity.Status);
            if (RunIdOf(identity.Record) != options.RunId) return Refused("supervisor-run");
            var driverRead = RcQualificationRecords.Read(files, files.Path.Combine(taskFull, "launch-child.json"));
            JsonElement? driver = null;
            if (driverRead.Status != "missing")
            {
                if (!driverRead.Ok) return Refused("driver-" + driverRead.Status);
                if (RunIdOf(driverRead.Record) != options.RunId) return Refused("driver-run");
                driver = driverRead.Record;
            }
            var lockRead = RcQualificationRecords.Read(files, lockPath);
            var lockState = "missing";
            byte[] preserved = null;
            if (lockRead.Ok)
            {
                lockState = "ok";
                preserved = ExactBytes(files, lockPath);
            }
            else if (lockRead.Status != "missing")
            {
                lockState = "unreadable";
            }

            RcQualificationObservation Observed(JsonElement? record)
            {
                var observation = QualificationSupervision.Observe(record, host);
                if (observation.Status != "alive") return observation;
                var expected = RcQualificationRecords.Text(RcQualificationRecords.Member(record, "commandLine"));
                if (string.IsNullOrWhiteSpace(expected)) return observation;
                var actual = seams.CommandLine((int)RcQualificationRecords.Member(record, "pid").Value.GetInt64());
                if (string.IsNullOrWhiteSpace(actual)) return new RcQualificationObservation("unknown", "command-unreadable");
                if (actual != expected) return new RcQualificationObservation("reused", "command-mismatch");
                return observation;
            }

            var supervisorObs = Observed(identity.Record);
            if (supervisorObs.Status == "reused" || supervisorObs.Status == "unknown") return Refused("supervisor-" + supervisorObs.Status);
            var driverObs = new RcQualificationObservation("absent", "not-recorded");
            if (driver != null)
            {
                driverObs = Observed(driver);
                if (driverObs.Status == "reused" || driverObs.Status == "unknown") return Refused("driver-" + driverObs.Status);
            }

            // Supervisor tree first. A later recorded pid already gone was killed with that tree (descendant-already-absent),
            // so the stop continues to the census; the second taskkill runs only for a driver that outlived the tree. Reused
            // or unknown after a kill stays incomplete.
            var steps = new[] { ("supervisor", identity.Record, supervisorObs.Status), ("driver", driver, driverObs.Status) };
            var kills = new List<QualificationKill>();
            var failed = false;
            try
            {
                foreach (var (phase, record, status) in steps)
                {
                    if (failed) break;
                    if (record == null || status != "alive") continue;
                    var again = Observed(record);
                    if (again.Status != "alive")
                    {
                        if (kills.Count == 0) return Refused(phase + "-generation");
                        if (again.Status == "absent") continue;
                        failed = true;
                        break;
                    }
                    var pid = (int)RcQualificationRecords.Member(record, "pid").Value.GetInt64();
                    var stopped = seams.StopTree(pid) ?? new QualificationTreeStop(false, "taskkill-start-failed", QualificationTreeStop.CommandFor(pid));
                    kills.Add(new QualificationKill(phase, pid, stopped.Ok, stopped.Reason ?? "", stopped.Command ?? ""));
                    if (!stopped.Ok) failed = true;
                }

                var censusOk = !failed;
                if (censusOk)
                {
                    var deadline = host.UtcNow.AddMilliseconds(Math.Max(0, options.CensusMilliseconds));
                    while (true)
                    {
                        var supervisorNow = Observed(identity.Record);
                        var driverNow = driver == null ? new RcQualificationObservation("absent", "not-recorded") : Observed(driver);
                        var pending = supervisorNow.Status != "absent" || (driver != null && driverNow.Status != "absent");
                        if (!pending) break;
                        if (options.CensusMilliseconds <= 0 || host.UtcNow >= deadline)
                        {
                            censusOk = false;
                            break;
                        }
                        host.Sleep(TimeSpan.FromMilliseconds(200));
                    }
                }

                var exit = 3;
                var outcome = "incomplete";
                var lockAction = "not-attempted";
                string retained = null;
                if (censusOk)
                {
                    exit = 0;
                    outcome = "stopped";
                    lockAction = "absent";
                    string kept = null;
                    if (lockState == "missing") { }
                    else if (lockState != "ok") kept = "retained-unreadable";
                    else if (RunIdOf(lockRead.Record) != options.RunId) kept = "retained-foreign";
                    else
                    {
                        var ownerObs = Observed(lockRead.Record);
                        if (ownerObs.Status != "absent") kept = "retained-" + ownerObs.Status;
                        else
                        {
                            var current = ExactBytes(files, lockPath);
                            var reread = RcQualificationRecords.Read(files, lockPath);
                            if (!reread.Ok || RunIdOf(reread.Record) != options.RunId || !Same(preserved, current)) kept = "retained-changed";
                            else
                            {
                                var evidence = string.IsNullOrWhiteSpace(options.EvidenceRoot) ? files.Path.Combine(taskFull, "stop-evidence") : options.EvidenceRoot;
                                var evidenceFull = files.Path.GetFullPath(evidence);
                                if (!files.Directory.Exists(evidenceFull))
                                {
                                    var parent = files.Path.GetDirectoryName(evidenceFull);
                                    if (!string.IsNullOrEmpty(parent) && files.Directory.Exists(parent)) files.Directory.CreateDirectory(evidenceFull);
                                }
                                var stamp = host.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
                                var destination = files.Path.Combine(evidenceFull, "native-run.lock.moved-" + stamp);
                                if (files.File.Exists(destination) || !SameVolume(files, coordFull, evidenceFull)) kept = "retained-volume";
                                else
                                {
                                    try
                                    {
                                        files.File.Move(lockPath, destination);
                                        var moved = ExactBytes(files, destination);
                                        if (Same(preserved, moved) && !files.File.Exists(lockPath))
                                        {
                                            lockAction = "moved";
                                            retained = destination;
                                        }
                                        else
                                        {
                                            kept = "retained-move-mismatch";
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        kept = "retained-move-failed";
                                    }
                                }
                            }
                        }
                    }
                    if (kept != null)
                    {
                        lockAction = kept;
                        exit = 4;
                        outcome = "lock-retained";
                    }
                }

                var line = "STOP reason=\"" + FormatReason(options.Reason) + "\" outcome=" + outcome + " lock=" + lockAction + Killed(kills);
                try { QualificationFiles.WriteLine(files, logPath, line, host.UtcNow); }
                catch (Exception) { }
                return Result(exit, outcome, line, kills, lockAction, retained, logPath);
            }
            catch (Exception)
            {
                var faultLine = "STOP reason=\"" + FormatReason(options.Reason) + "\" outcome=incomplete lock=not-attempted detail=stop-fault" + Killed(kills);
                try { QualificationFiles.WriteLine(files, logPath, faultLine, host.UtcNow); }
                catch (Exception) { }
                return Result(3, "incomplete", "stop-fault" + Killed(kills), kills, "not-attempted", null, logPath);
            }
        }

        private static string Killed(IReadOnlyList<QualificationKill> kills) =>
            kills.Count == 0 ? "" : " killed=" + string.Join(",", kills.Select(kill => kill.Phase + ":" + kill.Pid.ToString(CultureInfo.InvariantCulture)));

        private static string RunIdOf(JsonElement? record) => RcQualificationRecords.Text(RcQualificationRecords.Member(record, "runId"));

        private static QualificationStopResult Refused(string detail) => Result(2, "refused", detail, null, "not-attempted", null, null);

        private static QualificationStopResult Result(int exit, string outcome, string detail, IReadOnlyList<QualificationKill> kills, string lockAction, string retained, string logPath) =>
            new QualificationStopResult
            {
                ExitCode = exit,
                Outcome = outcome,
                Detail = detail,
                Kills = kills?.ToArray() ?? Array.Empty<QualificationKill>(),
                LockAction = lockAction,
                RetainedLockPath = retained,
                LogPath = logPath,
            };

        // Read-QualificationExactBytes: the whole file through the shared read, at most 1 MiB; null when it cannot be read whole.
        private static byte[] ExactBytes(IFileSystem files, string path)
        {
            try
            {
                using var stream = RcQualificationRecords.OpenSharedRead(files, path);
                if (stream.Length > RcQualificationRecords.MaxBytes) return null;
                var buffer = new byte[stream.Length];
                var count = 0;
                while (count < buffer.Length)
                {
                    var got = stream.Read(buffer, count, buffer.Length - count);
                    if (got <= 0) break;
                    count += got;
                }
                return count == buffer.Length ? buffer : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Same(byte[] left, byte[] right) => left != null && right != null && left.AsSpan().SequenceEqual(right);

        // Test-QualificationSameVolume on Windows (the stop refuses every other host): the same path root, compared exactly.
        private static bool SameVolume(IFileSystem files, string left, string right)
        {
            try
            {
                var leftRoot = files.Path.GetPathRoot(files.Path.GetFullPath(left));
                var rightRoot = files.Path.GetPathRoot(files.Path.GetFullPath(right));
                return !string.IsNullOrWhiteSpace(leftRoot) && !string.IsNullOrWhiteSpace(rightRoot) && leftRoot == rightRoot;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
