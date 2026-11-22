// The CARD-1313 test timing journal (schema antiphon-test-timing-v1) and its report (antiphon-test-timing-report-v1).
// W2-nightly S03 ported the journal of scripts/lib/test-timings.ps1 here; the 2026-11-22 plan
// (2026-11-22-remaining-ps1-to-csharp.md, unit 2) ported the rest (Get-TestTimingReport, Add-TestTimingEstimates) and
// deleted the ps1 and scripts/test-timing-report.ps1. Writers: the nightly tests run (NightlyTests.cs) in-process and
// run-checkpoint.ps1 through `antiphon-scripts test-timing-event`. Readers: `antiphon-scripts test-timing-report` and
// select-test-rosters.ps1, which calls Report and AddEstimates from the loaded core. A line is ConvertTo-Json -Compress
// -Depth 8's for the same event (Newtonsoft's default escaping and number forms), written under a named mutex per
// journal directory. Persistence failure is not a test result: callers catch. Missing configuration writes nothing.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>ReadEvents: the events in file order, first eventId wins; torn-tail warnings; the count of lines that are not JSON.</summary>
    public sealed record TestTimingEvents(IReadOnlyList<JsonNode> Events, IReadOnlyList<string> Warnings, int CorruptCount);

    /// <summary>
    /// One comparable series of set-completed events: the identity of its first event, the metric (host-wall or
    /// inclusive-case-window) over its passed non-retry events, the trend of the last ten, and the cost of every attempt.
    /// </summary>
    public sealed record TestTimingGroup(string SetId, string SetKind, string MembershipDigest, string HostId, string Os, string RuntimeVersion,
        JsonNode Concurrency, string Metric, int SampleCount, double? Median, double? P90, double? Latest, double? PriorFiveMedian,
        double? LatestFiveMedian, double? AbsoluteChange, double? PercentageChange, bool InsufficientHistory, bool Provisional,
        double TotalAttemptedCost, int MissingCount)
    {
        /// <summary>The group as the report prints it.</summary>
        public JsonObject ToJson() => new JsonObject
        {
            ["setId"] = SetId, ["setKind"] = SetKind, ["membershipDigest"] = MembershipDigest, ["hostId"] = HostId, ["os"] = Os,
            ["runtimeVersion"] = RuntimeVersion, ["concurrency"] = Concurrency?.DeepClone(), ["metric"] = Metric, ["sampleCount"] = SampleCount,
            ["median"] = Median, ["p90"] = P90, ["latest"] = Latest, ["priorFiveMedian"] = PriorFiveMedian, ["latestFiveMedian"] = LatestFiveMedian,
            ["absoluteChange"] = AbsoluteChange, ["percentageChange"] = PercentageChange, ["insufficientHistory"] = InsufficientHistory,
            ["provisional"] = Provisional, ["totalAttemptedCost"] = TotalAttemptedCost, ["missingCount"] = MissingCount,
        };
    }

    /// <summary>The timing report: the reader's corrupt count and warnings, and the groups.</summary>
    public sealed record TestTimingReport(int CorruptCount, IReadOnlyList<string> Warnings, IReadOnlyList<TestTimingGroup> Groups)
    {
        /// <summary>The report's schema version.</summary>
        public const string Schema = "antiphon-test-timing-report-v1";

        /// <summary>The report as the test-timing-report command prints it.</summary>
        public JsonObject ToJson() => new JsonObject
        {
            ["schemaVersion"] = Schema,
            ["corruptCount"] = CorruptCount,
            ["warnings"] = new JsonArray(Warnings.Select(warning => (JsonNode)JsonValue.Create(warning)).ToArray()),
            ["groups"] = new JsonArray(Groups.Select(group => (JsonNode)group.ToJson()).ToArray()),
        };
    }

    /// <summary>One selected set's estimate: its group's median, p90, sample count and latest value, or unbudgeted without one.</summary>
    public sealed record TestTimingEstimate(string SetId, double? Median, double? P90, int SampleCount, bool Provisional, bool Unbudgeted, double? Last)
    {
        /// <summary>The estimate as select-test-rosters.ps1's selection carries it (an ordered dictionary).</summary>
        public OrderedDictionary ToDictionary() => new OrderedDictionary
        {
            { "setId", SetId }, { "median", Median }, { "p90", P90 }, { "sampleCount", SampleCount }, { "provisional", Provisional },
            { "unbudgeted", Unbudgeted }, { "last", Last },
        };

        /// <summary>The estimate as the test-timing-report command prints it.</summary>
        public JsonObject ToJson() => new JsonObject
        {
            ["setId"] = SetId, ["median"] = Median, ["p90"] = P90, ["sampleCount"] = SampleCount, ["provisional"] = Provisional,
            ["unbudgeted"] = Unbudgeted, ["last"] = Last,
        };
    }

    /// <summary>The test-timing-report command's options.</summary>
    public sealed class TestTimingReportOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "test-timing-report [--roots <dir,dir>] [--group-by class|roster|category] [--top 20] [--selection-path <selection.json>]";

        private static readonly string[] Valued = { "--roots", "--group-by", "--top", "--selection-path" };

        /// <summary>The journal roots; none reads nothing.</summary>
        public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();

        /// <summary>class (default), roster or category.</summary>
        public string GroupBy { get; init; } = "class";

        /// <summary>The groups shown, largest median first; 0 or less shows all.</summary>
        public int Top { get; init; } = 20;

        /// <summary>A selection (antiphon-test-selection-v1 JSON) to print with the estimates instead of the report; empty for none.</summary>
        public string SelectionPath { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or an unknown grouping.</summary>
        public static TestTimingReportOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            var groupBy = flags.Value("--group-by", "class");
            if (groupBy != "roster" && groupBy != "class" && groupBy != "category")
                throw new NightlyUsageException("--group-by expects roster, class or category, not '" + groupBy + "'");
            return new TestTimingReportOptions
            {
                Roots = flags.List("--roots"), GroupBy = groupBy, Top = flags.Int("--top", 20), SelectionPath = flags.Value("--selection-path", ""),
            };
        }
    }

    /// <summary>The test-timing-event command's options: the journal root and one event as a JSON object.</summary>
    public sealed class TestTimingEventOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "test-timing-event --root <dir> --event <json>";

        private static readonly string[] Valued = { "--root", "--event" };

        /// <summary>The journal root.</summary>
        public string Root { get; init; } = "";

        /// <summary>The event: one JSON object, its members in the order the line keeps.</summary>
        public string Event { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or a missing flag.</summary>
        public static TestTimingEventOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            return new TestTimingEventOptions { Root = flags.Required("--root"), Event = flags.Required("--event") };
        }
    }

    /// <summary>The antiphon-test-timing-v1 event journal: runs/&lt;startedAt UTC date&gt;/&lt;runId&gt;/&lt;attempt&gt;/events.jsonl under a root.</summary>
    public static class TestTimings
    {
        /// <summary>The journal's schema version.</summary>
        public const string Schema = "antiphon-test-timing-v1";

        private const int Depth = 8;
        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>One path component: refuses (path-component-refused) a blank value, ".", "..", one containing "..", a separator, or a rooted path.</summary>
        public static void RequireComponent(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".." || value.Contains("..", StringComparison.Ordinal)
                || value.Contains('/') || value.Contains('\\') || Path.IsPathRooted(value))
                throw new InvalidOperationException("path-component-refused");
        }

        /// <summary>
        /// The journal writer: nothing without a root. Refuses a runId, attempt or eventId that is not one path component,
        /// then, holding the directory's named mutex, appends the event's line unless the journal already holds that eventId
        /// (the same line: nothing; another: event-conflict) or it is a set-completed whose set another producer completed.
        /// </summary>
        public static void WriteEvent(string root, IDictionary timingEvent)
        {
            if (string.IsNullOrEmpty(root)) return;
            if (timingEvent == null) throw new ArgumentNullException(nameof(timingEvent));
            RequireComponent(Text(Field(timingEvent, "runId")));
            RequireComponent(Text(Field(timingEvent, "attempt")));
            RequireComponent(Text(Field(timingEvent, "eventId")));
            var date = Instant(Field(timingEvent, "startedAt")).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dir = JoinPath(JoinPath(JoinPath(JoinPath(root, "runs"), date), Text(Field(timingEvent, "runId"))), Text(Field(timingEvent, "attempt")));
            Directory.CreateDirectory(dir);
            var path = JoinPath(dir, "events.jsonl");
            using var mutex = new Mutex(false, MutexName(dir));
            mutex.WaitOne();
            try
            {
                var existing = new List<JsonObject>();
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        // ConvertFrom-Json of a blank line is null: it can match nothing. Any other line that is not JSON throws.
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (JsonNode.Parse(line, null, ReadOptions) is JsonObject item) existing.Add(item);
                    }
                }
                var eventId = Text(Field(timingEvent, "eventId"));
                var payload = EventJson(timingEvent);
                var match = existing.FirstOrDefault(item => Same(item["eventId"], eventId));
                if (match != null)
                {
                    if (!string.Equals(NodeJson(match), payload, StringComparison.Ordinal)) throw new InvalidOperationException("event-conflict");
                    return;
                }
                var owned = existing.Any(item => Same(item["eventKind"], "set-completed") && Same(item["setId"], Field(timingEvent, "setId"))
                    && Same(item["setKind"], Field(timingEvent, "setKind")) && !Same(item["producer"], Field(timingEvent, "producer")));
                if (Text(Field(timingEvent, "eventKind")) == "set-completed" && owned) return;
                File.AppendAllText(path, payload + "\n");
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// The journal reader: every events.jsonl under each existing root (recursive, sorted by full path, culture order
        /// ignoring case), each complete line parsed; a line that is not JSON is counted, never returned; an event whose
        /// eventId (ignoring case) was already read is skipped; a last line without its newline is a torn-tail warning.
        /// </summary>
        public static TestTimingEvents ReadEvents(IEnumerable<string> roots)
        {
            var events = new List<JsonNode>();
            var warnings = new List<string>();
            var corrupt = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var root in roots ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                var files = Directory.EnumerateFiles(root, "events.jsonl", walk).Select(Path.GetFullPath).ToList();
                files.Sort(StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true));
                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    if (text.Length == 0) continue;
                    var torn = !text.EndsWith('\n');
                    var lines = text.Split('\n');
                    var last = lines.Length - 1;
                    for (var i = 0; i < last; i++)
                    {
                        if (lines[i].Length == 0) continue;
                        JsonNode item;
                        if (string.IsNullOrWhiteSpace(lines[i])) item = null;
                        else
                        {
                            try { item = JsonNode.Parse(lines[i], null, ReadOptions); }
                            catch (JsonException) { corrupt++; continue; }
                        }
                        var id = item is JsonObject row ? Text(row["eventId"]) : "";
                        if (id.Length > 0 && !seen.Add(id)) continue;
                        events.Add(item);
                    }
                    if (torn && lines[last].Length > 0) warnings.Add("torn-tail");
                }
            }
            return new TestTimingEvents(events, warnings, corrupt);
        }

        /// <summary>
        /// The event's journal line: ConvertTo-Json -Compress -Depth 8 of it. Keys in the dictionary's order; strings with
        /// Newtonsoft's default escaping (control characters, quote, backslash, U+0085, U+2028, U+2029; lower-case hex);
        /// integers as digits; floating values in round-trip form with ".0" when integral; dates in ISO 8601 with the
        /// fraction trimmed; enums as numbers; nested dictionaries and lists as objects and arrays.
        /// </summary>
        public static string EventJson(IDictionary timingEvent)
        {
            var text = new StringBuilder();
            WriteValue(text, timingEvent, 0);
            return text.ToString();
        }

        /// <summary>A read event (or any node) in the same form as EventJson: what Write compares a replayed eventId with.</summary>
        public static string NodeJson(JsonNode node)
        {
            var text = new StringBuilder();
            WriteNode(text, node);
            return text.ToString();
        }

        /// <summary>The named mutex both writers hold for one journal directory: antiphon-timing- and 32 hex digits of SHA-256(UTF-8 path).</summary>
        public static string MutexName(string directory) =>
            "antiphon-timing-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory ?? ""))).Substring(0, 32);

        /// <summary>
        /// Join-Path's text for two parts: on Windows every '/' becomes '\'; one separator joins them unless the first
        /// already ends with one. The mutex name is a hash of this text, so every writer of a journal must join the same way.
        /// </summary>
        public static string JoinPath(string parent, string child)
        {
            var separator = Path.DirectorySeparatorChar;
            parent ??= "";
            child ??= "";
            if (separator == '\\')
            {
                parent = parent.Replace('/', '\\');
                child = child.Replace('/', '\\');
            }
            child = child.TrimStart(separator);
            if (parent.Length == 0) return child;
            return parent[parent.Length - 1] == separator ? parent + child : parent + separator + child;
        }

        // ---- the report (Get-TestTimingReport) and the selection estimates (Add-TestTimingEstimates) ----

        private static readonly string[] ComparableKey =
            { "repositoryIdentity", "setId", "membershipDigest", "measurementKind", "os", "architecture", "runtimeVersion", "concurrency", "profile", "hostId" };

        private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = false };

        /// <summary>
        /// The report over every root: the set-completed events whose setKind the grouping reads (roster: roster, run and
        /// chunk; category: category; class: class, checkpoint-row and category), bucketed by the comparability key
        /// (repository, set, membership, measurement, platform, runtime, concurrency, profile and host). The series of a bucket is
        /// its passed, non-retry events ordered by startedAt then sequence; its metric is wallSeconds for a roster (host-wall),
        /// else caseWindowSeconds, or wallSeconds without one (inclusive-case-window). Every attempt counts towards the cost:
        /// its wall time, and each producing build's time once. Keys are read as spelled. The groups are in key order.
        /// </summary>
        public static TestTimingReport Report(IEnumerable<string> roots, string groupBy)
        {
            var read = ReadEvents(roots);
            var roster = groupBy == "roster";
            var wanted = roster ? new[] { "roster", "run", "chunk" } : groupBy == "category" ? new[] { "category" } : new[] { "class", "checkpoint-row", "category" };
            var buckets = new SortedDictionary<string, List<JsonObject>>(StringComparer.Ordinal);
            foreach (var node in read.Events)
            {
                if (!(node is JsonObject item) || Text(item["eventKind"]) != "set-completed" || Array.IndexOf(wanted, Text(item["setKind"])) < 0) continue;
                var key = string.Join("|", ComparableKey.Select(name => Text(item[name])));
                if (!buckets.TryGetValue(key, out var rows)) buckets[key] = rows = new List<JsonObject>();
                rows.Add(item);
            }
            var groups = new List<TestTimingGroup>();
            foreach (var rows in buckets.Values)
            {
                double? Metric(JsonObject row) => roster ? Number(row["wallSeconds"]) : Number(row["caseWindowSeconds"]) ?? Number(row["wallSeconds"]);
                var success = rows.Where(row => Text(row["outcome"]) == "passed" && Text(row["purpose"]) != "retry").ToList();
                var values = success.Select(Metric).Where(value => value.HasValue).Select(value => value.Value).ToList();
                var ordered = success.OrderBy(row => StartedAt(row["startedAt"])).ThenBy(row => Number(row["sequence"]) ?? double.NegativeInfinity).ToList();
                var insufficient = ordered.Count < 10;
                double? prior = null, latestFive = null, change = null, percent = null;
                if (!insufficient)
                {
                    // A series value without its metric counts as 0 here, as [double] of a missing value is.
                    var window = ordered.Skip(ordered.Count - 10).Select(row => Metric(row) ?? 0).ToList();
                    prior = Median(window.Take(5).ToList());
                    latestFive = Median(window.Skip(5).ToList());
                    change = latestFive - prior;
                    percent = prior == 0 ? null : 100 * change / prior;
                }
                var builds = new HashSet<string>(StringComparer.Ordinal);
                double wallCost = 0, buildCost = 0;
                foreach (var row in rows)
                {
                    wallCost += Number(row["wallSeconds"]) ?? 0;
                    if (Truthy(row["producingBuildId"]) && builds.Add(Text(row["producingBuildId"]))) buildCost += Number(row["buildSeconds"]) ?? 0;
                }
                var sample = rows[0];
                groups.Add(new TestTimingGroup(Text(sample["setId"]), Text(sample["setKind"]), Text(sample["membershipDigest"]), Text(sample["hostId"]),
                    Text(sample["os"]), Text(sample["runtimeVersion"]), sample["concurrency"]?.DeepClone(), roster ? "host-wall" : "inclusive-case-window",
                    values.Count, Median(values), NearestRank(values, 0.9), ordered.Count > 0 ? Metric(ordered[ordered.Count - 1]) ?? 0 : (double?)null,
                    prior, latestFive, change, percent, insufficient, values.Count < 5, wallCost + buildCost, rows.Count(row => Truthy(row["unavailableReason"]))));
            }
            return new TestTimingReport(read.CorruptCount, read.Warnings, groups);
        }

        /// <summary>The median of the values (the mean of the middle two for an even count); null for none.</summary>
        public static double? Median(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return null;
            var sorted = values.OrderBy(value => value).ToList();
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        /// <summary>The nearest-rank percentile (percent in 0..1, the rank clamped to the values); null for none.</summary>
        public static double? NearestRank(IReadOnlyList<double> values, double percent)
        {
            if (values == null || values.Count == 0) return null;
            var sorted = values.OrderBy(value => value).ToList();
            var rank = (int)Math.Ceiling(percent * sorted.Count);
            return sorted[Math.Min(Math.Max(rank, 1), sorted.Count) - 1];
        }

        /// <summary>One estimate per set id, in order: the report group of that setId (ordinal) with a median, else unbudgeted.</summary>
        public static IReadOnlyList<TestTimingEstimate> Estimates(IEnumerable<string> setIds, TestTimingReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return (setIds ?? Array.Empty<string>()).Select(id =>
            {
                var group = report.Groups.FirstOrDefault(item => string.Equals(item.SetId, id, StringComparison.Ordinal));
                return group?.Median == null
                    ? new TestTimingEstimate(id ?? "", null, null, 0, true, true, null)
                    : new TestTimingEstimate(id ?? "", group.Median, group.P90, group.SampleCount, group.Provisional, false, group.Latest);
            }).ToArray();
        }

        /// <summary>
        /// Add-TestTimingEstimates for select-test-rosters.ps1 (the selection is its ordered dictionary): a copy of the
        /// selection's entries in order without estimates and budgetObservations, then estimates (one dictionary per
        /// selected set: setId, median, p90, sampleCount, provisional, unbudgeted, last), budgetObservations
        /// ["budget-observed"] and the same selectedSets. It never adds, drops or reorders a set, and never touches the digest.
        /// </summary>
        public static OrderedDictionary AddEstimates(IDictionary selection, TestTimingReport report)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            var selected = selection.Contains("selectedSets") ? selection["selectedSets"] : null;
            var sets = selected == null ? Array.Empty<object>() : PsValue.AsArray(selected);
            var copy = new OrderedDictionary();
            foreach (DictionaryEntry entry in selection)
                if (!(entry.Key is string key && (key == "estimates" || key == "budgetObservations"))) copy[entry.Key] = entry.Value;
            copy["estimates"] = Estimates(sets.Select(set => PsValue.Str(PsValue.Prop(set, "id"))), report).Select(item => (object)item.ToDictionary()).ToArray();
            copy["budgetObservations"] = new object[] { "budget-observed" };
            copy["selectedSets"] = sets;
            return copy;
        }

        /// <summary>The test-timing-report command's selection: AddEstimates over a selection read from JSON.</summary>
        private static JsonObject AddEstimates(JsonObject selection, TestTimingReport report)
        {
            var selected = selection["selectedSets"];
            var sets = selected is JsonArray array ? array.ToList() : selected == null ? new List<JsonNode>() : new List<JsonNode> { selected };
            var copy = new JsonObject();
            foreach (var pair in selection)
                if (pair.Key != "estimates" && pair.Key != "budgetObservations") copy[pair.Key] = pair.Value?.DeepClone();
            copy["estimates"] = new JsonArray(Estimates(sets.Select(set => set is JsonObject item ? Text(item["id"]) : ""), report).Select(item => (JsonNode)item.ToJson()).ToArray());
            copy["budgetObservations"] = new JsonArray("budget-observed");
            copy["selectedSets"] = new JsonArray(sets.Select(set => set?.DeepClone()).ToArray());
            return copy;
        }

        /// <summary>
        /// The test-timing-report command (it replaced scripts/test-timing-report.ps1): the report over options.Roots, its
        /// groups ranked by median (largest first, a group without one last) and cut to options.Top (0: all), as one JSON line
        /// on host.Out; with options.SelectionPath, that selection with the estimates of the shown groups instead. Exit 0;
        /// 1 when the selection cannot be read (one line on host.Error).
        /// </summary>
        public static int Run(TestTimingReportOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                var report = Report(options.Roots, options.GroupBy);
                IEnumerable<TestTimingGroup> ranked = report.Groups.OrderByDescending(group => group.Median ?? double.MinValue);
                if (options.Top > 0) ranked = ranked.Take(options.Top);
                var shown = report with { Groups = ranked.ToArray() };
                JsonNode output = shown.ToJson();
                if (!string.IsNullOrEmpty(options.SelectionPath))
                {
                    if (!(JsonNode.Parse(File.ReadAllText(options.SelectionPath), null, ReadOptions) is JsonObject selection))
                        throw new InvalidOperationException("selection-not-an-object: " + options.SelectionPath);
                    output = AddEstimates(selection, shown);
                }
                host.Out.WriteLine(output.ToJsonString(Compact));
                return 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is InvalidOperationException)
            {
                host.Error.WriteLine("test-timing-report: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        /// <summary>
        /// The test-timing-event command (run-checkpoint.ps1's Write-WrapperTiming): WriteEvent of the JSON object
        /// options.Event under options.Root, its members in order. Exit 0 written or already there; 1 refused (path-component-refused,
        /// event-conflict, not one JSON object, or the journal cannot be written: one line on host.Error).
        /// </summary>
        public static int Run(TestTimingEventOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                WriteEventJson(options.Root, options.Event);
                return 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is InvalidOperationException
                || e is FormatException || e is InvalidCastException)
            {
                host.Error.WriteLine("test-timing-event: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        /// <summary>WriteEvent of one JSON object's members, in order; InvalidOperationException (event-not-an-object) for any other JSON.</summary>
        public static void WriteEventJson(string root, string eventJson)
        {
            if (!(JsonNode.Parse(eventJson ?? "", null, ReadOptions) is JsonObject item)) throw new InvalidOperationException("event-not-an-object");
            var map = new OrderedDictionary(StringComparer.Ordinal);
            foreach (var pair in item) map[pair.Key] = pair.Value;
            WriteEvent(root, map);
        }

        // A JSON number (or numeric text) as a double; null when absent, null or not a number.
        private static double? Number(JsonNode node)
        {
            if (!(node is JsonValue value) || !value.TryGetValue(out JsonElement element)) return null;
            switch (element.ValueKind)
            {
                case JsonValueKind.Number: return element.GetDouble();
                case JsonValueKind.String:
                    return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null;
                default: return null;
            }
        }

        // An event's startedAt as an instant (an ISO text; one without a zone is local time); the earliest instant when unreadable.
        private static DateTimeOffset StartedAt(JsonNode node) =>
            DateTimeOffset.TryParse(Text(node), CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant) ? instant : DateTimeOffset.MinValue;

        // A value that is set: non-empty text, a non-zero number, true, or an object or a non-empty array.
        private static bool Truthy(JsonNode node)
        {
            switch (node)
            {
                case null: return false;
                case JsonArray array: return array.Count > 0;
                case JsonObject _: return true;
                case JsonValue value when value.TryGetValue(out JsonElement element):
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.String: return (element.GetString() ?? "").Length > 0;
                        case JsonValueKind.Number: return element.GetDouble() != 0;
                        case JsonValueKind.True: return true;
                        default: return false;
                    }
                default: return true;
            }
        }

        // A field of the event: the dictionary's own lookup.
        private static object Field(IDictionary map, string name) => map.Contains(name) ? map[name] : null;

        // [string]$value: empty for null, invariant text for numbers and dates, True/False for booleans.
        private static string Text(object value)
        {
            switch (value)
            {
                case null: return "";
                case string s: return s;
                case JsonValue v when v.TryGetValue(out JsonElement e):
                    return e.ValueKind switch
                    {
                        JsonValueKind.String => e.GetString() ?? "",
                        JsonValueKind.True => "True",
                        JsonValueKind.False => "False",
                        JsonValueKind.Null => "",
                        _ => e.GetRawText(),
                    };
                case JsonValue v when v.TryGetValue(out string s): return s;
                case JsonNode n: return n.ToJsonString();
                case bool b: return b ? "True" : "False";
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString() ?? "";
            }
        }

        // -ceq between a read value and the event's: null only equals null; otherwise the text forms, ordinal.
        private static bool Same(JsonNode read, object value)
        {
            var readNull = read == null || (read is JsonValue v && v.TryGetValue(out JsonElement e) && e.ValueKind == JsonValueKind.Null);
            if (readNull || value == null) return readNull && value == null;
            return string.Equals(Text(read), Text(value), StringComparison.Ordinal);
        }

        // [DateTimeOffset]$Event.startedAt: a DateTimeOffset, a DateTime, or text parsed as the invariant culture does.
        private static DateTimeOffset Instant(object value)
        {
            switch (value)
            {
                case DateTimeOffset offset: return offset;
                case DateTime time: return new DateTimeOffset(time);
                case null: throw new InvalidCastException("Cannot convert null to type System.DateTimeOffset.");
                default: return DateTimeOffset.Parse(Text(value), CultureInfo.InvariantCulture);
            }
        }

        private static void WriteValue(StringBuilder text, object value, int depth)
        {
            switch (value)
            {
                case null:
                case DBNull _:
                    text.Append("null");
                    return;
                case string s:
                    WriteString(text, s);
                    return;
                case char c:
                    WriteString(text, c.ToString());
                    return;
                case bool b:
                    text.Append(b ? "true" : "false");
                    return;
                case JsonNode node:
                    WriteNode(text, node);
                    return;
                case JsonElement element:
                    WriteElement(text, element);
                    return;
                case Enum e:
                    text.Append(Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    return;
                case sbyte or byte or short or ushort or int or uint or long or ulong or BigInteger:
                    text.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    return;
                case double d:
                    WriteFloat(text, d, d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    WriteFloat(text, f, f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case decimal m:
                    text.Append(DecimalPlace(m.ToString(CultureInfo.InvariantCulture)));
                    return;
                case DateTime time:
                    WriteString(text, time.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK", CultureInfo.InvariantCulture));
                    return;
                case DateTimeOffset offset:
                    WriteString(text, offset.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFzzz", CultureInfo.InvariantCulture));
                    return;
                case IDictionary map:
                    if (depth > Depth) { WriteString(text, map.ToString()); return; }
                    text.Append('{');
                    var first = true;
                    foreach (DictionaryEntry entry in map)
                    {
                        if (!first) text.Append(',');
                        first = false;
                        WriteString(text, Text(entry.Key));
                        text.Append(':');
                        WriteValue(text, entry.Value, depth + 1);
                    }
                    text.Append('}');
                    return;
                case IEnumerable items:
                    if (depth > Depth) { WriteString(text, items.ToString()); return; }
                    text.Append('[');
                    var head = true;
                    foreach (var item in items)
                    {
                        if (!head) text.Append(',');
                        head = false;
                        WriteValue(text, item, depth + 1);
                    }
                    text.Append(']');
                    return;
                default:
                    WriteString(text, Text(value));
                    return;
            }
        }

        private static void WriteNode(StringBuilder text, JsonNode node)
        {
            switch (node)
            {
                case null:
                    text.Append("null");
                    return;
                case JsonObject map:
                    text.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        if (!first) text.Append(',');
                        first = false;
                        WriteString(text, pair.Key);
                        text.Append(':');
                        WriteNode(text, pair.Value);
                    }
                    text.Append('}');
                    return;
                case JsonArray items:
                    text.Append('[');
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (i > 0) text.Append(',');
                        WriteNode(text, items[i]);
                    }
                    text.Append(']');
                    return;
                case JsonValue value when value.TryGetValue(out JsonElement element):
                    WriteElement(text, element);
                    return;
                default:
                    WriteValue(text, node.GetValue<object>(), 0);
                    return;
            }
        }

        private static void WriteElement(StringBuilder text, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String: WriteString(text, element.GetString()); return;
                case JsonValueKind.Number: text.Append(element.GetRawText()); return;
                case JsonValueKind.True: text.Append("true"); return;
                case JsonValueKind.False: text.Append("false"); return;
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    WriteNode(text, JsonNode.Parse(element.GetRawText()));
                    return;
                default: text.Append("null"); return;
            }
        }

        private static void WriteFloat(StringBuilder text, double value, string roundTrip)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { WriteString(text, roundTrip); return; }
            text.Append(DecimalPlace(roundTrip));
        }

        // Newtonsoft's EnsureDecimalPlace: an integral number keeps a ".0".
        private static string DecimalPlace(string number) =>
            number.IndexOf('.') >= 0 || number.IndexOf('E') >= 0 || number.IndexOf('e') >= 0 ? number : number + ".0";

        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (var c in value ?? "")
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\u0085':
                    case '\u2028':
                    case '\u2029':
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                    default:
                        if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
