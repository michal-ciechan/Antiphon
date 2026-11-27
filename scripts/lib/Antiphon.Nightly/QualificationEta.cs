// CARD-2093: the CLI and watcher print the same scheduled TRX projection used by operator status.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The qualification-eta command's inputs.</summary>
    public sealed class QualificationEtaOptions
    {
        public const string Usage = "qualification-eta --timing-root <dir> [--run-log-dir <dir>] [--previous-run-log-dir <dir>] [--now-utc <iso-8601>]";

        private static readonly string[] Valued = { "--timing-root", "--run-log-dir", "--previous-run-log-dir", "--now-utc" };

        public string TimingRoot { get; init; } = "";
        public string RunLogDir { get; init; } = "";
        public string PreviousRunLogDir { get; init; } = "";
        public DateTime? NowUtc { get; init; }

        public static QualificationEtaOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            DateTime? now = null;
            var text = flags.Value("--now-utc", "");
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                    throw new NightlyUsageException("--now-utc expects an ISO-8601 instant, not '" + text + "'");
                now = parsed;
            }
            return new QualificationEtaOptions
            {
                TimingRoot = flags.Required("--timing-root"),
                RunLogDir = flags.Value("--run-log-dir", ""),
                PreviousRunLogDir = flags.Value("--previous-run-log-dir", ""),
                NowUtc = now,
            };
        }
    }

    internal sealed record QualificationEtaReading(
        int Outstanding, int Running, int Completed, string[] UnknownRemaining,
        (string Chunk, double Seconds)[] KnownRemaining, int FinishedWithoutBaseline, int MatchedFinished,
        string PreviousRunLogDir, double Ratio, double BaselineSeconds, double? PointSeconds, DateTimeOffset? P50Utc,
        DateTimeOffset? P95Utc, string Confidence)
    {
        internal bool LowerBound => UnknownRemaining.Length > 0;

        internal void Write(TextWriter output)
        {
            var point = PointSeconds?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unavailable";
            output.WriteLine(FormattableString.Invariant(
                $"ETA outstanding={Outstanding} running={Running} completed={Completed} unpriced={UnknownRemaining.Length} pointSeconds={point} p50={FormatUtc(P50Utc)} p95={FormatUtc(P95Utc)} confidence={Confidence} lowerBound={(LowerBound ? "true" : "false")}"));
        }

        private static string FormatUtc(DateTimeOffset? value) =>
            value?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) ?? "unavailable";
    }

    /// <summary>The read-only qualification ETA command.</summary>
    public static class QualificationEta
    {
        /// <summary>Prints the ETA and any unverified inputs. Exit 1 when the live run cannot be read.</summary>
        public static int Run(QualificationEtaOptions options, NightlyHost host, IFileSystem files)
        {
            if (TryWrite(options, host, files, out var error)) return 0;
            host.Error.WriteLine("qualification-eta: " + error);
            return 1;
        }

        internal static bool TryWrite(QualificationEtaOptions options, NightlyHost host, IFileSystem files, out string error)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(files);

            var issues = new List<string>();
            try
            {
                var input = ReadInputs(options, files, issues);
                if (input.Schedule != null)
                {
                    var observed = OperatorTrxStatus.Read(files, input.LogDir, input.Schedule, input.Active,
                        (options.NowUtc ?? host.UtcNow).ToUniversalTime(), null, completed: false, comparable: true,
                        options.PreviousRunLogDir, issues, reportFailures: false);
                    if (observed?.Eta != null)
                    {
                        observed.Eta.Write(host.Out);
                        foreach (var issue in issues) host.Out.WriteLine("UNVERIFIED " + issue);
                        error = "";
                        return true;
                    }
                }
            }
            catch (Exception e) when (OperatorRcStatus.ReadError(e))
            {
                issues.Add(e.Message);
            }
            error = string.Join(" ", issues);
            return false;
        }

        private static (string LogDir, JsonObject Schedule, JsonObject Active) ReadInputs(
            QualificationEtaOptions options, IFileSystem files, List<string> issues)
        {
            if (string.IsNullOrWhiteSpace(options.TimingRoot) || !files.Directory.Exists(options.TimingRoot))
            {
                issues.Add("timing-root-missing " + options.TimingRoot);
                return default;
            }

            var root = files.Path.TrimEndingDirectorySeparator(files.Path.GetFullPath(options.TimingRoot));
            var active = OperatorRcStatus.ReadRecord(files, files.Path.Combine(root, "active-chunk.json"), issues, required: false);
            var localSchedule = files.Path.Combine(root, "dispatch-schedule.json");
            const string prefix = "timing-";
            var name = files.Path.GetFileName(root);
            var rootRunId = name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length
                ? name.Substring(prefix.Length) : null;
            var activeRunId = OperatorRcStatus.Text(active, "runId");
            var hasExplicitLog = !string.IsNullOrWhiteSpace(options.RunLogDir);
            string logDir;
            JsonObject schedule;
            if (hasExplicitLog || files.File.Exists(localSchedule))
            {
                logDir = hasExplicitLog ? files.Path.GetFullPath(options.RunLogDir) : root;
                schedule = OperatorRcStatus.ReadRecord(files, files.Path.Combine(logDir, "dispatch-schedule.json"), issues);
                if (schedule == null) return default;
            }
            else
            {
                // The log directory uses the first eight characters. Verify the full run id in the schedule
                // so a collision with another attempt cannot select its timings.
                var runId = rootRunId ?? activeRunId;
                var parent = files.Path.GetDirectoryName(root);
                var candidates = string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(runId)
                    ? Array.Empty<(string Dir, JsonObject Schedule)>()
                    : FindSchedules(files, parent, runId, issues);
                if (candidates.Length != 1)
                {
                    issues.Add(candidates.Length == 0
                        ? "rc run log directory not found for the timing root; supply --run-log-dir."
                        : "rc run log directory is ambiguous; supply --run-log-dir.");
                    return default;
                }
                (logDir, schedule) = candidates[0];
            }

            var scheduledRun = OperatorRcStatus.Text(schedule, "runId");
            if (scheduledRun == null)
            {
                issues.Add("rc dispatch schedule has no run id.");
                return default;
            }
            var localRun = hasExplicitLog && !OperatorRcStatus.SamePath(files, root, logDir)
                ? OperatorRcStatus.Text(OperatorRcStatus.ReadRecord(files, localSchedule, issues, required: false), "runId")
                : null;
            if ((rootRunId != null && rootRunId != scheduledRun) || (localRun != null && localRun != scheduledRun))
            {
                issues.Add("rc timing root and dispatch schedule belong to different runs.");
                return default;
            }
            if (hasExplicitLog && activeRunId != null && activeRunId != scheduledRun)
            {
                issues.Add("rc active-chunk.json belongs to a different run.");
                return default;
            }
            return (logDir, schedule, OperatorRcStatus.BindActive(active, scheduledRun, issues));
        }

        private static (string Dir, JsonObject Schedule)[] FindSchedules(IFileSystem files, string parent, string runId, List<string> issues)
        {
            var shortSuffix = "-" + runId.Substring(0, Math.Min(8, runId.Length));
            return OperatorRcStatus.Directories(files, parent, "*")
                .Where(dir => files.Path.GetFileName(dir).EndsWith(shortSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(dir => (Dir: dir, Schedule: OperatorRcStatus.ReadRecord(files, files.Path.Combine(dir, "dispatch-schedule.json"), issues, required: false)))
                .Where(candidate => OperatorRcStatus.Text(candidate.Schedule, "runId") == runId)
                .Take(2)
                .ToArray();
        }
    }
}
