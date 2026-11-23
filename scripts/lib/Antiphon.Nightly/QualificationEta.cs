// CARD-1842 S4: the read-only batch ETA of a qualification timing root. It replaced scripts/qualification-eta.ps1
// (deleted 2026-11-23, unit 6 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The CLI runs it as
// `antiphon-scripts qualification-eta` (Run); the watch-qual command (WatchQual.cs) calls TryRead in-process once per poll.
//
// One line: "ETA outstanding=<n> running=<n> completed=<n> unpriced=<n> pointSeconds=<s> p50=<utc> p95=<utc|unavailable>".
// - The chunks are the rows of the root's groups-planned-<suite>.json plans (not groups-planned.json or
//   groups-planned-index.json) and of its <suite>-<chunk>.timing.json sidecars. The first priced plan row of a chunk
//   wins: plannedSeconds when it is a number of at least 0, else (plannedSeconds absent or blank) the sum of its classes'
//   seconds from the class-cost file, when every class is priced. Anything else leaves the chunk unpriced, never zero.
// - A chunk is completed when its sidecar has completedAt or a settled lastPhase (complete, timeout, cancelled,
//   teardown), running when active-chunk.json names it with state Active, else outstanding. A completed chunk adds
//   nothing; a running one adds its price less the time since startedAt (at least 0; the whole price without a start).
// - The headline scale is sum(wall)/sum(planned) over the completed priced chunks with a wall, not the unweighted mean of
//   the ratios. p50 and p95 need at least eight ratios (nearest rank); with fewer, p50 is the unscaled remainder and p95
//   is "unavailable".
// It reads the root and the class-cost file and writes nothing. JSON keys are matched exactly. Plain C#: no PowerShell
// types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>A timing root or class-cost file the ETA refuses: the message is the reason (timing-root-missing, plan-suite, ...).</summary>
    public sealed class QualificationEtaRefusal : Exception
    {
        /// <summary>A refusal with its reason.</summary>
        public QualificationEtaRefusal(string message) : base(message) { }
    }

    /// <summary>The qualification-eta command's options.</summary>
    public sealed class QualificationEtaOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "qualification-eta --timing-root <dir> [--now-utc <iso-8601>] [--class-cost-path <class-costs.json>]";

        private static readonly string[] Valued = { "--timing-root", "--now-utc", "--class-cost-path" };

        /// <summary>The qualification timing root (the plans, the timing sidecars and active-chunk.json).</summary>
        public string TimingRoot { get; init; } = "";

        /// <summary>The instant the ETA is taken at (UTC); null: the host's clock.</summary>
        public DateTime? NowUtc { get; init; }

        /// <summary>An antiphon-nightly-class-costs-v1 file that prices a plan row without plannedSeconds; empty for none.</summary>
        public string ClassCostPath { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line, a missing --timing-root or an unreadable --now-utc.</summary>
        public static QualificationEtaOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            DateTime? now = null;
            var text = flags.Value("--now-utc", "");
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, QualificationEta.UtcStyles, out var parsed))
                    throw new NightlyUsageException("--now-utc expects an ISO-8601 instant, not '" + text + "'");
                now = parsed;
            }
            return new QualificationEtaOptions
            {
                TimingRoot = flags.Required("--timing-root"), NowUtc = now, ClassCostPath = flags.Value("--class-cost-path", ""),
            };
        }
    }

    /// <summary>One ETA: the chunk counts, the scaled remaining seconds and the p50/p95 finish instants (UTC).</summary>
    public sealed record QualificationEtaReading(int Outstanding, int Running, int Completed, int Unpriced, double PointSeconds, DateTime P50Utc, DateTime? P95Utc)
    {
        /// <summary>The printed line.</summary>
        public string Line =>
            "ETA outstanding=" + Outstanding.ToString(CultureInfo.InvariantCulture)
            + " running=" + Running.ToString(CultureInfo.InvariantCulture)
            + " completed=" + Completed.ToString(CultureInfo.InvariantCulture)
            + " unpriced=" + Unpriced.ToString(CultureInfo.InvariantCulture)
            + " pointSeconds=" + QualificationEta.FormatNumber(PointSeconds)
            + " p50=" + QualificationEta.FormatUtc(P50Utc)
            + " p95=" + (P95Utc.HasValue ? QualificationEta.FormatUtc(P95Utc.Value) : "unavailable");
    }

    /// <summary>The read-only qualification ETA (CARD-1842).</summary>
    public static class QualificationEta
    {
        /// <summary>How an instant without an offset is read: as UTC, and returned as UTC.</summary>
        public const DateTimeStyles UtcStyles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

        private static readonly string[] SettledPhases = { "complete", "timeout", "cancelled", "teardown" };

        /// <summary>
        /// The qualification-eta command: the ETA line of options.TimingRoot at options.NowUtc (else host.UtcNow) on
        /// host.Out. Exit 0 printed; 1 refused or unreadable (the reason, one line on host.Error).
        /// </summary>
        public static int Run(QualificationEtaOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (!TryRead(files, options.TimingRoot, options.NowUtc ?? host.UtcNow, options.ClassCostPath, out var reading, out var error))
            {
                host.Error.WriteLine("qualification-eta: " + error);
                return 1;
            }
            host.Out.WriteLine(reading.Line);
            return 0;
        }

        /// <summary>
        /// Read, with a refusal or an unreadable file as the error text instead of an exception: false and the reason
        /// (the reading null), or true and the reading (the error empty).
        /// </summary>
        public static bool TryRead(IFileSystem files, string timingRoot, DateTime nowUtc, string classCostPath, out QualificationEtaReading reading, out string error)
        {
            try
            {
                reading = Read(files, timingRoot, nowUtc, classCostPath);
                error = "";
                return true;
            }
            catch (Exception e) when (e is QualificationEtaRefusal || e is IOException || e is UnauthorizedAccessException || e is JsonException
                || e is ArgumentOutOfRangeException)
            {
                reading = null;
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// The ETA of the timing root at nowUtc. classCostPath (empty: none) is opened only when a plan row needs it.
        /// Throws QualificationEtaRefusal for a missing root, a plan without suite or chunk, a sidecar without suite or
        /// chunk, a second sidecar for a chunk or a class-cost file of another format; IOException, JsonException and
        /// UnauthorizedAccessException for a file that cannot be read.
        /// </summary>
        public static QualificationEtaReading Read(IFileSystem files, string timingRoot, DateTime nowUtc, string classCostPath)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (string.IsNullOrWhiteSpace(timingRoot) || !files.Directory.Exists(timingRoot)) throw new QualificationEtaRefusal("timing-root-missing " + timingRoot);
            var root = files.Path.GetFullPath(timingRoot);
            var now = nowUtc.ToUniversalTime();
            var costs = new ClassCosts(files, classCostPath);
            var chunks = new List<Chunk>();
            var byKey = new Dictionary<string, Chunk>(StringComparer.Ordinal);
            Chunk Get(string suite, string chunkId)
            {
                var key = suite + "\n" + chunkId;
                if (!byKey.TryGetValue(key, out var chunk))
                {
                    chunk = new Chunk { Suite = suite, Id = chunkId };
                    byKey[key] = chunk;
                    chunks.Add(chunk);
                }
                return chunk;
            }

            var names = files.Directory.GetFiles(root).Select(path => files.Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            foreach (var name in names)
            {
                if (name == "groups-planned-index.json") continue;
                if (!name.StartsWith("groups-planned-", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal)) continue;
                using var plan = ReadJson(files, files.Path.Combine(root, name));
                var suite = Text(Member(plan.RootElement, "suite"));
                if (string.IsNullOrWhiteSpace(suite)) throw new QualificationEtaRefusal("plan-suite " + name);
                foreach (var group in Items(Member(plan.RootElement, "groups")))
                {
                    if (group.ValueKind == JsonValueKind.Null) continue;
                    var chunkId = Text(Member(group, "chunk"));
                    if (string.IsNullOrWhiteSpace(chunkId)) throw new QualificationEtaRefusal("plan-chunk " + name);
                    var chunk = Get(suite, chunkId);
                    if (chunk.Priced) continue;
                    var planned = Member(group, "plannedSeconds");
                    var seconds = Number(planned);
                    if (seconds.HasValue)
                    {
                        // A negative plannedSeconds prices nothing; a later row of the chunk may.
                        if (seconds.Value < 0) continue;
                        chunk.Price(seconds.Value);
                        continue;
                    }
                    // A plannedSeconds that is there but not a number is not priced from the classes either.
                    if (planned.HasValue && planned.Value.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(Text(planned))) continue;
                    var classes = Member(group, "classes");
                    var classPrice = classes.HasValue ? costs.Price(classes.Value) : null;
                    if (classPrice.HasValue) chunk.Price(classPrice.Value);
                }
            }

            foreach (var name in names.Where(name => name.EndsWith(".timing.json", StringComparison.Ordinal)))
            {
                using var timing = ReadJson(files, files.Path.Combine(root, name));
                var sidecar = timing.RootElement;
                var suite = Text(Member(sidecar, "suite"));
                var chunkId = Text(Member(sidecar, "chunk"));
                if (string.IsNullOrWhiteSpace(suite) || string.IsNullOrWhiteSpace(chunkId)) throw new QualificationEtaRefusal("timing-identity " + name);
                var chunk = Get(suite, chunkId);
                if (chunk.HasTiming) throw new QualificationEtaRefusal("duplicate-timing " + name);
                chunk.HasTiming = true;
                chunk.Phase = Text(Member(sidecar, "lastPhase"));
                chunk.StartedAt = Text(Member(sidecar, "startedAt"));
                chunk.CompletedAt = Text(Member(sidecar, "completedAt"));
                chunk.Wall = Number(Member(sidecar, "runWallSeconds"));
            }

            var activeSuite = "";
            var activeChunk = "";
            var activePath = files.Path.Combine(root, "active-chunk.json");
            if (files.File.Exists(activePath))
            {
                using var active = ReadJson(files, activePath);
                if (Text(Member(active.RootElement, "state")) == "Active")
                {
                    activeSuite = Text(Member(active.RootElement, "suite"));
                    activeChunk = Text(Member(active.RootElement, "chunk"));
                }
            }

            int outstanding = 0, running = 0, completed = 0, unpriced = 0;
            double remainder = 0, sumWall = 0, sumPlanned = 0;
            var ratios = new List<double>();
            foreach (var chunk in chunks)
            {
                var settled = !string.IsNullOrWhiteSpace(chunk.CompletedAt) || Array.IndexOf(SettledPhases, chunk.Phase) >= 0;
                if (settled)
                {
                    completed++;
                    if (chunk.Priced && chunk.Seconds > 0 && chunk.Wall.HasValue)
                    {
                        ratios.Add(chunk.Wall.Value / chunk.Seconds);
                        sumWall += chunk.Wall.Value;
                        sumPlanned += chunk.Seconds;
                    }
                    continue;
                }
                var isRunning = !string.IsNullOrWhiteSpace(activeChunk) && chunk.Suite == activeSuite && chunk.Id == activeChunk;
                if (isRunning) running++;
                else outstanding++;
                if (!chunk.Priced)
                {
                    unpriced++;
                    continue;
                }
                if (!isRunning)
                {
                    remainder += chunk.Seconds;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(chunk.StartedAt) && DateTime.TryParse(chunk.StartedAt, CultureInfo.InvariantCulture, UtcStyles, out var started))
                    remainder += Math.Max(0.0, chunk.Seconds - (now - started.ToUniversalTime()).TotalSeconds);
                else
                    remainder += chunk.Seconds;
            }

            var sorted = ratios.ToArray();
            Array.Sort(sorted);
            var scale = sorted.Length > 0 && sumPlanned > 0 ? sumWall / sumPlanned : 1.0;
            var p50Seconds = remainder;
            DateTime? p95 = null;
            if (sorted.Length >= 8)
            {
                p50Seconds = remainder * sorted[NearestRank(0.50m, sorted.Length)];
                p95 = now.AddSeconds(remainder * sorted[NearestRank(0.95m, sorted.Length)]);
            }
            return new QualificationEtaReading(outstanding, running, completed, unpriced, remainder * scale, now.AddSeconds(p50Seconds), p95);
        }

        /// <summary>Seconds rounded to three places: a whole number without a point, else up to three decimals (invariant).</summary>
        public static string FormatNumber(double value)
        {
            var rounded = Math.Round(value, 3);
            var whole = Math.Round(rounded, 0);
            if (Math.Abs(rounded - whole) < 0.0000001) return ((long)whole).ToString(CultureInfo.InvariantCulture);
            return rounded.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>An instant as yyyy-MM-ddTHH:mm:ss.fffZ (UTC).</summary>
        public static string FormatUtc(DateTime value) =>
            value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        // The nearest-rank index of a fraction of n: ceil(fraction * n) - 1, in decimal so 0.95 * 20 is exactly 19.
        private static int NearestRank(decimal fraction, int n) => (int)Math.Ceiling(fraction * n) - 1;

        private static JsonDocument ReadJson(IFileSystem files, string path) => JsonDocument.Parse(files.File.ReadAllText(path));

        // The member of an object, matched exactly; null when the element is not an object or has no such member.
        internal static JsonElement? Member(JsonElement? element, string name)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return null;
            return element.Value.TryGetProperty(name, out var value) ? value : null;
        }

        // A value as text: a string as itself, a number as written, true/false as True/False, an array as its items'
        // text joined by spaces, an object as written; empty for null or absent.
        internal static string Text(JsonElement? element)
        {
            if (!element.HasValue) return "";
            var value = element.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString() ?? "";
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Array: return string.Join(" ", value.EnumerateArray().Select(item => Text(item)));
                case JsonValueKind.Object: return value.GetRawText();
                default: return "";
            }
        }

        // A finite number from a JSON number or a numeric string (invariant); null for anything else.
        private static double? Number(JsonElement? element)
        {
            if (!element.HasValue) return null;
            var value = element.Value;
            if (value.ValueKind != JsonValueKind.Number && value.ValueKind != JsonValueKind.String) return null;
            var text = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
            if (double.IsNaN(number) || double.IsInfinity(number)) return null;
            return number;
        }

        // The items of a list member: an array's elements, any other value as one item, nothing when absent.
        private static IEnumerable<JsonElement> Items(JsonElement? element)
        {
            if (!element.HasValue) return Array.Empty<JsonElement>();
            if (element.Value.ValueKind == JsonValueKind.Array) return element.Value.EnumerateArray().ToArray();
            return new[] { element.Value };
        }

        private sealed class Chunk
        {
            public string Suite;
            public string Id;
            public bool Priced;
            public double Seconds;
            public bool HasTiming;
            public string Phase = "";
            public string StartedAt = "";
            public string CompletedAt = "";
            public double? Wall;

            public void Price(double seconds)
            {
                Seconds = seconds;
                Priced = true;
            }
        }

        // The class-cost file, opened on the first row that needs it (antiphon-nightly-class-costs-v1: classes.<name>.seconds).
        private sealed class ClassCosts
        {
            private readonly IFileSystem _files;
            private readonly string _path;
            private Dictionary<string, double> _table;
            private bool _opened;

            public ClassCosts(IFileSystem files, string path)
            {
                _files = files;
                _path = path;
            }

            // The sum of the classes' seconds; null when there is no class, no cost file, or a class it does not price.
            public double? Price(JsonElement classes)
            {
                var names = Items(classes).Select(item => Text(item)).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
                if (names.Length == 0) return null;
                var table = Table();
                if (table == null) return null;
                var sum = 0.0;
                foreach (var name in names)
                {
                    if (!table.TryGetValue(name, out var seconds)) return null;
                    sum += seconds;
                }
                return sum;
            }

            private Dictionary<string, double> Table()
            {
                if (_opened) return _table;
                _opened = true;
                if (string.IsNullOrWhiteSpace(_path)) return null;
                using var document = ReadJson(_files, _path);
                if (Text(Member(document.RootElement, "format")) != "antiphon-nightly-class-costs-v1") throw new QualificationEtaRefusal("class-cost-format");
                var table = new Dictionary<string, double>(StringComparer.Ordinal);
                var classes = Member(document.RootElement, "classes");
                if (classes.HasValue && classes.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var row in classes.Value.EnumerateObject())
                    {
                        var seconds = Number(Member(row.Value, "seconds"));
                        if (seconds.HasValue) table[row.Name] = seconds.Value;
                    }
                }
                _table = table;
                return table;
            }
        }
    }
}
