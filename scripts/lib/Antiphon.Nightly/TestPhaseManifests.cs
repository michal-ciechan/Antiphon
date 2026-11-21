// The planned and executed manifests of test-phase-evidence.ps1, compiled (W2-nightly S09): the phase-timing variables
// of a native test child (Enable-TestPhaseExecutionEnvironment, Remove-TestPhaseVariables), the timing directory, the
// class basis map, the bounded TRX read and its warm-up, the planned manifest with its per-suite index and the planned
// documents, the owner observation, the executed chunk built from a TRX (Write-TestPhaseExecutedManifest; its journal
// half is TestPhasePublication.PublishChunk, S08) and Complete-TestPhaseLaunch. The run summary they feed is
// TestPhaseRunSummary.cs.
//
// Plain C#: what PowerShell read from its script scope arrives as a TestPhasePublicationScope (the process timing
// root, the read clocks, the writer environment and the commit seams). Records come back as PowerShell objects under
// PowerShell and as PlainObjects without it (PsValue). Documents are read as ConvertFrom-Json reads them
// (NightlyJson.ParseText with dates); a blank document is null and one that is not one strict JSON object throws, as
// ConvertFrom-Json threw for malformed text. Bytes written are those the PowerShell functions wrote. ASCII-only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Antiphon.TestPhase;

namespace Antiphon.Nightly
{
    /// <summary>The phase-timing variables: set on a native test child, removed from every other child.</summary>
    public static class TestPhaseEnvironment
    {
        public const string RootVariable = "ANTIPHON_TEST_PHASE_ROOT";

        /// <summary>$script:TestPhaseVariableNames.</summary>
        public static readonly string[] VariableNames =
        {
            RootVariable, "ANTIPHON_TEST_PHASE_RUN_ID", "ANTIPHON_TEST_PHASE_GROUP_ID", "ANTIPHON_TEST_PHASE_ATTEMPT", "ANTIPHON_TEST_PHASE_SUITE",
        };

        /// <summary>Remove-TestPhaseVariables: the five names leave the map (null: nothing).</summary>
        public static void Remove(IDictionary environment)
        {
            if (environment == null) return;
            foreach (var name in VariableNames) environment.Remove(name);
        }

        /// <summary>
        /// Enable-TestPhaseExecutionEnvironment: under the rc and nightly profiles, an inherited root
        /// (ANTIPHON_TEST_PHASE_ROOT of this process, passed as inheritedRoot) or an explicit timing root, the map gets
        /// the root (the timing root, else the inherited root, else LogRoot/timing) with the run, group, attempt and
        /// suite; otherwise the five names are removed. Null map: a new one. Returns the map.
        /// </summary>
        public static IDictionary Enable(IDictionary environment, string logRoot, string runId, string groupId, string attempt, string suite,
            string profile, string timingRoot, string inheritedRoot)
        {
            if (environment == null) environment = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var qualification = string.Equals(profile ?? "", "rc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile ?? "", "nightly", StringComparison.OrdinalIgnoreCase);
            if (!qualification && string.IsNullOrWhiteSpace(inheritedRoot) && string.IsNullOrWhiteSpace(timingRoot))
            {
                Remove(environment);
                return environment;
            }
            var root = timingRoot;
            if (string.IsNullOrWhiteSpace(root)) root = inheritedRoot;
            if (string.IsNullOrWhiteSpace(root)) root = TestPhaseManifests.Join(logRoot, "timing");
            environment[RootVariable] = root ?? "";
            environment["ANTIPHON_TEST_PHASE_RUN_ID"] = runId ?? "";
            environment["ANTIPHON_TEST_PHASE_GROUP_ID"] = groupId ?? "";
            environment["ANTIPHON_TEST_PHASE_ATTEMPT"] = attempt ?? "";
            environment["ANTIPHON_TEST_PHASE_SUITE"] = suite ?? "";
            return environment;
        }

        /// <summary>Enable with this process's ANTIPHON_TEST_PHASE_ROOT as the inherited root.</summary>
        public static IDictionary Enable(IDictionary environment, string logRoot, string runId, string groupId, string attempt, string suite,
            string profile, string timingRoot) =>
            Enable(environment, logRoot, runId, groupId, attempt, suite, profile, timingRoot, System.Environment.GetEnvironmentVariable(RootVariable));
    }

