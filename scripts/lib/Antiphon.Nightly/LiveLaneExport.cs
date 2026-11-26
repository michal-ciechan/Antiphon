// The live-lane producer exports and receipts of live-lane-manifest.ps1 (CARD-1545 S1a D-4), compiled in W2-nightly
// S24. A candidate roster row (CP-25 for the PTY project, CP-1 for Antiphon.Tests) writes its discovery as a pending
// export and marks it terminal only after its own checks pass; a consumer (the Antiphon.Tests roster, the census)
// accepts an export only when it is terminal, non-empty, of the expected project, source, run and build. The census
// also requires the producers' green checkpoint receipts (state.json and report.json under checkpointResults). The
// portable context (ANTIPHON_LIVE_PORTABLE_CONTEXT) says which mode a row runs in. Plain C#. ASCII-only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>An accept-or-refuse answer and its reason (blank when accepted).</summary>
    public sealed record LiveLaneVerdict(bool Accepted, string Reason)
    {
        public static LiveLaneVerdict Yes { get; } = new LiveLaneVerdict(true, "");

        public static LiveLaneVerdict No(string reason) => new LiveLaneVerdict(false, reason);
    }

    /// <summary>One producer's provenance in the context (producerProvenance.&lt;project&gt;): its source and assembly hash.</summary>
    public sealed record LiveLaneProvenance(string Project, string SourceSha, string AssemblyHash);

    /// <summary>
    /// The portable context of a live-lane commission (Read-LiveLaneContextObject): the JSON in
    /// ANTIPHON_LIVE_PORTABLE_CONTEXT, or the file it names. The roster rows read mode, exportRequired, sourceSha,
    /// runId and the two export paths; the census (LiveLaneCensus, W2-nightly S25) also reads the candidate root, the
    /// seed, the pre-S1 base, the landing target, the fixture binding, the producers' provenance and receipts.
    /// </summary>
    public sealed class LiveLaneContext
    {
        public string Mode { get; init; } = "";
        public bool ExportRequired { get; init; }
        public string SourceSha { get; init; } = "";
        public string RunId { get; init; } = "";
        public string Cp25Export { get; init; } = "";
        public string Cp1Export { get; init; } = "";
        public string CandidateRoot { get; init; } = "";
        public string CensusBaseSha { get; init; } = "";
        public string PreS1BaseSha { get; init; } = "";
        public string LandingTarget { get; init; } = "";
        public string FixturePath { get; init; } = "";
        public string FixtureBlob { get; init; } = "";
        public string CheckpointResults { get; init; } = "";

        /// <summary>producerProvenance's rows in the context's order; null when it has none (a baseline context).</summary>
        public IReadOnlyList<LiveLaneProvenance> ProducerProvenance { get; init; }

        /// <summary>Get-LiveLaneProp $provenance $project: the first row whose name matches, ignoring case; null when none.</summary>
        public static LiveLaneProvenance FindProvenance(IReadOnlyList<LiveLaneProvenance> provenance, string project)
        {
            if (provenance == null) return null;
            foreach (var row in provenance)
                if (string.Equals(row.Project, project ?? "", StringComparison.OrdinalIgnoreCase)) return row;
            return null;
        }

        /// <summary>The context ANTIPHON_LIVE_PORTABLE_CONTEXT names (variables: this process's environment when null); null when there is none.</summary>
        public static LiveLaneContext FromEnvironment(Func<string, string> variables = null) =>
            Read((variables ?? Environment.GetEnvironmentVariable)("ANTIPHON_LIVE_PORTABLE_CONTEXT"));

        /// <summary>
        /// A context from its JSON text, or from the file a value not starting with { names: null for a blank value or
        /// a missing file. Malformed JSON refuses, as ConvertFrom-Json did.
        /// </summary>
        public static LiveLaneContext Read(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = raw;
            if (!raw.TrimStart().StartsWith("{", StringComparison.CurrentCulture))
            {
                if (!File.Exists(raw) && !Directory.Exists(raw)) return null;
                text = File.ReadAllText(raw);
            }
            using var doc = LiveLaneJson.Parse(text);
            var root = doc.RootElement;
            var flag = LiveLaneJson.Prop(root, "exportRequired");
            List<LiveLaneProvenance> provenance = null;
            var rows = LiveLaneJson.Prop(root, "producerProvenance");
            if (rows != null)
            {
                provenance = new List<LiveLaneProvenance>();
                if (rows.Value.ValueKind == JsonValueKind.Object)
                    foreach (var row in rows.Value.EnumerateObject())
                        provenance.Add(new LiveLaneProvenance(row.Name, LiveLaneJson.Field(row.Value, "sourceSha"), LiveLaneJson.Field(row.Value, "assemblyHash")));
            }
            return new LiveLaneContext
            {
                Mode = LiveLaneJson.Field(root, "mode"),
                ExportRequired = flag != null && LiveLaneJson.IsTrue(flag.Value),
                SourceSha = LiveLaneJson.Field(root, "sourceSha"),
                RunId = LiveLaneJson.Field(root, "runId"),
                Cp25Export = LiveLaneJson.Field(root, "cp25Export"),
                Cp1Export = LiveLaneJson.Field(root, "cp1Export"),
                CandidateRoot = LiveLaneJson.Field(root, "candidateRoot"),
                CensusBaseSha = LiveLaneJson.Field(root, "censusBaseSha"),
                PreS1BaseSha = LiveLaneJson.Field(root, "preS1BaseSha"),
                LandingTarget = LiveLaneJson.Field(root, "landingTarget"),
                FixturePath = LiveLaneJson.Field(root, "fixturePath"),
                FixtureBlob = LiveLaneJson.Field(root, "fixtureBlob"),
                CheckpointResults = LiveLaneJson.Field(root, "checkpointResults"),
                ProducerProvenance = provenance,
            };
        }
    }

    /// <summary>A producer export (antiphon-live-lane-export-v1).</summary>
    public sealed class LiveLaneExport
    {
        private object[] _rawNodes;
        private IReadOnlyList<LiveLaneNode> _nodes = new LiveLaneNode[0];

        public string Format { get; set; } = "antiphon-live-lane-export-v1";
        public string Project { get; set; } = "";
        public string Cp { get; set; } = "";
        public string SourceSha { get; set; } = "";
        public string RunId { get; set; } = "";
        public string AssemblyHash { get; set; } = "";
        public string AssemblyPath { get; set; } = "";

        /// <summary>The terminal flag ([bool] of it); null when the export has none.</summary>
        public bool? Terminal { get; set; }

        public IReadOnlyList<LiveLaneNode> Nodes
        {
            get
            {
                if (_rawNodes != null)
                {
                    var nodes = new LiveLaneNode[_rawNodes.Length];
                    for (var i = 0; i < nodes.Length; i++) nodes[i] = LiveLaneNode.FromObject(_rawNodes[i]);
                    _nodes = nodes;
                    _rawNodes = null;
                }
                return _nodes;
            }
            set
            {
                _nodes = value ?? new LiveLaneNode[0];
                _rawNodes = null;
            }
        }

        /// <summary>The node count, without reading the nodes of a PowerShell export.</summary>
        public int NodeCount => _rawNodes?.Length ?? _nodes.Count;

        /// <summary>An export file's document.</summary>
        public static LiveLaneExport FromJson(JsonElement export)
        {
            var terminal = LiveLaneJson.Prop(export, "terminal");
            var nodes = new List<LiveLaneNode>();
            foreach (var node in LiveLaneJson.Rows(export, "nodes")) nodes.Add(LiveLaneNode.FromJson(node));
            return new LiveLaneExport
            {
                Format = LiveLaneJson.Field(export, "format"),
                Project = LiveLaneJson.Field(export, "project"),
                Cp = LiveLaneJson.Field(export, "cp"),
                SourceSha = LiveLaneJson.Field(export, "sourceSha"),
                RunId = LiveLaneJson.Field(export, "runId"),
                AssemblyHash = LiveLaneJson.Field(export, "assemblyHash"),
                AssemblyPath = LiveLaneJson.Field(export, "assemblyPath"),
                Terminal = terminal == null ? (bool?)null : LiveLaneJson.IsTrue(terminal.Value),
                Nodes = nodes,
            };
        }

        /// <summary>A PowerShell caller's export (ConvertFrom-Json's object, possibly edited), read by name; null stays null.</summary>
        public static LiveLaneExport FromObject(object export)
        {
            if (export == null) return null;
            var terminal = PsValue.Prop(export, "terminal");
            return new LiveLaneExport
            {
                Format = PsValue.Str(PsValue.Prop(export, "format")),
                Project = PsValue.Str(PsValue.Prop(export, "project")),
                Cp = PsValue.Str(PsValue.Prop(export, "cp")),
                SourceSha = PsValue.Str(PsValue.Prop(export, "sourceSha")),
                RunId = PsValue.Str(PsValue.Prop(export, "runId")),
                AssemblyHash = PsValue.Str(PsValue.Prop(export, "assemblyHash")),
                AssemblyPath = PsValue.Str(PsValue.Prop(export, "assemblyPath")),
                Terminal = terminal == null ? (bool?)null : PsValue.IsTrue(terminal),
                _rawNodes = LiveLaneDiscovery.Items(PsValue.Prop(export, "nodes")),
            };
        }
    }

    public static class LiveLaneExports
    {
        public const string Format = "antiphon-live-lane-export-v1";

        /// <summary>
        /// Test-LiveLaneExportDocument: producer-incomplete for a missing, pending or empty export (and, with requireCore,
        /// one without exactly one node per core identity of the expected project), source-mismatch for another project or source,
        /// run-mismatch, and build-mismatch for another assembly hash or, with assemblyPath, a kept assembly that is
        /// missing or hashes otherwise. Text compares as PowerShell's -ne did (ignoring case); hashes ignore case.
        /// </summary>
        public static LiveLaneVerdict Check(LiveLaneExport export, string expectedSource, string expectedRun, string expectedHash, string expectedProject,
            bool requireCore, string assemblyPath = "")
        {
            if (export == null) return LiveLaneVerdict.No("producer-incomplete");
            if (export.Terminal != true) return LiveLaneVerdict.No("producer-incomplete");
            if (export.NodeCount == 0) return LiveLaneVerdict.No("producer-incomplete");
            if (!LiveLaneJson.Same(export.Project, expectedProject)) return LiveLaneVerdict.No("source-mismatch");
            if (!LiveLaneJson.Same(export.SourceSha, expectedSource)) return LiveLaneVerdict.No("source-mismatch");
            if (!LiveLaneJson.Same(export.RunId, expectedRun)) return LiveLaneVerdict.No("run-mismatch");
            if (!string.Equals(export.AssemblyHash ?? "", expectedHash ?? "", StringComparison.OrdinalIgnoreCase)) return LiveLaneVerdict.No("build-mismatch");
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                if (!File.Exists(assemblyPath)) return LiveLaneVerdict.No("build-mismatch");
                var fileHash = NightlyClassNames.FileSha256(assemblyPath);
                if (!string.Equals(fileHash, export.AssemblyHash ?? "", StringComparison.OrdinalIgnoreCase)) return LiveLaneVerdict.No("build-mismatch");
            }
            if (requireCore)
            {
                foreach (var want in LiveLaneContract.Core)
                {
                    if (!LiveLaneJson.Same(want.Project, expectedProject)) continue;
                    if (LiveLaneRoster.MatchingNodes(export.Nodes, want).Count != 1) return LiveLaneVerdict.No("producer-incomplete");
                }
            }
            return LiveLaneVerdict.Yes;
        }

        /// <summary>Read-LiveLaneExportFile: the export at path; null when the path is blank or missing.</summary>
        public static LiveLaneExport Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var doc = LiveLaneJson.ReadFile(path);
            return LiveLaneExport.FromJson(doc.RootElement);
        }

        /// <summary>
        /// Write-LiveLaneExportFile: the pending export (terminal false) at path, its directory made. A green checkpoint's
        /// cleanup deletes the build output, so an existing assembly is kept beside the export as &lt;cp&gt;-assembly.dll and
        /// the export names that copy.
        /// </summary>
        public static void Write(string path, string project, string cp, string sourceSha, string runId, string assemblyHash, string assemblyPath,
            IReadOnlyList<LiveLaneNode> nodes)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var keptPath = assemblyPath ?? "";
            if (!string.IsNullOrWhiteSpace(assemblyPath) && (File.Exists(assemblyPath) || Directory.Exists(assemblyPath)))
            {
                keptPath = Path.Combine(dir ?? "", (cp ?? "").ToLowerInvariant() + "-assembly.dll");
                File.Copy(assemblyPath, keptPath, true);
            }
            var rows = new List<LiveLaneJsonObject>();
            foreach (var node in nodes ?? new LiveLaneNode[0]) rows.Add(node.ToJson());
            var doc = new LiveLaneJsonObject
            {
                { "format", Format },
                { "project", project ?? "" },
                { "cp", cp ?? "" },
                { "sourceSha", sourceSha ?? "" },
                { "runId", runId ?? "" },
                { "assemblyHash", assemblyHash ?? "" },
                { "assemblyPath", keptPath },
                { "terminal", false },
                { "nodes", rows },
            };
            LiveLaneJson.WriteFile(path, doc, finalNewLine: true);
        }

        /// <summary>
        /// Complete-LiveLaneExport: when the row's checks passed, the export at path becomes terminal (the flag moves last,
        /// as Add-Member -Force put it); otherwise, or without a file, nothing changes.
        /// </summary>
        public static void Complete(string path, bool checksPassed)
        {
            if (!checksPassed) return;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            string text;
            using (var doc = LiveLaneJson.ReadFile(path))
            {
                var export = new LiveLaneJsonObject();
                foreach (var property in doc.RootElement.EnumerateObject())
                    if (!string.Equals(property.Name, "terminal", StringComparison.OrdinalIgnoreCase)) export.Add(property.Name, property.Value);
                export.Add("terminal", true);
                text = LiveLaneJson.Text(export);
            }
            File.WriteAllText(path, text + Environment.NewLine, new System.Text.UTF8Encoding(false));
        }

        /// <summary>
        /// The witness of D-4 early-final (the old ExportPending case): a written export is pending, stays pending when the
        /// checks failed, and is terminal once they passed; its scratch is gone afterwards. Problems: finalized-early,
        /// finalized-on-failure, not-finalized, export-scratch-residue. None: accepted.
        /// </summary>
        public static IReadOnlyList<string> ExportPendingWitness(string repoRoot)
        {
            var problems = new List<string>();
            var scratch = new LiveLaneScratch();
            var root = scratch.NewOwned(repoRoot, "exportpending" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = Path.Combine(root, "export.json");
                Write(path, "Antiphon.Tests", "CP-1", new string('a', 40), "run", string.Concat(Enumerable.Repeat("ab", 32)), "", new LiveLaneNode[0]);
                if (Terminal(path)) problems.Add("finalized-early");
                Complete(path, false);
                if (Terminal(path)) problems.Add("finalized-on-failure");
                Complete(path, true);
                if (!Terminal(path)) problems.Add("not-finalized");
            }
            finally
            {
                scratch.Clear();
            }
            if (Directory.Exists(root) || File.Exists(root)) problems.Add("export-scratch-residue");
            return problems;
        }

        // [bool](Get-LiveLaneProp (ConvertFrom-Json $file) 'terminal').
        private static bool Terminal(string path)
        {
            using var doc = LiveLaneJson.ReadFile(path);
            var terminal = LiveLaneJson.Prop(doc.RootElement, "terminal");
            return terminal != null && LiveLaneJson.IsTrue(terminal.Value);
        }
    }

    public static class LiveLaneReceipt
    {
        private static readonly string[] Rows = { "CP-25", "CP-1" };
        private static readonly Regex FieldRx = new Regex(@"(?:^|\s)([A-Za-z][A-Za-z0-9]*)=([^\s]*)");

        /// <summary>
        /// Test-LiveLaneReceiptLine: "CHECKPOINT &lt;id&gt; ..." whose first commit, source, buildSource, sourceState and
        /// dirty fields (names ignoring case) are the commit, the commit, verified, clean and 0 (values ignoring case).
        /// </summary>
        public static bool Line(string line, string id, string commit)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (!line.StartsWith("CHECKPOINT " + id + " ", StringComparison.Ordinal)) return false;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in FieldRx.Matches(line))
            {
                var key = match.Groups[1].Value;
                if (!fields.ContainsKey(key)) fields[key] = match.Groups[2].Value;
            }
            bool Is(string name, string want) => fields.TryGetValue(name, out var value) && LiveLaneJson.Same(value, want);
            return Is("commit", commit) && Is("source", commit) && Is("buildSource", "verified") && Is("sourceState", "clean") && Is("dirty", "0");
        }

        /// <summary>
        /// Test-LiveLaneProducerReceipt: the latest CP-25 and CP-1 rows under checkpointResults (each run directory's
        /// state.json, in name order, a later run's row replacing an earlier one) must be green in state and report, with a
        /// receipt line, the expected commit at start, end and report, a verified build and a clean source.
        /// producer-receipt-missing (no directory, row, report, report row or line), producer-row-not-green,
        /// producer-receipt-foreign.
        /// </summary>
        public static LiveLaneVerdict Check(string sourceSha, string checkpointResults)
        {
            var expected = sourceSha ?? "";
            var root = checkpointResults ?? "";
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return LiveLaneVerdict.No("producer-receipt-missing");
            var latest = new Dictionary<string, (JsonElement State, JsonElement Row, JsonElement? Report)>(StringComparer.OrdinalIgnoreCase);
            var documents = new List<JsonDocument>();
            try
            {
                var compare = CultureInfo.CurrentCulture.CompareInfo;
                var runs = new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
                    .OrderBy(d => d.Name, Comparer<string>.Create((a, b) => compare.Compare(a, b, CompareOptions.IgnoreCase)));
                foreach (var run in runs)
                {
                    var statePath = Path.Combine(run.FullName, "state.json");
                    if (!File.Exists(statePath)) continue;
                    var stateDoc = LiveLaneJson.ReadFile(statePath);
                    documents.Add(stateDoc);
                    JsonElement? report = null;
                    var reportPath = Path.Combine(run.FullName, "report.json");
                    if (File.Exists(reportPath))
                    {
                        var reportDoc = LiveLaneJson.ReadFile(reportPath);
                        documents.Add(reportDoc);
                        report = reportDoc.RootElement;
                    }
                    foreach (var row in LiveLaneJson.Rows(stateDoc.RootElement, "rows"))
                    {
                        var id = LiveLaneJson.Field(row, "id");
                        if (!LiveLaneJson.Same(id, "CP-25") && !LiveLaneJson.Same(id, "CP-1")) continue;
                        latest[id] = (stateDoc.RootElement, row, report);
                    }
                }
                foreach (var id in Rows)
                {
                    if (!latest.TryGetValue(id, out var item)) return LiveLaneVerdict.No("producer-receipt-missing");
                    if (!LiveLaneJson.Same(LiveLaneJson.Field(item.Row, "state"), "green")) return LiveLaneVerdict.No("producer-row-not-green");
                    if (item.Report == null) return LiveLaneVerdict.No("producer-receipt-missing");
                    JsonElement? match = null;
                    foreach (var reportRow in LiveLaneJson.Rows(item.Report, "rows"))
                        if (LiveLaneJson.Same(LiveLaneJson.Field(reportRow, "id"), id)) match = reportRow;
                    if (match == null) return LiveLaneVerdict.No("producer-receipt-missing");
                    if (!LiveLaneJson.Same(LiveLaneJson.Field(match, "state"), "green")) return LiveLaneVerdict.No("producer-row-not-green");
                    var line = LiveLaneJson.Field(match, "line");
                    if (string.IsNullOrWhiteSpace(line)) return LiveLaneVerdict.No("producer-receipt-missing");
                    var source = LiveLaneJson.Prop(item.State, "source");
                    var reportSource = LiveLaneJson.Prop(item.Report, "source");
                    var rowSource = LiveLaneJson.Prop(match, "source");
                    var build = LiveLaneJson.Field(rowSource, "buildSource");
                    if (string.IsNullOrWhiteSpace(build)) build = LiveLaneJson.Field(reportSource, "buildSource");
                    if (string.IsNullOrWhiteSpace(build)) build = LiveLaneJson.Field(source, "buildSource");
                    var commit = LiveLaneJson.Field(item.Report, "commit");
                    var startCommit = LiveLaneJson.Field(LiveLaneJson.Prop(source, "start"), "commit");
                    if (string.IsNullOrWhiteSpace(commit)) commit = startCommit;
                    var endCommit = LiveLaneJson.Field(LiveLaneJson.Prop(source, "end"), "commit");
                    var sourceState = LiveLaneJson.Field(source, "state");
                    if (!LiveLaneJson.Same(commit, expected) || !LiveLaneJson.Same(startCommit, expected) || !LiveLaneJson.Same(endCommit, expected)
                        || !LiveLaneJson.Same(build, "verified") || !LiveLaneJson.Same(sourceState, "clean") || !Line(line, id, expected))
                        return LiveLaneVerdict.No("producer-receipt-foreign");
                }
                return LiveLaneVerdict.Yes;
            }
            finally
            {
                foreach (var doc in documents) doc.Dispose();
            }
        }

        /// <summary>
        /// Write-LiveLaneReceiptFixture: root/run-producers/state.json and report.json (unless omitReport) for CP-25 and
        /// CP-1 in rowState (exit 0 when green, else 1), the source clean at commit, the receipt lines at lineCommit
        /// (blank: the commit) with buildSource.
        /// </summary>
        public static void WriteFixture(string root, string commit, string rowState, string buildSource = "verified", string lineCommit = "", bool omitReport = false)
        {
            if (string.IsNullOrWhiteSpace(lineCommit)) lineCommit = commit;
            var run = Path.Combine(root, "run-producers");
            Directory.CreateDirectory(run);
            var green = LiveLaneJson.Same(rowState, "green");
            var exitCode = green ? 0 : 1;
            var rows = new List<LiveLaneJsonObject>();
            foreach (var id in Rows)
            {
                var line = "CHECKPOINT " + id + " commit=" + lineCommit + " build=ok filter=live executed=1 passed=1 failed=0 skipped=0 dirty=0 source=" + lineCommit
                    + " sourceState=clean buildSource=" + buildSource;
                rows.Add(new LiveLaneJsonObject
                {
                    { "id", id },
                    { "state", rowState },
                    { "exitCode", exitCode },
                    { "source", new LiveLaneJsonObject { { "buildSource", buildSource }, { "state", "clean" } } },
                    { "line", line },
                });
            }
            var stateRows = new List<LiveLaneJsonObject>();
            foreach (var id in Rows) stateRows.Add(new LiveLaneJsonObject { { "id", id }, { "state", rowState }, { "exitCode", exitCode } });
            LiveLaneJsonObject Boundary() => new LiveLaneJsonObject { { "commit", commit }, { "captureStatus", "known" }, { "dirtyFiles", 0 } };
            var source = new LiveLaneJsonObject
            {
                { "state", "clean" },
                { "buildSource", buildSource },
                { "start", Boundary() },
                { "end", Boundary() },
            };
            var state = new LiveLaneJsonObject { { "source", source }, { "rows", stateRows } };
            LiveLaneJson.WriteFile(Path.Combine(run, "state.json"), state, finalNewLine: false);
            if (omitReport) return;
            var report = new LiveLaneJsonObject
            {
                { "commit", commit },
                { "exitCode", exitCode },
                { "verdict", green ? "GREEN" : "INVALID" },
                { "source", source },
                { "rows", rows },
            };
            LiveLaneJson.WriteFile(Path.Combine(run, "report.json"), report, finalNewLine: false);
        }

        /// <summary>
        /// The witness of D-4 skip-receipt (the old ProducerReceipt case): a missing receipt, a red row, a row still running
        /// and a foreign build each refuse with their reason, and a green receipt is accepted. censusRefusesRed (the
        /// census over a red receipt: repo, source, receipt root; true when it refused with producer-row-not-green) is the
        /// case's census check, census-skipped-receipt (LiveLaneCensus.RefusesRedReceipt); null skips it. Problems:
        /// missing-shape, red-shape, timeout-shape, foreign-shape, green-shape, census-skipped-receipt. None: accepted.
        /// </summary>
        public static IReadOnlyList<string> ProducerReceiptWitness(string repoRoot, Func<string, string, string, bool> censusRefusesRed = null)
        {
            var problems = new List<string>();
            var sha = new string('a', 40);
            var scratch = new LiveLaneScratch();
            var root = scratch.NewOwned(repoRoot, "receiptwitness" + Guid.NewGuid().ToString("N"));
            try
            {
                if (Check(sha, Path.Combine(root, "absent")).Reason != "producer-receipt-missing") problems.Add("missing-shape");
                var red = Path.Combine(root, "red");
                Directory.CreateDirectory(red);
                WriteFixture(red, sha, "red");
                if (Check(sha, red).Reason != "producer-row-not-green") problems.Add("red-shape");
                var timed = Path.Combine(root, "timed");
                Directory.CreateDirectory(timed);
                WriteFixture(timed, sha, "running");
                if (Check(sha, timed).Reason != "producer-row-not-green") problems.Add("timeout-shape");
                var foreign = Path.Combine(root, "foreign");
                Directory.CreateDirectory(foreign);
                WriteFixture(foreign, new string('b', 40), "green", "unknown", new string('b', 40));
                if (Check(sha, foreign).Reason != "producer-receipt-foreign") problems.Add("foreign-shape");
                var green = Path.Combine(root, "green");
                Directory.CreateDirectory(green);
                WriteFixture(green, sha, "green");
                if (!Check(sha, green).Accepted) problems.Add("green-shape");
                if (censusRefusesRed != null && !censusRefusesRed(repoRoot, sha, red)) problems.Add("census-skipped-receipt");
            }
            finally
            {
                scratch.Clear();
            }
            return problems;
        }
    }
}
