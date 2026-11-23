// The rc qualification watcher (CARD-1842 S5 for the ETA line). It replaced scripts/orchestration/watch-qual.ps1 (deleted
// 2026-11-23, unit 6 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts watch-qual` (Run) from a Monitor task (.claude/skills/antiphon-rc-qualification/SKILL.md).
//
// It observes completed TRX failures, qualification completion and heartbeat stalls without touching the run. It writes
// only the caller-owned state file (one completed TRX path per line, reused on re-arm; it must be outside the observed
// run) and starts, stops and locks nothing. Each poll, in order:
//   - LOG-DIR-MISSING when the log directory is not there; else, for each *.trx not yet in the state file and at least
//     --min-file-age-seconds old (oldest first): "FAILURES <name>: <n> failed :: <Class> <n>, ..." (the eight classes
//     with most failures; Failed, Error, Timeout and Aborted results, as TrxReport counts them) and the path recorded,
//     or TRX-PARSE-ERROR for a file that is not a complete TRX yet (no ResultSummary): it stays unrecorded and is retried;
//   - with --timing-root, the one QualificationEta line (CARD-1842), or ETA-ERROR; without it no ETA line;
//   - RUN-ENDED when summary.json is in the log directory, which ends the watch;
//   - HANG-SUSPECTED and HEARTBEAT-STALE when the heartbeat (utc, else observedAtUtc) turns hung or stale, again after
//     it recovers; HEARTBEAT-MISSING and HEARTBEAT-ERROR once per change.
// Then it sleeps (the host's Sleep, clamped to the window) until --max-minutes have passed, or stops after one poll with
// --once, and prints "watch-qual window ended; re-arm" unless the run ended. Heartbeat stalls are a detection, never stop
// authority. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.XPath;

namespace Antiphon.Nightly
{
    /// <summary>The watch-qual command's options.</summary>
    public sealed class WatchQualOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "watch-qual --log-dir <dir> --state-file <file> --heartbeat <heartbeat.json> [--poll-seconds 60] [--max-minutes 28]"
            + " [--min-file-age-seconds 5] [--heartbeat-stale-minutes 5] [--timing-root <dir>] [--once]";

        private static readonly string[] Valued =
        {
            "--log-dir", "--state-file", "--heartbeat", "--poll-seconds", "--max-minutes", "--min-file-age-seconds", "--heartbeat-stale-minutes", "--timing-root",
        };

        private static readonly string[] Switches = { "--once" };

        /// <summary>The observed run's log directory (its TRX files and summary.json).</summary>
        public string LogDir { get; init; } = "";

        /// <summary>The caller-owned state file: the completed TRX paths already reported, one per line.</summary>
        public string StateFile { get; init; } = "";

        /// <summary>The supervisor heartbeat (supervisor-heartbeat.json).</summary>
        public string Heartbeat { get; init; } = "";

        /// <summary>Seconds between polls, 1 to 300.</summary>
        public int PollSeconds { get; init; } = 60;

        /// <summary>The watch window in minutes, 1 to 29 (under the Monitor's 30-minute timeout).</summary>
        public int MaxMinutes { get; init; } = 28;

        /// <summary>A TRX younger than this many seconds (0 to 300) waits for the next poll.</summary>
        public int MinFileAgeSeconds { get; init; } = 5;

        /// <summary>A heartbeat this many minutes old (1 to 60) is stale.</summary>
        public int HeartbeatStaleMinutes { get; init; } = 5;

        /// <summary>The qualification timing root whose ETA line each poll prints; empty for no ETA line.</summary>
        public string TimingRoot { get; init; } = "";

