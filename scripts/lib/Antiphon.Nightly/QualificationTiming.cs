// Qualification chunk timing, moved from test-phase-evidence.ps1 (Update-QualificationChunkTiming and its readers,
// Get-QualificationTimingReport, Write-QualificationRunPhase, Get-QualificationSetupAccounting).
//
// Update runs at each phase of a chunk (preflight, discovery, run, launch, the end). It writes
// <logRoot>/<suite>-<chunk>.timing.json, plus a copy under the timing root, and carries forward what earlier
// calls in this process passed for the chunk. A terminal call (Terminal = $true) also reads three things under
// one 200 ms budget:
//   - the prior sidecar, for fields this process no longer remembers;
//   - the chunk's TRX: test count, p50/p95/max and the slowest tests;
//   - the chunk's own phase journals under the timing root. These are
//     chunk-<sha256 of chunk, run, attempt, suite>/cases-<pid>-<nonce>.jsonl, owned by an owner file's name
//     or by their header record. Only that directory is listed.
// Readers open only regular files: a link, FIFO or directory is refused before any open. A refused or
// over-budget read nulls the metrics and records why = timing-read-refused; the next publication retries.
// Update never throws once its keys are valid: a timing sidecar must not change a suite verdict.
//
// Test seams:
//   - BudgetWatch and BudgetDeadlineMilliseconds replace the terminal budget (a stopped Stopwatch holds the
//     clock still);
//   - ReadLog records every open, stream, listing and byte read;
//   - Forget drops one chunk's process memory.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
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
        private static readonly EnumerationOptions ListAll = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        private static readonly EnumerationOptions ListVisible = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
        private static readonly string[] UpdateKeys =
        {
            "LogRoot", "TimingRoot", "Suite", "Chunk", "TrxPath", "LastPhase", "Why", "QueueWaitSeconds", "BuildSeconds",
            "DiscoverySeconds", "RunWallSeconds", "StartedAt", "CompletedAt", "RunId", "Attempt", "Terminal",
        };
        private static readonly char[] FractionMarks = { '.', 'e', 'E' };
        private static bool warmed;

        private sealed class ChunkMemory
        {
            public string TimingRoot, Trx, Why, LastPhase, StartedAt, CompletedAt;
            public object QueueWait, Build, Discovery, RunWall;
        }

        // One journal's share: fixture setup/teardown by fixture id (a fixture shared by several journals counts
        // once), process spawn and wait.
        private sealed class Contribution
        {
            public readonly Dictionary<string, double?[]> Fixtures = new Dictionary<string, double?[]>(StringComparer.OrdinalIgnoreCase);
            public double? Spawn;
            public double? Wait;
        }

        private struct Sums
        {
            public double? Setup, Teardown, Spawn, Wait;
            public bool Refused;
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
        /// Update-QualificationChunkTiming: values holds the parameters this call passes (LogRoot, TimingRoot,
        /// Suite, Chunk, TrxPath, LastPhase, Why, QueueWaitSeconds, BuildSeconds, DiscoverySeconds, RunWallSeconds,
        /// StartedAt, CompletedAt, RunId, Attempt, Terminal); a missing key keeps what this process remembers for
        /// the chunk. nowUtc stamps lastPhaseUtc. An unknown key throws; nothing else does.
        /// </summary>
        public static void Update(IDictionary values, DateTime nowUtc)
        {
            var bound = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (values != null)
            {
                foreach (DictionaryEntry entry in values)
                {
                    var name = PsValue.Str(entry.Key);
                    if (Array.FindIndex(UpdateKeys, k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) < 0)
                        throw new ArgumentException("qualification-timing-unknown-key: " + name);
                    bound[name] = entry.Value;
                }
            }
            try { UpdateCore(bound, nowUtc); }
            catch { }
        }

        private static void UpdateCore(Dictionary<string, object> bound, DateTime nowUtc)
        {
            bool Has(string name) => bound.ContainsKey(name);
            string Text(string name) => bound.TryGetValue(name, out var v) ? PsValue.Str(v) : "";
            var logRoot = Text("LogRoot");
            var suite = Text("Suite");
            var chunk = Text("Chunk");
            if (Blank(logRoot) || Blank(suite) || Blank(chunk)) return;
            var runId = Text("RunId");
            var attempt = Text("Attempt");
            var key = logRoot + "|" + suite + "|" + chunk;
            ChunkMemory memory;
            lock (Gate) Memory.TryGetValue(key, out memory);
            var path = Path.Combine(logRoot, suite + "-" + chunk + ".timing.json");

            var timingRoot = "";
            if (Has("TimingRoot") && !Blank(Text("TimingRoot"))) timingRoot = Text("TimingRoot");
            else if (memory != null && !Blank(memory.TimingRoot)) timingRoot = memory.TimingRoot;
            else if (Has("TimingRoot")) timingRoot = Text("TimingRoot");
            var trx = Has("TrxPath") ? Text("TrxPath") : memory?.Trx ?? "";
            var why = Has("Why") ? Text("Why") : memory?.Why ?? "";
            var phase = Has("LastPhase") ? Text("LastPhase") : memory?.LastPhase ?? "";
            var started = Has("StartedAt") ? Stamp(bound["StartedAt"]) : memory?.StartedAt ?? "";
            var completed = Has("CompletedAt") ? Stamp(bound["CompletedAt"]) : memory?.CompletedAt ?? "";
            var queue = Has("QueueWaitSeconds") ? bound["QueueWaitSeconds"] : memory?.QueueWait;
            var build = Has("BuildSeconds") ? bound["BuildSeconds"] : memory?.Build;
            var discovery = Has("DiscoverySeconds") ? bound["DiscoverySeconds"] : memory?.Discovery;
            var runWall = Has("RunWallSeconds") ? bound["RunWallSeconds"] : memory?.RunWall;

            var trxMissing = true;
            object p50 = null, p95 = null, max = null;
            var slowest = new object[0];
            var count = 0;
            var sums = new Sums();
            if (Has("Terminal") && LanguagePrimitives.IsTrue(bound["Terminal"]))
            {
                // A refused read is a gap for that publication, not a permanent suite failure.
                if (!Has("Why") && Same(why, RefusedWhy)) why = "";
                Warm();
                var watch = BudgetWatch ?? Stopwatch.StartNew();
                var deadline = BudgetDeadlineMilliseconds;
                var refused = false;
                PSObject prior = null;
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
                    if (Blank(started) && prior.Properties["startedAt"] != null) started = Stamp(prior.Properties["startedAt"].Value);
                    if (!Has("QueueWaitSeconds") && queue == null && prior.Properties["queueWaitSeconds"] != null) queue = prior.Properties["queueWaitSeconds"].Value;
                    if (!Has("BuildSeconds") && build == null && prior.Properties["buildSeconds"] != null) build = prior.Properties["buildSeconds"].Value;
                    if (!Has("DiscoverySeconds") && discovery == null && prior.Properties["discoverySeconds"] != null) discovery = prior.Properties["discoverySeconds"].Value;
                    if (!Has("RunWallSeconds") && runWall == null && prior.Properties["runWallSeconds"] != null) runWall = prior.Properties["runWallSeconds"].Value;
                    if (!Has("TrxPath") && Blank(trx) && prior.Properties["trx"] != null) trx = PsValue.Str(prior.Properties["trx"].Value);
                    if (!Has("Why") && Blank(why) && prior.Properties["why"] != null && !Same(PsValue.Str(prior.Properties["why"].Value), RefusedWhy))
                        why = PsValue.Str(prior.Properties["why"].Value);
                    if (!Has("CompletedAt") && Blank(completed) && prior.Properties["completedAt"] != null) completed = Stamp(prior.Properties["completedAt"].Value);
                }
                TrxTimingStats stats = null;
                if (!Blank(trx))
                {
                    var trxKind = TestPhaseFiles.Kind(trx, out _);
                    if (trxKind == "missing") trxMissing = true;
                    else if (trxKind != "regular") refused = true;
                    else if (watch.ElapsedMilliseconds >= deadline) { trxMissing = false; refused = true; }
                    else
                    {
                        trxMissing = false;
                        stats = TrxCore(trx, watch, deadline);
                        if (stats == null) refused = true;
                    }
                }
                if (!Blank(timingRoot))
                {
                    if (watch.ElapsedMilliseconds >= deadline) refused = true;
                    else
                    {
                        var read = PhaseSumsCore(timingRoot, chunk, runId, suite, attempt, watch, deadline);
                        if (read.Refused) refused = true;
                        else sums = read;
                    }
                }
                if (watch.ElapsedMilliseconds >= deadline) refused = true;
                if (refused)
                {
                    sums = new Sums();
                    stats = null;
                    if (Blank(why)) why = RefusedWhy;
                }
                if (stats != null)
                {
                    if (stats.Count > 0) { p50 = stats.P50; p95 = stats.P95; max = stats.Max; }
                    slowest = SlowRows(stats);
                    count = stats.Count;
                }
            }
            var record = new OrderedDictionary
            {
                { "suite", suite }, { "chunk", chunk }, { "timingRoot", timingRoot }, { "trx", trx }, { "trxMissing", trxMissing },
                { "why", why }, { "lastPhase", phase }, { "lastPhaseUtc", nowUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "startedAt", started }, { "completedAt", completed }, { "queueWaitSeconds", queue }, { "buildSeconds", build },
                { "buildSlotWaitSeconds", null }, { "buildSlot", "not-used" }, { "discoverySeconds", discovery },
                { "runWallSeconds", runWall }, { "distillationSeconds", null }, { "fixtureSetupSeconds", sums.Setup },
                { "fixtureTeardownSeconds", sums.Teardown }, { "processSpawnSeconds", sums.Spawn },
                { "processWaitSeconds", sums.Wait }, { "testCount", count }, { "p50Seconds", p50 }, { "p95Seconds", p95 },
                { "maxSeconds", max }, { "slowest", slowest },
            };
            lock (Gate)
            {
                Memory[key] = new ChunkMemory
                {
                    TimingRoot = timingRoot, Trx = trx, Why = why, LastPhase = phase, StartedAt = started, CompletedAt = completed,
                    QueueWait = queue, Build = build, Discovery = discovery, RunWall = runWall,
                };
            }
            var text = TestPhaseJson.Text(record, 0);
            EvidenceFiles.WriteAtomic(path, text, true);
            if (!Blank(timingRoot))
            {
                var copy = Path.Combine(timingRoot, suite + "-" + chunk + ".timing.json");
                if (!string.Equals(Path.GetFullPath(copy), Path.GetFullPath(path), PathComparison)) EvidenceFiles.WriteAtomic(copy, text, true);
            }
        }

        private static object[] SlowRows(TrxTimingStats stats)
        {
            var names = stats.SlowNames ?? new string[0];
            var values = stats.SlowSeconds ?? new double[0];
            var rows = new object[names.Length];
            for (var i = 0; i < names.Length; i++)
                rows[i] = new OrderedDictionary { { "name", names[i] ?? "" }, { "seconds", i < values.Length ? values[i] : 0.0 } };
            return rows;
        }

        /// <summary>
        /// Get-QualificationTimingReport: every *.timing.json sidecar of logRoot (in name order) and one timeline row per
        /// suite (earliest start, latest end, the phase with the latest lastPhaseUtc), as { TimingRoot, Chunks, Timeline }.
        /// </summary>
        public static PSObject Report(string logRoot, string timingRoot)
        {
            EvidenceFiles.FlushKeeping(null);
            var chunks = new List<object>();
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
            var bySuite = new Dictionary<string, OrderedDictionary>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in chunks)
            {
                var suite = PsValue.Str(PsValue.Prop(row, "suite"));
                if (Blank(suite)) continue;
                if (!bySuite.TryGetValue(suite, out var bag))
                {
                    bag = new OrderedDictionary { { "suite", suite }, { "startedAt", "" }, { "completedAt", "" }, { "lastPhase", "" }, { "lastPhaseUtc", "" } };
                    bySuite.Add(suite, bag);
                }
                var start = PsValue.Str(PsValue.Prop(row, "startedAt"));
                var bagStart = (string)bag["startedAt"];
                if (!Blank(start) && (Blank(bagStart) || string.CompareOrdinal(start, bagStart) < 0)) bag["startedAt"] = start;
                var end = PsValue.Str(PsValue.Prop(row, "completedAt"));
                if (!Blank(end) && string.CompareOrdinal(end, (string)bag["completedAt"]) > 0) bag["completedAt"] = end;
                var utc = PsValue.Str(PsValue.Prop(row, "lastPhaseUtc"));
                if (!Blank(utc) && string.CompareOrdinal(utc, (string)bag["lastPhaseUtc"]) > 0)
                {
                    bag["lastPhaseUtc"] = utc;
                    bag["lastPhase"] = PsValue.Str(PsValue.Prop(row, "lastPhase"));
                }
            }
            var suites = new List<string>(bySuite.Keys);
            suites.Sort(StringComparer.OrdinalIgnoreCase);
            var timeline = new object[suites.Count];
            for (var i = 0; i < suites.Count; i++) timeline[i] = bySuite[suites[i]];
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("TimingRoot", timingRoot ?? ""));
            result.Properties.Add(new PSNoteProperty("Chunks", chunks.ToArray()));
            result.Properties.Add(new PSNoteProperty("Timeline", timeline));
            return result;
        }

        // ---- setup-phases.json -----------------------------------------------------------------------------

        /// <summary>
        /// Write-QualificationRunPhase: records one named setup phase (npm-ci, dotnet-build, ...) in
        /// timingDirectory/setup-phases.json, replacing an earlier row of that name. A name outside
        /// [a-z0-9][a-z0-9-]{0,40} is ignored; an unreadable file throws.
        /// </summary>
        public static void WriteRunPhase(string timingDirectory, string name, string startedAt, string endedAt, double seconds)
        {
            if (Blank(timingDirectory) || Blank(name) || !PhaseName(name)) return;
            var path = Path.Combine(timingDirectory, "setup-phases.json");
            var kept = new List<object>();
            foreach (var row in SetupRows(path, false))
            {
                if (row == null || Same(PsValue.Str(PsValue.Prop(row, "name")), name)) continue;
                var rowSeconds = PsValue.Prop(row, "seconds");
                kept.Add(new OrderedDictionary
                {
                    { "name", PsValue.Str(PsValue.Prop(row, "name")) }, { "startedAt", Stamp(PsValue.Prop(row, "startedAt")) },
                    { "endedAt", Stamp(PsValue.Prop(row, "endedAt")) },
                    { "seconds", rowSeconds == null ? 0.0 : (double)LanguagePrimitives.ConvertTo(rowSeconds, typeof(double), CultureInfo.InvariantCulture) },
                });
            }
            kept.Add(new OrderedDictionary { { "name", name }, { "startedAt", startedAt ?? "" }, { "endedAt", endedAt ?? "" }, { "seconds", seconds } });
            var doc = new OrderedDictionary { { "version", 1 }, { "phases", kept.ToArray() } };
            EvidenceFiles.WriteAtomic(path, TestPhaseJson.Text(doc, 0), true);
        }

        /// <summary>
        /// Get-QualificationSetupAccounting: { countedOnce, setupSeconds, perChunkBuildSum (build, npm-build,
        /// dotnet-build), overlapped, firstStartUtcTicks } of timingDirectory/setup-phases.json.
        /// </summary>
        public static PSObject SetupAccounting(string timingDirectory)
        {
            var path = Path.Combine(timingDirectory ?? "", "setup-phases.json");
            var countedOnce = TestPhaseFiles.Kind(path, out _) == "regular";
            double setup = 0, sum = 0;
            object firstStart = null;
            var recordedFirst = false;
            var intervals = new List<long[]>();
            foreach (var row in SetupRows(path, true))
            {
                if (row == null) continue;
                var rowSeconds = PsValue.Prop(row, "seconds");
                var value = rowSeconds == null ? 0.0 : (double)LanguagePrimitives.ConvertTo(rowSeconds, typeof(double), CultureInfo.InvariantCulture);
                setup += value;
                var phaseName = PsValue.Str(PsValue.Prop(row, "name"));
                if (Same(phaseName, "build") || Same(phaseName, "npm-build") || Same(phaseName, "dotnet-build")) sum += value;
                var start = UtcTicks(PsValue.Prop(row, "startedAt"));
                var end = UtcTicks(PsValue.Prop(row, "endedAt"));
                if (!recordedFirst) { recordedFirst = true; firstStart = start; }
                if (start != null && end != null) intervals.Add(new[] { start.Value, end.Value });
            }
            intervals.Sort((a, b) => a[0].CompareTo(b[0]));
            var overlapped = false;
            for (var i = 1; i < intervals.Count; i++) if (intervals[i][0] < intervals[i - 1][1]) overlapped = true;
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("countedOnce", countedOnce));
            result.Properties.Add(new PSNoteProperty("setupSeconds", setup));
            result.Properties.Add(new PSNoteProperty("perChunkBuildSum", sum));
            result.Properties.Add(new PSNoteProperty("overlapped", overlapped));
            result.Properties.Add(new PSNoteProperty("firstStartUtcTicks", firstStart));
            return result;
        }

        // The phases of setup-phases.json (none when it is not a regular file); dates as ConvertFrom-Json reads them.
        private static object[] SetupRows(string path, bool dates)
        {
            if (TestPhaseFiles.Kind(path, out _) != "regular") return new object[0];
            var doc = NightlyJson.ParseText(EvidenceFiles.ReadAllText(path), 60, dates);
            if (doc == null) throw new InvalidDataException("setup-phases-unreadable: " + path);
            var phases = doc.Properties["phases"]?.Value;
            return phases == null ? new object[0] : PsValue.AsArray(phases);
        }

        private static long? UtcTicks(object value)
        {
            if (value is PSObject ps) value = ps.BaseObject;
            if (value == null) return null;
            try
            {
                if (value is DateTimeOffset offset) return offset.UtcTicks;
                if (value is DateTime stamp)
                {
                    if (stamp.Kind == DateTimeKind.Local) return stamp.ToUniversalTime().Ticks;
                    if (stamp.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(stamp, DateTimeKind.Utc).Ticks;
                    return stamp.Ticks;
                }
                return DateTimeOffset.Parse(PsValue.Str(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcTicks;
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
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

        /// <summary>ConvertFrom-QualificationOwnerName: { Token, Journal } of an owner file name, or null.</summary>
        public static PSObject OwnerName(string name)
        {
            if (!OwnerName(name ?? "", out var token, out var journal)) return null;
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Token", token));
            result.Properties.Add(new PSNoteProperty("Journal", journal));
            return result;
        }

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
        /// Get-QualificationPhaseSums: fixture setup/teardown and process spawn/wait seconds (null where none was
        /// observed) of the chunk's own journals, with refused when a read was refused or the budget ran out.
        /// Without a watch each journal gets its own 200 ms slice of deadlineMilliseconds.
        /// </summary>
        public static PSObject PhaseSums(string timingRoot, string chunk, string runId, string suite, string attempt) =>
            PhaseSums(timingRoot, chunk, runId, suite, attempt, null, 500);

        public static PSObject PhaseSums(string timingRoot, string chunk, string runId, string suite, string attempt, Stopwatch watch, int deadlineMilliseconds)
        {
            Warm();
            var sums = PhaseSumsCore(timingRoot, chunk ?? "", runId ?? "", suite ?? "", attempt ?? "", watch, deadlineMilliseconds);
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("fixtureSetupSeconds", sums.Setup));
            result.Properties.Add(new PSNoteProperty("fixtureTeardownSeconds", sums.Teardown));
            result.Properties.Add(new PSNoteProperty("processSpawnSeconds", sums.Spawn));
            result.Properties.Add(new PSNoteProperty("processWaitSeconds", sums.Wait));
            result.Properties.Add(new PSNoteProperty("refused", sums.Refused));
            return result;
        }

        private static Sums PhaseSumsCore(string timingRoot, string chunk, string runId, string suite, string attempt, Stopwatch watch, int deadline)
        {
            var result = new Sums();
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
            if (setup != null) result.Setup = Math.Round(setup.Value, 3);
            if (teardown != null) result.Teardown = Math.Round(teardown.Value, 3);
            if (spawn != null) result.Spawn = Math.Round(spawn.Value, 3);
            if (wait != null) result.Wait = Math.Round(wait.Value, 3);
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
                if (record == null || !Same(Str(record.Value, "recordKind"), "header")) return null;
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
            if (!Same(Str(h, "recordKind"), "header") || !Same(Str(h, "groupId"), chunk) || !Same(Str(h, "runId"), runId)
                || !Same(Str(h, "suite"), suite) || !Same(Str(h, "attempt"), attempt)) return false;
            if (!JournalName(fileName, out var filePid, out var fileNonce)) return false;
            var headerPid = Str(h, "pid");
            var headerNonce = Str(h, "nonce");
            if (!Blank(headerPid) && !Same(headerPid, filePid)) return false;
            if (!Blank(headerNonce) && !Same(headerNonce, fileNonce)) return false;
            return true;
        }

        private static Contribution Contribute(List<JsonElement> records, string fileName, string chunk, string runId, string suite, string attempt)
        {
            JsonElement? header = null;
            foreach (var record in records) if (Same(Str(record, "recordKind"), "header")) { header = record; break; }
            var result = new Contribution();
            if (!ShardOwned(header, fileName, chunk, runId, suite, attempt)) return result;
            foreach (var record in records)
            {
                var kind = Str(record, "recordKind");
                if (Same(kind, "fixture"))
                {
                    var id = Str(record, "fixtureId");
                    if (Blank(id)) id = Guid.NewGuid().ToString("N");
                    if (result.Fixtures.ContainsKey(id)) continue;
                    var bucket = new double?[2];
                    foreach (var phase in Phases(record))
                    {
                        var seconds = Span(phase);
                        if (seconds == null) continue;
                        var name = Str(phase, "phase");
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
                        var name = Str(phase, "phase");
                        if (Same(name, "process-spawn")) result.Spawn = (result.Spawn ?? 0) + seconds.Value;
                        else if (Same(name, "process-wait")) result.Wait = (result.Wait ?? 0) + seconds.Value;
                    }
                }
            }
            return result;
        }

        private static IEnumerable<JsonElement> Phases(JsonElement record)
        {
            if (!Field(record, "phases", out var phases)) yield break;
            if (phases.ValueKind == JsonValueKind.Array) { foreach (var phase in phases.EnumerateArray()) yield return phase; }
            else yield return phases;
        }

        // Seconds between startOffsetMs and endOffsetMs (an absent offset is 0, an end before the start is the
        // start); null for a phase that is not an object or whose start is not a number.
        private static double? Span(JsonElement phase)
        {
            if (phase.ValueKind != JsonValueKind.Object) return null;
            var start = Number(phase, "startOffsetMs");
            if (start == null) return null;
            var end = Number(phase, "endOffsetMs") ?? start.Value;
            if (end < start.Value) end = start.Value;
            return (end - start.Value) / 1000.0;
        }

        // [double]$o.name: absent or null is 0.
        private static double? Number(JsonElement o, string name)
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

        // $o.name, matched as PowerShell matches property names (ignoring case).
        private static bool Field(JsonElement o, string name, out JsonElement value)
        {
            value = default;
            if (o.ValueKind != JsonValueKind.Object) return false;
            if (o.TryGetProperty(name, out value)) return true;
            foreach (var property in o.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
            return false;
        }

        // [string]$o.name.
        private static string Str(JsonElement o, string name)
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

        // ---- TRX -------------------------------------------------------------------------------------------

        /// <summary>
        /// Get-QualificationTrxStats: { P50, P95, Max, Slowest, Count, Complete } of the TRX's results (null
        /// percentiles without results; Complete false, nothing else, when the read was refused or out of budget);
        /// null for a blank path.
        /// </summary>
        public static PSObject TrxStats(string trxPath) => TrxStats(trxPath, null, DefaultBudget);

        public static PSObject TrxStats(string trxPath, Stopwatch watch, int deadlineMilliseconds)
        {
            Warm();
            if (Blank(trxPath)) return null;
            var stats = TrxCore(trxPath, watch ?? Stopwatch.StartNew(), deadlineMilliseconds);
            var result = new PSObject();
            var known = stats != null && stats.Count > 0;
            result.Properties.Add(new PSNoteProperty("P50", known ? (object)stats.P50 : null));
            result.Properties.Add(new PSNoteProperty("P95", known ? (object)stats.P95 : null));
            result.Properties.Add(new PSNoteProperty("Max", known ? (object)stats.Max : null));
            result.Properties.Add(new PSNoteProperty("Slowest", stats != null ? SlowRows(stats) : new object[0]));
            result.Properties.Add(new PSNoteProperty("Count", stats != null ? stats.Count : 0));
            result.Properties.Add(new PSNoteProperty("Complete", stats != null));
            return result;
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
        /// Read-QualificationSidecar: the sidecar as ConvertFrom-Json gives it, or null when it is not a regular
        /// file of at most 256 KiB, is denser than 4000 commas or colons, is not one strict JSON object, or the
        /// read ran out of budget.
        /// </summary>
        public static object ReadSidecar(string path) => ReadSidecar(path, null, DefaultBudget);

        public static object ReadSidecar(string path, Stopwatch watch, int deadlineMilliseconds) => ReadSidecarCore(path, watch, deadlineMilliseconds);

        private static PSObject ReadSidecarCore(string path, Stopwatch watch, int deadline)
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
            var text = Encoding.UTF8.GetString(bytes);
            if (TestPhaseRecords.Dense(text, 4000, watch, deadline)) return null;
            if (watch.ElapsedMilliseconds >= deadline) return null;
            return NightlyJson.ParseText(text, 60);
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
                NightlyJson.ParseText("{\"a\":1}", 60);
                TestPhaseJson.Text(new OrderedDictionary { { "why", "" }, { "testCount", 0 }, { "slowest", new object[0] } }, 0);
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

        // ConvertTo-QualificationStampText: a DateTime as round-trip UTC, anything else as text.
        private static string Stamp(object value)
        {
            if (value is PSObject ps) value = ps.BaseObject;
            if (value == null) return "";
            if (value is DateTime stamp) return stamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            return PsValue.Str(value);
        }
    }
}
