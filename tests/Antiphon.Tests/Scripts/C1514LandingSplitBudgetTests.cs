using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The launch-budget cases of scripts/test-nightly-tests.ps1 (C1584, C1577, C1404, C1410, G2865), in-process since
/// W2-nightly S11: the real class costs, the shipped policy and the committed listings planned by NightlyTestsPlan, the
/// emitted filters evaluated against the whole fixture. Each keeps its one row name.
/// </summary>
[Category("Integration")]
public sealed class C1514LandingSplitBudgetTests
{
    private static readonly string Trx = Path.Combine(Path.GetTempPath(), "c1514-budget", "budget.trx");
    private static readonly string Diag = Path.Combine(Path.GetTempPath(), "c1514-budget");

    [Test]
    public void C1584_E2EExpandedBudget()
    {
        var rows = new NightlyCaseRows();
        var ok = false;
        string detail;
        try
        {
            // Source-derived planner census. No E2E assembly or fixture is executed.
            var nodes = new List<PlainObject>();
            var roster = new List<string>();
            var e2e = Path.Combine(Repo, "tests", "Antiphon.E2E");
            var excluded = new Regex(@"[/\\](bin[^/\\]*|obj)[/\\]", RegexOptions.IgnoreCase);
            var files = Directory.EnumerateFiles(e2e, "*.cs", SearchOption.AllDirectories).Where(f => !excluded.IsMatch(f)).ToList();
            files.Add(Path.Combine(Repo, "tests", "Shared", "TestClassificationGuardTests.cs"));
            var methodPattern = new Regex(@"((?:\s*\[[^\]]+\])+)\s*public (?:async )?(?:Task(?:<[^>]+>)?|void) (\w+)\(");
            var classPattern = new Regex(@"public (?:(?:sealed|partial|abstract) )*class (\w+)");
            var argumentPattern = new Regex(@"\[Arguments\((.*?)\)\]");
            foreach (var file in files)
            {
                var source = File.ReadAllText(file);
                var ns = Regex.Match(source, @"namespace ([\w.]+)").Groups[1].Value;
                foreach (Match method in methodPattern.Matches(source))
                {
                    if (!Regex.IsMatch(method.Groups[1].Value, @"\[Test\]", RegexOptions.IgnoreCase)) continue;
                    var classes = classPattern.Matches(source.Substring(0, method.Index));
                    if (classes.Count == 0) throw new InvalidOperationException("source-census-class");
                    var type = classes[classes.Count - 1].Groups[1].Value;
                    var name = method.Groups[2].Value;
                    var bare = ns + "." + type;
                    var arguments = argumentPattern.Matches(method.Groups[1].Value).Select(m => m.Groups[1].Value).ToList();
                    if (arguments.Count == 0) arguments.Add("");
                    foreach (var argument in arguments)
                    {
                        // Batch wrappers retain the original matrix's argument identity.
                        roster.Add(bare + "|" + Regex.Replace(name, @"_Batch\d+$", "", RegexOptions.IgnoreCase) + "|" + argument);
                        nodes.Add(Node(bare, type, name, bare + "|" + name + "|" + argument, "OptIn"));
                    }
                }
            }
            var expected = File.ReadAllLines(Path.Combine(Repo, "scripts", "fixtures", "nightly", "c1584-e2e-source-roster.txt"))
                .Where(l => l.Length > 0 && !l.StartsWith('#')).OrderBy(s => s, SortObject).ToArray();
            var actual = roster.OrderBy(s => s, SortObject).ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                // Name the drift so a qualification failure says which identities to refresh.
                var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
                var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
                throw new InvalidOperationException("expanded-source-roster-changed: added=" + string.Join(";", actual.Where(a => !expectedSet.Contains(a)))
                    + " removed=" + string.Join(";", expected.Where(e => !actualSet.Contains(e))));
            }
            var policy = Policy();
            var exclusions = Exclusions(policy, "rc", "e2e", "windows");
            var required = nodes.Where(n => !NightlyCensus.Excluded(n, exclusions, true)).ToArray();
            var reasons = new List<string>();
            // Exact census of the committed roster, including the C467 checkout SHA probe and the review engine
            // browser E2E. Windows rc excludes seven.
            if (nodes.Count != 197 || required.Length != 190) reasons.Add("source-census");
            // Independent conservative per-expanded-case bounds. 600 s is 1.72x the slowest supplied Windows
            // observation (348.1 s), not a p95.
            var bounds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var costRows = CostRows();
            foreach (var group in nodes.GroupBy(n => (string)n["ClassName"]!, StringComparer.OrdinalIgnoreCase))
            {
                var bare = group.Key;
                var unit = 300.0;
                if (Regex.IsMatch(bare, "(AgentTaskLandDelivery|DispatchBaseWarningDelivery)E2ETests$", RegexOptions.IgnoreCase)) unit = 600.0;
                else if (Regex.IsMatch(bare, "WorktreeRetirementDeliveryE2ETests$", RegexOptions.IgnoreCase)) unit = 500.0;
                else if (Regex.IsMatch(bare, "(OutputDistillationCanaryGuardTests|LandDeliveryFixtureProbeTests|PtyBackendEnvGuardTests|TestClassificationGuardTests)$", RegexOptions.IgnoreCase)) unit = 10.0;
                bounds[bare] = unit;
                if (!costRows.TryGetValue(bare, out var row) || RowInt(row, "nodes") != group.Count() || RowDouble(row, "seconds") != unit * group.Count()
                    || !RowText(row, "source").Contains("CARD-1584", StringComparison.Ordinal)) reasons.Add("cost-row:" + bare);
            }
            var chunks = NightlyTestsPlan.SuiteChunks(nodes.Cast<object>().ToArray(), AllChunk(), exclusions, true, true, "antiphon", null, Costs());
            var covered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var maxSeconds = 0.0;
            var shapes = new List<string>();
            foreach (var chunk in chunks)
            {
                var filter = Filter(Argv(chunk, Trx, Diag)) ?? throw new InvalidOperationException("missing emitted filter");
                var segments = filter.Split('/');
                if (segments.Length != 5) throw new InvalidOperationException("invalid emitted filter shape");
                var hits = nodes.Where(node =>
                {
                    var className = (string)node["ClassName"]!;
                    var classHit = segments[3].Split('|').Any(op => Like((string)node["Type"]!, op.Trim('(', ')')));
                    var methodHit = segments[4].Split('|').Any(op => Like((string)node["Method"]!, op.Trim('(', ')')));
                    return Like(className.Substring(0, className.LastIndexOf('.')), segments[2]) && classHit && methodHit;
                }).ToList();
                if (hits.Count == 0) throw new InvalidOperationException("empty emitted selection");
                var seconds = 0.0;
                foreach (var node in hits)
                {
                    if (NightlyCensus.Excluded(node, exclusions, true)) reasons.Add("selected-exclusion");
                    seconds += bounds[(string)node["ClassName"]!];
                    var uid = (string)node["Uid"]!;
                    covered[uid] = covered.GetValueOrDefault(uid) + 1;
                }
                if (seconds >= 1800.01) reasons.Add("over-budget:" + Str(chunk, "id"));
                if (Math.Abs(seconds - Num(chunk, "seconds")) > 0.11) reasons.Add("underpriced:" + Str(chunk, "id"));
                maxSeconds = Math.Max(maxSeconds, seconds);
                shapes.Add(Str(chunk, "id") + ":" + hits.Count + "/" + seconds.ToString(CultureInfo.InvariantCulture) + "s");
            }
            if (covered.Count != required.Length || covered.Values.Any(v => v != 1)) reasons.Add("census");
            foreach (var node in required) if (!covered.ContainsKey((string)node["Uid"]!)) reasons.Add("missing-node");
            var watchdog = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(policy.Object, "e2e", 0);
            if (watchdog != 7200000) reasons.Add("watchdog");
            // A future four-row method cannot silently defeat the E2E ceiling.
            var oversize = Enumerable.Range(1, 4).Select(i => (object)Node("Antiphon.E2E.AgentTaskLandDeliveryE2ETests", "AgentTaskLandDeliveryE2ETests",
                "Unsplittable", "oversize-" + i, "OptIn")).ToArray();
            var refusal = Refusal(() => NightlyTestsPlan.ProfileLaunchPlan(oversize, AllChunk(), exclusions, "antiphon", null, Costs()));
            if (refusal == null || !Like(refusal, "launch-estimate-ceiling*ceiling=1800")) reasons.Add("oversized-method-not-refused");
            detail = "required=" + required.Length + " excluded=" + (nodes.Count - required.Length) + " launches=" + chunks.Length + " maxBoundSeconds="
                + maxSeconds.ToString(CultureInfo.InvariantCulture) + " watchdog=" + watchdog + " groups=" + string.Join(",", shapes) + " reasons=" + string.Join(",", reasons);
            ok = reasons.Count == 0;
        }
        catch (Exception ex) { detail = "setup-error: " + ex.Message; }
        rows.Check(ok, "C1584 E2E expanded cases fit conservative launch bounds with every required node once", detail);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C1577_Group71MeasuredBudget()
    {
        // Completed-case sums from Windows a779085c, not p95 or standalone wall times.
        var rows = new NightlyCaseRows();
        var ok = false;
        string detail;
        try
        {
            var observations = new Dictionary<string, (int Count, double Seconds)>(StringComparer.OrdinalIgnoreCase)
            {
                ["RollingVolumeRecycleScriptTests"] = (27, 993.855), ["RunCheckpointScriptTests"] = (26, 811.425),
                ["EvidenceDeletionGuardTests"] = (6, 344.007), ["ReleaseGatePublicationAuthorityTests"] = (15, 339.016),
                ["RunCheckpointSourceScriptTests"] = (6, 223.116), ["RetiredTempContainerScriptTests"] = (13, 124.8),
                ["EvidenceDiffGuardTests"] = (19, 119.9), ["EvidenceApprovedFixtureGuardTests"] = (7, 103.7),
                ["BuildSlotScriptTests"] = (19, 100.8), ["EvidenceSupplementalDeletionGuardTests"] = (10, 75.9),
            };
            var nodes = new List<PlainObject>();
            var bounds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var reasons = new List<string>();
            var costRows = CostRows();
            foreach (var type in observations.Keys.OrderBy(s => s, SortObject))
            {
                var sample = observations[type];
                var bare = "Antiphon.Tests.Scripts." + type;
                bounds[bare] = Math.Ceiling(sample.Seconds * 1.75 / 10) * 10;
                // Planner-only nodes, full class counts. Whole-class groups must select every method, so synthetic
                // method names suffice here.
                for (var i = 0; i < sample.Count; i++)
                    nodes.Add(type.StartsWith("Evidence", StringComparison.OrdinalIgnoreCase)
                        ? Node(bare, type, "M" + i, bare + "." + i, "Slow")
                        : Node(bare, type, "M" + i, bare + "." + i));
                if (!costRows.TryGetValue(bare, out var row) || RowInt(row, "nodes") != sample.Count || RowDouble(row, "seconds") != bounds[bare]
                    || RowText(row, "basis") != "observed-bound" || !RowText(row, "source").Contains("a779085c09ec4397a4b49941fd3cad07", StringComparison.Ordinal))
                    reasons.Add("cost-row:" + type);
            }
            var policy = Policy();
            var exclusions = Exclusions(policy, "rc", "antiphon", "windows");
            if (exclusions.Length == 0) throw new InvalidOperationException("missing rc exclusions");
            var chunks = NightlyTestsPlan.SuiteChunks(nodes.Cast<object>().ToArray(), AllChunk(), exclusions, true, true, "antiphon", null, Costs());
            var covered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var maxSeconds = 0.0;
            var shapes = new List<string>();
            foreach (var chunk in chunks)
            {
                var filter = Filter(Argv(chunk, Trx, Diag)) ?? throw new InvalidOperationException("missing emitted filter");
                var segments = filter.Split('/');
                if (segments.Length != 5 || segments[1] != "*" || segments[4] != "*") throw new InvalidOperationException("unexpected class filter: " + filter);
                var hits = nodes.Where(node =>
                {
                    var className = (string)node["ClassName"]!;
                    var match = segments[3].Split('|').Any(op => Like((string)node["Type"]!, op.Trim('(', ')')));
                    return Like(className.Substring(0, className.LastIndexOf('.')), segments[2]) && match;
                }).ToList();
                if (hits.Count == 0) throw new InvalidOperationException("empty emitted selection");
                var seconds = 0.0;
                foreach (var node in hits)
                {
                    seconds += bounds[(string)node["ClassName"]!] / observations[(string)node["Type"]!].Count;
                    var uid = (string)node["Uid"]!;
                    covered[uid] = covered.GetValueOrDefault(uid) + 1;
                }
                var types = hits.Select(n => (string)n["Type"]!).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (seconds > 1800.01 || (types > 1 && seconds > 900.01)) reasons.Add("over-budget:" + Str(chunk, "id"));
                if (Math.Abs(seconds - Num(chunk, "seconds")) > 0.11) reasons.Add("underpriced:" + Str(chunk, "id"));
                maxSeconds = Math.Max(maxSeconds, seconds);
                shapes.Add(Str(chunk, "id") + ":" + hits.Count + "/" + Math.Round(seconds, 1).ToString(CultureInfo.InvariantCulture) + "s");
            }
            if (covered.Count != nodes.Count || covered.Values.Any(v => v != 1)) reasons.Add("census");
            var watchdog = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(policy.Object, "antiphon", 0);
            if (watchdog != 3600000) reasons.Add("watchdog");
            detail = "nodes=" + nodes.Count + " launches=" + chunks.Length + " maxBoundSeconds=" + Math.Round(maxSeconds, 1).ToString(CultureInfo.InvariantCulture)
                + " watchdog=" + watchdog + " groups=" + string.Join(",", shapes) + " reasons=" + string.Join(",", reasons);
            ok = reasons.Count == 0;
        }
        catch (Exception ex) { detail = "setup-error: " + ex.Message; }
        rows.Check(ok, "C1577 group 71 uses observed bounds within half the unchanged watchdog with every node once", detail);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C1404_Group10MeasuredBudget()
    {
        // Independent R/P evidence prices; Publication is a partial-sample pace bound. Exercise the real cost loader, rc
        // policy and packer against its exact S3 projection.
        var rows = new NightlyCaseRows();
        var ok = false;
        string detail;
        try
        {
            var expected = new Dictionary<string, (int Nodes, double Seconds, string Basis, int Launches)>(StringComparer.OrdinalIgnoreCase)
            {
                ["AgentTaskLandCheckpointMatrixTests"] = (41, 1560, "pace", 1),
                ["AgentTaskLandCheckpointWorkerTests"] = (2, 1560, "pace", 1),
                ["AgentTaskLandPersistenceFailureTests"] = (52, 1980, "measured", 1),
                ["AgentTaskLandPublicationTests"] = (63, 3780, "pace", 5),
            };
            static List<PlainObject> ReadNodes(string listing)
            {
                var nodes = new List<PlainObject>();
                var entryShape = new Regex("^([A-Za-z_][A-Za-z0-9_]*)=([1-9][0-9]*)$");
                foreach (var line in File.ReadAllLines(listing))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                    var fields = line.Split('|');
                    if (fields.Length != 3 || (fields[1] != "S" && fields[1] != "-")) throw new InvalidOperationException("invalid fixture row");
                    var className = fields[0];
                    var dot = className.LastIndexOf('.');
                    var type = className.Substring(dot + 1);
                    foreach (var entry in fields[2].Split(','))
                    {
                        var m = entryShape.Match(entry);
                        if (!m.Success) throw new InvalidOperationException("invalid fixture method");
                        var cases = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        for (var k = 0; k < cases; k++)
                        {
                            var node = fields[1] == "S" ? Node(className, type, m.Groups[1].Value, className + "." + m.Groups[1].Value + "." + k, "Slow")
                                : Node(className, type, m.Groups[1].Value, className + "." + m.Groups[1].Value + "." + k);
                            node["Namespace"] = className.Substring(0, dot);
                            nodes.Add(node);
                        }
                    }
                }
                return nodes;
            }
            var fixtures = Path.Combine(Repo, "scripts", "fixtures", "nightly");
            var historical = ReadNodes(Path.Combine(fixtures, "c1404-group10-listing.txt"));
            var nodes = ReadNodes(Path.Combine(fixtures, "c1514-checkpoint-split-listing.txt"));
            // Preserve the raw evidence oracle and prove the projection is a bijection.
            var oldMatrix = historical.Count(n => (string)n["Type"]! == "AgentTaskLandCheckpointMatrixTests");
            if (historical.Count != 158 || oldMatrix != 43) throw new InvalidOperationException("historical fixture census");
            var historicalIds = historical.Select(n => (string)n["Uid"]!).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var mappedIds = nodes.Select(node =>
            {
                if ((string)node["Type"]! != "AgentTaskLandCheckpointWorkerTests") return (string)node["Uid"]!;
                if ((string)node["Method"]! != "C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop") throw new InvalidOperationException("unexpected moved method");
                return ((string)node["Uid"]!).Replace(".AgentTaskLandCheckpointWorkerTests.", ".AgentTaskLandCheckpointMatrixTests.", StringComparison.Ordinal);
            }).ToArray();
            if (mappedIds.Length != 158 || mappedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 158
                || !historicalIds.SequenceEqual(mappedIds.OrderBy(s => s, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidOperationException("historical-to-split mapping is not bijective");
            var reasons = new List<string>();
            var prices = new List<string>();
            var costRows = CostRows();
            var table = NightlyTestsPlan.ClassCostTable(CostPath);
            foreach (var type in expected.Keys.OrderBy(s => s, SortObject))
            {
                var oracle = expected[type];
                var bare = "Antiphon.Tests.Application." + type;
                var classNodes = nodes.Where(n => (string)n["ClassName"]! == bare).Cast<object>().ToArray();
                if (classNodes.Length != oracle.Nodes) reasons.Add("fixture:" + type);
                var hasRow = costRows.TryGetValue(bare, out var row);
                if (!hasRow || RowInt(row, "nodes") != oracle.Nodes || RowDouble(row, "seconds") != oracle.Seconds || RowText(row, "basis") != oracle.Basis)
                    reasons.Add("cost-row:" + type);
                if (type == "AgentTaskLandCheckpointMatrixTests" || type == "AgentTaskLandCheckpointWorkerTests")
                {
                    if (!hasRow || !RowHas(row, "source") || !Like(RowText(row, "source"), "*original CheckpointMatrix 43 nodes / 1560 seconds measured*"))
                        reasons.Add("split-bound-provenance:" + type);
                }
                var cost = NightlyTestsPlan.LaunchClassCost(classNodes, type, bare, table);
                prices.Add(type + "=" + Math.Round(Num(cost, "Seconds"), 1).ToString(CultureInfo.InvariantCulture));
                if (PsValue.Double(PsValue.Prop(cost, "NodeCount")) != oracle.Nodes || Math.Abs(Num(cost, "Seconds") - oracle.Seconds) > 0.01)
                    reasons.Add("class-cost:" + type);
            }
            var uids = nodes.Select(n => (string)n["Uid"]!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (nodes.Count != 158 || uids.Length != 158) reasons.Add("fixture-census");
            var policy = Policy();
            var exclusions = Exclusions(policy, "rc", "antiphon", "windows");
            if (exclusions.Length == 0) reasons.Add("rc-exclusions");
            foreach (var node in nodes) if (NightlyCensus.Excluded(node, exclusions, true)) reasons.Add("excluded:" + node["Uid"]);
            var chunks = NightlyTestsPlan.SuiteChunks(nodes.Cast<object>().ToArray(), AllChunk(), exclusions, true, true, "antiphon", null, Costs());
            var watchdog = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(policy.Object, "antiphon", 0);
            if (watchdog != 3600000) reasons.Add("watchdog");
            if (chunks.Length != 8) reasons.Add("launch-count");
            // Evaluate the actual emitted filter against the entire fixture. Do not use chunk.nodes or chunk.classes as
            // the selection oracle.
            static bool Segment(string value, string pattern) =>
                pattern.Split('|').Any(operand => Regex.IsMatch(value, "^" + Regex.Escape(operand.Trim('(', ')')).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant));
            var covered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var launchCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var publicationCounts = new List<int>();
            var launchDetails = new List<string>();
            var maxSeconds = 0.0;
            foreach (var chunk in chunks)
            {
                var filter = Filter(Argv(chunk, Trx, Diag));
                if (filter == null) { reasons.Add("missing-filter"); continue; }
                var segments = filter.Split('/');
                if (segments.Length != 5 || segments[1] != "*") { reasons.Add("invalid-filter:" + filter); continue; }
                var hits = nodes.Where(n => Segment((string)n["Namespace"]!, segments[2]) && Segment((string)n["Type"]!, segments[3]) && Segment((string)n["Method"]!, segments[4])).ToList();
                var types = hits.Select(n => (string)n["Type"]!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (types.Length != 1) reasons.Add("mixed-or-empty-launch:" + Str(chunk, "id"));
                var seconds = 0.0;
                foreach (var node in hits)
                {
                    var oracle = expected[(string)node["Type"]!];
                    seconds += oracle.Seconds / oracle.Nodes;
                    var uid = (string)node["Uid"]!;
                    covered[uid] = covered.GetValueOrDefault(uid) + 1;
                }
                if (Math.Abs(seconds - Num(chunk, "seconds")) > 0.11) reasons.Add("launch-price:" + Str(chunk, "id"));
                if (seconds >= watchdog / 1000.0) reasons.Add("watchdog-fit:" + Str(chunk, "id"));
                maxSeconds = Math.Max(maxSeconds, seconds);
                foreach (var type in types) launchCounts[type] = launchCounts.GetValueOrDefault(type) + 1;
                if (types.Length == 1 && types[0] == "AgentTaskLandPublicationTests")
                {
                    publicationCounts.Add(hits.Count);
                    if (seconds > 900.01 || segments[4] == "*") reasons.Add("publication-split");
                }
                else if (types.Length == 1 && (hits.Count != expected[types[0]].Nodes || segments[4] != "*")) reasons.Add("complete-class:" + types[0]);
                launchDetails.Add(Str(chunk, "id") + ":" + hits.Count + "/" + Math.Round(seconds, 1).ToString(CultureInfo.InvariantCulture) + "s");
            }
            foreach (var type in expected.Keys) if (launchCounts.GetValueOrDefault(type) != expected[type].Launches) reasons.Add("class-launches:" + type);
            var uidSet = new HashSet<string>(uids, StringComparer.Ordinal);
            var missing = uids.Count(u => !covered.ContainsKey(u));
            var doubled = covered.Count(c => c.Value != 1);
            var extra = covered.Keys.Count(k => !uidSet.Contains(k));
            if (missing != 0 || doubled != 0 || extra != 0) reasons.Add("census");
            var publicationShape = string.Join(",", publicationCounts.OrderBy(n => n));
            if (publicationShape != "3,15,15,15,15") reasons.Add("publication-node-counts");
            if (Math.Abs(maxSeconds - 1980) > 0.01) reasons.Add("largest-launch");
            ok = reasons.Count == 0;
            detail = "prices=" + string.Join(",", prices) + " launches=" + chunks.Length + " nodes=" + nodes.Count + " missing=" + missing + " double=" + doubled
                + " extra=" + extra + " publicationNodes=" + publicationShape + " maxEstimatedSeconds=" + Math.Round(maxSeconds, 1).ToString(CultureInfo.InvariantCulture)
                + " watchdog=" + watchdog + " groups=" + string.Join(",", launchDetails) + " reasons=" + string.Join(",", reasons);
        }
        catch (Exception ex) { detail = "setup-error: " + ex.Message; }
        rows.Check(ok, "C1404 group 10 uses evidence costs, eight isolated launches and the unchanged watchdog", detail);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C1410_MeasuredCostGroups()
    {
        // CARD-1410 (supersedes the CARD-1404 120-node cap case). The real Antiphon.Tests listing
        // (scripts/fixtures/nightly/c1410-antiphon-listing.txt, 19012 nodes) is packed by estimated serial seconds:
        // measured rows from nightly-class-costs.json, else 0.5 s per node, Slow 1.5 s, Slow in a Land or Delivery type
        // 4 s, and a type naming Land, Delivery or Check anywhere doubles. Every launch stays under watchdogs.antiphon, a
        // multi-class or multi-method launch under the 15-minute target, every required node launched once.
        var rows = new NightlyCaseRows();
        var ok = false;
        string detail;
        try
        {
            var policy = Policy();
            const double target = 900.0;
            const double watchdogSeconds = 3600.0;
            var nodes = new List<PlainObject>();
            foreach (var line in File.ReadAllLines(Path.Combine(Repo, "scripts", "fixtures", "nightly", "c1410-antiphon-listing.txt")))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                var fields = line.Split('|');
                var className = fields[0];
                var slow = fields[1] == "S";
                var paren = className.IndexOf('(');
                var head = paren >= 0 ? className.Substring(0, paren) : className;
                var type = className.Substring(head.LastIndexOf('.') + 1);
                var i = 0;
                foreach (var entry in fields[2].Split(','))
                {
                    var eq = entry.IndexOf('=');
                    string method;
                    int cases;
                    if (eq >= 0) { method = entry.Substring(0, eq); cases = (int)Math.Round(PsValue.Double(entry.Substring(eq + 1)), MidpointRounding.ToEven); }
                    else { method = "M" + i.ToString("0000", CultureInfo.InvariantCulture); cases = (int)Math.Round(PsValue.Double(entry), MidpointRounding.ToEven); }
                    i++;
                    for (var k = 0; k < cases; k++)
                        nodes.Add(slow ? Node(className, type, method, className + "." + method + "." + k, "Slow") : Node(className, type, method, className + "." + method + "." + k));
                }
            }
            var realCount = nodes.Count;
            // Suffix hazard: an oversized land class whose method Run prefixes RunLater.
            const string synthetic = "Antiphon.Tests.C1410.C1410LandSplitTests";
            foreach (var (method, count) in new[] { ("Run", 80), ("RunLater", 80), ("Alpha", 30), ("Beta", 30), ("Gamma", 30), ("Delta", 30), ("Epsilon", 30), ("Zeta", 30) })
                for (var k = 0; k < count; k++) nodes.Add(Node(synthetic, "C1410LandSplitTests", method, "c1410." + method + "." + k, "Slow"));
            var nodeArray = nodes.Cast<object>().ToArray();
            var exclusions = Exclusions(policy, "rc", "antiphon", "windows");
            var costs = Costs();
            var plan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), exclusions, true, true, "antiphon", null, costs);
            var nightlyEx = Exclusions(policy, "nightly", "antiphon", "windows");
            var nightlyPlan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), nightlyEx, false, true, "antiphon", null, costs);
            var emptyPlan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), Array.Empty<object>(), true, true, "antiphon", null, costs);

            // Independent estimate: per-class measured rows, else the node model.
            var measured = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var costSeconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var costNodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, row) in CostRows())
            {
                measured[name] = RowDouble(row, "seconds") / RowInt(row, "nodes");
                costSeconds[name] = RowDouble(row, "seconds");
                costNodes[name] = RowInt(row, "nodes");
            }
            var byBare = new Dictionary<string, List<PlainObject>>(StringComparer.OrdinalIgnoreCase);
            var required = new List<PlainObject>();
            foreach (var node in nodes)
            {
                if (NightlyCensus.Excluded(node, exclusions, true)) continue;
                required.Add(node);
                var bare = NightlyCensus.BareClassIdentity((string)node["ClassName"]!);
                if (!byBare.TryGetValue(bare, out var list)) byBare[bare] = list = new List<PlainObject>();
                list.Add(node);
            }
            double UnitSeconds(PlainObject node, string bare)
            {
                if (measured.TryGetValue(bare, out var perNode)) return perNode;
                var type = bare.Substring(bare.LastIndexOf('.') + 1);
                var slow = PsValue.AsArray(node["Categories"]).Any(c => PsValue.Str(c) == "Slow");
                var unit = 0.5;
                if (slow) unit = type.Contains("Land") || type.Contains("Delivery") ? 4.0 : 1.5;
                if (type.Contains("Land") || type.Contains("Delivery") || type.Contains("Check")) unit *= 2;
                return unit;
            }
            var covered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var suiteSeconds = 0.0;
            var methodGroups = 0;
            var maxSeconds = 0.0;
            var maxId = "";
            var overTarget = new List<string>();
            var invalid = new List<string>();
            var splitIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var reasons = new List<string>();
            foreach (var chunk in plan)
            {
                var gf = Str(chunk, "groupFilter");
                var mf = Str(chunk, "methodFilter");
                var ef = Str(chunk, "exactFilter");
                var classes = Classes(chunk);
                try { NightlyTestsPlan.NativeExecutionArguments(Trx, Diag, classes, ef, gf, mf); }
                catch (NightlyRefusal ex) { invalid.Add(Str(chunk, "id") + ":" + ex.Message); }
                // What the TUnit filter selects: whole classes, an exact method, or every method starting with a (Name*) operand.
                var hits = new List<PlainObject>();
                var first = classes.Length > 0 ? classes[0] : "";
                if (ef.Length > 0)
                {
                    var method = ef.Substring(ef.LastIndexOf('/') + 1);
                    if (byBare.TryGetValue(first, out var list)) hits.AddRange(list.Where(n => (string)n["Method"]! == method));
                }
                else if (mf.Length > 0)
                {
                    var ops = Regex.Matches(mf, @"\(([A-Za-z_][A-Za-z0-9_]*)\*\)").Select(m => m.Groups[1].Value).ToArray();
                    if (byBare.TryGetValue(first, out var list)) hits.AddRange(list.Where(n => ops.Any(op => ((string)n["Method"]!).StartsWith(op, StringComparison.Ordinal))));
                }
                else
                {
                    foreach (var className in classes) if (byBare.TryGetValue(className, out var list)) hits.AddRange(list);
                }
                var seconds = 0.0;
                var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var placed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in hits)
                {
                    var bare = NightlyCensus.BareClassIdentity((string)node["ClassName"]!);
                    var unit = UnitSeconds(node, bare);
                    seconds += unit;
                    if (bare != synthetic) suiteSeconds += unit;
                    methods.Add((string)node["Method"]!);
                    if (costNodes.ContainsKey(bare)) placed[bare] = placed.GetValueOrDefault(bare) + 1;
                    var uid = (string)node["Uid"]!;
                    covered[uid] = covered.GetValueOrDefault(uid) + 1;
                }
                // Every recorded class cost in this chunk, prorated to the nodes the chunk actually selected, must fit
                // inside the chunk price. This is the group 28/65 check extended to every row in the cost file.
                var chunkSeconds = Num(chunk, "seconds");
                var recordedNeed = 0.0;
                foreach (var (bare, count) in placed)
                {
                    var portion = costSeconds[bare] * (count / (double)costNodes[bare]);
                    recordedNeed += portion;
                    if (portion > chunkSeconds + 0.25) reasons.Add("measured-exceeds-chunk:" + bare);
                }
                if (recordedNeed > chunkSeconds + 0.25) reasons.Add("measured-exceeds-chunk:" + Str(chunk, "id"));
                if (seconds > maxSeconds) { maxSeconds = seconds; maxId = Str(chunk, "id") + "(" + string.Join(";", classes) + ")"; }
                var filtered = ef.Length > 0 || mf.Length > 0;
                var single = classes.Length == 1 && ((filtered && methods.Count == 1) || !filtered);
                if (seconds > target + 0.01 && !single) overTarget.Add(Str(chunk, "id"));
                if (mf.Length > 0) methodGroups++;
                if (filtered) splitIds[first] = splitIds.GetValueOrDefault(first) + 1;
            }
            var missing = 0;
            var doubled = 0;
            foreach (var node in required)
            {
                var count = covered.GetValueOrDefault((string)node["Uid"]!);
                if (count == 0) missing++;
                else if (count > 1) doubled++;
            }
            var extra = covered.Count - (required.Count - missing);
            var family = NightlyLaunchPlan.HeavyFamilyType("AgentTaskLandPublicationTests") && NightlyLaunchPlan.HeavyFamilyType("AgentTaskDeliveryWatchdogTests")
                && NightlyLaunchPlan.HeavyFamilyType("AgentTaskCheckSweepTests") && !NightlyLaunchPlan.HeavyFamilyType("AgentTaskReplyIntegrationTests");
            var runAlone = plan.Count(c => Str(c, "exactFilter") == "/*/Antiphon.Tests.C1410/C1410LandSplitTests/Run");
            var runOr = plan.Count(c => Str(c, "methodFilter").Contains("(Run*)", StringComparison.Ordinal));
            var rejected = Refusal(() => NightlyTestsPlan.NativeExecutionArguments(Trx, Diag, [synthetic], "", "",
                "/*/Antiphon.Tests.C1410/C1410LandSplitTests/(Run*)|(RunLater*)")) == "invalid-method-filter";
            var watchdog = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(policy.Object, "antiphon", 3600000);
            var nightlyOk = nightlyEx.Length == 0 && nightlyPlan.Length == 1 && string.Equals(Str(nightlyPlan[0], "id"), "all", StringComparison.OrdinalIgnoreCase);
            var emptyOk = emptyPlan.Length == 1 && string.Equals(Str(emptyPlan[0], "id"), "all", StringComparison.OrdinalIgnoreCase);
            var launches = plan.Length;
            if (realCount != 19012) reasons.Add("listing");
            // Task 44a6e14e merged 702 measured rows into the same listing; the independent sum is 106343.8397645542 s,
            // 150 listing launches plus the four suffix probes (154). See scripts/test-nightly-tests.ps1 history.
            // 2026-11-25: +1 (155). CodexCliObservationTests and CodexCliObservationGapTests moved to their own namespace,
            // Antiphon.Tests.Application.Codex, so the planner (which packs by namespace) gives them their own launch.
            if (launches != 155) reasons.Add("launch-count");
            // Unit 20 of the 2026-11-22 plan: +30 s. The rc Windows rows of three deleted Linux-only classes left the policy
            // (CheckpointFixtureRoutingTests 4, CheckpointInheritedFileTests 4, CheckpointVerifierBoundsTests 2 listing nodes,
            // Slow in a Check type: 3 s each), so the fixed listing's nodes for them now count.
            if (Math.Abs(suiteSeconds - 106373.8397645542) > 0.11) reasons.Add("suite-hours");
            if (maxSeconds >= watchdogSeconds) reasons.Add("watchdog-budget");
            if (overTarget.Count != 0) reasons.Add("target");
            if (missing != 0 || doubled != 0 || extra != 0) reasons.Add("census");
            if (invalid.Count != 0 || !rejected || runAlone != 1 || runOr != 0 || methodGroups < 1) reasons.Add("suffix");
            foreach (var big in new[] { "Antiphon.Tests.Scripts.RemoteScriptContractTests", "Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests", synthetic })
                if (splitIds.GetValueOrDefault(big) < 2) reasons.Add("split:" + big.Substring(big.LastIndexOf('.') + 1));
            if (!family) reasons.Add("family-anywhere");
            if (!nightlyOk || !emptyOk || watchdog != 3600000) reasons.Add("nightly-watchdog");
            ok = reasons.Count == 0;
            detail = "launches=" + launches + " suiteHours=" + Math.Round(suiteSeconds / 3600, 2).ToString(CultureInfo.InvariantCulture) + " methodGroups=" + methodGroups
                + " maxEstimatedSeconds=" + Math.Round(maxSeconds, 1).ToString(CultureInfo.InvariantCulture) + " maxLaunch=" + maxId + " overTarget=" + overTarget.Count
                + " required=" + required.Count + " missing=" + missing + " double=" + doubled + " extra=" + extra + " splitClasses=" + splitIds.Count
                + " invalid=" + string.Join(",", invalid) + " watchdog=" + watchdog + " reasons=" + string.Join(",", reasons);
        }
        catch (Exception ex) { detail = ex.Message; }
        rows.Check(ok, "C1410 rc launch groups pack by measured cost under the watchdog and assign every node once", detail);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void G2865_Group28Group65MeasuredBudget()
    {
        // Run 8f055f87 journals: group 28 and group 65 were killed at the 3600 s watchdog while tests were still
        // passing. The model priced each chunk at 900 s. A class with a completed serial test-second total must be priced
        // at least that high, and no chunk of these classes may be priced above the watchdog. Combined buckets without a
        // per-class split are omitted.
        var rows = new NightlyCaseRows();
        var ok = false;
        string detail;
        try
        {
            (string Type, int Count, bool Slow, int Group, double Seconds)[] classes =
            [
                ("HerdrLabelFollowTests", 33, true, 28, 0), ("AgentCreateRaceTests", 4, false, 28, 0), ("OutputDistillationGateTests", 103, false, 28, 0),
                ("AgentTaskServiceIntegrationTests", 120, false, 28, 0), ("TaskInputAttentionCostTests", 35, true, 28, 0),
                ("DelegateBranchDeliveryTests", 7, true, 28, 371), ("BlockedTaskParkDeliveryTests", 6, true, 28, 611),
                ("PostLandMutationCustodyTests", 56, false, 28, 1225), ("RepairSourceDispatchTests", 41, true, 28, 337),
                ("RepairSourceSettlementTests", 36, true, 28, 520), ("SessionRuntimeActionRoutingTests", 35, true, 28, 115),
                ("BlockedTaskParkReclaimTests", 38, true, 28, 978), ("ChannelBridgeTests", 40, true, 28, 0), ("ChannelInboundRecoveryTests", 41, true, 28, 0),
                ("CodexStartupDeliveryTests", 62, false, 28, 0), ("LandEvidenceWarningDeliveryTests", 7, true, 28, 0), ("ApiErrorRecoveryServiceTests", 40, true, 28, 0),
                ("CompletedCardWorktreeRemovalTests", 37, true, 65, 942), ("LandRecoveryCheckoutTests", 34, false, 65, 0), ("LandingGitTests", 58, false, 65, 0),
                ("LandRecoveryIgnoredNestedRepositoryTests", 15, false, 65, 0), ("LandingRemovalControlTests", 34, false, 65, 761),
                ("LandingSourceFreshnessTests", 21, false, 65, 0), ("LandingSourceBoundaryControlTests", 30, false, 65, 0),
                ("RepositoryChildRecoveryTests", 15, true, 65, 0), ("SettledWorktreeRemovalTests", 78, false, 65, 668),
                ("WorktreeGuardedCleanupTests", 49, true, 65, 1593), ("WorktreeManagerGitIntegrationTests", 30, true, 65, 0),
                ("LandingAdmissionControlTests", 42, false, 65, 96), ("GitServiceTests", 12, true, 65, 0), ("DockerDeliveryBarrierTests", 24, false, 65, 0),
                ("DockerDeliveryDatabaseTests", 16, false, 65, 0), ("DockerDeliveryObservationTests", 31, false, 65, 0), ("DockerStackContractTests", 115, false, 65, 0),
                ("HostCleanupCheckpointAuthorityTests", 16, false, 65, 0), ("HostCleanupCheckpointCompatibilityTests", 54, false, 65, 0),
                ("HostCleanupCheckpointCustodyTests", 13, false, 65, 0), ("HostCleanupPolicyTests", 22, false, 65, 0), ("LandingGitProfileTests", 23, false, 65, 0),
                ("LandingIdentityControlTests", 26, false, 65, 0), ("LandingRemovalPolicyControlTests", 72, false, 65, 0), ("FileUseCaptureTests", 13, true, 65, 0),
                ("GrokRunnerImageContractTests", 26, false, 65, 0), ("WorktreeDeleteAccessProbeTests", 38, false, 65, 0), ("WorktreeLockDiagnosticsTests", 37, false, 65, 0),
            ];
            var measured = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var declared = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var groupOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nodes = new List<PlainObject>();
            foreach (var row in classes)
            {
                var ns = row.Group == 28 ? "Antiphon.Tests.Application" : "Antiphon.Tests.Infrastructure";
                var bare = ns + "." + row.Type;
                declared[row.Type] = row.Count;
                groupOf[row.Type] = row.Group;
                if (row.Seconds > 0) measured[row.Type] = row.Seconds;
                for (var i = 0; i < row.Count; i++)
                    nodes.Add(row.Slow ? Node(bare, row.Type, "Case", bare + "." + i, "Slow") : Node(bare, row.Type, "Case", bare + "." + i));
            }
            if (classes.Length != 45 || groupOf.Values.Count(g => g == 28) != 17 || groupOf.Values.Count(g => g == 65) != 28)
                throw new InvalidOperationException("group census");
            // The seven rows replaced by the slower 79b742de sample cite that run. The five 9a16e2cb rows the new table
            // did not replace still cite 8f055f87.
            var provenance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DelegateBranchDeliveryTests"] = "79b742de", ["BlockedTaskParkDeliveryTests"] = "79b742de", ["PostLandMutationCustodyTests"] = "79b742de",
                ["RepairSourceDispatchTests"] = "79b742de", ["RepairSourceSettlementTests"] = "79b742de", ["SessionRuntimeActionRoutingTests"] = "79b742de",
                ["BlockedTaskParkReclaimTests"] = "79b742de",
            };
            var reasons = new List<string>();
            var costRows = CostRows();
            foreach (var type in measured.Keys.OrderBy(s => s, SortObject))
            {
                var bare = (groupOf[type] == 28 ? "Antiphon.Tests.Application" : "Antiphon.Tests.Infrastructure") + "." + type;
                var marker = provenance.GetValueOrDefault(type) ?? "8f055f87";
                if (!costRows.TryGetValue(bare, out var row) || RowInt(row, "nodes") != declared[type] || RowDouble(row, "seconds") != measured[type]
                    || RowText(row, "basis") != "measured" || !RowText(row, "source").Contains(marker, StringComparison.Ordinal))
                    reasons.Add("cost-row:" + type);
            }
            var policy = Policy();
            var exclusions = Exclusions(policy, "rc", "antiphon", "windows");
            var table = NightlyTestsPlan.ClassCostTable(CostPath);
            var chunks = NightlyTestsPlan.SuiteChunks(nodes.Cast<object>().ToArray(), AllChunk(), exclusions, true, true, "antiphon", null, NightlyClassCosts.Of(table));
            var watchdog = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(policy.Object, "antiphon", 0);
            if (watchdog != 3600000) reasons.Add("watchdog");
            var byType = nodes.GroupBy(n => (string)n["Type"]!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var share = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var groupChunks = new Dictionary<int, HashSet<string>> { [28] = new(StringComparer.OrdinalIgnoreCase), [65] = new(StringComparer.OrdinalIgnoreCase) };
            var shapes = new List<string>();
            foreach (var chunk in chunks)
            {
                var need = 0.0;
                var names = new List<string>();
                foreach (var bare in Classes(chunk))
                {
                    var type = bare.Substring(bare.LastIndexOf('.') + 1);
                    var ef = Str(chunk, "exactFilter");
                    var mf = Str(chunk, "methodFilter");
                    if (!byType.TryGetValue(type, out var bucket)) continue;
                    var hits = bucket.Where(node =>
                    {
                        if (ef.Length > 0) return (string)node["Method"]! == ef.Substring(ef.LastIndexOf('/') + 1);
                        if (mf.Length > 0) return Regex.Matches(mf, @"\(([A-Za-z_][A-Za-z0-9_]*)\*\)").Any(m => ((string)node["Method"]!).StartsWith(m.Groups[1].Value, StringComparison.Ordinal));
                        return true;
                    }).Cast<object>().ToArray();
                    if (hits.Length == 0) continue;
                    var cost = NightlyTestsPlan.LaunchClassCost(hits, type, bare, table);
                    share[type] = share.GetValueOrDefault(type) + Num(cost, "Seconds");
                    seen[type] = seen.GetValueOrDefault(type) + 1;
                    names.Add(type);
                    groupChunks[groupOf[type]].Add(Str(chunk, "id"));
                    if (measured.TryGetValue(type, out var total)) need += total * (hits.Length / (double)declared[type]);
                }
                var seconds = Num(chunk, "seconds");
                if (seconds > 3600) reasons.Add("over-watchdog:" + Str(chunk, "id"));
                if (need > seconds + 0.25) reasons.Add("under-need:" + Str(chunk, "id"));
                shapes.Add(Str(chunk, "id") + ":" + names.Count + "/" + seconds.ToString(CultureInfo.InvariantCulture) + "s/need=" + Math.Round(need, 1).ToString(CultureInfo.InvariantCulture));
            }
            foreach (var type in measured.Keys.OrderBy(s => s, SortObject))
            {
                var priced = share.GetValueOrDefault(type);
                if (priced + 0.25 < measured[type]) reasons.Add("share:" + type + "=" + Math.Round(priced, 1).ToString(CultureInfo.InvariantCulture));
                if (seen.GetValueOrDefault(type) < 1) reasons.Add("unplaced:" + type);
            }
            if (groupChunks[28].Count != 5) reasons.Add("g28-chunks:" + groupChunks[28].Count);
            if (groupChunks[65].Count != 6) reasons.Add("g65-chunks:" + groupChunks[65].Count);
            if (seen.Count != classes.Length) reasons.Add("census");
            detail = "g28chunks=" + groupChunks[28].Count + " g65chunks=" + groupChunks[65].Count + " launches=" + chunks.Length + " watchdog=" + watchdog
                + " groups=" + string.Join(",", shapes) + " reasons=" + string.Join(",", reasons);
            ok = reasons.Count == 0;
        }
        catch (Exception ex) { detail = "setup-error: " + ex.Message; }
        rows.Check(ok, "G2865 group 28 and 65 measured class costs fit inside chunk prices under the watchdog", detail);
        rows.ShouldAllPass(1);
    }
}
