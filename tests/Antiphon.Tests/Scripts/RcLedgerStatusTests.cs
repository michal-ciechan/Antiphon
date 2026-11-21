using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S5c: exact partitions, status and diagnostic plans, in-process (moved from test-rc-ledger-status.ps1).
/// Decisions must cover the discovered universe exactly; whole methods and packed chunks launch together; a suite
/// without a trusted adapter is one whole obligation; status counts executions separately from historical origins;
/// and a plan stays forced Full, write-once, with revisions that only ever add results.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(120_000)]
public sealed class RcLedgerStatusTests : IDisposable
{
    private readonly RcInputFixtureRepository repo = new("rs-");

    public void Dispose() => repo.Dispose();

    private static JsonObject Obligation(string method, string caseKey = "one", string chunk = "chunk-a") => new()
    {
        ["suite"] = "server", ["className"] = "Example.Tests", ["method"] = method, ["caseKey"] = caseKey, ["stable"] = true, ["boundary"] = "core", ["chunkId"] = chunk,
    };

    private static JsonObject Decision(JsonNode row, string kind = "Carry", string chunk = "historical-one", double wall = 12) => new()
    {
        ["suite"] = (string?)row["suite"], ["className"] = (string?)row["className"], ["method"] = (string?)row["method"], ["caseKey"] = (string?)row["caseKey"],
        ["disposition"] = kind, ["reasons"] = new JsonArray(JsonValue.Create("fixture")),
        ["origin"] = new JsonObject
        {
            ["suite"] = (string?)row["suite"], ["envelopeDigest"] = new string('a', 64), ["runId"] = "old-run", ["sourceSha"] = new string('b', 40), ["chunkId"] = chunk,
            ["wallSeconds"] = wall,
        },
    };

    private static JsonObject Suite(string name, string adapter, params JsonNode[] rows) => new()
    {
        ["suite"] = name, ["adapter"] = adapter, ["cases"] = Arr(rows),
    };

    private static JsonArray Suites(params JsonNode[] rows) => new(Suite("server", "tunit", rows));

    // Nodes have one parent: every array gets copies.
    private static JsonArray Arr(params JsonNode[] items) => new(items.Select(i => i.DeepClone()).ToArray());

    private static JsonObject Partition(JsonArray suites, JsonArray decisions) => RcIncrementalPlan.ExactPartition(suites, decisions, false);

    private static int Count(JsonNode? node, string name) => ((JsonArray)node![name]!).Count;

