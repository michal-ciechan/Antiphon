// The live-lane core roster of live-lane-manifest.ps1 (CARD-1545 S1a, V-1 and V-2), compiled in W2-nightly S24. The six
// core identities (LiveLaneContract.Core) live in one project since unit 60 of the remaining-ps1 plan,
// tests/Antiphon.LiveClaude.Tests: its compiled discovery is checked against them and the manifest's bound core rows
// (the whole six-entry manifest, each identity local, the C680 node and the submit expansion). The matrix also refuses
// a five-entry manifest, a duplicate, a renamed method, an unexpected, unbound or foreign row, an extra [Arguments]
// expansion, a wrong assembly or hash, a non-zero or diagnostic-less listing, a run instead of a list, and every
// incomplete or foreign producer export. Any other project (Antiphon.Tests, the PTY project: the two that held the
// identities before) must discover none of the core classes' tests.
//
// RunCase is the old ExactCoreRoster / ExactPtyCoreRoster case of scripts/test-live-lane-manifest.ps1:
// LiveLaneInventoryTests.ExactCoreRoster (the live project's staged assembly) and LiveClaudeInventoryTests.ExactPtyCoreRoster
// (the PTY assembly, which must carry none) call it in-process, and
// "antiphon-scripts live-lane-roster" prints its result line. Text compares as PowerShell's -eq did (the invariant
// culture, ignoring case); arguments and categories compare ordinally. Plain C#. ASCII-only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>A core row of tests/live-lanes.json (Copy-LiveLaneEntries).</summary>
    public sealed class LiveLaneEntry
    {
        public string Project { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string Class { get; set; } = "";
        public string Method { get; set; } = "";
        public IReadOnlyList<string> Arguments { get; set; } = new string[0];
        public string Uid { get; set; } = "";
        public IReadOnlyList<string> Categories { get; set; } = new string[0];
        public bool Bound { get; set; }

        /// <summary>Get-LiveLaneIdentityKey.</summary>
        public string Key => LiveLaneGateScan.IdentityKey(Project, Namespace, Class, Method, Arguments);

        public LiveLaneEntry Copy() => (LiveLaneEntry)MemberwiseClone();

        public static LiveLaneEntry FromJson(JsonElement row)
        {
            var bound = LiveLaneJson.Prop(row, "bound");
            return new LiveLaneEntry
            {
                Project = LiveLaneJson.Field(row, "project"),
                Namespace = LiveLaneJson.Field(row, "namespace"),
                Class = LiveLaneJson.Field(row, "class"),
                Method = LiveLaneJson.Field(row, "method"),
                Arguments = LiveLaneJson.Strings(row, "arguments"),
                Uid = LiveLaneJson.Field(row, "uid"),
                Categories = LiveLaneJson.Strings(row, "categories"),
                Bound = bound != null && LiveLaneJson.IsTrue(bound.Value),
            };
        }
    }

    /// <summary>Test-LiveLaneRosterAccepted's { Accepted; Reasons; Count; ContractCount }.</summary>
    public sealed record LiveLaneRosterVerdict(bool Accepted, IReadOnlyList<string> Reasons, int Count, int ContractCount);

    /// <summary>Test-LiveLaneDiscoveryAdmission's { Accepted; Reason; Observed; Bound }.</summary>
    public sealed record LiveLaneAdmission(bool Accepted, string Reason, string Observed, string Bound);

    /// <summary>A roster case's result (Write-LiveLaneCaseResult's line); ExitCode is the case's exit (1: no discovery).</summary>
    public sealed class LiveLaneCaseResult
    {
        public bool DiscoveryOk { get; init; }
        public string Error { get; init; } = "";
        public IReadOnlyList<string> Failures { get; init; } = new string[0];
        public int LaunchCount { get; init; }
        public int DiscoveryMs { get; init; } = -1;
        public int ExportMs { get; init; } = -1;
        public int MatrixMs { get; init; } = -1;
        public int ExitCode { get; init; }

        /// <summary>{"discoveryOk":...,"error":...,"failures":[...],"launchCount":n,"discoveryMs":n,"exportMs":n,"matrixMs":n}; blank failures are left out.</summary>
        public string ToJsonLine()
        {
            var sb = new StringBuilder();
            sb.Append("{\"discoveryOk\":").Append(DiscoveryOk ? "true" : "false");
            sb.Append(",\"error\":").Append(Quote(Error));
            sb.Append(",\"failures\":[");
            var first = true;
            foreach (var failure in Failures ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(failure)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Quote(failure));
            }
            sb.Append("],\"launchCount\":").Append(LaunchCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"discoveryMs\":").Append(DiscoveryMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"exportMs\":").Append(ExportMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"matrixMs\":").Append(MatrixMs.ToString(CultureInfo.InvariantCulture)).Append('}');
            return sb.ToString();
        }

        // ConvertTo-LiveLaneJsonString: \ " CR LF TAB escaped.
        private static string Quote(string text) =>
            "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    }

    public static class LiveLaneRoster
    {
        public const string TestsProject = "Antiphon.Tests";
        public const string PtyProject = "Antiphon.Agents.Pty.Tests";

        /// <summary>The live Claude core lane's project: every core identity, and its namespace (unit 60).</summary>
        public const string LiveClaudeProject = "Antiphon.LiveClaude.Tests";
        private const string SubmitMethod = "Submitting_via_two_writes_completes_a_turn";
        private const string C680Method = "C680_LiveNudgeRefusesPermissionAndQuestionDialogs";

        /// <summary>Copy-LiveLaneEntries of the manifest's core rows.</summary>
        public static List<LiveLaneEntry> Entries(JsonElement manifest)
        {
            var entries = new List<LiveLaneEntry>();
            foreach (var row in LiveLaneJson.Rows(manifest, "core")) entries.Add(LiveLaneEntry.FromJson(row));
            return entries;
        }

        /// <summary>The repository's tests/live-lanes.json core rows (manifest-missing refuses).</summary>
        public static List<LiveLaneEntry> Entries(string repoRoot)
        {
            using var manifest = LiveLaneGateScan.ReadManifest(repoRoot);
            return Entries(manifest.RootElement);
        }

        /// <summary>
        /// Get-LiveLaneMatchingNodes: the nodes of the identity's namespace, class (a node's type, else its class) and
        /// method, of its project when the node names one, with exactly its arguments in order (ordinal).
        /// </summary>
        public static List<LiveLaneNode> MatchingNodes(IEnumerable<LiveLaneNode> nodes, LiveLaneIdentity identity)
        {
            var hits = new List<LiveLaneNode>();
            foreach (var n in nodes ?? new LiveLaneNode[0])
            {
                if (!LiveLaneJson.Same(n.Namespace, identity.Namespace)) continue;
                if (!LiveLaneJson.Same(n.TypeOrClass, identity.Class)) continue;
                if (!LiveLaneJson.Same(n.Method, identity.Method)) continue;
                if (!string.IsNullOrWhiteSpace(n.Project) && !LiveLaneJson.Same(n.Project, identity.Project)) continue;
                if (!SameArguments(n.Arguments, identity.Arguments)) continue;
                hits.Add(n);
            }
            return hits;
        }

        // Test-LiveLaneSameArguments.
        private static bool SameArguments(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            left ??= new string[0];
            right ??= new string[0];
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
                if (!string.Equals(left[i] ?? "", right[i] ?? "", StringComparison.Ordinal)) return false;
            return true;
        }

        // Test-LiveLaneCategoryEqual: the same sets of non-blank categories (ordinal).
        private static bool CategoryEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var a = CategorySet(left);
            var b = CategorySet(right);
            return a.Count == b.Count && a.All(b.Contains);
        }

        private static HashSet<string> CategorySet(IReadOnlyList<string> values)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in values ?? new string[0])
                if (!string.IsNullOrWhiteSpace(c)) set.Add(c);
            return set;
        }

        // Get-LiveLaneArgumentKey.
        private static string ArgumentKey(IReadOnlyList<string> arguments) => string.Join("\n", arguments ?? new string[0]);

        // Sort-Object -Unique of text: the current culture, ignoring case; an item equal to the one before it is dropped.
        private static List<string> SortUnique(IEnumerable<string> values)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var sorted = values.OrderBy(v => v, Comparer<string>.Create((a, b) => compare.Compare(a, b, CompareOptions.IgnoreCase))).ToList();
            var unique = new List<string>();
            foreach (var v in sorted)
                if (unique.Count == 0 || compare.Compare(unique[unique.Count - 1], v, CompareOptions.IgnoreCase) != 0) unique.Add(v);
            return unique;
        }

        /// <summary>
        /// Test-LiveLaneRosterAccepted: the entries (all of them with fullManifest, else the project's) against the core
        /// identities (all, else the project's) and the project's discovered nodes. Reasons, in order and each once:
        /// manifest-uid-bound, manifest-count, duplicate-uid, local-identity, uid-equals-discovery,
        /// category-equals-discovery, exact-set, extra-expansion.
        /// </summary>
        public static LiveLaneRosterVerdict Accepted(IReadOnlyList<LiveLaneEntry> entries, IReadOnlyList<LiveLaneNode> nodes, string project, bool fullManifest)
        {
            nodes ??= new LiveLaneNode[0];
            var contract = LiveLaneContract.Core.Where(c => fullManifest || LiveLaneJson.Same(c.Project, project)).ToList();
            var core = (entries ?? new LiveLaneEntry[0]).Where(e => fullManifest || LiveLaneJson.Same(e.Project, project)).ToList();
            var reasons = new List<string>();
            var allBound = core.All(e => e.Bound && !string.IsNullOrWhiteSpace(e.Uid));
            if (!allBound) reasons.Add("manifest-uid-bound");
            if (core.Count != contract.Count) reasons.Add("manifest-count");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exact = true;
            foreach (var want in contract)
            {
                var key = want.Key;
                var have = core.Where(e => LiveLaneJson.Same(e.Key, key)).ToList();
                if (have.Count != 1) { exact = false; continue; }
                var entry = have[0];
                var uid = entry.Uid ?? "";
                if (!string.IsNullOrWhiteSpace(uid) && seen.Contains(uid)) reasons.Add("duplicate-uid");
                if (!string.IsNullOrWhiteSpace(uid)) seen.Add(uid);
                if (!LiveLaneJson.Same(want.Project, project)) continue;
                var local = MatchingNodes(nodes, want);
                if (local.Count != 1) { reasons.Add("local-identity"); continue; }
                if (allBound)
                {
                    if (!LiveLaneJson.Same(local[0].Uid, uid)) reasons.Add("uid-equals-discovery");
                    if (!CategoryEqual(entry.Categories, local[0].Categories)) reasons.Add("category-equals-discovery");
                }
            }
            if (!exact) reasons.Add("exact-set");
            var expansionExtra = false;
            foreach (var want in contract)
            {
                if (!LiveLaneJson.Same(want.Project, project)) continue;
                var observed = SortUnique(nodes
                    .Where(n => LiveLaneJson.Same(n.Namespace, want.Namespace) && LiveLaneJson.Same(n.Type, want.Class) && LiveLaneJson.Same(n.Method, want.Method))
                    .Select(n => ArgumentKey(n.Arguments)));
                var wanted = SortUnique(contract
                    .Where(c => LiveLaneJson.Same(c.Class, want.Class) && LiveLaneJson.Same(c.Method, want.Method) && LiveLaneJson.Same(c.Project, project))
                    .Select(c => ArgumentKey(c.Arguments)));
                if (!LiveLaneJson.Same(string.Join("|", observed), string.Join("|", wanted))) expansionExtra = true;
            }
            if (expansionExtra) reasons.Add("extra-expansion");
            var unique = reasons.Distinct(StringComparer.Ordinal).ToList();
            return new LiveLaneRosterVerdict(unique.Count == 0, unique, core.Count, contract.Count);
        }

        // Get-LiveLaneLocalCoreCount.
        private static int LocalCoreCount(IReadOnlyList<LiveLaneNode> nodes, string project) =>
            LiveLaneContract.Core.Where(c => LiveLaneJson.Same(c.Project, project)).Sum(want => MatchingNodes(nodes, want).Count);

        /// <summary>
        /// Test-LiveLaneSubmitExtraExpansion: with the project's one submit node cloned under other arguments (null:
        /// "other"; an empty list: "other"), the project's roster reports extra-expansion.
        /// </summary>
        public static bool SubmitExtraExpansion(string project, IReadOnlyList<LiveLaneNode> nodes, IReadOnlyList<LiveLaneEntry> entries, IReadOnlyList<string> cloneArguments = null)
        {
            var extraArguments = new List<string> { "other" };
            if (cloneArguments != null)
            {
                extraArguments.Clear();
                foreach (var item in cloneArguments)
                    if (item != null) extraArguments.Add(item);
                if (extraArguments.Count == 0) extraArguments.Add("other");
            }
            var extraNodes = new List<LiveLaneNode>(nodes ?? new LiveLaneNode[0]);
            var wanted = LiveLaneContract.Core.Where(c => LiveLaneJson.Same(c.Project, project) && LiveLaneJson.Same(c.Method, SubmitMethod)).ToList();
            if (wanted.Count == 1)
            {
                var hit = MatchingNodes(extraNodes, wanted[0]);
                if (hit.Count == 1)
                {
                    var submit = hit[0];
                    extraNodes.Add(new LiveLaneNode
                    {
                        Project = project,
                        Uid = submit.Uid + "-extra",
                        Namespace = submit.Namespace,
                        Type = submit.Type,
                        Method = submit.Method,
                        DisplayName = SubmitMethod + "(" + string.Join(",", extraArguments) + ")",
                        Arguments = extraArguments.ToArray(),
                        Signature = submit.Signature,
                        Categories = submit.Categories,
                    });
                }
            }
            return Accepted(entries, extraNodes, project, fullManifest: false).Reasons.Contains("extra-expansion");
        }

        /// <summary>Test-LiveLaneDiscoveryAdmission: nonzero-exit, missing-diag (no log or no nodes), wrong-assembly-hash (last wins).</summary>
        public static LiveLaneAdmission DiscoveryAdmission(int exitCode, bool diagExists, string observedHash, string boundHash, int nodeCount)
        {
            var reason = "";
            var accepted = true;
            if (exitCode != 0) { accepted = false; reason = "nonzero-exit"; }
            if (!diagExists) { accepted = false; if (reason.Length == 0) reason = "missing-diag"; }
            if (nodeCount <= 0) { accepted = false; if (reason.Length == 0) reason = "missing-diag"; }
            if (!string.Equals(observedHash ?? "", boundHash ?? "", StringComparison.OrdinalIgnoreCase)) { accepted = false; reason = "wrong-assembly-hash"; }
            return new LiveLaneAdmission(accepted, reason, observedHash ?? "", boundHash ?? "");
        }

        private static LiveLaneExport Export(string project, string source, string run, string hash, bool terminal, IReadOnlyList<LiveLaneNode> nodes) => new LiveLaneExport
        {
            Project = project,
            Cp = "CP-25",
            SourceSha = source,
            RunId = run,
            AssemblyHash = hash,
            Terminal = terminal,
            Nodes = nodes,
        };

        /// <summary>
        /// Invoke-LiveLaneRosterMatrix: the failed checks of the project's roster; none when it is exact and every refusal
        /// holds. For the live Claude project: the manifest, duplicate, rename, unexpected, unbound, foreign and export
        /// checks ("C1545 V-1/...") and the expansion, assembly, listing and producer checks ("C1545 V-2/..."), which were
        /// the Antiphon.Tests and PTY rosters while the identities lived there. For any other project: "C1545
        /// V-2/moved-core" when its discovery still has a test of a core class. context: the portable context (null:
        /// none). launchCount: the transport's admitted launches (the run-not-list probe must not add one).
        /// </summary>
        public static List<string> Matrix(string project, IReadOnlyList<LiveLaneNode> nodes, IReadOnlyList<LiveLaneEntry> manifestCore, LiveLaneContext context,
            string assemblyHash, string assemblyPath, Func<int> launchCount = null)
        {
            nodes ??= new LiveLaneNode[0];
            assemblyHash ??= "";
            assemblyPath ??= "";
            var failures = new List<string>();
            if (!LiveLaneJson.Same(project, LiveClaudeProject))
            {
                var moved = nodes.Where(n => LiveLaneContract.Core.Any(c => LiveLaneJson.Same(c.Class, n.TypeOrClass) && LiveLaneJson.Same(c.Method, n.Method)))
                    .Select(n => n.Uid).ToList();
                if (moved.Count > 0) failures.Add("C1545 V-2/moved-core " + string.Join(",", moved));
                return failures;
            }
            // The whole manifest is this project's: every core identity is local.
            const string prefix = "V-1";
            const bool full = true;
            var core = (manifestCore ?? new LiveLaneEntry[0]).Select(e => e.Copy()).ToList();
            var projectCore = core.Where(e => LiveLaneJson.Same(e.Project, project)).ToList();
            var pool = core;
            List<LiveLaneEntry> CopyPool() => pool.Select(e => e.Copy()).ToList();
            var eval = Accepted(pool, nodes, project, full);
            if (!eval.Accepted)
            {
                foreach (var reason in eval.Reasons)
                {
                    if (reason == "manifest-uid-bound") failures.Add("C1545 " + prefix + "/valid manifest-uid-bound");
                    else if (reason == "manifest-count" || reason == "exact-set") failures.Add("C1545 " + prefix + "/valid exact-set");
                    else if (reason == "uid-equals-discovery") failures.Add("C1545 " + prefix + "/valid uid-equals-discovery");
                    else if (reason == "category-equals-discovery") failures.Add("C1545 " + prefix + "/valid category-equals-discovery");
                    else if (reason == "extra-expansion") failures.Add("C1545 " + prefix + "/valid extra-expansion");
                    else failures.Add("C1545 " + prefix + "/valid " + reason);
                }
            }

            var five = pool.Take(Math.Max(0, pool.Count - 1)).ToList();
            var fiveEval = Accepted(five, nodes, project, full);
            var expectedCount = LiveLaneContract.Core.Count;
            if (fiveEval.Accepted || fiveEval.Count == expectedCount) failures.Add("C1545 " + prefix + "/five manifest.count == " + expectedCount);

            var dup = CopyPool();
            if (dup.Count > 0) dup.Add(dup[0]);
            if (Accepted(dup, nodes, project, full).Accepted) failures.Add("C1545 " + prefix + "/duplicate exact-set");

            var renamed = CopyPool();
            if (renamed.Count > 0) renamed[0].Method = renamed[0].Method + "_renamed";
            if (MatchingNodes(nodes, LiveLaneContract.Core.First(c => LiveLaneJson.Same(c.Method, C680Method))).Count != 1)
                failures.Add("C1545 V-1/renamed localC680Matches == 1");
            if (MatchingNodes(nodes, LiveLaneContract.Core.First(c => LiveLaneJson.Same(c.Method, SubmitMethod))).Count != 1)
                failures.Add("C1545 V-2/renamed localSubmitMatches == 1");
            if (Accepted(renamed, nodes, project, full).Accepted) failures.Add("C1545 " + prefix + "/renamed exact-set");

            // The manifest refusals (the Antiphon.Tests roster's V-1 cases).
            {
                var unexpected = CopyPool();
                unexpected.Add(new LiveLaneEntry { Project = project, Namespace = "Antiphon.Tests.Scripts", Class = "NotALiveLane", Method = "Unexpected", Uid = "unexpected", Bound = true });
                if (Accepted(unexpected, nodes, project, true).Accepted) failures.Add("C1545 V-1/unexpected exact-set");

                var unbound = CopyPool();
                if (unbound.Count > 0) { unbound[0].Uid = ""; unbound[0].Bound = false; }
                if (!Accepted(unbound, nodes, project, true).Reasons.Contains("manifest-uid-bound")) failures.Add("C1545 V-1/unbound-uid manifest-uid-bound");

                var foreign = CopyPool();
                if (foreign.Count > 0) foreign[0].Project = "Foreign.Tests";
                if (Accepted(foreign, nodes, project, true).Accepted) failures.Add("C1545 V-1/foreign-project exact-set");
            }
            // The expansion, assembly, listing and producer refusals (the PTY roster's V-2 cases).
            {
                if (!SubmitExtraExpansion(project, nodes, projectCore)) failures.Add("C1545 V-2/extra-argument expandedUidSet has no extra argument");

                var wrong = projectCore.Select(e => e.Copy()).ToList();
                foreach (var row in wrong) row.Project = "Wrong.Assembly";
                var wrongEval = Accepted(wrong, nodes, project, false);
                if (wrongEval.Accepted || LocalCoreCount(nodes, project) != LiveLaneContract.Core.Count) failures.Add("C1545 V-2/wrong-assembly localCoreSet == expectedSix");

                var hashProbe = DiscoveryAdmission(0, true, new string('0', 64), assemblyHash, nodes.Count);
                if (hashProbe.Accepted || hashProbe.Observed == hashProbe.Bound) failures.Add("C1545 V-2/wrong-assembly-hash observedAssemblyHash == boundBuildAssemblyHash");
                if (DiscoveryAdmission(7, true, assemblyHash, assemblyHash, nodes.Count).Accepted) failures.Add("C1545 V-2/nonzero-exit producerAccepted == false");
                var diagProbe = DiscoveryAdmission(0, false, assemblyHash, assemblyHash, 0);
                if (diagProbe.Accepted || diagProbe.Reason != "missing-diag") failures.Add("C1545 V-2/missing-diag producerAccepted == false");

                var before = launchCount?.Invoke() ?? 0;
                var refused = !LiveLaneDiscovery.ListOnly("dotnet", new[] { "exec", assemblyPath, "--report-trx" }, assemblyPath);
                var after = launchCount?.Invoke() ?? 0;
                if (!refused || after != before) failures.Add("C1545 V-2/run-not-list launchCount == 0");

                var partial = LiveLaneExports.Check(Export(project, "abc", "run", assemblyHash, true, new LiveLaneNode[0]), "abc", "run", assemblyHash, project, true);
                if (partial.Accepted || partial.Reason != "producer-incomplete") failures.Add("C1545 V-2/partial-export producerAccepted == false");
            }

            // The export refusals, against an export of this project.
            var exportRequired = context != null && context.ExportRequired;
            {
                var source = "local-source";
                var run = "local-run";
                var hash = assemblyHash.Length > 0 ? assemblyHash : "local-hash";
                if (context != null && exportRequired)
                {
                    source = context.SourceSha;
                    run = context.RunId;
                }
                var healthyNodes = nodes.Take(1).ToList();
                var healthy = LiveLaneExports.Check(Export(project, source, run, hash, true, healthyNodes), source, run, hash, project, false);
                if (!healthy.Accepted) failures.Add("C1545 V-1/valid exportAccepted");
                var pairs = new (string Id, LiveLaneExport Export, string Reason)[]
                {
                    ("missing", null, "producer-incomplete"),
                    ("partial", Export(project, source, run, hash, true, new LiveLaneNode[0]), "producer-incomplete"),
                    ("pending", Export(project, source, run, hash, false, healthyNodes), "producer-incomplete"),
                    ("wrong-source", Export(project, "other-source", run, hash, true, healthyNodes), "source-mismatch"),
                    ("wrong-build", Export(project, source, run, new string('f', 64), true, healthyNodes), "build-mismatch"),
                    ("wrong-run", Export(project, source, "other-run", hash, true, healthyNodes), "run-mismatch"),
                };
                foreach (var pair in pairs)
                {
                    var probe = LiveLaneExports.Check(pair.Export, source, run, hash, project, false);
                    if (probe.Accepted || probe.Reason != pair.Reason) failures.Add("C1545 V-1/" + pair.Id + " exportAccepted == false; reason=" + pair.Reason);
                }
                // The roster that writes the core export (RunCase, candidate mode) cannot also consume it: it is pending
                // until this matrix is clean. The two-producer consumption ended when the identities moved into one project.
            }
            return failures;
        }

        /// <summary>
        /// The roster case (Invoke-LiveLaneRosterCase): discovers the project's assembly into captureDir (blank:
        /// &lt;repo&gt;/.antiphon/live-lane-capture), writes the row's pending export in candidate export mode (CP-25 for the
        /// PTY project, CP-1 otherwise), runs the matrix, and marks the export terminal only when the discovery was
        /// accepted and the matrix is clean. Each phase is appended to captureDir/phase.log ("phase=&lt;name&gt;
        /// elapsedMs=&lt;n&gt;") and written to phases when given. context null: the one ANTIPHON_LIVE_PORTABLE_CONTEXT names.
        /// A discovery that throws or an assembly that is missing answers DiscoveryOk false and exit 1.
        /// </summary>
        public static LiveLaneCaseResult RunCase(string repoRoot, string project, string assemblyPath, string captureDir = null, LiveLaneContext context = null,
            TextWriter phases = null, INightlyLauncher inner = null)
        {
            repoRoot = Path.GetFullPath(repoRoot);
            var capture = string.IsNullOrWhiteSpace(captureDir) ? Path.Combine(repoRoot, ".antiphon", "live-lane-capture") : captureDir;
            var clock = Stopwatch.StartNew();
            Phase(capture, phases, "start", 0);
            LiveLaneDiscoveryResult discovery;
            try
            {
                discovery = LiveLaneDiscovery.Run(repoRoot, project, assemblyPath, capture, inner);
            }
            catch (Exception e)
            {
                Phase(capture, phases, "discovery-failed", (int)clock.ElapsedMilliseconds);
                return new LiveLaneCaseResult { DiscoveryOk = false, Error = e.Message, DiscoveryMs = (int)clock.ElapsedMilliseconds, ExitCode = 1 };
            }
            var discoveryMs = (int)clock.ElapsedMilliseconds;
            Phase(capture, phases, "discovery", discoveryMs);
            if (LiveLaneJson.Same(discovery.Error, "assembly-missing"))
                return new LiveLaneCaseResult { DiscoveryOk = false, Error = discovery.Error, DiscoveryMs = discoveryMs, ExitCode = 1 };
            var manifestCore = Entries(repoRoot);
            context ??= LiveLaneContext.FromEnvironment();
            var exportPathWritten = "";
            if (context != null && LiveLaneJson.Same(context.Mode, "candidate") && context.ExportRequired)
            {
                var pty = LiveLaneJson.Same(project, PtyProject);
                // The live project's roster is LiveLaneInventoryTests.ExactCoreRoster, the CP-1 row; the PTY one stays CP-25.
                var exportPath = pty ? context.Cp25Export : context.Cp1Export;
                if (!string.IsNullOrWhiteSpace(exportPath))
                {
                    // Pending until the roster checks pass. A killed or red row must not look final.
                    LiveLaneExports.Write(exportPath, project, pty ? "CP-25" : "CP-1", context.SourceSha, context.RunId, discovery.AssemblyHash, discovery.AssemblyPath, discovery.Nodes);
                    exportPathWritten = exportPath;
                }
            }
            var exportMs = (int)clock.ElapsedMilliseconds;
            Phase(capture, phases, "export", exportMs);
            var launches = discovery.LaunchCount;
            var failures = Matrix(project, discovery.Nodes, manifestCore, context, discovery.AssemblyHash, discovery.AssemblyPath, () => launches);
            var matrixMs = (int)clock.ElapsedMilliseconds;
            Phase(capture, phases, "matrix", matrixMs);
            if (exportPathWritten.Length > 0) LiveLaneExports.Complete(exportPathWritten, failures.Count == 0 && discovery.Ok);
            Phase(capture, phases, "final", (int)clock.ElapsedMilliseconds);
            return new LiveLaneCaseResult
            {
                DiscoveryOk = discovery.Ok,
                Error = discovery.Error,
                Failures = failures,
                LaunchCount = discovery.LaunchCount,
                DiscoveryMs = discoveryMs,
                ExportMs = exportMs,
                MatrixMs = matrixMs,
                ExitCode = 0,
            };
        }

        // Write-LiveLanePhase.
        private static void Phase(string capture, TextWriter phases, string name, int elapsedMs)
        {
            var line = "phase=" + name + " elapsedMs=" + elapsedMs.ToString(CultureInfo.InvariantCulture);
            if (phases != null)
            {
                phases.WriteLine(line);
                phases.Flush();
            }
            if (string.IsNullOrWhiteSpace(capture)) return;
            Directory.CreateDirectory(capture);
            File.AppendAllText(Path.Combine(capture, "phase.log"), line + "\n");
        }

        /// <summary>
        /// The old ExtraArgumentDecoyFirst case: the submit expansion check finds the live submit node behind a decoy of
        /// another class listed first (the default "other" clone expands, a clone under the live argument does not), and
        /// a diagnostic record's display arguments parse the same with LF and CRLF line ends ("claude"), none once the
        /// argument is removed. The diagnostic records are written to owned scratch. None: accepted.
        /// </summary>
        public static IReadOnlyList<string> ExtraArgumentDecoyFirstWitness(string repoRoot)
        {
            var failures = new List<string>();
            var entries = Entries(repoRoot).Where(e => LiveLaneJson.Same(e.Project, LiveClaudeProject)).ToList();
            var nodes = new[]
            {
                new LiveLaneNode
                {
                    Project = LiveClaudeProject, Uid = "decoy-submit", Namespace = LiveClaudeProject, Type = "ClaudeSubmitContractTests", Method = SubmitMethod,
                    DisplayName = SubmitMethod + "(fakeclaude)", Arguments = new[] { "fakeclaude" }, Categories = new[] { "Pty" },
                },
                new LiveLaneNode
                {
                    Project = LiveClaudeProject, Uid = "live-submit", Namespace = LiveClaudeProject, Type = "ClaudeSubmitContractLiveTests", Method = SubmitMethod,
                    DisplayName = SubmitMethod + "(claude)", Arguments = new[] { "claude" }, Categories = new[] { "Pty" },
                },
            };
            if (!SubmitExtraExpansion(LiveClaudeProject, nodes, entries)) failures.Add("C1545 V-2/extra-argument expandedUidSet has no extra argument");
            if (SubmitExtraExpansion(LiveClaudeProject, nodes, entries, new[] { "claude" })) failures.Add("C1545 V-2/extra-argument same argument still expanded");

            var scratch = new LiveLaneScratch();
            try
            {
                var dir = scratch.NewOwned(repoRoot, "c1545display" + Guid.NewGuid().ToString("N"));
                var lfPath = Path.Combine(dir, "lf.diag");
                var crlfPath = Path.Combine(dir, "crlf.diag");
                const string line = "TestNodeUid { Value = uid-submit }, DisplayName = Submitting_via_two_writes_completes_a_turn(claude), Properties = [ ParameterTypeFullNames = [System.String] ]";
                File.WriteAllText(lfPath, line + "\n");
                File.WriteAllText(crlfPath, line + "\r\n");
                string Parsed(string path) =>
                    LiveLaneDiscovery.DisplayMap(path).TryGetValue("uid-submit", out var shown) ? string.Join("|", LiveLaneDiscovery.ParsedArguments(SubmitMethod, shown.Display)) : "";
                var lf = Parsed(lfPath);
                var crlf = Parsed(crlfPath);
                if (!LiveLaneJson.Same(lf, "claude") || !LiveLaneJson.Same(crlf, "claude")) failures.Add("C1545 V-2/display lf=" + lf + " crlf=" + crlf);
                File.WriteAllText(crlfPath, line.Replace("(claude)", "()") + "\r\n");
                if (Parsed(crlfPath).Length != 0) failures.Add("C1545 V-2/display removed argument still parsed");
            }
            finally
            {
                scratch.Clear();
            }
            return failures;
        }
    }
}