    /// <summary>Read-TestPhaseTrxRows' result: the timing rows of a complete TRX, else none and Refused.</summary>
    public sealed class TestPhaseTrxRows
    {
        public object[] Rows { get; set; } = new object[0];
        public bool Complete { get; set; }
        public bool Refused { get; set; } = true;
    }

    /// <summary>Write-TestPhasePlannedManifest's parameters, with the class-cost path and table of the caller.</summary>
    public sealed class TestPhasePlannedRequest
    {
        public string LogRoot { get; set; } = "";
        public string RunId { get; set; } = "";
        public string Attempt { get; set; } = "";
        public string Suite { get; set; } = "";
        public string SourceDigest { get; set; } = "";
        public string AssemblyDigest { get; set; } = "";
        public string PolicyDigest { get; set; } = "";
        public string CostFileDigest { get; set; } = "";
        public string AssemblyId { get; set; } = "";
        /// <summary>The launch groups (id, classes, nodes, exactFilter or groupFilter, seconds).</summary>
        public object Groups { get; set; }
        public object DiscoveryNodes { get; set; }
        public object ProfileExclusions { get; set; }
        /// <summary>With DiscoveryPath, the suite's discovery index (its view is shared with the planner).</summary>
        public NightlyDiscoveryIndex DiscoveryIndex { get; set; }
        public string DiscoveryPath { get; set; } = "";
        /// <summary>$script:NightlyClassCostPath: the file the class basis map is read from (blank: none).</summary>
        public string ClassCostPath { get; set; } = "";
        /// <summary>$script:NightlyClassCostTable: a class it holds that the basis map lacks is priced 'measured'.</summary>
        public IDictionary ClassCostTable { get; set; }
    }

