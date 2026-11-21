// The run summary of test-phase-evidence.ps1, compiled (W2-nightly S09): the summary rows (ConvertTo-TestPhaseSummaryRows:
// the phase shards joined with the TRX results through the executed journal or flat manifest), the summary files
// (Write-TestPhaseSummaryFiles: timing/summary.json, tests.csv, summary.md), the run summary (Invoke-TestPhaseRunSummary)
// and the summarize-test-phases command (formerly scripts/summarize-test-phases.ps1; antiphon-scripts
// summarize-test-phases runs Summarize).
//
// Plain C#: see TestPhaseManifests.cs. The rows and the bytes written are those the PowerShell functions produced, with
// one difference: the PowerShell @{} maps of complete and start records enumerated in hash order (which varied by
// process), so the order of tied rows and of the quality tokens found in that loop varied; here they are in first-seen
// order. ASCII-only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>ConvertTo-TestPhaseSummaryRows' result.</summary>
    public sealed class TestPhaseSummaryReport
    {
        public string[] Quality { get; set; } = new string[0];
        public int Torn { get; set; }
        public int Malformed { get; set; }
        /// <summary>One ordered dictionary per test (testId, suite, className, method, duration, groupId, outcomes, counts).</summary>
        public object[] Tests { get; set; } = new object[0];
        /// <summary>One ordered dictionary per shared fixture record.</summary>
        public object[] Shared { get; set; } = new object[0];
        public bool Complete { get; set; }
        public int SummaryAggregationBudgetMilliseconds { get; set; } = TestPhaseFiles.SummaryBudgetMilliseconds;
        public int SummaryAggregationMilliseconds { get; set; }
    }

    /// <summary>summarize-test-phases' flags.</summary>
    public sealed class TestPhaseSummarizeOptions
    {
        public string RunDirectory { get; set; } = "";
        public int Top { get; set; } = 50;
        public bool RecoverExecutedManifest { get; set; }
        public string TimingRoot { get; set; } = "";
    }

    public static class TestPhaseRunSummary
    {
        /// <summary>The writer environment's host of the command (a recovery's metadata): this command, not PowerShell.</summary>
        public const string CommandEdition = "antiphon-scripts";

        private static readonly string[] CsvHeaders =
        {
            "testId", "duration", "groupId", "outcome", "trxOutcome", "phaseOutcome", "joined", "incomplete", "db", "host", "process", "fs",
            "network", "fixture", "overlapMs", "remainderMs", "droppedPhaseCount",
        };

        private static readonly string[] Exclusive = { "db", "host", "process", "fs", "network", "fixture" };

        private static readonly string[] TotalFields = { "db", "host", "process", "fs", "network", "fixture", "overlapMs", "remainderMs" };

        private static bool Has(object o, string name) => TestPhaseManifests.Has(o, name);

        private static string Text(object o, string name) => TestPhaseManifests.Text(o, name);

        private static bool Same(string left, string right) => PsValue.Eq(left ?? "", right ?? "", true);

        private static void AddQuality(List<string> quality, string token)
        {
            if (!quality.Contains(token)) quality.Add(token);
        }

        // [long]$value (null is 0).
        private static long Long(object value)
        {
            if (value == null) return 0;
            value = PsValue.BaseObject(value);
            if (value is long l) return l;
            if (value is int i) return i;
            return PsRuntime.Loaded ? PsFields.ToLong(value) : PlainValues.Long(value);
        }

        // ---- the summary rows ----

        private sealed class Bag
        {
            public object Record;
            public object Header;
            public bool Conflict;
        }

        // An insertion-ordered map with @{}'s case-insensitive keys; a repeated key keeps its first position.
        private sealed class OrderedMap<T>
        {
            private readonly Dictionary<string, T> _values = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _keys = new List<string>();

            public bool ContainsKey(string key) => _values.ContainsKey(key);

            public T this[string key]
            {
                get => _values[key];
                set
                {
                    if (!_values.ContainsKey(key)) _keys.Add(key);
                    _values[key] = value;
                }
            }

            public IEnumerable<string> Keys => _keys;
        }

        /// <summary>
        /// ConvertTo-TestPhaseSummaryRows: every phase shard (timing/cases-*.jsonl and those of each chunk-* directory; a
        /// linked chunk directory is refused) not stale against its suite's planned document, joined with the complete
        /// TRX results of the run directory (labelled with their chunk and suite by the executed journal, else the flat
        /// manifest), with the quality tokens of what could not be joined. Shards past the inline bound are re-read on
        /// one shared budget (the scope's SummaryReReadWatch, else a new stopwatch).
        /// </summary>
        public static TestPhaseSummaryReport Rows(object planned, string runDirectory, TestPhasePublicationScope scope)
        {
            scope = TestPhaseManifests.Of(scope);
            var quality = new List<string>();
            var torn = 0;
            var malformed = 0;
            var budget = TestPhaseReadBudget.For(scope.SummaryReReadWatch ?? new Stopwatch(), TestPhaseFiles.SummaryBudgetMilliseconds, 8388608);
            var plannedDocs = TestPhaseManifests.ReadPlannedDocuments(runDirectory, scope.QualificationTimingRoot);
            var timing = TestPhaseManifests.TimingDirectory(runDirectory, scope.QualificationTimingRoot);
            // A listing reads the disk: queued evidence writes land first (a failure stays stored for the run verdict).
            EvidenceFiles.FlushKeeping(null);
            var shards = new List<FileInfo>();
            if (TestPhaseManifests.Exists(timing))
            {
                shards.AddRange(Files(timing, "cases-*.jsonl"));
                foreach (var chunkDir in Entries(timing, "chunk-*"))
                {
                    var kind = QualificationTiming.DirectoryKind(chunkDir);
                    if (kind == "symlink")
                    {
                        AddQuality(quality, "chunk-link-refused");
                        continue;
                    }
                    if (kind != "directory") continue;
                    shards.AddRange(Files(chunkDir, "cases-*.jsonl"));
                }
            }
            shards = SortByName(shards);
            var completes = new OrderedMap<Bag>();
            var starts = new OrderedMap<object>();
            var fixtures = new List<object>();
            foreach (var shard in shards)
            {
                var parsed = TestPhaseReads.JsonlForSummary(shard.FullName, budget);
                torn += parsed.Torn;
                malformed += parsed.Malformed;
                object header = null;
                foreach (var rec in parsed.Records)
                {
                    if (Same(Text(rec, "recordKind"), "header")) { header = rec; break; }
                }
                var planForShard = planned;
                var headerSuite = header != null && Has(header, "suite") ? Text(header, "suite") : "";
                if (!string.IsNullOrWhiteSpace(headerSuite))
                {
                    foreach (var plannedDoc in plannedDocs)
                    {
                        if (plannedDoc == null) continue;
                        var docSuite = Has(plannedDoc, "suite") ? Text(plannedDoc, "suite") : "";
                        if (Same(docSuite, headerSuite)) { planForShard = plannedDoc; break; }
                    }
                }
                var stale = false;
                if (header != null && planForShard != null)
                {
                    if (!string.IsNullOrWhiteSpace(Text(planForShard, "runId")) && !Same(Text(header, "runId"), Text(planForShard, "runId"))) stale = true;
                    if (!string.IsNullOrWhiteSpace(Text(planForShard, "assembly")) && !Same(Text(header, "assembly"), Text(planForShard, "assembly"))) stale = true;
                }
                if (stale)
                {
                    AddQuality(quality, "stale-input-not-joined");
                    continue;
                }
                foreach (var rec in parsed.Records)
                {
                    var kind = Text(rec, "recordKind");
                    if (Same(kind, "fixture")) { fixtures.Add(rec); continue; }
                    var id = Text(rec, "testId");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var evidenceKey = TestPhaseText.EvidenceKey(header, id);
                    if (Same(kind, "start")) { starts[evidenceKey] = rec; continue; }
                    if (!Same(kind, "complete")) continue;
                    if (completes.ContainsKey(evidenceKey))
                    {
                        var prior = completes[evidenceKey];
                        if (!Same(Text(prior.Record, "outcome"), Text(rec, "outcome")))
                        {
                            prior.Conflict = true;
                            AddQuality(quality, "conflicting-duplicate");
                        }
                    }
                    else
                    {
                        completes[evidenceKey] = new Bag { Record = rec, Header = header, Conflict = false };
                    }
                }
            }
            if (torn > 0) AddQuality(quality, "torn-tail");
            if (malformed > 0) AddQuality(quality, "malformed-line");

            // The chunk and suite of each TRX: from the live journal's reduced rows, else the flat manifest.
            var trxGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var trxSuites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TestPhaseGenerationPrefix journalPrefix = null;
            var journalRoot = TestPhaseJournal.JournalRoot(timing);
            var currentPath = TestPhaseManifests.Join(journalRoot, "current.json");
            if (TestPhaseManifests.Regular(currentPath))
            {
                var current = TestPhaseJournal.ReadJsonFile(currentPath);
                var generation = current == null ? "" : Text(current, "generation");
                if (current != null && !string.IsNullOrWhiteSpace(generation))
                {
                    journalPrefix = TestPhaseJournal.ReadGenerationPrefix(TestPhaseManifests.Join(journalRoot, generation));
                    // The validated seed and retained records, predecessor rows included. This read-only context never
                    // acquires writer ownership or projects files.
                    var snapshot = TestPhaseJournal.RefusedContext("");
                    snapshot.Refused = false;
                    snapshot.TimingRoot = timing;
                    snapshot.Generation = generation;
                    snapshot.Records = journalPrefix.Records;
                    TestPhaseJournal.ReadSeedCache(snapshot, generation);
                    var reduced = TestPhaseJournalRead.Reduce(snapshot);
                    for (var entryIndex = 0; entryIndex < reduced.Entries.Count; entryIndex++)
                    {
                        var entry = reduced.Entries[entryIndex];
                        var rowJson = entryIndex == reduced.Entries.Count - 1 ? entry.Fresh : entry.Json;
                        if (string.IsNullOrWhiteSpace(rowJson)) continue;
                        // Only trx, chunk and suite are read, from a strict parse of the document.
                        var fields = TestPhaseJournalRead.ChunkFields(rowJson);
                        if (fields == null) continue;
                        var trxRel = fields[0] ?? "";
                        if (string.IsNullOrWhiteSpace(trxRel)) continue;
                        var fullTrx = Path.GetFullPath(TestPhaseManifests.Join(runDirectory, trxRel));
                        trxGroups[fullTrx] = fields[1] ?? "";
                        trxSuites[fullTrx] = fields[2] ?? "";
                    }
                }
            }
            var executedPath = TestPhaseManifests.Join(timing, "groups-executed.json");
            if (journalPrefix == null && TestPhaseManifests.Regular(executedPath))
            {
                var executed = TestPhaseManifests.ReadDocument(executedPath);
                foreach (var chunk in PsValue.AsArray(PsValue.Prop(executed, "chunks")))
                {
                    var trx = Text(chunk, "trx");
                    if (string.IsNullOrWhiteSpace(trx)) continue;
                    var fullTrx = Path.GetFullPath(TestPhaseManifests.Join(runDirectory, trx));
                    trxGroups[fullTrx] = Text(chunk, "chunk");
                    trxSuites[fullTrx] = Has(chunk, "suite") ? Text(chunk, "suite") : "";
                }
            }
            var trxRows = new List<object>();
            foreach (var trx in Files(runDirectory, "*.trx"))
            {
                if (!TestPhaseManifests.Regular(trx.FullName)) continue;
                var read = TestPhaseManifests.ReadTrxRows(trx.FullName, scope);
                if (read == null || !read.Complete || read.Refused) continue;
                trxGroups.TryGetValue(trx.FullName, out var group);
                trxSuites.TryGetValue(trx.FullName, out var suiteText);
                TestPhaseSummary.TagRows(read.Rows, group ?? "", suiteText ?? "");
                trxRows.AddRange(read.Rows);
            }
            var nativeMap = TestPhaseSummary.MapNative(trxRows.ToArray());

            var tests = new List<object>();
            var joinedTrx = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var completeTestIds = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var completeKey in completes.Keys) completeTestIds[Text(completes[completeKey].Record, "testId")] = true;
            foreach (var completeKey in completes.Keys)
            {
                var bag = completes[completeKey];
                var id = Text(bag.Record, "testId");
                var headerSuite = bag.Header != null && Has(bag.Header, "suite") ? Text(bag.Header, "suite") : "";
                var matches = nativeMap[id] is List<object> found ? found : new List<object>();
                var distinct = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var match in matches)
                {
                    var trxSuite = Has(match, "Suite") ? Text(match, "Suite") : "";
                    if (!string.IsNullOrWhiteSpace(headerSuite) && !string.IsNullOrWhiteSpace(trxSuite) && !Same(headerSuite, trxSuite)) continue;
                    distinct[Text(match, "TestId") + "|" + Text(match, "Path")] = match;
                }
                var matchRows = distinct.Values.ToArray();
                var joined = matchRows.Length == 1 && !bag.Conflict;
                var trxOutcome = "";
                string phaseOutcome;
                object duration = null;
                if (bag.Conflict)
                {
                    phaseOutcome = "conflict";
                    AddQuality(quality, "conflicting-duplicate");
                }
                else phaseOutcome = Text(bag.Record, "outcome");
                if (matchRows.Length > 1)
                {
                    joined = false;
                    AddQuality(quality, "ambiguous-id");
                }
                else if (matchRows.Length == 1)
                {
                    trxOutcome = Text(matchRows[0], "Outcome");
                    duration = Text(matchRows[0], "Duration");
                    joinedTrx[Text(matchRows[0], "TestId") + "|" + Text(matchRows[0], "Path")] = true;
                }
                var outcome = "incomplete";
                var incomplete = true;
                if (joined)
                {
                    if (Same(trxOutcome, phaseOutcome) && !string.IsNullOrWhiteSpace(trxOutcome))
                    {
                        outcome = trxOutcome;
                        incomplete = false;
                    }
                    else AddQuality(quality, "outcome-disagreement");
                }
                else AddQuality(quality, "missing-terminal-remains-incomplete");
                var src = bag.Record;
                var ex = PsValue.Prop(src, "exclusive");
                var row = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "testId", id },
                    { "suite", headerSuite },
                    { "className", Text(src, "className") },
                    { "method", Text(src, "method") },
                    { "duration", duration },
                    { "groupId", Text(bag.Header, "groupId") },
                    { "outcome", outcome },
                    { "trxOutcome", trxOutcome },
                    { "phaseOutcome", phaseOutcome },
                    { "joined", joined },
                    { "incomplete", incomplete },
                    { "countsKnown", true },
                };
                var exTrue = PsValue.IsTrue(ex);
                foreach (var name in Exclusive) row.Add(name, exTrue ? Long(PsValue.Prop(ex, name)) : 0);
                var overlap = PsValue.Prop(src, "overlapMs");
                var remainder = PsValue.Prop(src, "remainderMs");
                var dropped = PsValue.Prop(src, "droppedPhaseCount");
                row.Add("overlapMs", PsValue.IsTrue(overlap) ? Long(overlap) : 0);
                row.Add("remainderMs", PsValue.IsTrue(remainder) ? Long(remainder) : 0);
                row.Add("droppedPhaseCount", PsValue.IsTrue(dropped) ? PsOps.ToInt(dropped) : 0);
                tests.Add(row);
            }
            foreach (var startKey in starts.Keys)
            {
                if (completes.ContainsKey(startKey)) continue;
                var startId = Text(starts[startKey], "testId");
                AddQuality(quality, "missing-terminal-remains-incomplete");
                var row = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "testId", startId }, { "suite", "" }, { "className", "" }, { "method", "" }, { "duration", null }, { "groupId", null },
                    { "outcome", "incomplete" }, { "trxOutcome", "" }, { "phaseOutcome", "" }, { "joined", false }, { "incomplete", true },
                    { "countsKnown", false },
                };
                foreach (var name in Exclusive) row.Add(name, 0);
                row.Add("overlapMs", 0);
                row.Add("remainderMs", 0);
                row.Add("droppedPhaseCount", 0);
                tests.Add(row);
            }
            var unjoined = TestPhaseSummary.UnjoinedTrxRows(trxRows.ToArray(), joinedTrx, completeTestIds);
            tests.AddRange(PsValue.AsArray(PsValue.Prop(unjoined, "Rows")));
            if (PsValue.IsTrue(PsValue.Prop(unjoined, "PassedWithoutPhase"))) AddQuality(quality, "missing-phase-unknown");
            var shared = new List<object>();
            foreach (var fix in fixtures)
            {
                var ex = PsValue.Prop(fix, "exclusive");
                var exTrue = PsValue.IsTrue(ex);
                var row = new OrderedDictionary(StringComparer.OrdinalIgnoreCase) { { "fixtureId", Text(fix, "fixtureId") } };
                foreach (var name in Exclusive) row.Add(name, exTrue ? Long(PsValue.Prop(ex, name)) : 0);
                shared.Add(row);
            }
            foreach (var token in TestPhaseJournal.PublicationQuality(runDirectory, scope.QualificationTimingRoot, journalPrefix)) AddQuality(quality, PsValue.Str(token));
            var complete = quality.Count == 0 && !TestPhaseSummaryFast.AnyIncomplete(tests);
            var spent = budget.Watch == null ? 0 : (int)budget.Watch.ElapsedMilliseconds;
            return new TestPhaseSummaryReport
            {
                Quality = quality.ToArray(),
                Torn = torn,
                Malformed = malformed,
                Tests = tests.ToArray(),
                Shared = shared.ToArray(),
                Complete = complete,
                SummaryAggregationBudgetMilliseconds = budget.Milliseconds,
                SummaryAggregationMilliseconds = spent,
            };
        }

        // Get-ChildItem -LiteralPath dir -Filter pattern -File: the files in the directory's own order, hidden ones left out;
        // nothing for a missing or unreadable directory.
        private static List<FileInfo> Files(string directory, string pattern)
        {
            var files = new List<FileInfo>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return files;
            try
            {
                var options = new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden, IgnoreInaccessible = true, MatchType = MatchType.Win32, RecurseSubdirectories = false };
                foreach (var path in Directory.EnumerateFiles(directory, pattern, options)) files.Add(new FileInfo(path));
            }
            catch (Exception) { }
            return files;
        }

        // Get-ChildItem -LiteralPath dir -Force -Filter pattern: the directories (links included), then the files, each in
        // the directory's order; nothing for a directory that cannot be listed.
        private static List<string> Entries(string directory, string pattern)
        {
            var entries = new List<string>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return entries;
            try
            {
                var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, MatchType = MatchType.Win32, RecurseSubdirectories = false };
                entries.AddRange(Directory.EnumerateDirectories(directory, pattern, options));
                entries.AddRange(Directory.EnumerateFiles(directory, pattern, options));
            }
            catch (Exception) { }
            return entries;
        }

        // @($files | Sort-Object { [string]$_.Name }): Sort-Object's unstable List<T>.Sort on the current culture,
        // ignoring case; equal names keep the permutation that sort gives for this input order.
        private static List<FileInfo> SortByName(List<FileInfo> files)
        {
            var sorted = new List<FileInfo>(files);
            var culture = CultureInfo.CurrentCulture;
            sorted.Sort((x, y) => culture.CompareInfo.Compare(x.Name, y.Name, CompareOptions.IgnoreCase));
            return sorted;
        }

        // ---- the summary files ----

        /// <summary>
        /// Write-TestPhaseSummaryFiles: timing/summary.json (the report with its tests sorted by testId), tests.csv (one
        /// row per test) and summary.md (the top tests by exclusive+overlap+remainder, at least one).
        /// </summary>
        public static void WriteFiles(TestPhaseSummaryReport report, string runDirectory, int top, TestPhasePublicationScope scope)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var timing = TestPhaseManifests.TimingDirectory(runDirectory, TestPhaseManifests.Of(scope).QualificationTimingRoot);
            if (!TestPhaseManifests.Exists(timing)) Directory.CreateDirectory(timing);
            var orderedTests = TestPhaseSummaryWrite.SortByTestId(report.Tests);
            var summary = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "version", 1 },
                { "complete", report.Complete },
                { "quality", report.Quality ?? new string[0] },
                { "tornLines", report.Torn },
                { "malformedLines", report.Malformed },
                { "summaryAggregationBudgetMilliseconds", report.SummaryAggregationBudgetMilliseconds },
                { "summaryAggregationMilliseconds", report.SummaryAggregationMilliseconds },
                { "tests", orderedTests },
                { "sharedFixtures", report.Shared ?? new object[0] },
            };
            // Diagnostic summary only: the compiled writer, not ConvertTo-Json -Depth 50 -Compress (same bytes).
            TestPhaseFiles.WriteTextAtomic(TestPhaseManifests.Join(timing, "summary.json"), TestPhaseSummaryWrite.SummaryJson(summary));

            var lines = new List<string>();
            lines.Add(string.Join(",", CsvHeaders.Select(h => TestPhaseSummary.Cell(h))));
            lines.AddRange(TestPhaseSummaryFast.Rows(orderedTests, CsvHeaders));
            TestPhaseManifests.WriteEvidenceLines(TestPhaseManifests.Join(timing, "tests.csv"), lines.ToArray());

            var limit = top < 1 ? 1 : top;
            var ranked = TestPhaseSummaryWrite.RankByTotal(orderedTests);
            var md = new List<string>
            {
                "# Phase timing",
                "",
                "TRX remains authoritative. Sidecars are private and are not gate inputs.",
                "",
                "| test | outcome | exclusive+overlap+remainder |",
                "| --- | --- | --- |",
            };
            var shown = 0;
            foreach (var test in ranked)
            {
                if (shown >= limit) break;
                shown++;
                md.Add("| " + TestPhaseText.MarkdownCell(PsValue.Prop(test, "testId")) + " | " + TestPhaseText.MarkdownCell(PsValue.Prop(test, "outcome"))
                    + " | " + TestPhaseText.MarkdownCell(Total(test)) + " |");
            }
            md.Add("");
            md.Add("Shared fixture rows are separate from per-test totals.");
            TestPhaseManifests.WriteEvidenceLines(TestPhaseManifests.Join(timing, "summary.md"), md.ToArray());
        }

        // [long]$test.db + ... + [long]$test.remainderMs: Int64, continuing in Double past it as PowerShell's arithmetic does.
        private static object Total(object test)
        {
            long sum = 0;
            double sumDouble = 0;
            var isDouble = false;
            foreach (var field in TotalFields)
            {
                var part = Long(PsValue.Prop(test, field));
                if (isDouble) { sumDouble += part; continue; }
                try { sum = checked(sum + part); }
                catch (OverflowException) { sumDouble = (double)sum + part; isDouble = true; }
            }
            return isDouble ? (object)sumDouble : sum;
        }

        // ---- the run summary and the command ----

        /// <summary>
        /// Invoke-TestPhaseRunSummary: with any planned document (the index's, else the singleton), the summary rows against
        /// timing/groups-planned.json (else the first document) and the summary files; a failure there writes the failure
        /// marker. Nothing without a planned document. An unreadable planned document throws (it is read first).
        /// </summary>
        public static void Run(string runDirectory, int top, TestPhasePublicationScope scope)
        {
            scope = TestPhaseManifests.Of(scope);
            var plannedPath = TestPhaseManifests.Join(TestPhaseManifests.TimingDirectory(runDirectory, scope.QualificationTimingRoot), "groups-planned.json");
            var docs = TestPhaseManifests.ReadPlannedDocuments(runDirectory, scope.QualificationTimingRoot);
            if (docs.Length < 1) return;
            try
            {
                // A historical singleton is one suite. It stays the stale-check document and is not treated as the
                // membership of every other suite.
                var planned = docs[0];
                if (TestPhaseManifests.Regular(plannedPath)) planned = TestPhaseManifests.ReadDocument(plannedPath);
                var report = Rows(planned, runDirectory, scope);
                WriteFiles(report, runDirectory, top, scope);
            }
            catch (Exception)
            {
                TestPhasePublication.WriteFailureMarker(runDirectory, scope.QualificationTimingRoot, "phase-summary-failed");
            }
        }

        /// <summary>The command's scope: no process timing root, this command as the writer's host, no seams.</summary>
        public static TestPhasePublicationScope CommandScope() => new TestPhasePublicationScope("", CommandEdition, "1", null, null);

        /// <summary>
        /// summarize-test-phases (formerly scripts/summarize-test-phases.ps1): with --timing-root that directory is the
        /// timing root; --recover-executed-manifest first rebuilds the flat projection and state from the committed
        /// journal (TestPhasePublication.ExecutedRecovery: refuses a live writer, never writes a terminal seal); when a
        /// journal or flat executed manifest is there, one progress line (publication generation, sequence, record kind
        /// and quality); then the run summary (top rows). Writes timing/summary.json, tests.csv and summary.md; never
        /// edits TRX, the run's summary.json, receipts or the cost model. Exit 0; 1 with one stderr line when it failed.
        /// </summary>
        public static int Summarize(TestPhaseSummarizeOptions options, TestPhasePublicationScope scope, TextWriter output, TextWriter error)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            // A copy: --timing-root sets this run's timing root, not the caller's scope.
            var given = scope ?? CommandScope();
            scope = new TestPhasePublicationScope(given.QualificationTimingRoot, given.HostEdition, given.HostVersion, given.CommitBarrier, given.CommitFault)
            {
                WriterEnvironment = given.WriterEnvironment,
                TrxReadWatch = given.TrxReadWatch,
                SummaryReReadWatch = given.SummaryReReadWatch,
            };
            output = output ?? TextWriter.Null;
            error = error ?? TextWriter.Null;
            try
            {
                var runDirectory = options.RunDirectory ?? "";
                if (!string.IsNullOrWhiteSpace(options.TimingRoot)) scope.QualificationTimingRoot = Path.GetFullPath(options.TimingRoot);
                if (options.RecoverExecutedManifest) TestPhasePublication.ExecutedRecovery(runDirectory, "", "", "", "", "", scope);
                var timing = TestPhaseManifests.TimingDirectory(runDirectory, scope.QualificationTimingRoot);
                var hasFlat = TestPhaseManifests.Regular(TestPhaseManifests.Join(timing, "groups-executed.json"));
                var hasJournal = TestPhaseManifests.Regular(TestPhaseManifests.Join(TestPhaseJournal.JournalRoot(timing), "current.json"));
                if (hasFlat || hasJournal)
                {
                    var progress = TestPhasePublication.ReadExecutedPrefix(runDirectory, "", scope.QualificationTimingRoot);
                    var quality = TestPhaseJournal.PublicationQuality(runDirectory, scope.QualificationTimingRoot, null);
                    output.WriteLine("publication generation=" + progress.Generation + " sequence=" + progress.LastSequence.ToString(CultureInfo.InvariantCulture)
                        + " recordKind=" + progress.RecordKind + " quality=" + string.Join(",", quality));
                }
                Run(runDirectory, options.Top, scope);
                return 0;
            }
            catch (Exception e)
            {
                error.WriteLine("summarize-test-phases: " + e.GetType().Name + ": " + e.Message);
                return 1;
            }
        }
    }
}