    // Through the JSON text: a count may be held as an int or a long.
    private static long Int(JsonNode node, string name) => long.Parse(node[name]!.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    private static double Num(JsonNode node, string name) => double.Parse(node[name]!.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Strings(JsonNode? array) => ((JsonArray)array!).Select(n => (string)n!).ToArray();

    private static void Refuses(Action body, string reason)
    {
        try { body(); }
        catch (Exception e)
        {
            e.Message.ShouldContain(reason, Case.Sensitive, "wrong-refusal:" + reason + ":" + e.Message);
            return;
        }
        throw new ShouldAssertException("accepted:" + reason);
    }

    // Per test instance: parallel cases never share a node.
    private readonly JsonObject A = Obligation("A");
    private readonly JsonObject B = Obligation("B", "one", "chunk-b");
    private JsonArray Decisions() => new(Decision(A), Decision(B));

    [Test]
    public void PartialDecisionsCannotCloseUniverse()
    {
        Refuses(() => Partition(Suites(A, B), Arr(Decision(A))), "decision-universe-mismatch");
        Refuses(() => Partition(Suites(A, B), Arr(Decision(A), Decision(B), Decision(A))), "duplicate-decision");
        Refuses(() => Partition(Suites(A, B), Arr(Decision(A), Decision(B), Decision(Obligation("Extra")))), "extra-decision");
        Refuses(() => Partition(Suites(A, A), Arr(Decision(A))), "duplicate-obligation");
        var all = Partition(Suites(A, B), Decisions());
        (Count(all, "required") == 2 && Count(all, "carried") == 2 && Count(all, "launch") == 0).ShouldBeTrue("all-carried-no-launch");
        var none = Partition(Suites(A, B), Arr(Decision(A, "Dirty"), Decision(B, "Dirty")));
        (Count(none, "dirty") == 2 && Count(none, "carried") == 0 && Count(none, "launch") == 2).ShouldBeTrue("zero-carried-exact");
        var reverse = Partition(Suites(A, B), Arr(Decision(B), Decision(A)));
        ((string?)all["digest"]).ShouldBe((string?)reverse["digest"], "ordinal-digest-independent-of-order");
    }

    [Test]
    public void ParameterizedMethodsAndNonTunitStayRequired()
    {
        var x = Obligation("Rows", "alpha");
        var y = Obligation("Rows", "beta", "chunk-b");
        var p = Partition(Suites(x, y), Arr(Decision(x, "Dirty"), Decision(y)));
        (Count(p, "dirty") == 2 && Count(p, "carried") == 0).ShouldBeTrue("whole-parameterized-method-dirty");
        y = Obligation("B", "beta", "chunk-a");
        p = Partition(Suites(A, y), Arr(Decision(A, "Dirty"), Decision(y)));
        (Count(p, "launch") == 2 && Count(p, "carried") == 0).ShouldBeTrue("packed-chunk-actually-launched");
        p = Partition(Suites(A, y), Arr(Decision(A, "Sample"), Decision(y)));
        (Count(p, "sampled") == 2 && Count(p, "carried") == 0).ShouldBeTrue("sample-complete-chunk");
        p = Partition(new JsonArray(Suite("server", "tunit", A, B), Suite("client", "fresh")), Decisions());
        var dirty = (JsonArray)p["dirty"]!;
        (Count(p, "required") == 3 && dirty.Count == 1 && (string?)dirty[0]!["suite"] == "client" && Strings(dirty[0]!["reasons"]).Contains("fresh-suite-adapter-required"))
            .ShouldBeTrue("non-tunit-suite-cannot-vanish");
        Refuses(() => Partition(new JsonArray(Suite("client", "tunit")), new JsonArray()), "empty-tunit-discovery");
        var ambiguous = (JsonObject)A.DeepClone();
        ambiguous["stable"] = false;
        p = Partition(Suites(ambiguous), Arr(Decision(ambiguous)));
        dirty = (JsonArray)p["dirty"]!;
        (dirty.Count == 1 && Strings(dirty[0]!["reasons"]).Contains("ambiguous-case")).ShouldBeTrue("unstable-discovery-dirty");
        var other = (JsonObject)A.DeepClone();
        other["suite"] = "other";
        p = Partition(new JsonArray(Suite("server", "tunit", A, B), Suite("other", "tunit", other)), Arr(Decision(A), Decision(B), Decision(other)));
        Count(p, "required").ShouldBe(3, "same-method-cross-suite-distinct");
        var mixed = (JsonObject)A.DeepClone();
        mixed["boundary"] = "other";
        Refuses(() => Partition(Suites(A, mixed), Decisions()), "mixed-boundary-chunk");
    }

    [Test]
    public void StatusCountsOriginsWithoutDoubleCharging()
    {
        var p = Partition(Suites(A, B), Decisions());
        var s = RcIncrementalPlan.PartitionStatus(p, new JsonArray());
        (Int(s, "planned") == 2 && Int(s, "carried") == 2 && Int(s, "executed") == 0 && Int(s, "notRun") == 0 && Int(s, "uniqueOriginChunks") == 1
            && Num(s, "historicalPotentialWallSeconds") == 12).ShouldBeTrue("split-source-chunk-once: " + s.ToJsonString());
        p = Partition(Suites(A, B), Arr(Decision(A, "Dirty"), Decision(B, "Sample")));
        var firstLaunch = (string)p["launch"]![0]!["key"]!;
        s = RcIncrementalPlan.PartitionStatus(p, Arr(new JsonObject { ["key"] = firstLaunch, ["outcome"] = "Failed" }));
        (Int(s, "planned") == 2 && Int(s, "carried") == 0 && Int(s, "sampled") == 1 && Int(s, "failed") == 1 && Int(s, "executed") == 1 && Int(s, "notRun") == 1
            && Num(s, "historicalPotentialWallSeconds") == 0).ShouldBeTrue("actual-execution-separate-from-history: " + s.ToJsonString());
        var bad = Decisions();
        bad[1]!["origin"]!["wallSeconds"] = 13;
        Refuses(() => RcIncrementalPlan.PartitionStatus(Partition(Suites(A, B), bad), new JsonArray()), "origin-wall-conflict");
        bad = Decisions();
        bad[1]!["origin"]!["envelopeDigest"] = new string('c', 64);
        Refuses(() => RcIncrementalPlan.PartitionStatus(Partition(Suites(A, B), bad), new JsonArray()), "origin-envelope-conflict");
        Refuses(() => RcIncrementalPlan.PartitionStatus(p, Arr(new JsonObject { ["key"] = "absent", ["outcome"] = "Passed" })), "result-outside-launch");
        var r = new JsonObject { ["key"] = firstLaunch, ["outcome"] = "Unproven" };
        s = RcIncrementalPlan.PartitionStatus(p, Arr(r));
        (Int(s, "unproven") == 1 && Int(s, "notRun") == 1).ShouldBeTrue("unproven-not-green");
        Refuses(() => RcIncrementalPlan.PartitionStatus(p, Arr(r, r)), "duplicate-result");
    }

    [Test]
    public void CrashRevisionCannotPublishClosure()
    {
        // The real entry points bind a current committed fixture and receipts.
        var checkout = RcInputFixtureRepository.SourceRoot;
        var results = Directory.CreateDirectory(Path.Combine(repo.Scratch, "results")).FullName;
        repo.Init();
        repo.Put("input", "input");
        repo.Git("add", "input");
        repo.Git("commit", "-qm", "fixture");
        var sha = repo.Head();
        var tree = repo.Git("rev-parse", "HEAD^{tree}");
        var partition = Partition(Suites(A, B), Decisions());
        var bindings = new JsonObject();
        foreach (var kind in new[] { "input", "output" })
        {
            var receipt = new JsonObject { ["kind"] = kind, ["candidateSha"] = sha, ["treeId"] = tree, ["universeDigest"] = (string?)partition["universeDigest"] };
            var path = Path.Combine(results, kind + ".json");
            File.WriteAllText(path, receipt.ToJsonString());
            bindings[kind] = new JsonObject { ["path"] = path, ["digest"] = RcAdmission.FileDigest(path) };
        }
        var request = new JsonObject
        {
            ["format"] = "antiphon-rc-plan-request-v1", ["candidateSha"] = sha, ["treeId"] = tree, ["platform"] = "linux", ["rcLine"] = "rc/2026-11-23",
            ["suites"] = Suites(A, B), ["decisions"] = Decisions(), ["bindings"] = bindings, ["admissions"] = new JsonArray(), ["exclusionDigest"] = new string('c', 64),
            ["contractDigest"] = new string('d', 64), ["boundaryMapDigest"] = new string('e', 64), ["historyDigest"] = new string('f', 64), ["sampleSeed"] = "fixture-seed",
            ["sampleBudget"] = 30,
        };
        var requestPath = Path.Combine(results, "request.json");
        File.WriteAllText(requestPath, request.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var planPath = Path.Combine(results, "plan.json");
        // The plan entry (antiphon-scripts rc-ledger-plan), in-process.
        var sealedRequest = JsonNode.Parse(RcAdmission.ReadJson(requestPath, RcAdmission.FileDigest(requestPath)).GetRawText());
        RcIncrementalPlan.WritePlan(planPath, RcIncrementalPlan.NewPlan(sealedRequest, repo.Root, sha), checkout);
        var plan = JsonNode.Parse(RcAdmission.ReadJson(planPath, RcAdmission.FileDigest(planPath)).GetRawText())!.AsObject();
        var digest = (string)plan["digest"]!;
        ((string?)plan["effectiveMode"] == "Full" && Count(plan["partition"], "carried") == 0 && Count(plan["partition"], "launch") == 2 && !(bool)plan["gateCredit"]!
            && !(bool)plan["coverageComplete"]! && Strings(plan["forcedReasons"]).Contains("release-graph-incomplete")).ShouldBeTrue("admission-stays-fail-closed");
        var bad = (JsonObject)request.DeepClone();
        bad["bindings"]!["input"]!["digest"] = new string('0', 64);
        var fallback = RcIncrementalPlan.NewPlan(bad, repo.Root, sha);
        (Strings(fallback["forcedReasons"]).Contains("receipt-unproven:input") && Count(fallback["partition"], "launch") == 2).ShouldBeTrue("changed-receipt-full");
        bad = (JsonObject)request.DeepClone();
        bad["decisions"] = Arr(Decision(A));
        fallback = RcIncrementalPlan.NewPlan(bad, repo.Root, sha);
        (Strings(fallback["forcedReasons"]).Contains("proposal-invalid:decision-universe-mismatch") && Count(fallback["partition"], "launch") == 2)
            .ShouldBeTrue("partial-public-proposal-full: " + string.Join(",", Strings(fallback["forcedReasons"])));
        bad = (JsonObject)request.DeepClone();
        bad["sampleSeed"] = "different-seed";
        ((string?)RcIncrementalPlan.NewPlan(bad, repo.Root, sha)["digest"]).ShouldNotBe(digest, "plan-binds-sample-seed");
        bad = (JsonObject)request.DeepClone();
        bad["platform"] = "windows";
        var windows = RcIncrementalPlan.NewPlan(bad, repo.Root, sha);
        ((string?)plan["coverageScope"] == "PlatformDiagnostic" && (string?)windows["coverageScope"] == "IncrementalDiagnostic" && !(bool)windows["coverageComplete"]!
            && !(bool)windows["gateCredit"]!).ShouldBeTrue("platform-labels-grant-no-credit");
        Refuses(() => RcIncrementalPlan.NewPlan(request, repo.Root, new string('1', 40)), "candidate-binding-mismatch");
        Refuses(() => RcIncrementalPlan.WritePlan(planPath, plan, checkout), "write-once");
        var root = Path.Combine(results, "revisions");
        var revision = RcIncrementalPlan.WriteRevision(root, planPath, digest, new JsonArray(), checkout);
        var status = RcIncrementalPlan.ReadStatus(root, digest);
        (Int(status, "notRun") == 2 && !(bool)status["coverageComplete"]! && !(bool)status["gateCredit"]!).ShouldBeTrue("initial-open");
        var failed = new JsonObject { ["key"] = (string)plan["partition"]!["launch"]![0]!["key"]!, ["outcome"] = "Failed" };
        revision = RcIncrementalPlan.WriteRevision(root, planPath, digest, Arr(failed), checkout);
        Refuses(() => RcIncrementalPlan.WriteRevision(root, planPath, digest, new JsonArray(), checkout), "revision-result-history");
        var changed = (JsonObject)failed.DeepClone();
        changed["outcome"] = "Passed";
        Refuses(() => RcIncrementalPlan.WriteRevision(root, planPath, digest, Arr(changed), checkout), "revision-result-history");
        // Orphan durable revision after a crash cannot replace the published pointer.
        Directory.CreateDirectory(Path.Combine(root, "revisions", "3"));
        File.WriteAllText(Path.Combine(root, "revisions", "3", "revision.json"), "{\"state\":\"complete\",\"results\":[\"Passed\"]}");
        var again = RcIncrementalPlan.ReadStatus(root, digest);
        (Int(again, "sequence") == 2 && Int(again, "failed") == 1 && Int(again, "notRun") == 1).ShouldBeTrue("crash-keeps-previous-pointer");
        Refuses(() => RcIncrementalPlan.WriteRevision(root, planPath, digest, Arr(failed), checkout), "revision-already-exists");
        using (File.Open(Path.Combine(root, ".writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Refuses(() => RcIncrementalPlan.WriteRevision(root, planPath, digest, new JsonArray(), checkout), "writer-busy");
        var index = Path.Combine(root, "index.json");
        var before = RcAdmission.FileDigest(index);
        // The status entry (antiphon-scripts rc-ledger-status), in-process: file reads only.
        RcIncrementalPlan.ReadStatus(root, digest);
        RcAdmission.FileDigest(index).ShouldBe(before, "status-read-only");
        File.AppendAllText(Path.Combine(root, (string)revision["path"]!), " ");
        Refuses(() => RcIncrementalPlan.ReadStatus(root, digest), "admission-seal-mismatch");
        var allPassed = new JsonArray(((JsonArray)plan["partition"]!["launch"]!)
            .Select(row => (JsonNode)new JsonObject { ["key"] = (string)row!["key"]!, ["outcome"] = "Passed" }).ToArray());
        var otherRoot = Path.Combine(results, "all-passed");
        var pointer = RcIncrementalPlan.WriteRevision(otherRoot, planPath, digest, allPassed, checkout);
        status = RcIncrementalPlan.ReadStatus(otherRoot, digest);
        (Int(status, "executed") == 2 && Int(status, "notRun") == 0 && !(bool)status["coverageComplete"]! && !(bool)status["gateCredit"]!)
            .ShouldBeTrue("ordinary-results-cannot-publish-closure");
        var revisionPath = Path.Combine(otherRoot, (string)pointer["path"]!);
        var terminal = JsonNode.Parse(RcAdmission.ReadJson(revisionPath, (string)pointer["sha256"]!).GetRawText())!;
        terminal["state"] = "complete";
        File.WriteAllText(revisionPath, terminal.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        pointer["sha256"] = RcAdmission.FileDigest(revisionPath);
        File.WriteAllText(Path.Combine(otherRoot, "index.json"), pointer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Refuses(() => RcIncrementalPlan.ReadStatus(otherRoot, digest), "revision-not-open-or-bound");
    }
}