    public static class TestPhaseManifests
    {
        private static readonly Regex SuitePattern = new Regex("^[a-z0-9][a-z0-9-]{0,40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PlannedFilePattern = new Regex(@"^groups-planned-[a-z0-9][a-z0-9-]{0,40}\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly TestPhasePublicationScope NoScope = new TestPhasePublicationScope();
        private static int warmed;

        internal static TestPhasePublicationScope Of(TestPhasePublicationScope scope) => scope ?? NoScope;

        // ---- paths and documents ----

        /// <summary>
        /// Join-Path parent child: the two joined by one separator (an empty parent throws, as Join-Path refused it).
        /// Unlike Path.Combine, a child that starts with a separator stays under the parent.
        /// </summary>
        internal static string Join(string parent, string child)
        {
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException("Cannot bind argument to parameter 'Path' because it is an empty string.");
            return parent.TrimEnd('\\', '/') + Path.DirectorySeparatorChar + (child ?? "").TrimStart('\\', '/');
        }

        /// <summary>Get-TestPhaseTimingDirectory: the process timing root when set, else RunDirectory/timing.</summary>
        public static string TimingDirectory(string runDirectory, string qualificationTimingRoot) =>
            !string.IsNullOrWhiteSpace(qualificationTimingRoot) ? qualificationTimingRoot : Join(runDirectory, "timing");

        // Test-Path -LiteralPath: a file or a directory.
        internal static bool Exists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        // Get-TestPhasePathIdentity's kind is 'regular'.
        internal static bool Regular(string path) => TestPhaseFiles.Kind(path, out _) == "regular";

        // Read-NightlyEvidenceText.
        internal static string ReadEvidenceText(string path) =>
            EvidenceFiles.Buffered ? EvidenceFiles.ReadAllText(path) : File.ReadAllText(path);

        // Write-NightlyEvidenceLines: UTF-8 without a byte-order mark, each line ended by the platform's newline.
        internal static void WriteEvidenceLines(string path, string[] lines)
        {
            if (EvidenceFiles.Buffered) { EvidenceFiles.WriteAllLines(path, lines); return; }
            File.WriteAllLines(path, lines ?? new string[0], Utf8NoBom);
        }

        /// <summary>
        /// (Read-NightlyEvidenceText path) | ConvertFrom-Json for one evidence document: null for blank text; text that is
        /// not one strict JSON object throws.
        /// </summary>
        public static object ReadDocument(string path)
        {
            var text = ReadEvidenceText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return NightlyJson.ParseText(text, 1024, true) ?? throw new InvalidDataException("not one JSON object: " + path);
        }

        // $o.PSObject.Properties.Name -contains 'name' (case-insensitive).
        internal static bool Has(object o, string name)
        {
            if (o == null) return false;
            PsValue.Property(o, name, out var found);
            return found;
        }

        internal static string Text(object o, string name) => PsValue.Str(PsValue.Prop(o, name));

        // A note's text when the object has it and it is not null, else "".
        private static string NoteText(object o, string name) =>
            Has(o, name) && PsValue.Prop(o, name) != null ? Text(o, name) : "";

        // ---- the class basis map ----

        /// <summary>
        /// Get-TestPhaseClassBasisMap: class -> basis from the class-cost file (no file: empty), each call a copy. The parse
        /// is shared with NightlyTestsPlan.ClassCostTable through ClassCostCache while the file is unchanged.
        /// </summary>
        public static Hashtable ClassBasisMap(string classCostPath)
        {
            var map = new Hashtable(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(classCostPath) || !Exists(classCostPath)) return map;
            var full = Path.GetFullPath(classCostPath);
            var entry = ClassCostCache.Find(full);
            if (entry != null && entry.Basis != null) return (Hashtable)entry.Basis.Clone();
            var before = entry != null ? entry.Digest : ClassCostCache.Digest(full);
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(classCostPath).TrimStart((char)0xFEFF), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 64 }); }
            catch (JsonException e) { throw new InvalidDataException("class-cost-json: " + e.Message, e); }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("classes", out var classes) && classes.ValueKind == JsonValueKind.Object)
                    foreach (var row in classes.EnumerateObject()) map[row.Name] = JsonText(row.Value, "basis");
            }
            if (entry == null) entry = ClassCostCache.Store(full, before, null);
            entry.Basis = (Hashtable)map.Clone();
            return map;
        }

        // [string]$row.Value.<name>.
        private static string JsonText(JsonElement o, string name)
        {
            if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var v)) return "";
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number: return v.TryGetInt64(out var l) ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : PlainValues.Str(v.GetDouble());
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Null: return "";
                default: return v.GetRawText();
            }
        }

        // ---- the bounded TRX read ----

        /// <summary>
        /// Initialize-TestPhaseReadWarmup: the TRX scanner and the JSONL reader run once on small inputs before the first
        /// measured read, so a fresh process does not spend its first read budget compiling them. Once per process.
        /// </summary>
        public static void WarmReads()
        {
            if (Interlocked.Exchange(ref warmed, 1) != 0) return;
            try
            {
                TestPhaseReads.Warm();
                var trx = Encoding.UTF8.GetBytes("<TestRun><Results><UnitTestResult testName=\"warm\" duration=\"00:00:01\" outcome=\"Passed\" /></Results></TestRun>");
                using (var stream = new MemoryStream(trx))
                {
                    var read = TrxTimingScanner.Read(stream, Stopwatch.StartNew(), 200, 262144, 16384);
                    if (read != null && read.Complete) NightlyTrx.TimingRows(read, "warm.trx");
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Read-TestPhaseTrxRows: the timing rows ({TestId, Outcome, Name, Duration, StdOut, Path}) of a regular TRX read
        /// completely within the deadline on watch (null: a stopwatch started now), else none, refused.
        /// </summary>
        public static TestPhaseTrxRows ReadTrxRows(string path, Stopwatch watch, int deadlineMilliseconds)
        {
            WarmReads();
            var none = new TestPhaseTrxRows();
            if (string.IsNullOrWhiteSpace(path)) return none;
            if (watch == null) watch = Stopwatch.StartNew();
            if (deadlineMilliseconds < 1) deadlineMilliseconds = 1;
            if (!Regular(path)) return none;
            if (watch.ElapsedMilliseconds >= deadlineMilliseconds) return none;
            var opened = TestPhaseFiles.OpenReadStream(path);
            if (opened.Refused || opened.Stream == null) return none;
            try
            {
                var read = TrxTimingScanner.Read(opened.Stream, watch, deadlineMilliseconds, 262144, 16384);
                if (read == null || !read.Complete) return none;
                return new TestPhaseTrxRows { Rows = NightlyTrx.TimingRows(read, path), Complete = true, Refused = false };
            }
            catch (Exception) { return none; }
            finally
            {
                try { opened.Stream.Dispose(); } catch (Exception) { }
            }
        }

        /// <summary>ReadTrxRows on the scope's TRX clock ($script:TestPhaseTrxReadWatch), 200 ms.</summary>
        public static TestPhaseTrxRows ReadTrxRows(string path, TestPhasePublicationScope scope) =>
            ReadTrxRows(path, Of(scope).TrxReadWatch, 200);

        // ---- the planned manifest ----

        /// <summary>
        /// Write-TestPhasePlannedManifest: timing/groups-planned.json (and groups-planned-&lt;suite&gt;.json with its index
        /// row for an allowlisted suite): each group's planned ids (its carried nodes, else the view's required UIDs of
        /// its classes), classes, filter, seconds and pricing basis, and whether the planned ids partition the required
        /// set exactly. Returns the singleton's path.
        /// </summary>
        public static string WritePlanned(TestPhasePlannedRequest request, TestPhasePublicationScope scope)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            scope = Of(scope);
            var allPlanned = new List<string>();
            DiscoveryIndexEntry entry;
            if (request.DiscoveryIndex != null && !string.IsNullOrWhiteSpace(request.DiscoveryPath))
                entry = NightlyDiscoveryIndex.EntryOf(request.DiscoveryIndex, request.DiscoveryPath);
            else
                entry = new DiscoveryIndexEntry { Document = PsValue.Record(("Nodes", request.DiscoveryNodes == null ? new object[0] : PsValue.AsArray(request.DiscoveryNodes))) };
            var view = NightlyDiscoveryIndex.View(entry, request.ProfileExclusions, true);
            var basisMap = ClassBasisMap(request.ClassCostPath);
            var groupRows = new List<object>();
            foreach (var group in PsValue.AsArray(request.Groups))
            {
                var ids = new List<string>();
                var carried = Has(group, "nodes") && PsValue.Prop(group, "nodes") != null ? PsValue.AsArray(PsValue.Prop(group, "nodes")) : new object[0];
                var classNames = new List<string>();
                foreach (var c in PsValue.AsArray(PsValue.Prop(group, "classes"))) classNames.Add(PsValue.Str(c));
                if (carried.Length > 0)
                {
                    foreach (var node in carried)
                    {
                        var uid = Text(node, "Uid");
                        if (!string.IsNullOrWhiteSpace(uid)) ids.Add(uid);
                    }
                }
                else
                {
                    PlannedManifestCheck.AddStrings(ids, NightlyDiscoveryIndex.RequiredUids(view, classNames.ToArray()));
                }
                allPlanned.AddRange(ids);
                // One basis per class: the map's, else 'measured' for a class the cost table prices, else 'model'.
                var bases = new List<string>();
                foreach (var key in classNames)
                {
                    var basis = "model";
                    if (basisMap.ContainsKey(key)) basis = PsValue.Str(basisMap[key]);
                    else if (request.ClassCostTable != null && request.ClassCostTable.Contains(key)) basis = "measured";
                    if (!bases.Exists(seen => string.Equals(seen, basis, StringComparison.OrdinalIgnoreCase))) bases.Add(basis);
                }
                var pricing = bases.Count == 1 ? bases[0] : bases.Count > 1 ? "mixed" : "model";
                var filter = "";
                if (Has(group, "exactFilter") && !string.IsNullOrWhiteSpace(Text(group, "exactFilter"))) filter = Text(group, "exactFilter");
                else if (Has(group, "groupFilter")) filter = Text(group, "groupFilter");
                object seconds = 0;
                if (Has(group, "seconds") && PsValue.Prop(group, "seconds") != null) seconds = PsValue.Double(PsValue.Prop(group, "seconds"));
                groupRows.Add(new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "chunk", Text(group, "id") },
                    { "classes", classNames.ToArray() },
                    { "filter", filter },
                    { "plannedTestIds", ids.ToArray() },
                    { "plannedSeconds", seconds },
                    { "pricingBasis", pricing },
                    { "sharedManifestOrder", true },
                });
            }
            var partition = PlannedManifestCheck.Check(allPlanned, PsValue.AsArray(PsValue.Prop(view, "RequiredUids")));
            var duplicate = PsValue.IsTrue(PsValue.Prop(partition, "Duplicate"));
            var missing = PsValue.AsArray(PsValue.Prop(partition, "Missing"));
            var extra = PsValue.AsArray(PsValue.Prop(partition, "Extra"));
            var manifest = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "version", 1 },
                { "runId", request.RunId ?? "" },
                { "attempt", request.Attempt ?? "" },
                { "suite", request.Suite ?? "" },
                { "sourceDigest", request.SourceDigest ?? "" },
                { "assemblyDigest", request.AssemblyDigest ?? "" },
                { "policyDigest", request.PolicyDigest ?? "" },
                { "costFileDigest", request.CostFileDigest ?? "" },
                { "assembly", request.AssemblyId ?? "" },
                { "requiredPartitionExact", !duplicate && missing.Length == 0 && extra.Length == 0 },
                { "missingIds", missing },
                { "extraIds", extra },
                { "groups", groupRows.ToArray() },
            };
            var dir = TimingDirectory(request.LogRoot, scope.QualificationTimingRoot);
            var text = TestPhaseJson.Text(manifest, 0);
            var path = Join(dir, "groups-planned.json");
            TestPhaseFiles.WriteTextAtomic(path, text);
            // Keep the singleton for existing readers. Also retain every allowlisted suite.
            var suite = request.Suite ?? "";
            if (SuitePattern.IsMatch(suite))
            {
                var suiteFile = "groups-planned-" + suite + ".json";
                TestPhaseFiles.WriteTextAtomic(Join(dir, suiteFile), text);
                // One index filter per group. PowerShell read each row's filter as a property of an [ordered]
                // dictionary, which has none: every filter it recorded is blank, and so is every one here.
                var filters = new string[groupRows.Count];
                for (var i = 0; i < filters.Length; i++) filters[i] = "";
                UpdatePlannedIndex(dir, suite, suiteFile, request.RunId, request.Attempt, request.AssemblyId, request.SourceDigest, request.PolicyDigest,
                    request.CostFileDigest, request.AssemblyDigest, filters);
            }
            return path;
        }

        /// <summary>
        /// Update-TestPhasePlannedIndex: timing/groups-planned-index.json keeps every other suite's row (its digests and
        /// filters) and takes this suite's row last. A suite or file name outside the patterns writes nothing.
        /// </summary>
        public static void UpdatePlannedIndex(string directory, string suite, string fileName, string runId, string attempt, string assemblyId,
            string sourceDigest, string policyDigest, string costFileDigest, string assemblyDigest, object filters)
        {
            if (!SuitePattern.IsMatch(suite ?? "")) return;
            if (!PlannedFilePattern.IsMatch(fileName ?? "")) return;
            var path = Join(directory, "groups-planned-index.json");
            var kept = new List<object>();
            if (Regular(path))
            {
                var existing = ReadDocument(path);
                if (Has(existing, "suites") && PsValue.Prop(existing, "suites") != null)
                {
                    foreach (var row in PsValue.AsArray(PsValue.Prop(existing, "suites")))
                    {
                        if (row == null) continue;
                        if (PsValue.Eq(Text(row, "suite"), suite, true)) continue;
                        var keptFilters = new List<string>();
                        if (Has(row, "filters") && PsValue.Prop(row, "filters") != null)
                            foreach (var item in PsValue.AsArray(PsValue.Prop(row, "filters"))) keptFilters.Add(PsValue.Str(item));
                        kept.Add(IndexRow(Text(row, "suite"), Text(row, "file"), Text(row, "runId"), Text(row, "attempt"), Text(row, "assembly"),
                            NoteText(row, "sourceDigest"), NoteText(row, "policyDigest"), NoteText(row, "costFileDigest"), NoteText(row, "assemblyDigest"),
                            keptFilters.ToArray()));
                    }
                }
            }
            var freshFilters = new List<string>();
            if (filters != null) foreach (var item in PsValue.AsArray(filters)) freshFilters.Add(PsValue.Str(item));
            kept.Add(IndexRow(suite, fileName, runId, attempt, assemblyId, sourceDigest, policyDigest, costFileDigest, assemblyDigest, freshFilters.ToArray()));
            var doc = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "version", 1 }, { "runId", runId ?? "" }, { "attempt", attempt ?? "" }, { "suites", kept.ToArray() },
            };
            TestPhaseFiles.WriteTextAtomic(path, TestPhaseJson.Text(doc, 0));
        }

        private static OrderedDictionary IndexRow(string suite, string file, string runId, string attempt, string assembly, string sourceDigest,
            string policyDigest, string costFileDigest, string assemblyDigest, string[] filters) =>
            new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "suite", suite ?? "" }, { "file", file ?? "" }, { "runId", runId ?? "" }, { "attempt", attempt ?? "" }, { "assembly", assembly ?? "" },
                { "sourceDigest", sourceDigest ?? "" }, { "policyDigest", policyDigest ?? "" }, { "costFileDigest", costFileDigest ?? "" },
                { "assemblyDigest", assemblyDigest ?? "" }, { "filters", filters ?? new string[0] },
            };

        /// <summary>
        /// Read-TestPhasePlannedDocuments: the per-suite planned documents the index names (in index order, only names in
        /// the pattern and regular files), else the singleton groups-planned.json; a blank document is a null entry.
        /// </summary>
        public static object[] ReadPlannedDocuments(string runDirectory, string qualificationTimingRoot)
        {
            var dir = TimingDirectory(runDirectory, qualificationTimingRoot);
            var docs = new List<object>();
            var indexPath = Join(dir, "groups-planned-index.json");
            if (Regular(indexPath))
            {
                var index = ReadDocument(indexPath);
                if (Has(index, "suites") && PsValue.Prop(index, "suites") != null)
                {
                    foreach (var row in PsValue.AsArray(PsValue.Prop(index, "suites")))
                    {
                        if (row == null) continue;
                        var file = Has(row, "file") ? Text(row, "file") : "";
                        if (!PlannedFilePattern.IsMatch(file)) continue;
                        var docPath = Join(dir, file);
                        if (!Regular(docPath)) continue;
                        docs.Add(ReadDocument(docPath));
                    }
                }
            }
            if (docs.Count == 0)
            {
                var singleton = Join(dir, "groups-planned.json");
                if (Regular(singleton)) docs.Add(ReadDocument(singleton));
            }
            return docs.ToArray();
        }

        // ---- the executed chunk ----

        /// <summary>
        /// Write-TestPhaseOwnerObservation: timing/owner-&lt;suite&gt;-&lt;chunk&gt;-&lt;run&gt;-&lt;attempt&gt;.json from the
        /// verdict's owner fields (TestPhaseRecords.OwnerDocument), never over an existing one. Diagnostic: a failure
        /// writes nothing and does not throw.
        /// </summary>
        public static void WriteOwnerObservation(object verdict, string logRoot, string suite, string chunk, string runId, string attempt,
            TestPhasePublicationScope scope)
        {
            try
            {
                var doc = TestPhaseRecords.OwnerDocument(verdict, suite, chunk, runId, attempt);
                if (doc == null) return;
                var dir = TimingDirectory(logRoot, Of(scope).QualificationTimingRoot);
                var path = Join(dir, "owner-" + suite + "-" + chunk + "-" + PsValue.Str(doc["runId"]) + "-" + PsValue.Str(doc["attempt"]) + ".json");
                if (Regular(path)) return;
                TestPhaseFiles.WriteTextAtomic(path, TestPhaseJson.Text(doc, 0));
            }
            catch (Exception)
            {
                // Owner stamps are diagnostic. A write failure must not change the verdict.
            }
        }

        /// <summary>
        /// Write-TestPhaseExecutedManifest's chunk document: the outcome rows of the TRX when it is read completely (none
        /// otherwise; trxMissing when it is not a regular file), their passed count, the TRX's relative path and digest,
        /// and the verdict's ExitCode, coverageComplete and testsPassed where it has them.
        /// </summary>
        public static OrderedDictionary FreshChunk(object verdict, string logRoot, string trxPath, string suite, string chunk, string runId, string attempt,
            double hostWallSeconds, TestPhasePublicationScope scope)
        {
            var rows = new object[0];
            var regular = Regular(trxPath);
            if (regular)
            {
                var read = ReadTrxRows(trxPath, scope);
                if (read != null && read.Complete && !read.Refused) rows = read.Rows;
            }
            // One row per test (about 19,000 in the main suite).
            var outcomeRows = TestPhaseSummary.OutcomeRows(rows, "TestId", "Outcome", false);
            var passed = TestPhaseSummary.PassedCount(outcomeRows);
            var digest = "";
            if (regular)
            {
                try { digest = EvidenceFiles.Buffered ? EvidenceFiles.Sha256(trxPath) : NightlyClassNames.FileSha256(trxPath); }
                catch (Exception) { digest = ""; }
            }
            object exitCode = null;
            object coverageComplete = null;
            object testsPassed = null;
            if (verdict != null)
            {
                if (Has(verdict, "ExitCode")) exitCode = PsValue.Prop(verdict, "ExitCode");
                if (Has(verdict, "coverageComplete")) coverageComplete = PsValue.IsTrue(PsValue.Prop(verdict, "coverageComplete"));
                if (Has(verdict, "testsPassed")) testsPassed = PsValue.IsTrue(PsValue.Prop(verdict, "testsPassed"));
            }
            return TestPhaseJournal.FreshChunk(chunk, runId, attempt, suite, hostWallSeconds, !regular, passed, outcomeRows,
                TestPhaseText.RelativePath(logRoot, trxPath), digest, exitCode, coverageComplete, testsPassed);
        }

        /// <summary>
        /// Write-TestPhaseExecutedManifest: the owner observation, then the chunk built from the TRX published into the
        /// bound context, else one opened, published, projected and closed (TestPhasePublication.PublishChunk). True when
        /// the chunk committed; a refused or failed publication writes the failure marker and does not throw.
        /// </summary>
        public static bool WriteExecuted(object verdict, string logRoot, string trxPath, string suite, string chunk, string runId, string attempt,
            double hostWallSeconds, TestPhaseJournalContext bound, TestPhasePublicationScope scope)
        {
            scope = Of(scope);
            var fresh = FreshChunk(verdict, logRoot, trxPath, suite, chunk, runId, attempt, hostWallSeconds, scope);
            WriteOwnerObservation(verdict, logRoot, suite, chunk, runId, attempt, scope);
            return TestPhasePublication.PublishChunk(bound, logRoot, fresh, suite, chunk, runId, attempt, scope);
        }

        /// <summary>
        /// Complete-TestPhaseLaunch: the executed manifest, the bound context's projection, and the run summary (top 50)
        /// when timing/groups-planned.json is there. Whatever fails writes the failure marker; the verdict is returned
        /// unchanged. beforeSummary is the $script:TestPhaseSeams.Summary seam (runs first; null in production).
        /// </summary>
        public static object CompleteLaunch(object verdict, string logRoot, string trxPath, string suite, string chunk, string runId, string attempt,
            double hostWallSeconds, TestPhaseJournalContext bound, TestPhasePublicationScope scope, Action beforeSummary = null)
        {
            scope = Of(scope);
            try
            {
                beforeSummary?.Invoke();
                WriteExecuted(verdict, logRoot, trxPath, suite, chunk, runId, attempt, hostWallSeconds, bound, scope);
                if (bound != null && !bound.Closed && !bound.Refused) TestPhasePublication.CompleteProjection(bound, "", scope);
                var planned = Join(TimingDirectory(logRoot, scope.QualificationTimingRoot), "groups-planned.json");
                if (TestPhaseJournal.EvidencePathExists(planned)) TestPhaseRunSummary.Run(logRoot, 50, scope);
            }
            catch (Exception)
            {
                TestPhasePublication.WriteFailureMarker(logRoot, scope.QualificationTimingRoot, "phase-summary-failed");
            }
            return verdict;
        }
    }
}
