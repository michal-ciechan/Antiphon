using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using H = Antiphon.Tests.Scripts.Nightly.FailedFirstHistoryCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1404/1514: the failure history (export from closed retained evidence, the reader's digest and provenance
/// checks, the resolver onto the current plan). W2-nightly S14: in-process over the compiled PriorFailureHistory; were
/// the EvidenceUnion, RefusesIncompleteEvidence, WindowsShapedHistory and OrdinaryFailedChunk cases of
/// scripts/test-nightly-failed-first.ps1, with the same rows.
/// </summary>
[Category("Integration")]
[NotInParallel("nightly-history-statics")]
public sealed class RcFailureHistoryTests
{
    // One shared replay: the 19,012-node anchor plus supplemental suites.
    [Test]
    public void EvidenceUnion()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-union");
        HistoryLinkCache.Reset();
        var replay = FailedFirstDispatchCases.Replay(NightlyPlanCases.Policy());
        var fx = H.Historical(fixture.Root);
        var path = fixture.PathOf("history.json");
        var export = PriorFailureHistory.Export(fx.Run, path);
        var history = PriorFailureHistory.Read(path, H.Str(export, "Digest"), "");
        var members = H.Items(history, "members");
        var obs = members.SelectMany(m => H.Items(m, "observations")).ToArray();
        rows.Check(obs.Count(o => H.Str(o, "kind") == "trx") == 36, "FF EvidenceUnion.trx36");
        rows.Check(obs.Count(o => H.Str(o, "state") == "Error") == 7, "FF EvidenceUnion.diag7");
        rows.Check(obs.Count(o => H.Str(o, "state") == "InProgress" && H.Str(o, "uid") == "diag-7") == 1, "FF EvidenceUnion.hang1");
        var observed = members.Count(m => H.Items(m, "observations").Length > 0);
        rows.Check(observed == 34, "FF EvidenceUnion.classes34", observed.ToString(CultureInfo.InvariantCulture));
        rows.Check(members.Count(m => H.Str(m, "className") == "Antiphon.Tests.Checkpoints.UnobservedCoPackedTests") == 1 && PsValue.Prop(history, "membershipComplete") is true
            && H.Items(history, "fallbackChunks").Length == 1, "FF EvidenceUnion.fallback");
        var replayNodes = (object[])replay["nodes"]!;
        var replayChunks = (object[])replay["chunks"]!;
        // 151 chunks since 2026-11-25: the Codex CLI tests moved to Antiphon.Tests.Application.Codex, their own launch.
        rows.Check(replayNodes.Length == 19012 && replayChunks.Length == 151, "FF EvidenceUnion.replay");
        var suites = new List<object> { replay };
        foreach (var suite in H.Suites4)
        {
            // Supplemental fixtures are separate from the unchanged 19,012-node anchor.
            var nodes = fx.Nodes[suite].ToArray();
            var plan = H.Plan(nodes, "supplemental", suite);
            if (suite == "antiphon")
                suites[0] = FailedFirstDispatchCases.Suite(suite, [.. replayNodes, .. nodes], [.. replayChunks, .. plan], (object[])replay["exclusions"]!);
            else suites.Add(FailedFirstDispatchCases.Suite(suite, nodes, plan, []));
        }
        var all = suites.ToArray();
        var resolved = PriorFailureHistory.Resolve(history, all, "Full", null, null);
        var splits = true;
        foreach (var m in members)
        {
            var s = all.First(x => H.Str(x, "id") == H.Str(m, "suite"));
            var expected = H.Items(s, "chunks").Where(c => H.Items(c, "classes").Any(k => PsValue.Str(k) == H.Str(m, "className"))).Select(c => H.Str(c, "id")).ToArray();
            var actual = H.Items(resolved, "members").Where(r => H.Str(r, "suite") == H.Str(m, "suite") && H.Str(r, "className") == H.Str(m, "className")).ToArray();
            if (actual.Length != 1 || string.Join(",", H.Items(actual[0], "chunks").Select(PsValue.Str)) != string.Join(",", expected) || expected.Length == 0) splits = false;
        }
        rows.Check(splits && H.Items(resolved, "unresolved").Length == 0, "FF EvidenceUnion.allSplits");
        rows.Check(!members.Any(m => H.Str(m, "className").EndsWith("RecoveredTests", StringComparison.Ordinal)), "FF EvidenceUnion.transitions");
        rows.Check(obs.Count(o => H.Str(o, "state") == "InProgress" && H.Str(o, "chunk") == "all-group-105" && H.Str(o, "artifact") == "antiphon-all-group-105-execution/invocation.diag") == 1,
            "FF EvidenceUnion.exactObservations");
        var before = File.ReadAllBytes(path);
        var refused = H.Refuses(() => PriorFailureHistory.Export(fx.Run, path), "");
        rows.Check(refused && before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "FF EvidenceUnion.immutable");
        rows.ShouldAllPass(10);
    }

    [Test]
    public void WindowsShapedHistory()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-shaped");
        HistoryLinkCache.Reset();
        WindowsShaped(fixture.Root, rows, nameof(WindowsShapedHistory));
        rows.ShouldAllPass(1);
    }

    [Test]
    public void OrdinaryFailedChunk()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-ordinary");
        HistoryLinkCache.Reset();
        Ordinary(fixture.Root, rows, nameof(OrdinaryFailedChunk));
        rows.ShouldAllPass(4);
    }

    [Test]
    public void RefusesIncompleteEvidence()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-refuses");
        HistoryLinkCache.Reset();
        var root = fixture.Root;
        WindowsShaped(root, rows, nameof(RefusesIncompleteEvidence));
        Ordinary(root, rows, nameof(RefusesIncompleteEvidence));
        var fx = H.Historical(root);
        var path = Path.Combine(root, "valid-history.json");
        var exp = PriorFailureHistory.Export(fx.Run, path);
        var h = PriorFailureHistory.Read(path, H.Str(exp, "Digest"), "");
        var current = H.Suites4.Select(suite =>
        {
            var nodes = fx.Nodes[suite].ToArray();
            return (object)FailedFirstDispatchCases.Suite(suite, nodes, H.Plan(nodes, "all", suite), []);
        }).ToArray();
        var policy = NightlyPlanCases.Policy();
        object Sweep(object history, object[] suites, object? context = null) => PriorFailureHistory.Resolve(history, suites, "PriorFailureSweep", context, NightlyPlanCases.Policy());
        var control = Sweep(h, current);
        rows.Check(H.Items(control, "warnings").Count(w => PsValue.Str(w).StartsWith("PARTIAL MISSING EXCLUSION CONTEXT:", StringComparison.Ordinal)) == 1, "FF RefusesIncompleteEvidence.missingContextWarning");
        var candidateSha = H.HeadSha(H.Repo);
        rows.Check(H.Items(control, "members").Length >= 34 && H.Items(control, "unresolved").Length == 0 && H.Str(h, "sourceSha") != candidateSha && H.Str(h, "policyHash") != policy.Hash,
            "FF RefusesIncompleteEvidence.validControl");
        foreach (var variant in new[] { "exit", "result", "detail", "short", "crash", "crash-unbound", "missing-bound" })
            rows.Check(H.ChunkAccounting(root, variant), "FF RefusesIncompleteEvidence.chunk-" + variant);
        // No timeout, diagnostic, failing summary marker or retained plan can mask this witness.
        rows.Check(H.ChunkAccounting(root, "missing"), "FF RefusesIncompleteEvidence.standaloneMissingTrx");
        var planPath = Path.Combine(fx.Run, "timing", "groups-planned-antiphon.json");
        var retainedPlan = File.ReadAllText(planPath);
        File.Delete(planPath);
        var wrong = PriorFailureHistory.Export(fx.Run, Path.Combine(root, "wrong-suite.json"));
        var wrongHistory = PsValue.Prop(wrong, "History");
        var full = PriorFailureHistory.Resolve(wrongHistory, current, "Full", null, null);
        var un = H.Items(full, "unresolved").Count(u => H.Str(u, "suite") == "antiphon" && H.Str(u, "chunk") == "all-group-105" && H.Str(u, "reason") == "historical-membership-unavailable");
        rows.Check(!H.True(wrongHistory, "membershipComplete") && un == 1 && !H.Items(wrongHistory, "members").Any(m => H.Str(m, "className") == "Foreign.WrongSuiteTests")
            && H.Items(wrongHistory, "members").Length == 34, "FF RefusesIncompleteEvidence.wrongSuiteFull");
        rows.Check(H.Refuses(() => Sweep(wrongHistory, current), "historical-membership-unavailable"), "FF RefusesIncompleteEvidence.wrongSuitePartial");
        File.WriteAllText(planPath, retainedPlan);
        const string name = "Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests";
        var missing = (object[])H.Copy(current)!;
        var first = (PlainObject)missing[0];
        first["nodes"] = H.Items(first, "nodes").Where(n => H.Str(n, "ClassName") != name).ToArray();
        var r = PriorFailureHistory.Resolve(h, missing, "Full", null, null);
        rows.Check(H.Items(r, "unresolved").Count(u => H.Str(u, "className") == name && H.Str(u, "reason") == "current-class-missing") == 1, "FF RefusesIncompleteEvidence.missingFull");
        rows.Check(H.Refuses(() => Sweep(h, missing), "current-class-missing"), "FF RefusesIncompleteEvidence.missingPartial");
        var renamed = (object[])H.Copy(missing)!;
        ((PlainObject)renamed[0])["nodes"] = H.Items(renamed[0], "nodes").Append(FailedFirstDispatchCases.Node(name + "Renamed")).ToArray();
        rows.Check(H.Refuses(() => Sweep(h, renamed), "current-class-missing"), "FF RefusesIncompleteEvidence.renamed");
        // D-21 uses real rc exclusions even when the run caller supplies nightly's empty rows.
        const string whole = "Antiphon.Tests.Card0168InteractiveProbeTests";
        const string methodClass = "Antiphon.Tests.TestHelpers.TestDbFixtureLazyInitializationTests";
        var executionPlatform = Path.DirectorySeparatorChar == '/' ? "linux" : "windows";
        var methodRow = NightlyPolicy.ProfileExclusions(policy.Object, "rc", "antiphon", executionPlatform).First(e => e.Class == "TestDbFixtureLazyInitializationTests");
        var exHistory = (PlainObject)H.Copy(h)!;
        exHistory["members"] = new object[] { new PlainObject(("suite", "antiphon"), ("className", whole), ("reasons", new object[] { "prior-failure" }), ("observations", Array.Empty<object>())) };
        object[] exNodes = [FailedFirstDispatchCases.Node(whole)];
        var exSuite = (PlainObject)FailedFirstDispatchCases.Suite("antiphon", exNodes, [new PlainObject(("id", "all"), ("classes", new object[] { whole }), ("nodes", exNodes))], []);
        object[] exSuites = [exSuite];
        rows.Check(H.Refuses(() => Sweep(exHistory, exSuites), "entirely-excluded-class"), "FF RefusesIncompleteEvidence.excluded");
        ((PlainObject)((object[])exHistory["members"]!)[0])["className"] = methodClass;
        exNodes = methodRow.Methods.Select(m => (object)FailedFirstDispatchCases.Node(methodClass, m)).Append(FailedFirstDispatchCases.Node(methodClass, "A_db_free_exact_method_selection_constructs_and_starts_no_database")).ToArray();
        exSuite["nodes"] = exNodes;
        exSuite["chunks"] = new object[] { new PlainObject(("id", "all"), ("classes", new object[] { methodClass }), ("nodes", exNodes)) };
        r = Sweep(exHistory, exSuites);
        var excluded = H.Items(r, "excludedUids");
        rows.Check(H.Items(H.Items(r, "members")[0], "requiredUids").Length == 1 && excluded.Length == 3 && H.Str(r, "exclusionProfile") == "rc" && H.Str(r, "policyDigest") == policy.Hash
            && H.Str(r, "exclusionPolicyFileDigest") == PriorFailureHistory.Digest(policy.Path) && H.Str(r, "exclusionPlatform") == executionPlatform
            && !excluded.Any(e => H.Str(e, "reason") != methodRow.Reason || H.Str(e, "owner") != methodRow.Owner), "FF RefusesIncompleteEvidence.methodExclusions");
        rows.Check(H.Refuses(() => Sweep(exHistory, exSuites, new PlainObject(("profile", "nightly"))), "partial-exclusion-profile-mismatch"), "FF RefusesIncompleteEvidence.exclusionMismatch");
        Hashtable Good() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "profile", "rc" }, { "policyDigest", policy.Hash }, { "exclusionPolicyFileDigest", PriorFailureHistory.Digest(policy.Path) }, { "exclusionPlatform", executionPlatform },
        };
        static object Context(Hashtable table, bool dictionary) =>
            dictionary ? table : new PlainObject(table.Keys.Cast<string>().Select(k => (k, table[k]!)).ToArray());
        var bindings = true;
        foreach (var dictionary in new[] { true, false })
        {
            var bound = Sweep(exHistory, exSuites, Context(Good(), dictionary));
            if (H.Items(H.Items(bound, "members")[0], "requiredUids").Length != 1 || H.Items(bound, "excludedUids").Length != 3) bindings = false;
            foreach (var field in new[] { "policyDigest", "exclusionPolicyFileDigest", "exclusionPlatform" })
            {
                var table = Good();
                table[field] = "mismatching-context";
                if (!H.Refuses(() => Sweep(exHistory, exSuites, Context(table, dictionary)), "partial-exclusion-profile-mismatch")) bindings = false;
            }
        }
        rows.Check(bindings, "FF RefusesIncompleteEvidence.contextBindings");
        var noChunk = H.Copy(h)!;
        foreach (var m in H.Items(noChunk, "members")) foreach (var o in H.Items(m, "observations")) ((PlainObject)o)["chunk"] = "";
        r = Sweep(noChunk, current);
        rows.Check(H.Items(r, "members").Count(m => H.Str(m, "className") == name) == 1, "FF RefusesIncompleteEvidence.noOldChunk");
        var empty = (PlainObject)H.Copy(h)!;
        empty["members"] = Array.Empty<object>(); empty["unresolved"] = Array.Empty<object>(); empty["fallbackChunks"] = Array.Empty<object>();
        rows.Check(H.Items(PriorFailureHistory.Resolve(empty, current, "Full", null, null), "members").Length == 0 && H.Refuses(() => Sweep(empty, current), "empty-history"),
            "FF RefusesIncompleteEvidence.empty");
        rows.Check(H.Refuses(() => PriorFailureHistory.Read(path, new string('0', 64), ""), "history-digest-mismatch") && H.Refuses(() => PriorFailureHistory.Read(""), "empty-history-path"),
            "FF RefusesIncompleteEvidence.digest");
        var badPath = Path.Combine(root, "bad.json");
        var bad = (PlainObject)H.Copy(h)!;
        bad["format"] = "unsupported";
        H.WriteJson(badPath, bad);
        rows.Check(H.Refuses(() => PriorFailureHistory.Read(badPath), "history-schema"), "FF RefusesIncompleteEvidence.schema");
        // Compare each field to bound OLD evidence, never to candidate SHA/policy.
        var values = new Dictionary<string, string> { ["sourceRunId"] = "another-run", ["sourceSha"] = new('c', 40), ["policyHash"] = new('d', 64), ["profile"] = "nightly", ["platform"] = "linux", ["sourceAttempt"] = "2" };
        foreach (var (field, value) in values)
        {
            bad = (PlainObject)H.Copy(h)!;
            bad[field] = value;
            H.WriteJson(badPath, bad);
            rows.Check(H.Refuses(() => PriorFailureHistory.Read(badPath), "history-provenance"), "FF RefusesIncompleteEvidence.provenance-" + field);
        }
        var diagPath = Path.Combine(fx.Run, "antiphon-all-group-105-execution", "invocation.diag");
        var old = File.ReadAllText(diagPath);
        File.AppendAllText(diagPath, "\nchanged");
        rows.Check(H.Refuses(() => PriorFailureHistory.Read(path, H.Str(exp, "Digest"), ""), "evidence-digest-mismatch"), "FF RefusesIncompleteEvidence.evidenceTamper");
        File.WriteAllText(diagPath, old);
        var firstRow = fx.Rows[0];
        var oldTrx = (string)firstRow["trx"]!;
        object Export(string output) => PriorFailureHistory.Export(fx.Run, Path.Combine(root, output));
        firstRow["trx"] = "../escape.trx"; fx.WriteSummary();
        rows.Check(H.Refuses(() => Export("escape.json"), "evidence-path-escape"), "FF RefusesIncompleteEvidence.escape");
        firstRow["trx"] = oldTrx; fx.WriteSummary();
        File.CreateSymbolicLink(Path.Combine(fx.Run, "linked.trx"), Path.Combine(fx.Run, oldTrx));
        firstRow["trx"] = "linked.trx"; fx.WriteSummary();
        var symbolicRefused = H.Refuses(() => Export("link.json"), "evidence-link");
        var hard = Path.Combine(fx.Run, "hard-linked.trx");
        NativeHardLink.Create(hard, Path.Combine(fx.Run, oldTrx));
        firstRow["trx"] = "hard-linked.trx"; fx.WriteSummary();
        var hardRefused = H.Refuses(() => Export("hard-link.json"), "evidence-link");
        File.Delete(hard);
        rows.Check(symbolicRefused && hardRefused, "FF RefusesIncompleteEvidence.link");
        firstRow["trx"] = oldTrx; fx.Summary["completedAt"] = ""; fx.WriteSummary();
        rows.Check(H.Refuses(() => Export("working.json"), "run-not-closed"), "FF RefusesIncompleteEvidence.working");
        fx.Summary["completedAt"] = "2026-10-29T00:00:00Z"; fx.WriteSummary();
        rows.Check(H.Refuses(() => PriorFailureHistory.Export(fx.Run, Path.Combine(fx.Run, "inside.json")), "history-output-inside-run"), "FF RefusesIncompleteEvidence.outputInside");
        File.WriteAllText(diagPath, old + old[old.IndexOf("TestNodeUpdateMessage", StringComparison.Ordinal)..]);
        rows.Check(H.Refuses(() => Export("duplicate.json"), "history-identity-conflict"), "FF RefusesIncompleteEvidence.duplicateUid");
        File.WriteAllText(diagPath, old);
        var discPath = Path.Combine(fx.Run, "antiphon-profile.discovery.json");
        var disc = File.ReadAllText(discPath);
        var d = JsonNode.Parse(disc)!;
        d["nodes"]!.AsArray().First(n => (string?)n!["uid"] == "diag-0")!["type"] = "AnotherTests";
        File.WriteAllText(discPath, d.ToJsonString(), new UTF8Encoding(false));
        rows.Check(H.Refuses(() => Export("mapping.json"), "history-identity-conflict"), "FF RefusesIncompleteEvidence.invalidMapping");
        File.WriteAllText(discPath, disc);
        var lastRow = fx.Rows[^1];
        lastRow.Remove("execution"); fx.WriteSummary();
        File.WriteAllText(Path.Combine(fx.Run, "antiphon-all-group-105-execution", "other.diag"), old);
        rows.Check(H.Refuses(() => Export("ambiguous.json"), "ambiguous-invocation"), "FF RefusesIncompleteEvidence.ambiguous");
        lastRow["execution"] = "antiphon-all-group-105-execution/invocation.diag"; fx.WriteSummary();
        File.Delete(planPath);
        File.WriteAllText(diagPath, "truncated diagnostic");
        object? mal = null;
        try { mal = Export("malformed.json"); } catch (Exception) { }
        var malHistory = PsValue.Prop(mal, "History");
        rows.Check(mal != null && !H.True(malHistory, "membershipComplete") && H.Items(malHistory, "unresolved").Length > 0
            && H.Refuses(() => Sweep(malHistory!, current), "historical-membership-unavailable"), "FF RefusesIncompleteEvidence.malformedFallback");
        File.WriteAllText(diagPath, old);
        H.WriteJson(planPath, new OrderedDictionary
        {
            { "version", 1 }, { "suite", "antiphon" }, { "runId", "different-run" }, { "attempt", "1" }, { "sourceDigest", new string('a', 40) }, { "policyDigest", new string('b', 64) },
            { "groups", Array.Empty<object>() },
        });
        rows.Check(H.Refuses(() => Export("binding.json"), "history-provenance"), "FF RefusesIncompleteEvidence.boundPlan");
        File.Delete(planPath);
        var caseHistory = H.Copy(h)!;
        var caseMember = (PlainObject)H.Items(caseHistory, "members")[0];
        caseMember["className"] = H.Str(caseMember, "className").ToLowerInvariant();
        r = PriorFailureHistory.Resolve(caseHistory, current, "Full", null, null);
        rows.Check(H.Items(r, "unresolved").Length == 1 && H.Refuses(() => Sweep(caseHistory, current), "current-class-missing"), "FF RefusesIncompleteEvidence.caseSensitive");
        var selector = H.Copy(h)!;
        ((PlainObject)H.Items(selector, "members")[0])["className"] = "Antiphon.Tests.*";
        rows.Check(H.Refuses(() => PriorFailureHistory.Resolve(selector, current, "Full", null, null), "history-class-identity") && H.Refuses(() => Sweep(selector, current), "history-class-identity"),
            "FF RefusesIncompleteEvidence.invalidSelector");
        H.WriteJson(Path.Combine(fx.Run, "execution.json"), new OrderedDictionary
        {
            { "format", "antiphon-tunit-execution-v1" }, { "tunitVersion", "1.44.0" }, { "mtpVersion", "2.2.2" },
            { "nodes", new object[] { new OrderedDictionary { { "uid", "diag-7" }, { "state", "InProgress" } } } },
        });
        lastRow["execution"] = "execution.json"; fx.WriteSummary();
        var j = Export("execution-history.json");
        rows.Check(H.Items(PsValue.Prop(j, "History"), "members").Count(m => H.Str(m, "className") == "Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests") == 1,
            "FF RefusesIncompleteEvidence.executionJson");
        firstRow["trx"] = @"C:\retired\logs\retained-run\" + oldTrx; fx.WriteSummary();
        var rel = PsValue.Prop(Export("relocated-history.json"), "History");
        rows.Check(H.Items(rel, "evidence").Count(e => H.Str(e, "relativePath") == oldTrx) == 1 && H.Items(rel, "rebindings").Length >= 1, "FF RefusesIncompleteEvidence.relocation");
        rows.ShouldAllPass(48);
    }

    // Test-WindowsShapedHistory: a CRLF, BOM-prefixed summary with retained Windows paths, under en-GB, still exports.
    private static void WindowsShaped(string root, NightlyCaseRows rows, string caseName)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        var previousUi = Thread.CurrentThread.CurrentUICulture;
        var detail = "";
        var ok = false;
        try
        {
            var culture = CultureInfo.GetCultureInfo("en-GB");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            var run = Path.Combine(root, "shaped-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(run, "timing"));
            object[] nodes = [FailedFirstDispatchCases.Node("Antiphon.Tests.Accounting.ShapedTests", "Run", "shaped")];
            var row = new OrderedDictionary { { "id", "antiphon" }, { "chunk", "all" }, { "exitCode", 1 }, { "result", "FAIL" }, { "timedOut", false }, { "trx", @"C:\retired\logs\retained-run\antiphon-all.trx" } };
            var summary = new OrderedDictionary
            {
                { "runId", "retained-run" }, { "attempt", "1" }, { "invocationId", "retained-run" }, { "completedAt", "2026-10-29T00:00:00Z" }, { "sha", new string('a', 40) },
                { "profile", "rc" }, { "platform", "windows" }, { "policyHash", new string('b', 64) }, { "suites", new object[] { row } }, { "timingRoot", "timing" },
                { "logDir", @"C:\retired\logs\retained-run" },
            };
            var json = H.JsonText(summary, indented: true).Replace("\r\n", "\n").Replace("\n", "\r\n");
            File.WriteAllText(Path.Combine(run, "summary.json"), json, new UTF8Encoding(true));
            var raw = File.ReadAllBytes(Path.Combine(run, "summary.json"));
            if (raw.Length < 4 || raw[0] != 0xEF || raw[1] != 0xBB || raw[2] != 0xBF || !raw.Contains((byte)13)) throw new InvalidOperationException("witness-bytes");
            File.WriteAllText(Path.Combine(run, "antiphon-all.trx"), "<TestRun><TestDefinitions><UnitTest id=\"shaped\"><TestMethod className=\"Antiphon.Tests.Accounting.ShapedTests\" name=\"Run\" /></UnitTest></TestDefinitions><Results><UnitTestResult testId=\"shaped\" outcome=\"Failed\" /></Results></TestRun>");
            H.Discovery(Path.Combine(run, "antiphon-profile.discovery.json"), nodes);
            var exp = PriorFailureHistory.Export(run, Path.Combine(root, "shaped-history.json"));
            var history = PsValue.Prop(exp, "History");
            ok = H.Str(history, "sourceRunId") == "retained-run" && H.Items(history, "members").Count(m => H.Str(m, "className") == "Antiphon.Tests.Accounting.ShapedTests") == 1;
        }
        catch (Exception e) { detail = H.Message(e); ok = false; }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
            Thread.CurrentThread.CurrentUICulture = previousUi;
        }
        rows.Check(ok, "FF " + caseName + ".windows-shaped", detail);
    }

    // Test-OrdinaryFailedChunk: one Failed and one Passed node in a failing chunk, with and without the retained plan.
    private static void Ordinary(string root, NightlyCaseRows rows, string caseName)
    {
        foreach (var retainedPlan in new[] { true, false })
        {
            var variant = retainedPlan ? "bound" : "unbound";
            var run = Path.Combine(root, "ordinary-" + variant);
            Directory.CreateDirectory(Path.Combine(run, "timing"));
            object[] nodes = [FailedFirstDispatchCases.Node("Antiphon.Tests.Accounting.FailedTests", "Run", "failed"), FailedFirstDispatchCases.Node("Antiphon.Tests.Accounting.PassedTests", "Run", "passed")];
            var row = new OrderedDictionary { { "id", "antiphon" }, { "chunk", "all" }, { "exitCode", 2 }, { "result", "FAIL" }, { "timedOut", false } };
            H.WriteJson(Path.Combine(run, "summary.json"), new OrderedDictionary
            {
                { "runId", "ordinary" }, { "attempt", "1" }, { "completedAt", "2026-10-29T00:00:00Z" }, { "sha", new string('a', 40) }, { "profile", "rc" }, { "platform", "windows" },
                { "policyHash", new string('b', 64) }, { "suites", new object[] { row } }, { "timingRoot", "timing" },
            });
            H.Discovery(Path.Combine(run, "antiphon-profile.discovery.json"), nodes);
            if (retainedPlan) H.WritePlan(run, "ordinary", nodes);
            var defs = new StringBuilder();
            var results = new StringBuilder();
            foreach (var n in nodes)
            {
                defs.Append("<UnitTest id=\"").Append(H.Str(n, "Uid")).Append("\"><TestMethod className=\"").Append(H.Str(n, "ClassName")).Append("\" name=\"Run\" /></UnitTest>");
                results.Append("<UnitTestResult testId=\"").Append(H.Str(n, "Uid")).Append("\" outcome=\"").Append(H.Str(n, "Uid") == "failed" ? "Failed" : "Passed").Append("\" />");
            }
            H.WriteTrx(run, defs, results);
            var exp = PriorFailureHistory.Export(run, Path.Combine(root, "ordinary-" + variant + ".json"));
            var history = PriorFailureHistory.Read(H.Str(exp, "Path"), H.Str(exp, "Digest"), "");
            var suite = H.OneChunkSuite(nodes);
            var full = PriorFailureHistory.Resolve(history, suite, "Full", null, null);
            var fullMembers = H.Items(full, "members");
            var prefix = "FF " + caseName + ".ordinary-" + variant;
            rows.Check(H.True(history, "membershipComplete") && H.Items(full, "unresolved").Length == 0 && fullMembers.Length == 1 && H.Str(fullMembers[0], "className") == H.Str(nodes[0], "ClassName")
                && H.Items(history, "fallbackChunks").Length == 0, prefix + "-full");
            var accepted = false;
            var detail = "";
            try
            {
                var partial = PriorFailureHistory.Resolve(history, suite, "PriorFailureSweep", null, NightlyPlanCases.Policy());
                var members = H.Items(partial, "members");
                accepted = members.Length == 1 && H.Str(members[0], "className") == H.Str(nodes[0], "ClassName") && H.Items(members[0], "requiredUids").Length == 1
                    && PsValue.Str(H.Items(members[0], "requiredUids")[0]) == "failed" && H.Items(partial, "unresolved").Length == 0;
            }
            catch (Exception e) { detail = H.Message(e); }
            rows.Check(accepted, prefix + "-partial", detail);
        }
    }

}
