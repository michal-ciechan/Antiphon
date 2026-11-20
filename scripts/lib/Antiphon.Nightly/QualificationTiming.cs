// Qualification chunk timing, moved from test-phase-evidence.ps1 (Update-QualificationChunkTiming and its readers,
// Get-QualificationTimingReport, Write-QualificationRunPhase, Get-QualificationSetupAccounting). Plain C# types and
// System.Text.Json only: it runs in any process, with or without PowerShell.
//
// Update runs at each phase of a chunk (preflight, discovery, run, launch, the end). It writes
// <logRoot>/<suite>-<chunk>.timing.json, plus a copy under the timing root, and carries forward what earlier
// calls in this process passed for the chunk. A terminal call (Terminal) also reads three things under one 200 ms
// budget:
//   - the prior sidecar, for fields this process no longer remembers;
//   - the chunk's TRX: test count, p50/p95/max and the slowest tests;
//   - the chunk's own phase journals under the timing root. These are
//     chunk-<sha256 of chunk, run, attempt, suite>/cases-<pid>-<nonce>.jsonl, owned by an owner file's name
//     or by their header record. Only that directory is listed.
// Readers open only regular files: a link, FIFO or directory is refused before any open. A refused or
// over-budget read nulls the metrics and records why = timing-read-refused; the next publication retries.
// Update never throws: a timing sidecar must not change a suite verdict.
//
// Test seams:
//   - BudgetWatch and BudgetDeadlineMilliseconds replace the terminal budget (a stopped Stopwatch holds the
//     clock still);
//   - ReadLog records every open, stream, listing and byte read;
//   - Forget drops one chunk's process memory.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.TestPhase;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Nightly
{
    /// <summary>What the timing readers touched while QualificationTiming.ReadLog was set.</summary>
    public sealed class QualificationReadLog
    {
        /// <summary>Every open attempted, in order.</summary>
        public readonly List<string> Opens = new List<string>();
        /// <summary>Opens that gave a readable stream.</summary>
        public readonly List<string> Streams = new List<string>();
        /// <summary>Chunk directories listed, and the entries each listing returned.</summary>
        public readonly List<string> Listings = new List<string>();
        public readonly List<int> ListedEntries = new List<int>();
        /// <summary>Bytes read: journals (cases-*.jsonl), TRX files, sidecars.</summary>
        public long JournalBytes;
        public long TrxBytes;
        public long SidecarBytes;
        /// <summary>Sidecar reads started.</summary>
        public int SidecarReads;
        public long ReadBytes => JournalBytes + TrxBytes + SidecarBytes;
    }

    /// <summary>
    /// One Update call. Identity (LogRoot, Suite, Chunk, RunId, Attempt) and Terminal are always taken as given; every
    /// other field is either set (its value, possibly empty or null, replaces what the process remembers for the chunk)
    /// or left unset (the remembered value, or on a terminal call the prior sidecar's, is kept).
    /// </summary>
    public sealed class ChunkTimingUpdate
    {
        private string timingRoot = "", trxPath = "", why = "", lastPhase = "", startedAt = "", completedAt = "";
        private double? queueWait, build, discovery, runWall;

        public string LogRoot { get; set; } = "";
        public string Suite { get; set; } = "";
        public string Chunk { get; set; } = "";
        public string RunId { get; set; } = "";
        public string Attempt { get; set; } = "";
        public bool Terminal { get; set; }

        public string TimingRoot { get => timingRoot; set { timingRoot = value ?? ""; HasTimingRoot = true; } }
        public string TrxPath { get => trxPath; set { trxPath = value ?? ""; HasTrxPath = true; } }
        public string Why { get => why; set { why = value ?? ""; HasWhy = true; } }
        public string LastPhase { get => lastPhase; set { lastPhase = value ?? ""; HasLastPhase = true; } }
        public string StartedAt { get => startedAt; set { startedAt = value ?? ""; HasStartedAt = true; } }
        public string CompletedAt { get => completedAt; set { completedAt = value ?? ""; HasCompletedAt = true; } }
        public double? QueueWaitSeconds { get => queueWait; set { queueWait = value; HasQueueWaitSeconds = true; } }
        public double? BuildSeconds { get => build; set { build = value; HasBuildSeconds = true; } }
        public double? DiscoverySeconds { get => discovery; set { discovery = value; HasDiscoverySeconds = true; } }
        public double? RunWallSeconds { get => runWall; set { runWall = value; HasRunWallSeconds = true; } }

        public bool HasTimingRoot { get; private set; }
        public bool HasTrxPath { get; private set; }
        public bool HasWhy { get; private set; }
        public bool HasLastPhase { get; private set; }
        public bool HasStartedAt { get; private set; }
        public bool HasCompletedAt { get; private set; }
        public bool HasQueueWaitSeconds { get; private set; }
        public bool HasBuildSeconds { get; private set; }
        public bool HasDiscoverySeconds { get; private set; }
        public bool HasRunWallSeconds { get; private set; }
    }

    /// <summary>A slow test of a chunk's TRX.</summary>
    public sealed class SlowTest : IJsonWritable
    {
        public string Name { get; set; } = "";
        public double Seconds { get; set; }

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("name", Name);
            writer.WriteNumber("seconds", Seconds);
            writer.WriteEndObject();
        }
    }

    /// <summary>A chunk's timing sidecar (&lt;suite&gt;-&lt;chunk&gt;.timing.json).</summary>
    public sealed class ChunkTiming : IJsonWritable
    {
        public string Suite { get; set; } = "";
        public string Chunk { get; set; } = "";
        public string TimingRoot { get; set; } = "";
        public string Trx { get; set; } = "";
        public bool TrxMissing { get; set; } = true;
        public string Why { get; set; } = "";
        public string LastPhase { get; set; } = "";
        public string LastPhaseUtc { get; set; } = "";
        public string StartedAt { get; set; } = "";
        public string CompletedAt { get; set; } = "";
        public double? QueueWaitSeconds { get; set; }
        public double? BuildSeconds { get; set; }
        public double? BuildSlotWaitSeconds { get; set; }
        public string BuildSlot { get; set; } = "not-used";
        public double? DiscoverySeconds { get; set; }
        public double? RunWallSeconds { get; set; }
        public double? DistillationSeconds { get; set; }
        public double? FixtureSetupSeconds { get; set; }
        public double? FixtureTeardownSeconds { get; set; }
        public double? ProcessSpawnSeconds { get; set; }
        public double? ProcessWaitSeconds { get; set; }
        public int TestCount { get; set; }
        public double? P50Seconds { get; set; }
        public double? P95Seconds { get; set; }
        public double? MaxSeconds { get; set; }
        public SlowTest[] Slowest { get; set; } = new SlowTest[0];

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("suite", Suite);
            writer.WriteString("chunk", Chunk);
            writer.WriteString("timingRoot", TimingRoot);
            writer.WriteString("trx", Trx);
            writer.WriteBoolean("trxMissing", TrxMissing);
            writer.WriteString("why", Why);
            writer.WriteString("lastPhase", LastPhase);
            writer.WriteString("lastPhaseUtc", LastPhaseUtc);
            writer.WriteString("startedAt", StartedAt);
            writer.WriteString("completedAt", CompletedAt);
            CompactJson.Number(writer, "queueWaitSeconds", QueueWaitSeconds);
            CompactJson.Number(writer, "buildSeconds", BuildSeconds);
            CompactJson.Number(writer, "buildSlotWaitSeconds", BuildSlotWaitSeconds);
            writer.WriteString("buildSlot", BuildSlot);
            CompactJson.Number(writer, "discoverySeconds", DiscoverySeconds);
            CompactJson.Number(writer, "runWallSeconds", RunWallSeconds);
            CompactJson.Number(writer, "distillationSeconds", DistillationSeconds);
            CompactJson.Number(writer, "fixtureSetupSeconds", FixtureSetupSeconds);
            CompactJson.Number(writer, "fixtureTeardownSeconds", FixtureTeardownSeconds);
            CompactJson.Number(writer, "processSpawnSeconds", ProcessSpawnSeconds);
            CompactJson.Number(writer, "processWaitSeconds", ProcessWaitSeconds);
            writer.WriteNumber("testCount", TestCount);
            CompactJson.Number(writer, "p50Seconds", P50Seconds);
            CompactJson.Number(writer, "p95Seconds", P95Seconds);
            CompactJson.Number(writer, "maxSeconds", MaxSeconds);
            writer.WriteStartArray("slowest");
            foreach (var slow in Slowest ?? new SlowTest[0]) slow.WriteJson(writer);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>The sidecar read from JSON (a missing or mistyped field takes its default).</summary>
        public static ChunkTiming From(JsonElement root)
        {
            var slowest = new List<SlowTest>();
            if (Json.Field(root, "slowest", out var rows) && rows.ValueKind == JsonValueKind.Array)
                foreach (var row in rows.EnumerateArray())
                    if (row.ValueKind == JsonValueKind.Object) slowest.Add(new SlowTest { Name = Json.Text(row, "name"), Seconds = Json.Number(row, "seconds") ?? 0 });
            return new ChunkTiming
            {
                Suite = Json.Text(root, "suite"), Chunk = Json.Text(root, "chunk"), TimingRoot = Json.Text(root, "timingRoot"),
                Trx = Json.Text(root, "trx"), TrxMissing = !Json.Field(root, "trxMissing", out var missing) || missing.ValueKind != JsonValueKind.False,
                Why = Json.Text(root, "why"), LastPhase = Json.Text(root, "lastPhase"), LastPhaseUtc = Json.Text(root, "lastPhaseUtc"),
                StartedAt = Json.Text(root, "startedAt"), CompletedAt = Json.Text(root, "completedAt"),
                QueueWaitSeconds = Json.Number(root, "queueWaitSeconds"), BuildSeconds = Json.Number(root, "buildSeconds"),
                BuildSlotWaitSeconds = Json.Number(root, "buildSlotWaitSeconds"), BuildSlot = Json.Has(root, "buildSlot") ? Json.Text(root, "buildSlot") : "not-used",
                DiscoverySeconds = Json.Number(root, "discoverySeconds"), RunWallSeconds = Json.Number(root, "runWallSeconds"),
                DistillationSeconds = Json.Number(root, "distillationSeconds"), FixtureSetupSeconds = Json.Number(root, "fixtureSetupSeconds"),
                FixtureTeardownSeconds = Json.Number(root, "fixtureTeardownSeconds"), ProcessSpawnSeconds = Json.Number(root, "processSpawnSeconds"),
                ProcessWaitSeconds = Json.Number(root, "processWaitSeconds"), TestCount = (int)(Json.Number(root, "testCount") ?? 0),
                P50Seconds = Json.Number(root, "p50Seconds"), P95Seconds = Json.Number(root, "p95Seconds"), MaxSeconds = Json.Number(root, "maxSeconds"),
                Slowest = slowest.ToArray(),
            };
        }
    }

    /// <summary>One suite's span across its chunk sidecars.</summary>
    public sealed class SuiteTimeline : IJsonWritable
    {
        public string Suite { get; set; } = "";
        public string StartedAt { get; set; } = "";
        public string CompletedAt { get; set; } = "";
        public string LastPhase { get; set; } = "";
        public string LastPhaseUtc { get; set; } = "";

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("suite", Suite);
            writer.WriteString("startedAt", StartedAt);
            writer.WriteString("completedAt", CompletedAt);
            writer.WriteString("lastPhase", LastPhase);
            writer.WriteString("lastPhaseUtc", LastPhaseUtc);
            writer.WriteEndObject();
        }
    }

    public sealed class TimingReport
    {
        public string TimingRoot { get; set; } = "";
        public ChunkTiming[] Chunks { get; set; } = new ChunkTiming[0];
        public SuiteTimeline[] Timeline { get; set; } = new SuiteTimeline[0];
    }

    /// <summary>Fixture setup/teardown and process spawn/wait seconds of a chunk's journals (null: none observed).</summary>
    public sealed class JournalPhaseSums
    {
        public double? FixtureSetupSeconds { get; set; }
        public double? FixtureTeardownSeconds { get; set; }
        public double? ProcessSpawnSeconds { get; set; }
        public double? ProcessWaitSeconds { get; set; }
        /// <summary>A read was refused or the budget ran out.</summary>
        public bool Refused { get; set; }
    }

    /// <summary>A TRX's result durations: count, p50/p95/max (null without results) and the slowest tests.</summary>
    public sealed class TrxSummary
    {
        public int Count { get; set; }
        public double? P50 { get; set; }
        public double? P95 { get; set; }
        public double? Max { get; set; }
        public SlowTest[] Slowest { get; set; } = new SlowTest[0];
        /// <summary>False when the read was refused or ran out of budget (nothing else is then set).</summary>
        public bool Complete { get; set; }
    }

    /// <summary>An owner file name (own-&lt;token&gt;-&lt;pid&gt;-&lt;nonce&gt;.txt) and the journal it names.</summary>
    public sealed class OwnerFile
    {
        public string Token { get; set; } = "";
        public string Journal { get; set; } = "";
    }

    /// <summary>The setup phases of a run (setup-phases.json).</summary>
    public sealed class SetupPhaseAccounting
    {
        public bool CountedOnce { get; set; }
        public double SetupSeconds { get; set; }
        /// <summary>The build, npm-build and dotnet-build phases.</summary>
        public double PerChunkBuildSum { get; set; }
        public bool Overlapped { get; set; }
        public long? FirstStartUtcTicks { get; set; }
    }

    // Field reads that follow PowerShell's property rules (names ignore case) without PowerShell.
    internal static class Json
    {
        public static bool Field(JsonElement o, string name, out JsonElement value)
        {
            value = default;
            if (o.ValueKind != JsonValueKind.Object) return false;
            if (o.TryGetProperty(name, out value)) return true;
            foreach (var property in o.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
            return false;
        }

        public static bool Has(JsonElement o, string name) => Field(o, name, out _);

        private static readonly char[] FractionMarks = { '.', 'e', 'E' };

        // [string]$o.name.
        public static string Text(JsonElement o, string name)
        {
            if (!Field(o, name, out var v)) return "";
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString() ?? "";
                case JsonValueKind.Null: return "";
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Number:
                    var raw = v.GetRawText();
                    if (raw.IndexOfAny(FractionMarks) >= 0) return v.GetDouble().ToString(CultureInfo.InvariantCulture);
                    return v.TryGetInt64(out var whole) ? whole.ToString(CultureInfo.InvariantCulture) : raw;
                default: return v.GetRawText();
            }
        }

        // A number field (null when absent, null or not a number).
        public static double? Number(JsonElement o, string name) =>
            Field(o, name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;

        // [double]$o.name for a phase offset: absent or null is 0, a numeric string parses, anything else is null.
        public static double? Offset(JsonElement o, string name)
        {
            if (!Field(o, name, out var v)) return 0;
            switch (v.ValueKind)
            {
                case JsonValueKind.Null: return 0;
                case JsonValueKind.Number: return v.GetDouble();
                case JsonValueKind.True: return 1;
                case JsonValueKind.False: return 0;
                case JsonValueKind.String:
                    var text = v.GetString();
                    if (string.IsNullOrWhiteSpace(text)) return 0;
                    return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null;
                default: return null;
            }
        }
    }

    public static class QualificationTiming
    {
        public const string RefusedWhy = "timing-read-refused";
        private const int MaxBytes = 262144;
        private const int DefaultBudget = 200;

        /// <summary>Test seam: the terminal budget's clock (null: a new running Stopwatch per publication).</summary>
        public static Stopwatch BudgetWatch;
        /// <summary>Test seam: the terminal budget in milliseconds.</summary>
        public static int BudgetDeadlineMilliseconds = DefaultBudget;
        /// <summary>Test seam: while set, every read is recorded here.</summary>
        public static QualificationReadLog ReadLog;

        public static QualificationReadLog StartReadLog() => ReadLog = new QualificationReadLog();

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ChunkMemory> Memory = new Dictionary<string, ChunkMemory>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Contribution> ShardCache = new Dictionary<string, Contribution>(StringComparer.OrdinalIgnoreCase);
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private static readonly JsonDocumentOptions LineOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 };
        private static readonly JsonDocumentOptions StrictOptions = new JsonDocumentOptions { MaxDepth = 60 };
        private static readonly EnumerationOptions ListAll = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        private static readonly EnumerationOptions ListVisible = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
        private static bool warmed;

        private sealed class ChunkMemory
        {
            public string TimingRoot, Trx, Why, LastPhase, StartedAt, CompletedAt;
            public double? QueueWait, Build, Discovery, RunWall;
        }

        // One journal's share: fixture setup/teardown by fixture id (a fixture shared by several journals counts
        // once), process spawn and wait.
        private sealed class Contribution
        {
            public readonly Dictionary<string, double?[]> Fixtures = new Dictionary<string, double?[]>(StringComparer.OrdinalIgnoreCase);
            public double? Spawn;
            public double? Wait;
        }

        /// <summary>Drops the process memory of one chunk (the next update starts from its sidecar).</summary>
        public static bool Forget(string logRoot, string suite, string chunk)
        {
            lock (Gate) return Memory.Remove(logRoot + "|" + suite + "|" + chunk);
        }

        /// <summary>Clears the chunk memory, the journal cache and the test seams.</summary>
        public static void Reset()
        {
            lock (Gate) { Memory.Clear(); ShardCache.Clear(); }
            BudgetWatch = null;
            BudgetDeadlineMilliseconds = DefaultBudget;
            ReadLog = null;
        }

        // ---- the sidecar ---------------------------------------------------------------------------------------

        /// <summary>
        /// Update-QualificationChunkTiming: writes the chunk's sidecar from the update, what this process remembers for
        /// the chunk and, on a terminal update, the prior sidecar and the chunk's TRX and journals. nowUtc stamps
        /// lastPhaseUtc. Never throws.
        /// </summary>
        public static void Update(ChunkTimingUpdate update, DateTime nowUtc)
        {
            try { if (update != null) UpdateCore(update, nowUtc); }
            catch (Exception) { }
        }

        private static void UpdateCore(ChunkTimingUpdate u, DateTime nowUtc)
        {
            var logRoot = u.LogRoot ?? "";
            var suite = u.Suite ?? "";
            var chunk = u.Chunk ?? "";
            if (Blank(logRoot) || Blank(suite) || Blank(chunk)) return;
            var runId = u.RunId ?? "";
            var attempt = u.Attempt ?? "";
            var key = logRoot + "|" + suite + "|" + chunk;
            ChunkMemory memory;
            lock (Gate) Memory.TryGetValue(key, out memory);
            var path = Path.Combine(logRoot, suite + "-" + chunk + ".timing.json");

            var timingRoot = "";
            if (u.HasTimingRoot && !Blank(u.TimingRoot)) timingRoot = u.TimingRoot;
            else if (memory != null && !Blank(memory.TimingRoot)) timingRoot = memory.TimingRoot;
            else if (u.HasTimingRoot) timingRoot = u.TimingRoot;
            var record = new ChunkTiming
            {
                Suite = suite, Chunk = chunk, TimingRoot = timingRoot,
                Trx = u.HasTrxPath ? u.TrxPath : memory?.Trx ?? "",
                Why = u.HasWhy ? u.Why : memory?.Why ?? "",
                LastPhase = u.HasLastPhase ? u.LastPhase : memory?.LastPhase ?? "",
                StartedAt = u.HasStartedAt ? u.StartedAt : memory?.StartedAt ?? "",
                CompletedAt = u.HasCompletedAt ? u.CompletedAt : memory?.CompletedAt ?? "",
                QueueWaitSeconds = u.HasQueueWaitSeconds ? u.QueueWaitSeconds : memory?.QueueWait,
                BuildSeconds = u.HasBuildSeconds ? u.BuildSeconds : memory?.Build,
                DiscoverySeconds = u.HasDiscoverySeconds ? u.DiscoverySeconds : memory?.Discovery,
                RunWallSeconds = u.HasRunWallSeconds ? u.RunWallSeconds : memory?.RunWall,
                LastPhaseUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture),
            };
            if (u.Terminal) ReadTerminal(u, record, path, runId, attempt);
            lock (Gate)
            {
                Memory[key] = new ChunkMemory
                {
                    TimingRoot = record.TimingRoot, Trx = record.Trx, Why = record.Why, LastPhase = record.LastPhase,
                    StartedAt = record.StartedAt, CompletedAt = record.CompletedAt, QueueWait = record.QueueWaitSeconds,
                    Build = record.BuildSeconds, Discovery = record.DiscoverySeconds, RunWall = record.RunWallSeconds,
                };
            }
            var text = CompactJson.Text(record);
            EvidenceFiles.WriteAtomic(path, text, true);
            if (!Blank(record.TimingRoot))
            {
                var copy = Path.Combine(record.TimingRoot, suite + "-" + chunk + ".timing.json");
                if (!string.Equals(Path.GetFullPath(copy), Path.GetFullPath(path), PathComparison)) EvidenceFiles.WriteAtomic(copy, text, true);
            }
        }

        // The terminal reads, filling record's metrics (or why = timing-read-refused) under one budget.
        private static void ReadTerminal(ChunkTimingUpdate u, ChunkTiming record, string path, string runId, string attempt)
        {
            // A refused read is a gap for that publication, not a permanent suite failure.
            if (!u.HasWhy && Same(record.Why, RefusedWhy)) record.Why = "";
            Warm();
            var watch = BudgetWatch ?? Stopwatch.StartNew();
            var deadline = BudgetDeadlineMilliseconds;
            var refused = false;
            ChunkTiming prior = null;
            var sideKind = TestPhaseFiles.Kind(path, out var sideSize);
            if (sideKind == "missing") { }
            else if (sideKind != "regular" || sideSize > MaxBytes) refused = true;
            else
            {
                prior = ReadSidecarCore(path, watch, deadline);
                if (prior == null) refused = true;
            }
            if (prior != null && !refused)
            {
                if (Blank(record.StartedAt)) record.StartedAt = prior.StartedAt;
                if (!u.HasQueueWaitSeconds && record.QueueWaitSeconds == null) record.QueueWaitSeconds = prior.QueueWaitSeconds;
                if (!u.HasBuildSeconds && record.BuildSeconds == null) record.BuildSeconds = prior.BuildSeconds;
                if (!u.HasDiscoverySeconds && record.DiscoverySeconds == null) record.DiscoverySeconds = prior.DiscoverySeconds;
                if (!u.HasRunWallSeconds && record.RunWallSeconds == null) record.RunWallSeconds = prior.RunWallSeconds;
                if (!u.HasTrxPath && Blank(record.Trx)) record.Trx = prior.Trx;
                if (!u.HasWhy && Blank(record.Why) && !Same(prior.Why, RefusedWhy)) record.Why = prior.Why;
                if (!u.HasCompletedAt && Blank(record.CompletedAt)) record.CompletedAt = prior.CompletedAt;
            }
            TrxTimingStats stats = null;
            if (!Blank(record.Trx))
            {
                var trxKind = TestPhaseFiles.Kind(record.Trx, out _);
                if (trxKind == "missing") record.TrxMissing = true;
                else if (trxKind != "regular") refused = true;
                else if (watch.ElapsedMilliseconds >= deadline) { record.TrxMissing = false; refused = true; }
                else
                {
                    record.TrxMissing = false;
                    stats = TrxCore(record.Trx, watch, deadline);
                    if (stats == null) refused = true;
                }
            }
            JournalPhaseSums sums = null;
            if (!Blank(record.TimingRoot))
            {
                if (watch.ElapsedMilliseconds >= deadline) refused = true;
                else
                {
                    sums = PhaseSumsCore(record.TimingRoot, record.Chunk, runId, record.Suite, attempt, watch, deadline);
                    if (sums.Refused) refused = true;
                }
            }
            if (watch.ElapsedMilliseconds >= deadline) refused = true;
            if (refused)
            {
                if (Blank(record.Why)) record.Why = RefusedWhy;
                return;
            }
            if (sums != null)
            {
                record.FixtureSetupSeconds = sums.FixtureSetupSeconds;
                record.FixtureTeardownSeconds = sums.FixtureTeardownSeconds;
                record.ProcessSpawnSeconds = sums.ProcessSpawnSeconds;
                record.ProcessWaitSeconds = sums.ProcessWaitSeconds;
            }
            if (stats != null)
            {
                if (stats.Count > 0) { record.P50Seconds = stats.P50; record.P95Seconds = stats.P95; record.MaxSeconds = stats.Max; }
                record.Slowest = SlowRows(stats);
                record.TestCount = stats.Count;
            }
        }

        private static SlowTest[] SlowRows(TrxTimingStats stats)
        {
            var names = stats.SlowNames ?? new string[0];
            var values = stats.SlowSeconds ?? new double[0];
            var rows = new SlowTest[names.Length];
            for (var i = 0; i < names.Length; i++) rows[i] = new SlowTest { Name = names[i] ?? "", Seconds = i < values.Length ? values[i] : 0.0 };
            return rows;
        }

        /// <summary>
        /// Get-QualificationTimingReport: every *.timing.json sidecar of logRoot (in name order) and one timeline row per
        /// suite (earliest start, latest end, the phase with the latest lastPhaseUtc).
        /// </summary>
        public static TimingReport Report(string logRoot, string timingRoot)
        {
            EvidenceFiles.FlushKeeping(null);
            var chunks = new List<ChunkTiming>();
            if (!Blank(logRoot) && Directory.Exists(logRoot))
            {
                var files = new List<FileInfo>();
                try
                {
                    foreach (var file in new DirectoryInfo(logRoot).EnumerateFiles("*", ListVisible))
                        if (file.Name.EndsWith(".timing.json", StringComparison.OrdinalIgnoreCase)) files.Add(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                foreach (var file in files)
                {
                    var row = ReadSidecarCore(file.FullName, null, DefaultBudget);
                    if (row != null) chunks.Add(row);
                }
            }
            var bySuite = new Dictionary<string, SuiteTimeline>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in chunks)
            {
                if (Blank(row.Suite)) continue;
                if (!bySuite.TryGetValue(row.Suite, out var bag)) bySuite.Add(row.Suite, bag = new SuiteTimeline { Suite = row.Suite });
                if (!Blank(row.StartedAt) && (Blank(bag.StartedAt) || string.CompareOrdinal(row.StartedAt, bag.StartedAt) < 0)) bag.StartedAt = row.StartedAt;
                if (!Blank(row.CompletedAt) && string.CompareOrdinal(row.CompletedAt, bag.CompletedAt) > 0) bag.CompletedAt = row.CompletedAt;
                if (!Blank(row.LastPhaseUtc) && string.CompareOrdinal(row.LastPhaseUtc, bag.LastPhaseUtc) > 0)
                {
                    bag.LastPhaseUtc = row.LastPhaseUtc;
                    bag.LastPhase = row.LastPhase;
                }
            }
            var suites = new List<string>(bySuite.Keys);
            suites.Sort(StringComparer.OrdinalIgnoreCase);
            var timeline = new SuiteTimeline[suites.Count];
            for (var i = 0; i < suites.Count; i++) timeline[i] = bySuite[suites[i]];
            return new TimingReport { TimingRoot = timingRoot ?? "", Chunks = chunks.ToArray(), Timeline = timeline };
        }

        // ---- setup-phases.json -----------------------------------------------------------------------------

        private sealed class SetupPhase
        {
            public string Name = "", StartedAt = "", EndedAt = "";
            public double? Seconds;
        }

        /// <summary>
        /// Write-QualificationRunPhase: records one named setup phase (npm-ci, dotnet-build, ...) in
        /// timingDirectory/setup-phases.json, replacing an earlier row of that name. A name outside
        /// [a-z0-9][a-z0-9-]{0,40} is ignored; an unreadable file throws.
        /// </summary>
        public static void WriteRunPhase(string timingDirectory, string name, string startedAt, string endedAt, double seconds)
        {
            if (Blank(timingDirectory) || Blank(name) || !PhaseName(name)) return;
            var path = Path.Combine(timingDirectory, "setup-phases.json");
            var kept = new List<SetupPhase>();
            foreach (var row in SetupRows(path)) if (!Same(row.Name, name)) kept.Add(row);
            kept.Add(new SetupPhase { Name = name, StartedAt = startedAt ?? "", EndedAt = endedAt ?? "", Seconds = seconds });
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, CompactJson.Options))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WriteStartArray("phases");
                foreach (var row in kept)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", row.Name);
                    writer.WriteString("startedAt", row.StartedAt);
                    writer.WriteString("endedAt", row.EndedAt);
                    writer.WriteNumber("seconds", row.Seconds ?? 0);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            EvidenceFiles.WriteAtomic(path, Encoding.UTF8.GetString(buffer.ToArray()), true);
        }

        /// <summary>
        /// Get-QualificationSetupAccounting: the setup seconds, the build phases' sum, whether two phases overlap and
        /// the first phase's start, of timingDirectory/setup-phases.json.
        /// </summary>
        public static SetupPhaseAccounting SetupAccounting(string timingDirectory)
        {
            var path = Path.Combine(timingDirectory ?? "", "setup-phases.json");
            var result = new SetupPhaseAccounting { CountedOnce = TestPhaseFiles.Kind(path, out _) == "regular" };
            var recordedFirst = false;
            var intervals = new List<long[]>();
            foreach (var row in SetupRows(path))
            {
                var value = row.Seconds ?? 0;
                result.SetupSeconds += value;
                if (Same(row.Name, "build") || Same(row.Name, "npm-build") || Same(row.Name, "dotnet-build")) result.PerChunkBuildSum += value;
                var start = UtcTicks(row.StartedAt);
                var end = UtcTicks(row.EndedAt);
                if (!recordedFirst) { recordedFirst = true; result.FirstStartUtcTicks = start; }
                if (start != null && end != null) intervals.Add(new[] { start.Value, end.Value });
            }
            intervals.Sort((a, b) => a[0].CompareTo(b[0]));
            for (var i = 1; i < intervals.Count; i++) if (intervals[i][0] < intervals[i - 1][1]) result.Overlapped = true;
            return result;
        }

        // The phases of setup-phases.json (none when it is not a regular file).
        private static List<SetupPhase> SetupRows(string path)
        {
            var rows = new List<SetupPhase>();
            if (TestPhaseFiles.Kind(path, out _) != "regular") return rows;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(EvidenceFiles.ReadAllText(path), StrictOptions); }
            catch (JsonException) { throw new InvalidDataException("setup-phases-unreadable: " + path); }
            using (doc)
            {
                if (!Json.Field(doc.RootElement, "phases", out var phases) || phases.ValueKind != JsonValueKind.Array) return rows;
                foreach (var row in phases.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    double? seconds = null;
                    if (Json.Field(row, "seconds", out var s))
                    {
                        if (s.ValueKind == JsonValueKind.Number) seconds = s.GetDouble();
                        else if (s.ValueKind == JsonValueKind.String) seconds = double.Parse(s.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    }
                    rows.Add(new SetupPhase { Name = Json.Text(row, "name"), StartedAt = Json.Text(row, "startedAt"), EndedAt = Json.Text(row, "endedAt"), Seconds = seconds });
                }
            }
            return rows;
        }

        // UTC ticks of a stamp as ConvertFrom-Json reads one: "Z" is UTC, an offset converts, no zone is taken as UTC.
        private static long? UtcTicks(string value)
        {
            if (Blank(value)) return null;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)) return null;
            if (stamp.Kind == DateTimeKind.Local) return stamp.ToUniversalTime().Ticks;
            if (stamp.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(stamp, DateTimeKind.Utc).Ticks;
            return stamp.Ticks;
        }

        private static bool PhaseName(string name)
        {
            if (name.Length < 1 || name.Length > 41) return false;
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || (i > 0 && c == '-');
                if (!ok) return false;
            }
            return true;
        }

        // ---- identities ------------------------------------------------------------------------------------

        /// <summary>
        /// ConvertTo-QualificationChunkIdentity: lower-case SHA-256 hex of the length-prefixed chunk, run id, attempt
        /// and suite, LF-separated (the text TestTimingSession.IdentityToken hashes).
        /// </summary>
        public static string ChunkIdentity(string chunk, string runId, string attempt, string suite)
        {
            chunk ??= ""; runId ??= ""; attempt ??= ""; suite ??= "";
            var text = chunk.Length + ":" + chunk + "\n" + runId.Length + ":" + runId + "\n" + attempt.Length + ":" + attempt + "\n" + suite.Length + ":" + suite;
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>Get-QualificationChunkDirectoryName: chunk-&lt;ChunkIdentity&gt;.</summary>
        public static string ChunkDirectoryName(string chunk, string runId, string attempt, string suite) =>
            "chunk-" + ChunkIdentity(chunk, runId, attempt, suite);

        /// <summary>ConvertTo-QualificationGroupToken: lower-case hex of the chunk's UTF-8 bytes.</summary>
        public static string GroupToken(string chunk) =>
            string.IsNullOrEmpty(chunk) ? "" : Convert.ToHexStringLower(Encoding.UTF8.GetBytes(chunk));

        /// <summary>
        /// TestTimingSession.OwnerHashPrefixLength: the longest identity prefix whose owner path is still 240
        /// characters under the real candidate root.
        /// </summary>
        public static readonly int OwnerPrefixLength = OwnerPrefix();

        private static int OwnerPrefix()
        {
            var root = @"C:\Antiphon\releases\candidates\rc-2026-10-27-" + new string('0', 12) + @"\logs\timing-" + new string('0', 32);
            var bare = root + @"\chunk-" + new string('0', 64) + @"\own--2147483647-" + new string('0', 32) + ".txt";
            var room = 240 - bare.Length;
            return room < 1 ? 1 : room > 64 ? 64 : room;
        }

        /// <summary>Test-QualificationOwnerToken: the owner file's token is the chunk identity or its prefix.</summary>
        public static bool OwnerToken(string token, string identity)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(identity)) return false;
            if (string.Equals(token, identity, StringComparison.Ordinal)) return true;
            if (token.Length != OwnerPrefixLength || identity.Length < OwnerPrefixLength) return false;
            return string.CompareOrdinal(identity, 0, token, 0, OwnerPrefixLength) == 0;
        }

        /// <summary>ConvertFrom-QualificationOwnerName: the token and journal of an owner file name, or null.</summary>
        public static OwnerFile OwnerName(string name) =>
            OwnerName(name ?? "", out var token, out var journal) ? new OwnerFile { Token = token, Journal = journal } : null;

        // own-<hex token>-<pid>-<nonce>.txt names the journal cases-<pid>-<nonce>.jsonl.
        private static bool OwnerName(string name, out string token, out string journal)
        {
            token = journal = null;
            if (!Wildcard(name, "own-", ".txt")) return false;
            var body = name.Substring(4, name.Length - 8);
            var dash = body.IndexOf('-');
            if (dash < 1) return false;
            for (var i = 0; i < dash; i++) if (!Uri.IsHexDigit(body[i])) return false;
            var pidEnd = body.IndexOf('-', dash + 1);
            if (pidEnd <= dash + 1 || pidEnd == body.Length - 1) return false;
            for (var i = dash + 1; i < pidEnd; i++) if (!char.IsDigit(body[i])) return false;
            token = body.Substring(0, dash);
            journal = "cases-" + body.Substring(dash + 1, pidEnd - dash - 1) + "-" + body.Substring(pidEnd + 1) + ".jsonl";
            return true;
        }

        // cases-<pid>-<nonce without dots>.jsonl.
        private static bool JournalName(string name, out string pid, out string nonce)
        {
            pid = nonce = null;
            if (!Wildcard(name, "cases-", ".jsonl")) return false;
            var body = name.Substring(6, name.Length - 12);
            var dash = body.IndexOf('-');
            if (dash < 1 || dash == body.Length - 1) return false;
            for (var i = 0; i < dash; i++) if (!char.IsDigit(body[i])) return false;
            var rest = body.Substring(dash + 1);
            if (rest.IndexOf('.') >= 0) return false;
            pid = body.Substring(0, dash);
            nonce = rest;
            return true;
        }

        private static bool Wildcard(string name, string prefix, string suffix) =>
            name.Length >= prefix.Length + suffix.Length
            && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        // ---- phase journals --------------------------------------------------------------------------------

        /// <summary>
        /// Test-QualificationTimingDirectory: 'missing', 'directory', 'symlink', 'fifo' (Linux), 'other' or 'unreadable'.
        /// A link is never followed.
        /// </summary>
        public static string DirectoryKind(string path)
        {
            if (Blank(path)) return "missing";
            if (OperatingSystem.IsWindows())
            {
                FileAttributes attrs;
                try { attrs = File.GetAttributes(path); }
                catch (FileNotFoundException) { return "missing"; }
                catch (DirectoryNotFoundException) { return "missing"; }
                catch (Exception) { return "unreadable"; }
                if ((attrs & FileAttributes.ReparsePoint) != 0) return "symlink";
                return (attrs & FileAttributes.Directory) != 0 ? "directory" : "other";
            }
            if (FileIdentity.LStat(path, out LinuxStat stat) != 0)
                return System.Runtime.InteropServices.Marshal.GetLastWin32Error() == 2 ? "missing" : "unreadable";
            switch (stat.Mode & 0xF000)
            {
                case 0x4000: return "directory";
                case 0xA000: return "symlink";
                case 0x1000: return "fifo";
                default: return "other";
            }
        }

        /// <summary>
        /// Get-QualificationPhaseSums: the chunk's own journals' fixture and process seconds. Without a watch each
        /// journal gets its own 200 ms slice of deadlineMilliseconds.
        /// </summary>
        public static JournalPhaseSums PhaseSums(string timingRoot, string chunk, string runId, string suite, string attempt) =>
            PhaseSums(timingRoot, chunk, runId, suite, attempt, null, 500);

        public static JournalPhaseSums PhaseSums(string timingRoot, string chunk, string runId, string suite, string attempt, Stopwatch watch, int deadlineMilliseconds)
        {
            Warm();
            return PhaseSumsCore(timingRoot, chunk ?? "", runId ?? "", suite ?? "", attempt ?? "", watch, deadlineMilliseconds);
        }

        private static JournalPhaseSums PhaseSumsCore(string timingRoot, string chunk, string runId, string suite, string attempt, Stopwatch watch, int deadline)
        {
            var result = new JournalPhaseSums();
            if (Blank(timingRoot)) return result;
            var rootKind = DirectoryKind(timingRoot);
            if (rootKind == "missing") return result;
            if (rootKind != "directory") { result.Refused = true; return result; }
            var ownedWatch = watch == null;
            if (ownedWatch) watch = Stopwatch.StartNew();
            if (deadline < 1) deadline = 1;
            // The directory name is the hash of this chunk, run, attempt and suite: listing it enumerates no other
            // chunk and no earlier attempt.
            var folder = Path.Combine(timingRoot, ChunkDirectoryName(chunk, runId, attempt, suite));
            var folderKind = DirectoryKind(folder);
            if (folderKind == "missing") return result;
            if (folderKind != "directory") { result.Refused = true; return result; }
            EvidenceFiles.FlushKeeping(null);
            var entries = new List<FileSystemInfo>();
            try { foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ListAll)) entries.Add(entry); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            entries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var log = ReadLog;
            if (log != null) { log.Listings.Add(folder); log.ListedEntries.Add(entries.Count); }
            var identity = runId + "|" + suite + "|" + attempt + "|" + chunk;
            var ourToken = ChunkIdentity(chunk, runId, attempt, suite);
            var ownedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                if (OwnerName(entry.Name, out var token, out var journal) && OwnerToken(token, ourToken)) ownedNames.Add(journal);
            var seenFixtures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            double? setup = null, teardown = null, spawn = null, wait = null;
            var refused = false;
            foreach (var shard in entries)
            {
                if (!Wildcard(shard.Name, "cases-", ".jsonl")) continue;
                if (watch.ElapsedMilliseconds >= deadline) { refused = true; break; }
                if (TestPhaseFiles.Kind(shard.FullName, out var size) != "regular") { refused = true; continue; }
                long stamp = 0;
                try { stamp = shard.LastWriteTimeUtc.Ticks; } catch (IOException) { }
                var key = shard.FullName + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + stamp.ToString(CultureInfo.InvariantCulture) + "|" + identity;
                Contribution contribution;
                bool cached;
                lock (Gate) cached = ShardCache.TryGetValue(key, out contribution);
                if (!cached)
                {
                    var left = deadline - (int)watch.ElapsedMilliseconds;
                    if (left < 1) { refused = true; break; }
                    var slice = ownedWatch && left > DefaultBudget ? DefaultBudget : left;
                    var owned = ownedNames.Contains(shard.Name);
                    if (!owned)
                    {
                        var header = ownedWatch
                            ? ReadHeader(shard.FullName, Stopwatch.StartNew(), slice, out var headerRefused)
                            : ReadHeader(shard.FullName, watch, deadline, out headerRefused);
                        if (headerRefused) { refused = true; continue; }
                        owned = ShardOwned(header, shard.Name, chunk, runId, suite, attempt);
                    }
                    if (!owned)
                    {
                        contribution = new Contribution();
                        lock (Gate) ShardCache[key] = contribution;
                    }
                    else
                    {
                        var records = ownedWatch
                            ? ReadJournal(shard.FullName, Stopwatch.StartNew(), slice)
                            : ReadJournal(shard.FullName, watch, deadline);
                        if (records == null) refused = true;
                        contribution = Contribute(records ?? new List<JsonElement>(), shard.Name, chunk, runId, suite, attempt);
                        if (records != null) lock (Gate) ShardCache[key] = contribution;
                    }
                }
                foreach (var fixture in contribution.Fixtures)
                {
                    if (!seenFixtures.Add(fixture.Key)) continue;
                    if (fixture.Value[0] != null) setup = (setup ?? 0) + fixture.Value[0].Value;
                    if (fixture.Value[1] != null) teardown = (teardown ?? 0) + fixture.Value[1].Value;
                }
                if (contribution.Spawn != null) spawn = (spawn ?? 0) + contribution.Spawn.Value;
                if (contribution.Wait != null) wait = (wait ?? 0) + contribution.Wait.Value;
            }
            if (setup != null) result.FixtureSetupSeconds = Math.Round(setup.Value, 3);
            if (teardown != null) result.FixtureTeardownSeconds = Math.Round(teardown.Value, 3);
            if (spawn != null) result.ProcessSpawnSeconds = Math.Round(spawn.Value, 3);
            if (wait != null) result.ProcessWaitSeconds = Math.Round(wait.Value, 3);
            result.Refused = refused;
            return result;
        }

        // The journal's header record (null when its first line is not one); refused when the read was.
        private static JsonElement? ReadHeader(string path, Stopwatch watch, int deadline, out bool refused)
        {
            refused = true;
            if (Blank(path)) return null;
            if (deadline < 1) deadline = 1;
            if (watch.ElapsedMilliseconds >= deadline) return null;
            var stream = Open(path);
            if (stream == null) return null;
            try
            {
                var read = JsonlTimingScanner.ReadFirstLine(stream, watch, deadline, MaxBytes);
                if (read == null || read.Refused) return null;
                refused = false;
                var text = read.Line;
                if (Blank(text) || text.IndexOf("\"recordKind\":\"header\"", StringComparison.Ordinal) < 0) return null;
                var record = ParseRecord(text);
                if (record == null || !Same(Json.Text(record.Value, "recordKind"), "header")) return null;
                return record;
            }
            catch (Exception) { refused = true; return null; }
            finally { Account(stream, 0); stream.Dispose(); }
        }

        // The journal's timing records; null when the read was refused, incomplete or out of budget.
        private static List<JsonElement> ReadJournal(string path, Stopwatch watch, int deadline)
        {
            if (Blank(path)) return null;
            if (deadline < 1) deadline = 1;
            if (TestPhaseFiles.Kind(path, out _) != "regular") return null;
            if (watch.ElapsedMilliseconds >= deadline) return null;
            var stream = Open(path);
            if (stream == null) return null;
            try
            {
                var read = JsonlTimingScanner.Read(stream, watch, deadline, MaxBytes);
                if (read == null || !read.Complete) return null;
                var records = new List<JsonElement>(read.Lines.Count);
                foreach (var line in read.Lines)
                {
                    if (watch.ElapsedMilliseconds >= deadline) return null;
                    var record = ParseRecord(line);
                    if (record != null) records.Add(record.Value);
                }
                return records;
            }
            catch (Exception) { return null; }
            finally { Account(stream, 0); stream.Dispose(); }
        }

        private static JsonElement? ParseRecord(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line, LineOptions);
                return doc.RootElement.Clone();
            }
            catch (JsonException) { return null; }
        }

        private static bool ShardOwned(JsonElement? header, string fileName, string chunk, string runId, string suite, string attempt)
        {
            if (header == null) return false;
            var h = header.Value;
            if (!Same(Json.Text(h, "recordKind"), "header") || !Same(Json.Text(h, "groupId"), chunk) || !Same(Json.Text(h, "runId"), runId)
                || !Same(Json.Text(h, "suite"), suite) || !Same(Json.Text(h, "attempt"), attempt)) return false;
            if (!JournalName(fileName, out var filePid, out var fileNonce)) return false;
            var headerPid = Json.Text(h, "pid");
            var headerNonce = Json.Text(h, "nonce");
            if (!Blank(headerPid) && !Same(headerPid, filePid)) return false;
            if (!Blank(headerNonce) && !Same(headerNonce, fileNonce)) return false;
            return true;
        }

        private static Contribution Contribute(List<JsonElement> records, string fileName, string chunk, string runId, string suite, string attempt)
        {
            JsonElement? header = null;
            foreach (var record in records) if (Same(Json.Text(record, "recordKind"), "header")) { header = record; break; }
            var result = new Contribution();
            if (!ShardOwned(header, fileName, chunk, runId, suite, attempt)) return result;
            foreach (var record in records)
            {
                var kind = Json.Text(record, "recordKind");
                if (Same(kind, "fixture"))
                {
                    var id = Json.Text(record, "fixtureId");
                    if (Blank(id)) id = Guid.NewGuid().ToString("N");
                    if (result.Fixtures.ContainsKey(id)) continue;
                    var bucket = new double?[2];
                    foreach (var phase in Phases(record))
                    {
                        var seconds = Span(phase);
                        if (seconds == null) continue;
                        var name = Json.Text(phase, "phase");
                        if (Same(name, "fixture-setup")) bucket[0] = (bucket[0] ?? 0) + seconds.Value;
                        else if (Same(name, "fixture-teardown")) bucket[1] = (bucket[1] ?? 0) + seconds.Value;
                    }
                    result.Fixtures[id] = bucket;
                }
                else if (Same(kind, "complete"))
                {
                    foreach (var phase in Phases(record))
                    {
                        var seconds = Span(phase);
                        if (seconds == null) continue;
                        var name = Json.Text(phase, "phase");
                        if (Same(name, "process-spawn")) result.Spawn = (result.Spawn ?? 0) + seconds.Value;
                        else if (Same(name, "process-wait")) result.Wait = (result.Wait ?? 0) + seconds.Value;
                    }
                }
            }
            return result;
        }

        private static IEnumerable<JsonElement> Phases(JsonElement record)
        {
            if (!Json.Field(record, "phases", out var phases)) yield break;
            if (phases.ValueKind == JsonValueKind.Array) { foreach (var phase in phases.EnumerateArray()) yield return phase; }
            else yield return phases;
        }

        // Seconds between startOffsetMs and endOffsetMs (an absent offset is 0, an end before the start is the
        // start); null for a phase that is not an object or whose start is not a number.
        private static double? Span(JsonElement phase)
        {
            if (phase.ValueKind != JsonValueKind.Object) return null;
            var start = Json.Offset(phase, "startOffsetMs");
            if (start == null) return null;
            var end = Json.Offset(phase, "endOffsetMs") ?? start.Value;
            if (end < start.Value) end = start.Value;
            return (end - start.Value) / 1000.0;
        }

        // ---- TRX -------------------------------------------------------------------------------------------

        /// <summary>
        /// Get-QualificationTrxStats: the TRX's result durations (Complete false, nothing else, when the read was
        /// refused or out of budget); null for a blank path.
        /// </summary>
        public static TrxSummary TrxStats(string trxPath) => TrxStats(trxPath, null, DefaultBudget);

        public static TrxSummary TrxStats(string trxPath, Stopwatch watch, int deadlineMilliseconds)
        {
            Warm();
            if (Blank(trxPath)) return null;
            var stats = TrxCore(trxPath, watch ?? Stopwatch.StartNew(), deadlineMilliseconds);
            if (stats == null) return new TrxSummary();
            var known = stats.Count > 0;
            return new TrxSummary
            {
                Count = stats.Count, P50 = known ? stats.P50 : (double?)null, P95 = known ? stats.P95 : (double?)null,
                Max = known ? stats.Max : (double?)null, Slowest = SlowRows(stats), Complete = true,
            };
        }

        // The summary; null when the read was refused, incomplete or out of budget.
        private static TrxTimingStats TrxCore(string path, Stopwatch watch, int deadline)
        {
            if (deadline < 1) deadline = 1;
            if (TestPhaseFiles.Kind(path, out _) != "regular") return null;
            if (watch.ElapsedMilliseconds >= deadline) return null;
            var stream = Open(path);
            if (stream == null) return null;
            try
            {
                var stats = TrxTimingScanner.Summarize(stream, watch, deadline, MaxBytes);
                return stats != null && stats.Complete ? stats : null;
            }
            catch (Exception) { return null; }
            finally { Account(stream, 1); stream.Dispose(); }
        }

        // ---- sidecars --------------------------------------------------------------------------------------

        /// <summary>
        /// Read-QualificationSidecar: the sidecar, or null when it is not a regular file of at most 256 KiB, is denser
        /// than 4000 commas or colons, is not one strict JSON object, or the read ran out of budget.
        /// </summary>
        public static ChunkTiming ReadSidecar(string path) => ReadSidecar(path, null, DefaultBudget);

        public static ChunkTiming ReadSidecar(string path, Stopwatch watch, int deadlineMilliseconds) => ReadSidecarCore(path, watch, deadlineMilliseconds);

        private static ChunkTiming ReadSidecarCore(string path, Stopwatch watch, int deadline)
        {
            Warm();
            var log = ReadLog;
            if (log != null) log.SidecarReads++;
            if (Blank(path)) return null;
            EvidenceFiles.FlushKeeping(path);
            if (watch == null) watch = Stopwatch.StartNew();
            if (deadline < 1) deadline = 1;
            if (watch.ElapsedMilliseconds >= deadline) return null;
            if (TestPhaseFiles.Kind(path, out var size) != "regular" || size > MaxBytes) return null;
            var bytes = Bounded(path, watch, deadline);
            if (bytes == null || watch.ElapsedMilliseconds >= deadline) return null;
            // A leading BOM is not the writer's: the sidecar is unreadable, as any other non-strict text.
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return null;
            var text = Encoding.UTF8.GetString(bytes);
            if (TestPhaseRecords.Dense(text, 4000, watch, deadline)) return null;
            if (watch.ElapsedMilliseconds >= deadline) return null;
            return ParseSidecar(text);
        }

        /// <summary>A sidecar's text read strictly (one JSON object, no comments or trailing commas), or null.</summary>
        public static ChunkTiming ParseSidecar(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text, StrictOptions);
                return doc.RootElement.ValueKind == JsonValueKind.Object ? ChunkTiming.From(doc.RootElement) : null;
            }
            catch (JsonException) { return null; }
        }

        // At most 256 KiB, the whole file, inside the deadline; null otherwise.
        private static byte[] Bounded(string path, Stopwatch watch, int deadline)
        {
            var stream = Open(path);
            if (stream == null) return null;
            try
            {
                var buffer = new byte[8192];
                var ms = new MemoryStream();
                while (ms.Length < MaxBytes)
                {
                    if (watch.ElapsedMilliseconds >= deadline) return null;
                    var count = stream.Read(buffer, 0, Math.Min(buffer.Length, MaxBytes - (int)ms.Length));
                    if (count <= 0) return ms.ToArray();
                    ms.Write(buffer, 0, count);
                }
                if (watch.ElapsedMilliseconds >= deadline || stream.Read(buffer, 0, 1) > 0) return null;
                return ms.ToArray();
            }
            catch (Exception) { return null; }
            finally { Account(stream, 2); stream.Dispose(); }
        }

        // ---- files -----------------------------------------------------------------------------------------

        // A read-only stream of a regular file, or null. A queued evidence write of the path lands first. On Linux
        // the open does not follow a link or wait on a FIFO (O_NOFOLLOW | O_NONBLOCK) and fstat refuses anything
        // that is not a regular file.
        private static Stream Open(string path)
        {
            var log = ReadLog;
            log?.Opens.Add(path);
            EvidenceFiles.FlushKeeping(path);
            Stream stream;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
                }
                else
                {
                    var fd = FileIdentity.Open(path, 657408);
                    if (fd < 0) return null;
                    if (FileIdentity.FStat(fd, out LinuxStat stat) != 0 || (stat.Mode & 0xF000) != 0x8000)
                    {
                        FileIdentity.Close(fd);
                        return null;
                    }
                    var handle = new SafeFileHandle((IntPtr)fd, true);
                    try { stream = new FileStream(handle, FileAccess.Read, 1, false); }
                    catch { handle.Dispose(); throw; }
                }
            }
            catch (Exception) { return null; }
            log?.Streams.Add(path);
            return stream;
        }

        // kind: 0 journal, 1 TRX, 2 sidecar.
        private static void Account(Stream stream, int kind)
        {
            var log = ReadLog;
            if (log == null) return;
            long at;
            try { at = stream.Position; } catch (Exception) { return; }
            if (kind == 0) log.JournalBytes += at;
            else if (kind == 1) log.TrxBytes += at;
            else log.SidecarBytes += at;
        }

        // The readers' first use pays for the JIT here, before any budget clock starts, so a fresh process does not
        // spend its first terminal read compiling them.
        private static void Warm()
        {
            if (warmed) return;
            warmed = true;
            try
            {
                var watch = Stopwatch.StartNew();
                var trx = Encoding.UTF8.GetBytes("<TestRun><Results><UnitTestResult testName=\"warm\" duration=\"00:00:01\" outcome=\"Passed\" /></Results></TestRun>");
                TrxTimingScanner.Summarize(new MemoryStream(trx), watch, 10000, MaxBytes);
                var journal = Encoding.UTF8.GetBytes(
                    "{\"recordKind\":\"header\",\"pid\":1,\"nonce\":\"warm\",\"runId\":\"warm\",\"groupId\":\"warm\",\"attempt\":\"0\",\"suite\":\"warm\"}\n"
                    + "{\"recordKind\":\"fixture\",\"fixtureId\":\"f\",\"phases\":[{\"phase\":\"fixture-setup\",\"startOffsetMs\":0,\"endOffsetMs\":1}]}\n");
                JsonlTimingScanner.ReadFirstLine(new MemoryStream(journal), watch, 10000, MaxBytes);
                var lines = JsonlTimingScanner.Read(new MemoryStream(journal), watch, 10000, MaxBytes);
                var records = new List<JsonElement>();
                foreach (var line in lines.Lines) { var record = ParseRecord(line); if (record != null) records.Add(record.Value); }
                Contribute(records, "cases-1-warm.jsonl", "warm", "warm", "warm", "0");
                OwnerName("own-ab-1-warm.txt", out _, out _);
                ChunkIdentity("warm", "warm", "0", "warm");
                TestPhaseRecords.Dense("{\"a\":1}", 4000, watch, 10000);
                var sample = new ChunkTiming { Slowest = new[] { new SlowTest { Name = "warm", Seconds = 1 } } };
                ParseSidecar(CompactJson.Text(sample));
                var probe = Path.Combine(Path.GetTempPath(), "antiphon-timing-warm-" + Guid.NewGuid().ToString("N"));
                TestPhaseFiles.Kind(probe, out _);
                DirectoryKind(probe);
            }
            catch (Exception) { }
        }

        // ---- values ----------------------------------------------------------------------------------------

        private static bool Blank(string s) => string.IsNullOrWhiteSpace(s);

        // PowerShell's -eq on strings.
        private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
