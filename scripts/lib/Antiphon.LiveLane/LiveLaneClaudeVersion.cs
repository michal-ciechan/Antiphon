// The Claude CLI version as its own receipt axis (W2 slice S22; the owner, 2026-11-26: "There should also be a way to say
// digest vs what versions of Claude it was tested against so we can verify multiple version of Claude"). A run asks the
// executable it targets for `<exe> --version` through the process seam (a fake in every test; nothing here starts a
// process by itself) and records a stable token (for example 2.1.3) with the raw line beside it. A receipt of
// schemaVersion 2 carries both (LiveLaneExecutionReceipt); an older receipt has neither and reads as "unknown". Plain C#,
// framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>A Claude CLI version: the normalised token and the raw line it came from (null when there was none).</summary>
    public sealed class LiveLaneClaudeVersion
    {
        /// <summary>The token of a version that was not recorded or could not be read.</summary>
        public const string UnknownToken = "unknown";

        /// <summary>The longest raw line kept.</summary>
        public const int RawLimit = 256;

        /// <summary>How long `<exe> --version` may take.</summary>
        public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

        public static readonly LiveLaneClaudeVersion Unknown = new LiveLaneClaudeVersion(UnknownToken, null);

        private static readonly Regex Semantic = new Regex(@"(?<![0-9A-Za-z.])v?([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.]*)?)(?!\.?[0-9A-Za-z])", RegexOptions.CultureInvariant);
        private static readonly Regex Token = new Regex(@"\A(unknown|[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.]*)?)\z", RegexOptions.CultureInvariant);

        public LiveLaneClaudeVersion(string version, string raw)
        {
            Version = string.IsNullOrEmpty(version) ? UnknownToken : version;
            Raw = raw;
        }

        public string Version { get; }
        public string Raw { get; }
        public bool Known => Version != UnknownToken;

        /// <summary>A token a receipt may record: unknown, or major.minor.patch with an optional -prerelease.</summary>
        public static bool IsToken(string token) => token != null && Token.IsMatch(token);

        /// <summary>
        /// The version a `--version` output states: its first non-blank line (control characters dropped, at most RawLimit
        /// characters) and the first major.minor.patch in it; unknown when there is none.
        /// </summary>
        public static LiveLaneClaudeVersion Parse(string stdout)
        {
            var line = (stdout ?? "").Replace("\r\n", "\n").Split('\n').Select(Clean).FirstOrDefault(text => text.Length > 0);
            if (line == null) return Unknown;
            var match = Semantic.Match(line);
            return new LiveLaneClaudeVersion(match.Success ? match.Groups[1].Value : UnknownToken, line);
        }

        private static string Clean(string text)
        {
            var builder = new StringBuilder();
            foreach (var c in text) if (c >= ' ' && c != (char)0x7f) builder.Append(c);
            var clean = builder.ToString().Trim();
            return clean.Length > RawLimit ? clean.Substring(0, RawLimit) : clean;
        }

        /// <summary>
        /// Asks the executable for its version (`<executable> --version`, bounded) through the process seam. A blank
        /// executable, a start failure, a timeout or a non-zero exit is unknown (no raw line).
        /// </summary>
        public static LiveLaneClaudeVersion Probe(ILiveLaneProcess process, string executable)
        {
            if (process == null || string.IsNullOrWhiteSpace(executable)) return Unknown;
            LiveLaneProcessResult result;
            try { result = process.Run(executable, new[] { "--version" }, ProbeTimeout); }
            catch (Exception) { return Unknown; }
            if (result == null || result.TimedOut || result.Exit != 0) return Unknown;
            return Parse(result.Stdout);
        }

        public LiveLaneMap ToJson() => new LiveLaneMap(("version", Version), ("raw", Raw));
    }

    /// <summary>
    /// A live-lane-execution receipt as a run writes it: schemaVersion 2, which adds the Claude version (claude: {version,
    /// raw}) to the CARD-1545 S2b fields. Written once to a new file; a receipt is never rewritten.
    /// </summary>
    public sealed class LiveLaneExecutionReceipt
    {
        public string LaneId { get; set; }
        public string RunId { get; set; }
        public long Sequence { get; set; } = 1;
        public string Repository { get; set; }
        public string Ref { get; set; }
        public string SourceSha { get; set; }
        public bool SourceClean { get; set; }
        public string HostEnrollmentId { get; set; }
        public string AccountEnrollmentId { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset EndedUtc { get; set; }
        public long DurationMs { get; set; }
        /// <summary>The fingerprint the run executed (its digest and components) and the roster's digest.</summary>
        public string FingerprintDigest { get; set; }
        public LiveLaneMap FingerprintComponents { get; set; }
        public string RosterDigest { get; set; }
        public string InputBefore { get; set; }
        public string InputAfter { get; set; }
        public string ExternalEpoch { get; set; }
        /// <summary>uid -> Passed, Failed or Skipped, in roster order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Rows { get; set; } = Array.Empty<KeyValuePair<string, string>>();
        public long Missing { get; set; }
        public long ClassifierVerdict { get; set; }
        public long FinalExit { get; set; }
        /// <summary>name -> SHA-256 of the artifact file beside the receipt.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Artifacts { get; set; } = Array.Empty<KeyValuePair<string, string>>();
        public LiveLaneClaudeVersion Claude { get; set; } = LiveLaneClaudeVersion.Unknown;

        public LiveLaneMap ToJson()
        {
            var rows = Rows.Select(row => (object)new LiveLaneMap(("uid", row.Key), ("outcome", row.Value))).ToList();
            var executed = Rows.Count;
            return new LiveLaneMap(("kind", LiveLaneReceipt.ExecutionKind), ("schemaVersion", LiveLaneReceipt.CurrentSchemaVersion),
                ("laneId", LaneId), ("runId", RunId), ("sequence", Sequence), ("authority", "diagnostic"), ("repository", Repository), ("ref", Ref),
                ("sourceSha", SourceSha), ("sourceClean", SourceClean), ("host", new LiveLaneMap(("enrollmentId", HostEnrollmentId))),
                ("account", new LiveLaneMap(("enrollmentId", AccountEnrollmentId))), ("startedUtc", LiveLaneReceipt.Iso(StartedUtc)),
                ("endedUtc", LiveLaneReceipt.Iso(EndedUtc)), ("durationMs", DurationMs),
                ("fingerprint", new LiveLaneMap(("digest", FingerprintDigest), ("components", FingerprintComponents), ("rosterDigest", RosterDigest))),
                ("inputBefore", InputBefore), ("inputAfter", InputAfter), ("external", new LiveLaneMap(("epoch", ExternalEpoch))),
                ("planned", executed + Missing), ("executed", executed), ("missing", Missing),
                ("passed", Rows.LongCount(row => row.Value == "Passed")), ("failed", Rows.LongCount(row => row.Value == "Failed")),
                ("skipped", Rows.LongCount(row => row.Value == "Skipped")), ("rows", rows), ("classifierVerdict", ClassifierVerdict), ("finalExit", FinalExit),
                ("artifacts", Artifacts.Select(a => (object)new LiveLaneMap(("name", a.Key), ("sha256", a.Value))).ToList()),
                ("claude", Claude.ToJson()));
        }

        /// <summary>Writes the receipt's canonical JSON to a new file (receipt-exists when the path is taken).</summary>
        public void Write(string path) => LiveLaneReceipt.WriteNew(path, LiveLaneData.Canonical(ToJson()), "receipt-exists", "receipt-unwritable");
    }
}
