// CARD-2087: qualification facts for the operator's read-only status command.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    internal static class OperatorRcStatus
    {
        private static readonly Regex CandidateName = new Regex(@"^rc-\d{4}-\d{2}-\d{2}-[a-f0-9]{12}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        internal static JsonObject Read(StatusSnapshotOptions options, IFileSystem files, DateTimeOffset now, List<string> errors)
        {
            var logDir = options.RcRunLogDir;
            var stateDir = options.RcStateDir;
            var heartbeatPath = options.RcHeartbeatPath;
            var releaseRoot = options.RcReleaseRoot;
            var report = OperatorStatusShape.RcSkeleton();
            JsonObject state = null;
            JsonObject schedule = null;

            try
            {
                // The desktop default is fully qualified only on Windows. Explicit paths
                // also let a host read a copied run using its own filesystem conventions.
                var comparable = false;
                if (!string.IsNullOrEmpty(logDir))
                {
                    logDir = files.Path.TrimEndingDirectorySeparator(files.Path.GetFullPath(logDir));
                    if (string.IsNullOrEmpty(releaseRoot))
                        releaseRoot = FindReleaseRoot(files, logDir);
                    schedule = ReadRecord(files, files.Path.Combine(logDir, "dispatch-schedule.json"), errors);
                }
                if (string.IsNullOrEmpty(releaseRoot) && files.Path.IsPathFullyQualified(StatusSnapshotOptions.DesktopReleases))
                    releaseRoot = StatusSnapshotOptions.DesktopReleases;
                var runId = Text(schedule, "runId");
                if (!string.IsNullOrEmpty(stateDir))
                {
                    state = ReadRecord(files, files.Path.Combine(stateDir, "last-run.json"), errors, required: false);
                    if (string.IsNullOrEmpty(releaseRoot))
                        releaseRoot = files.DirectoryInfo.New(stateDir).Parent?.FullName;
                }
                else if (!string.IsNullOrEmpty(releaseRoot))
                {
                    var states = Directories(files, releaseRoot, "state-*")
                        .Select(dir => (Dir: dir, State: ReadRecord(files, files.Path.Combine(dir, "last-run.json"), errors, required: false)))
                        .Where(item => item.State != null && Text(item.State, "lane") == "rc")
                        .Where(item => runId != null ? Text(item.State, "runId") == runId
                            : !string.IsNullOrEmpty(logDir) ? SamePath(files, Text(item.State, "logDir"), logDir)
                            : item.State["completedAt"] == null && item.State["exitCode"] == null)
                        .ToArray();
                    if (states.Length == 1) (stateDir, state) = states[0];
                    else if (states.Length > 1) errors.Add("rc state is ambiguous; specify --rc-state-dir and --rc-run-log-dir.");
                }

                if (runId == null && state != null && (string.IsNullOrEmpty(logDir) || SamePath(files, Text(state, "logDir"), logDir)))
                    runId = Text(state, "runId");
                heartbeatPath = FindHeartbeat(files, releaseRoot, heartbeatPath, runId, errors);
                var taskRoot = string.IsNullOrEmpty(heartbeatPath) ? null : files.Path.GetDirectoryName(files.Path.GetFullPath(heartbeatPath));
                var supervisor = taskRoot == null ? null : ReadRecord(files, files.Path.Combine(taskRoot, "supervisor-identity.json"), errors, required: false);
                bool Matches(JsonObject record) =>
                    runId != null && Text(record, "runId") == runId &&
                    (string.IsNullOrEmpty(logDir) || Text(record, "logDir") == null || SamePath(files, Text(record, "logDir"), logDir));
                if (!Matches(state))
                {
                    var copy = taskRoot == null || Text(supervisor, "runId") != runId ? null
                        : ReadRecord(files, files.Path.Combine(taskRoot, "own-last-run.json"), errors, required: false);
                    if (Matches(copy))
                        state = copy;
                    else
                    {
                        errors.Add("rc last-run.json unavailable for the selected run.");
                        state = null;
                    }
                }

                if (string.IsNullOrEmpty(logDir) && state != null)
                {
                    logDir = Text(state, "logDir");
                    if (!string.IsNullOrEmpty(logDir))
                        schedule = ReadRecord(files, files.Path.Combine(logDir, "dispatch-schedule.json"), errors);
                }
                runId = Text(state, "runId") ?? runId;
                if (schedule != null && Text(schedule, "runId") != runId)
                {
                    errors.Add("rc dispatch schedule belongs to a different run or has no run id.");
                    schedule = null;
                }

                var candidate = Text(state, "candidateId") ?? CandidateFromLog(files, logDir);
                var pin = Text(state, "expectedSha") ?? Text(state, "sha");
                var started = Time(Text(state, "startedAt"));
                var completed = Time(Text(state, "completedAt"));
                report["candidateId"] = candidate;
                report["pin"] = pin;
                report["runId"] = runId;
                report["phase"] = Text(state, "phase");
                report["elapsedSeconds"] = started == null ? null : Math.Max(0, ((completed ?? now) - started.Value).TotalSeconds);
                report["lastRun"] = state;
                report["runLogDir"] = logDir;
                report["stateDir"] = stateDir;
                if (runId == null || candidate == null || pin == null || started == null)
                    errors.Add("rc run identity or start time unavailable; candidate, pin, run id and elapsed time require the matching state.");

                if (!string.IsNullOrEmpty(logDir))
                {
                    var scope = Text(state, "coverageScope");
                    if (candidate != null && CandidateFromLog(files, logDir) is string logCandidate && logCandidate != candidate)
                    {
                        errors.Add("rc selected state and log directory name different candidates.");
                        schedule = null;
                    }
                    var platform = Text(state, "platform");
                    comparable = (scope == null || scope == "Full") &&
                        (platform == null || platform.Equals("windows", StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    errors.Add("rc run log directory unavailable; progress, failures and ETA are unknown.");
                }

                var timingRoot = Text(state, "timingRoot");
                var launchPath = Text(supervisor, "launchRecord");
                if (timingRoot == null && launchPath != null && Text(supervisor, "runId") == runId)
                {
                    var launch = ReadRecord(files, launchPath, errors, required: false);
                    if (launch?["nightlyArguments"] is JsonArray args)
                    {
                        var arguments = args.Select(arg => arg is JsonValue value && value.TryGetValue<string>(out var text) ? text : "").ToArray();
                        if (QualificationLaunch.ArgumentValue(arguments, "RunId") == runId)
                            timingRoot = QualificationLaunch.ArgumentValue(arguments, "TimingRoot");
                    }
                }
                var defaultTiming = string.IsNullOrEmpty(logDir) || runId == null ? null
                    : NightlyPaths.TimingRoot("", files.Path.GetDirectoryName(logDir), runId);
                if (!string.IsNullOrEmpty(logDir) || timingRoot != null)
                {
                    var locations = new[] { timingRoot, defaultTiming, string.IsNullOrEmpty(logDir) ? null : files.Path.Combine(logDir, "timing"), logDir }
                        .Where(dir => !string.IsNullOrEmpty(dir)).Distinct(StringComparer.OrdinalIgnoreCase);
                    foreach (var dir in locations)
                    {
                        var active = ReadRecord(files, files.Path.Combine(dir, "active-chunk.json"), errors, required: false);
                        if (active == null) continue;
                        report["activeChunk"] = BindActive(active, runId, errors);
                        break;
                    }
                    if (report["activeChunk"] == null && completed == null)
                        errors.Add("rc active chunk unavailable.");
                }

                if (!string.IsNullOrEmpty(logDir))
                    OperatorTrxStatus.PopulateReport(report, OperatorTrxStatus.Read(files, logDir, schedule,
                        report["activeChunk"] as JsonObject, now, started, completed.HasValue, comparable,
                        options.RcPreviousRunLogDir, errors, reportFailures: true));

                if (string.IsNullOrEmpty(heartbeatPath))
                    errors.Add("rc supervisor heartbeat path unavailable.");
                else
                    ReadHeartbeat(report, files, heartbeatPath, Text(supervisor, "runId"), runId, now, errors);
            }
            catch (Exception error) when (ReadError(error))
            {
                errors.Add("rc filesystem observation unavailable (" + error.GetType().Name + ").");
            }

            return report;
        }

        private static string FindHeartbeat(IFileSystem files, string root, string path, string runId, List<string> errors)
        {
            if (!string.IsNullOrEmpty(path) || runId == null || string.IsNullOrEmpty(root)) return path;
            var matches = Directories(files, root, "task-*")
                .Where(dir => Text(ReadRecord(files, files.Path.Combine(dir, "supervisor-identity.json"), errors, required: false), "runId") == runId)
                .ToArray();
            if (matches.Length > 1) errors.Add("rc supervisor is ambiguous; specify --rc-heartbeat-path.");
            return matches.Length == 1 ? files.Path.Combine(matches[0], "supervisor-heartbeat.json") : null;
        }

        private static void ReadHeartbeat(JsonObject report, IFileSystem files, string path, string supervisorRun, string runId,
            DateTimeOffset now, List<string> errors)
        {
            var heartbeat = ReadRecord(files, path, errors);
            if (heartbeat == null) return;
            var claimedRun = Text(heartbeat, "runId");
            var boundRun = supervisorRun ?? claimedRun;
            if (runId == null || boundRun != runId || (claimedRun != null && claimedRun != runId))
            {
                errors.Add("rc supervisor-heartbeat.json cannot be bound to the selected run.");
                return;
            }
            var stamp = Time(Text(heartbeat, "observedAtUtc") ?? Text(heartbeat, "utc"));
            report["heartbeat"] = heartbeat;
            var age = stamp == null ? (TimeSpan?)null : now - stamp.Value;
            report["heartbeatAgeSeconds"] = age?.TotalSeconds;
            if (age == null || heartbeat["idleMinutes"] is not JsonValue minutes || !minutes.TryGetValue<double>(out _)
                || heartbeat["hangSuspected"] is not JsonValue hang || !hang.TryGetValue<bool>(out _))
                errors.Add("rc supervisor-heartbeat.json timestamp, idle minutes or hang flag unavailable.");
            else if (age.Value < TimeSpan.Zero)
                errors.Add("rc supervisor-heartbeat.json observation time is ahead of this host's clock.");
        }

        internal static IEnumerable<IGrouping<DateTime?, string>> PreviousRunsByStart(IFileSystem files, string live)
        {
            var root = FindReleaseRoot(files, live);
            var liveStamp = RunStamp(files, live);
            if (root == null || liveStamp == null) return Array.Empty<IGrouping<DateTime?, string>>();
            return Directories(files, files.Path.Combine(root, "candidates"), "rc-*")
                .Where(dir => CandidateName.IsMatch(files.Path.GetFileName(dir)))
                .SelectMany(dir => Directories(files, files.Path.Combine(dir, "logs"), "*"))
                .Select(dir => (Dir: dir, Start: RunStamp(files, dir)))
                .Where(item => item.Start < liveStamp)
                .OrderByDescending(item => item.Start)
                .GroupBy(item => item.Start, item => item.Dir);
        }

        private static DateTime? RunStamp(IFileSystem files, string dir)
        {
            var name = files.Path.GetFileName(dir);
            return name.Length > 16 && name[15] == '-' &&
                DateTime.TryParseExact(name.Substring(0, 15), "yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                ? start : null;
        }

        private static string CandidateFromLog(IFileSystem files, string logDir)
        {
            if (string.IsNullOrEmpty(logDir)) return null;
            var logs = files.DirectoryInfo.New(logDir).Parent;
            var candidate = logs?.Parent?.Name;
            return logs?.Name == "logs" && candidate != null && CandidateName.IsMatch(candidate) ? candidate : null;
        }

        private static string FindReleaseRoot(IFileSystem files, string logDir)
        {
            if (CandidateFromLog(files, logDir) == null) return null;
            var candidates = files.DirectoryInfo.New(logDir).Parent?.Parent?.Parent;
            return candidates?.Name == "candidates" ? candidates.Parent?.FullName : null;
        }

        internal static IEnumerable<string> Directories(IFileSystem files, string root, string pattern)
        {
            if (!files.Directory.Exists(root)) return Array.Empty<string>();
            return files.Directory.EnumerateDirectories(root, pattern, new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            });
        }

        internal static JsonObject ReadRecord(IFileSystem files, string path, List<string> errors, bool required = true)
        {
            var read = RcQualificationRecords.Read(files, path);
            if (read.Ok) return JsonNode.Parse(read.Record.Value.GetRawText()).AsObject();
            if (required || read.Status != "missing")
                errors?.Add("rc " + files.Path.GetFileName(path) + " unavailable (" + read.Reason + ").");
            return null;
        }

        internal static JsonObject BindActive(JsonObject active, string runId, List<string> errors)
        {
            var activeRun = Text(active, "runId");
            if (activeRun == null || activeRun == runId) return active;
            errors.Add("rc active-chunk.json belongs to a different run.");
            return null;
        }

        internal static bool SamePath(IFileSystem files, string left, string right) =>
            !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
            && string.Equals(files.Path.TrimEndingDirectorySeparator(files.Path.GetFullPath(left)),
                files.Path.TrimEndingDirectorySeparator(files.Path.GetFullPath(right)),
                files.Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        internal static string Text(JsonNode obj, string key) =>
            obj is JsonObject fields && fields[key] is JsonValue value && value.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text) ? text : null;

        internal static DateTimeOffset? Time(string text) =>
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant) ? instant : null;

        internal static bool ReadError(Exception error) =>
            error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is NotSupportedException || error is System.Security.SecurityException;
    }
}