        /// <summary>One poll, then stop (offline checks).</summary>
        public bool Once { get; init; }

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line, a missing directory or file flag, or a value out of range.</summary>
        public static WatchQualOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new WatchQualOptions
            {
                LogDir = flags.Required("--log-dir"),
                StateFile = flags.Required("--state-file"),
                Heartbeat = flags.Required("--heartbeat"),
                PollSeconds = Ranged(flags, "--poll-seconds", 60, 1, 300),
                MaxMinutes = Ranged(flags, "--max-minutes", 28, 1, 29),
                MinFileAgeSeconds = Ranged(flags, "--min-file-age-seconds", 5, 0, 300),
                HeartbeatStaleMinutes = Ranged(flags, "--heartbeat-stale-minutes", 5, 1, 60),
                TimingRoot = flags.Value("--timing-root", ""),
                Once = flags.Switch("--once"),
            };
        }

        private static int Ranged(NightlyArgs flags, string flag, int fallback, int minimum, int maximum)
        {
            var value = flags.Int(flag, fallback);
            if (value < minimum || value > maximum)
                throw new NightlyUsageException(flag + " expects " + minimum.ToString(CultureInfo.InvariantCulture) + " to "
                    + maximum.ToString(CultureInfo.InvariantCulture) + ", not " + value.ToString(CultureInfo.InvariantCulture));
            return value;
        }
    }

    /// <summary>The rc qualification watcher.</summary>
    public static class WatchQual
    {
        /// <summary>The TRX namespace (VisualStudio TeamTest 2010).</summary>
        public const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        // The failure set of TrxReport and run-checkpoint.ps1 (and of TrxFailures).
        internal const string FailedResults = "//t:UnitTestResult[@outcome='Failed' or @outcome='Error' or @outcome='Timeout' or @outcome='Aborted']";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// The watch-qual command: the poll lines on host.Out as they happen, the clock and the sleep from the host. Exit
        /// 0 when the window or the run ended; 1 when the state file cannot be read (one line on host.Error).
        /// </summary>
        public static int Run(WatchQualOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            // Full paths: a re-arm with another log directory never mistakes one run's TRX for another's of the same name.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (files.File.Exists(options.StateFile) || files.Directory.Exists(options.StateFile))
            {
                try
                {
                    foreach (var line in files.File.ReadAllLines(options.StateFile))
                        if (line.Length > 0) seen.Add(line);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    host.Error.WriteLine("watch-qual: state-file-unreadable " + options.StateFile + ": " + e.Message);
                    return 1;
                }
            }
            var heartbeat = new HeartbeatState();
            var deadline = host.UtcNow.AddMinutes(options.MaxMinutes);
            var ended = false;
            do
            {
                if (!files.Directory.Exists(options.LogDir)) host.Out.WriteLine("LOG-DIR-MISSING " + options.LogDir);
                else ReadTrx(options, host, files, seen);
                if (!string.IsNullOrWhiteSpace(options.TimingRoot))
                {
                    // The ETA only reads the timing root.
                    host.Out.WriteLine(QualificationEta.TryRead(files, options.TimingRoot, host.UtcNow, "", out var reading, out var error)
                        ? reading.Line
                        : "ETA-ERROR " + error);
                }
                var summary = files.Path.Combine(options.LogDir, "summary.json");
                if (files.File.Exists(summary) || files.Directory.Exists(summary))
                {
                    host.Out.WriteLine("RUN-ENDED summary.json present in " + options.LogDir);
                    ended = true;
                    break;
                }
                ReadHeartbeat(options, host, files, heartbeat);
                if (options.Once || host.UtcNow >= deadline) break;
                var remaining = Math.Max(0.0, Math.Floor((deadline - host.UtcNow).TotalMilliseconds));
                if (remaining > 0) host.Sleep(TimeSpan.FromMilliseconds(Math.Min(options.PollSeconds * 1000.0, remaining)));
            }
            while (host.UtcNow < deadline);
            if (!ended) host.Out.WriteLine("watch-qual window ended; re-arm");
            return 0;
        }

        /// <summary>
        /// The failure line of a complete TRX ("FAILURES name: n failed :: Class n, ..."), null when it has no failed
        /// result. Throws InvalidDataException for a document that is not a TeamTest 2010 TestRun or has no ResultSummary
        /// yet, and XmlException for one that is not well formed.
        /// </summary>
        public static string FailureLine(string name, string text)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text ?? ""), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                document.Load(reader);
            var ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("t", TrxNamespace);
            var root = document.DocumentElement;
            if (root == null || root.LocalName != "TestRun" || root.NamespaceURI != TrxNamespace) throw new InvalidDataException("Not a supported TRX TestRun.");
            // A complete TRX has a ResultSummary; a partially written document is retried.
            if (document.SelectSingleNode("/t:TestRun/t:ResultSummary", ns) == null) throw new InvalidDataException("TRX has no ResultSummary yet.");
            var failed = document.SelectNodes(FailedResults, ns).Cast<XmlElement>().ToArray();
            if (failed.Length == 0) return null;
            var classes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement test in document.SelectNodes("//t:UnitTest", ns))
            {
                if (test.SelectSingleNode("t:TestMethod", ns) is XmlElement method) classes[test.GetAttribute("id")] = method.GetAttribute("className");
            }
            var groups = failed
                .Select(result => classes.TryGetValue(result.GetAttribute("testId"), out var type) && type.Length > 0 ? type : "unknown")
                .Select(type => type.Split('.').Last())
                .GroupBy(type => type, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Take(8)
                .Select(group => group.Key + " " + group.Count().ToString(CultureInfo.InvariantCulture));
            return "FAILURES " + name + ": " + failed.Length.ToString(CultureInfo.InvariantCulture) + " failed :: " + string.Join(", ", groups);
        }

        private static void ReadTrx(WatchQualOptions options, NightlyHost host, IFileSystem files, HashSet<string> seen)
        {
            var trx = files.DirectoryInfo.New(options.LogDir).EnumerateFiles()
                .Where(file => file.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            foreach (var file in trx)
            {
                if (seen.Contains(file.FullName)) continue;
                if ((host.UtcNow - file.LastWriteTimeUtc).TotalSeconds < options.MinFileAgeSeconds) continue;
                try
                {
                    var line = FailureLine(file.Name, files.File.ReadAllText(file.FullName));
                    if (line != null) host.Out.WriteLine(line);
                    files.File.AppendAllText(options.StateFile, file.FullName + Environment.NewLine, Utf8NoBom);
                    seen.Add(file.FullName);
                }
                catch (Exception e) when (e is XmlException || e is XPathException || e is InvalidDataException || e is IOException || e is UnauthorizedAccessException)
                {
                    // Not recorded: the next poll reads it again.
                    host.Out.WriteLine("TRX-PARSE-ERROR " + file.Name + ": " + e.Message);
                }
            }
        }

        private sealed class HeartbeatState
        {
            public bool WasHang;
            public bool WasStale;
            public string LastIssue = "";
        }

        private static void ReadHeartbeat(WatchQualOptions options, NightlyHost host, IFileSystem files, HeartbeatState state)
        {
            if (!files.File.Exists(options.Heartbeat) && !files.Directory.Exists(options.Heartbeat))
            {
                if (state.LastIssue != "missing") host.Out.WriteLine("HEARTBEAT-MISSING " + options.Heartbeat);
                state.LastIssue = "missing";
                return;
            }
            try
            {
                using var document = JsonDocument.Parse(files.File.ReadAllText(options.Heartbeat));
                var beat = document.RootElement;
                // The supervisor writes utc; the repository heartbeat writer observedAtUtc.
                var stamp = Stamp(beat, "utc") ?? Stamp(beat, "observedAtUtc");
                if (stamp == null) throw new InvalidDataException("Heartbeat has no utc or observedAtUtc.");
                var utc = DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime;
                var hang = QualificationEta.Member(beat, "hangSuspected")?.ValueKind == JsonValueKind.True;
                var stale = (host.UtcNow - utc).TotalMinutes >= options.HeartbeatStaleMinutes;
                if (hang && !state.WasHang)
                    host.Out.WriteLine("HANG-SUSPECTED heartbeat " + stamp + " runId=" + Field(beat, "runId") + " idleMinutes=" + Field(beat, "idleMinutes"));
                if (stale && !state.WasStale)
                    host.Out.WriteLine("HEARTBEAT-STALE " + stamp + " runId=" + Field(beat, "runId") + " supervisorPid=" + Field(beat, "supervisorPid") + " trx=" + Field(beat, "trx"));
                state.WasHang = hang;
                state.WasStale = stale;
                state.LastIssue = "";
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidDataException || e is IOException || e is UnauthorizedAccessException)
            {
                var issue = "HEARTBEAT-ERROR " + e.Message;
                if (issue != state.LastIssue) host.Out.WriteLine(issue);
                state.LastIssue = issue;
            }
        }

        // A heartbeat instant: a non-empty string member, else null.
        private static string Stamp(JsonElement beat, string name)
        {
            var value = QualificationEta.Member(beat, name);
            return value?.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.Value.GetString()) ? value.Value.GetString() : null;
        }

        private static string Field(JsonElement beat, string name) => QualificationEta.Text(QualificationEta.Member(beat, name));
    }
}
