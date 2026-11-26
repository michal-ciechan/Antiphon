using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1323: offline policy and frozen Windows authority witnesses (no publication). In process since unit 18 of
/// 2026-11-22-remaining-ps1-to-csharp.md: the cases ran scripts/test-rc-platform.ps1, which is deleted. The rows keep their
/// C1323 names. The launch-plan cases are in ReleaseGatePlatformTests.Plan.cs, the Linux-only census cases in
/// ReleaseGatePlatformTests.Census.cs.
/// </summary>
[Category("Integration")]
public sealed partial class ReleaseGatePlatformTests
{
    private const string PolicyFixtureCard = "CARD-1323";
    private static readonly string[] DeliveryClasses = ["AgentTaskLandDeliveryE2ETests", "DispatchBaseWarningDeliveryE2ETests", "WorktreeRetirementDeliveryE2ETests"];

    /// <summary>
    /// The policy half: the schema 4 platform rows (Linux excludes exactly the 69 delivery cases, Windows keeps them), the
    /// release authority's nine-suite policy, every refusal of a malformed row or stale hash, schema 3 compatibility, the
    /// shipped rows, the selector admitting the schema 4 policy, and a frozen authority whose complete synthetic ledger
    /// validates and refuses once a Windows UID is missing.
    /// </summary>
    [Test, Timeout(300_000)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C1323_PlatformPolicy(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("c1323-policy");
        var rows = new NightlyCaseRows();
        // Supply the new contract even at the pre-fix commit: this must expose the global exclusion defect before the
        // shipped schema is updated.
        var policy = ShippedPolicyJson();
        policy["schemaVersion"] = 4;
        var exclusions = new JsonArray();
        foreach (var row in RcRows(policy).Where(r => !OwnedBy(r, PolicyFixtureCard))) exclusions.Add(row.DeepClone());
        foreach (var cls in DeliveryClasses)
            exclusions.Add(new JsonObject { ["suite"] = "e2e", ["class"] = cls, ["platform"] = "linux", ["reason"] = "Requires Windows ConPTY/native FakeGrok", ["owner"] = PolicyFixtureCard });
        exclusions.Add(LegacyLiveRow());
        policy["profiles"]!["rc"]!["exclusions"] = exclusions;
        var nodes = C1323Nodes();
        var expectedWindows = nodes.Select(n => n.Uid).Where(u => u != "legacy-live").ToArray();
        var windows = RcExclusions(ParsePolicy(policy), "e2e", "windows");
        var actualWindows = Required(nodes, windows);
        var retained = SameSet(actualWindows, expectedWindows);
        rows.Check(retained, "C1323 windows-retains-69-and-siblings", string.Join(",", actualWindows));
        if (!retained)
        {
            rows.ShouldAllPass(20);
            return;
        }
        var linux = NightlyPolicy.ProfileExclusions(ParsePolicy(policy), "rc", "e2e", "linux");
        rows.Check(SameSet(Required(nodes, linux), ["unclassified", "legacy-sibling"]), "C1323 linux-excludes-exactly-69");
        var scopedRows = linux.Where(r => string.Equals(r.Owner, PolicyFixtureCard, StringComparison.OrdinalIgnoreCase)).ToArray();
        rows.Check(scopedRows.Length == 3 && !scopedRows.Any(r => string.IsNullOrEmpty(r.Reason) || string.IsNullOrEmpty(r.Owner)), "C1323 exclusion-reasons-and-owners");
        rows.Check(SameSet(Required(nodes, RcExclusions(ParsePolicy(policy), "e2e", "windows")), expectedWindows), "C1323 default-is-windows");
        var path = fixture.PathOf("policy.json");
        WritePolicy(policy, path);
        rows.Check(NightlyPolicy.Read(Repo, path).SchemaVersion == 4, "C1323 schema4-loads");
        var bytes = File.ReadAllBytes(path);
        var authorityPolicy = ReleaseAuthority.PolicyFromBytes(bytes);
        var expectedSuites = new List<string> { "antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "client", "scripts", "e2e", "markdown-pdf" };
        var suites = policy["suites"]!.AsObject();
        bool HasSuite(string id) => suites.Any(p => string.Equals(p.Key, id, StringComparison.OrdinalIgnoreCase) && p.Value != null);
        foreach (var id in new[] { "observability", "server-git", "resilience", "git-in-process" })
            if (HasSuite(id)) expectedSuites.Add(id);
        var observabilityRequired = true;
        if (HasSuite("observability"))
        {
            var missing = policy.DeepClone().AsObject();
            var rc = missing["profiles"]!["rc"]!.AsObject();
            rc["requiredSuites"] = new JsonArray(rc["requiredSuites"]!.AsArray().Select(s => s!.GetValue<string>()).Where(s => s != "observability").Select(s => (JsonNode?)s).ToArray());
            missing["policyHash"] = NightlyPolicy.HashText(missing.ToJsonString());
            observabilityRequired = Refusal(() => ReleaseAuthority.PolicyFromBytes(Encoding.UTF8.GetBytes(missing.ToJsonString()))) == "policy-rc-suite-missing:observability";
        }
        rows.Check(SameSet(authorityPolicy.RequiredSuites, expectedSuites) && observabilityRequired, "C1323 nine-suite-authority");
        rows.Check(SameSet(ReleaseAuthority.RequiredUids(AuthorityNodes(nodes), authorityPolicy.Exclusions, "e2e"), expectedWindows), "C1323 authority-resolves-windows");

        JsonNode?[] invalid = ["macos", "", "Windows", 1, true, null, new JsonArray("linux"), new JsonObject { ["os"] = "linux" }];
        var missed = new List<string>();
        for (var i = 0; i < invalid.Length; i++)
        {
            var bad = policy.DeepClone().AsObject();
            FirstOwned(bad)["platform"] = invalid[i]?.DeepClone();
            WritePolicy(bad, path);
            var read = NightlyPolicy.Read(Repo, path);
            var message = Message(() => NightlyPolicy.ProfileExclusions(read.Object, "rc", "antiphon", "windows"));
            if (!Regex.IsMatch(message, "exclusion.*platform", RegexOptions.IgnoreCase)) missed.Add(i + "=" + message);
        }
        rows.Check(missed.Count == 0, "C1323 invalid-platforms-refuse-before-filtering", string.Join(";", missed));
        missed.Clear();
        foreach (var field in new[] { "reason", "owner" })
        {
            var bad = policy.DeepClone().AsObject();
            FirstOwned(bad)[field] = "";
            var message = Message(() => NightlyPolicy.ProfileExclusions(ParsePolicy(bad), "rc", "antiphon", "windows"));
            if (!Regex.IsMatch(message, "reason and owner", RegexOptions.IgnoreCase)) missed.Add(field);
        }
        rows.Check(missed.Count == 0, "C1323 invalid-attribution-refuses-before-filtering", string.Join(",", missed));
        var changed = policy.DeepClone().AsObject();
        FirstOwned(changed)["platform"] = "windows";
        rows.Check(NightlyPolicy.HashText(changed.ToJsonString()) != NightlyPolicy.HashText(policy.ToJsonString()), "C1323 platform-only-hash-changes");
        File.WriteAllText(path, changed.ToJsonString());
        rows.Check(Regex.IsMatch(Message(() => NightlyPolicy.Read(Repo, path)), "stale policy hash", RegexOptions.IgnoreCase), "C1323 stale-platform-hash-refuses");
        rows.Check(string.Equals(Message(() => ReleaseAuthority.PolicyFromBytes(File.ReadAllBytes(path))), "policy-hash-stale", StringComparison.OrdinalIgnoreCase),
            "C1323 authority-stale-hash-refuses");
        var historical = policy.DeepClone().AsObject();
        historical["schemaVersion"] = 3;
        historical["profiles"]!["rc"]!["exclusions"] = new JsonArray(RcRows(historical).Where(r => !OwnedBy(r, PolicyFixtureCard)).Select(r => (JsonNode?)r.DeepClone()).ToArray());
        WritePolicy(historical, path);
        var old = NightlyPolicy.Read(Repo, path);
        var oldAuthority = ReleaseAuthority.PolicyFromBytes(File.ReadAllBytes(path));
        rows.Check(old.SchemaVersion == 3 && SameSet(oldAuthority.RequiredSuites, expectedSuites)
            && SameSet(Required(nodes, NightlyPolicy.ProfileExclusions(old.Object, "rc", "e2e", "linux")), expectedWindows), "C1323 schema3-compatibility");

        var shipped = Policy();
        var shippedRows = shipped.Object.Profiles["rc"].Exclusions;
        var shipRows = shippedRows.Where(r => string.Equals(r.Owner, PolicyFixtureCard, StringComparison.OrdinalIgnoreCase)).ToArray();
        var linuxShipRows = shipRows.Where(r => r.Platform == "linux").ToArray();
        rows.Check(shipped.SchemaVersion == 4 && SameSet(shipRows.Select(r => r.Suite + "|" + r.Class + "|" + r.Platform),
                ["e2e|AgentTaskLandDeliveryE2ETests|linux", "e2e|DispatchBaseWarningDeliveryE2ETests|linux", "e2e|WorktreeRetirementDeliveryE2ETests|linux",
                    "antiphon|PosixHarnessToolTests|windows"])
            && linuxShipRows.Length == 3 && linuxShipRows.All(r => r.Suite == "e2e") && SameSet(linuxShipRows.Select(r => r.Class), scopedRows.Select(r => r.Class)),
            "C1323 shipped-three-linux-rows");
        var c1614 = shippedRows.Where(r => string.Equals(r.Owner, "CARD-1614", StringComparison.OrdinalIgnoreCase)).ToArray();
        static bool LinuxOnly(ProfileExclusion r) => (r.Reason ?? "").StartsWith(LinuxOnlyCensus.Prefix, StringComparison.OrdinalIgnoreCase);
        var inbox = c1614.Where(r => r.Class == "HostCustodyTests" && r.Platform == "linux").ToArray();
        var life = c1614.Where(r => r.Class == "LinuxRunnerCustodyLifecycleTests" && r.Platform == "windows" && LinuxOnly(r)).ToArray();
        var boot = c1614.Where(r => r.Class == "CustodyNamespaceBootstrapTests" && r.Platform == "windows" && LinuxOnly(r)).ToArray();
        var nativeHost = c1614.Where(r => r.Suite == "pty-host" && r.Class == "LinuxCustodyReleaseHostTests" && r.Platform == "windows" && LinuxOnly(r)
            && r.Methods.All(string.IsNullOrWhiteSpace)).ToArray();
        var nativeHelper = c1614.Where(r => r.Suite == "agents-pty" && r.Class == "LinuxCgroupContainmentTests" && r.Platform == "windows" && LinuxOnly(r)
            && SameSet(r.Methods, ["C1614_Helper_written_tree_is_read_by_production_reader"])).ToArray();
        var inboxMethods = inbox.SelectMany(r => r.Methods).ToArray();
        rows.Check(c1614.Length == 5 && nativeHost.Length == 1 && nativeHelper.Length == 1 && inbox.Length == 1
            && inboxMethods.Contains("Inbox_is_unsupported_and_never_becomes_no_start_proof", StringComparer.OrdinalIgnoreCase)
            && inboxMethods.Contains("C1614_Windows_tracked_linger_releases_job", StringComparer.OrdinalIgnoreCase) && !LinuxOnly(inbox[0])
            && life.Length == 1 && boot.Length == 1 && shipped.Hash == shipped.Object.PolicyHash, "C1323 card-1614-mixed-platform-rows");
        var shippedCensus = NightlyPolicy.TestProjectUniverse(Repo, shipped.Object);
        rows.Check(shippedCensus.Ok && shipped.Object.SelectionCatalog?.Path == "tests/test-selection-policy.json"
            && shippedRows.Count(r => string.Equals(r.Class, "ChannelOutboundIsolatedTests", StringComparison.OrdinalIgnoreCase)) == 1
            && shippedRows.Any(r => string.Equals(r.Owner, "CARD-1313", StringComparison.OrdinalIgnoreCase)), "C1323 master-inventory-preserved", shippedCensus.Message);

        var root = fixture.PathOf("git-policy");
        Directory.CreateDirectory(root);
        FixtureGit(root, "init", "--quiet");
        FixtureGit(root, "config", "user.name", "C1323 fixture");
        FixtureGit(root, "config", "user.email", "c1323@example.invalid");
        FixtureGit(root, "config", "core.autocrlf", "false");
        FixtureGit(root, "remote", "add", "origin", "https://github.com/fixture/c1323");
        foreach (var file in new[] { "tests/test-selection-policy.json" }.Concat(TestSelection.SelectorFiles))
            WriteFixtureFile(root, file, File.ReadAllText(Path.Combine(Repo, file)));
        WriteFixtureFile(root, "tests/test-execution-policy.json", Encoding.UTF8.GetString(bytes));
        var catalog = TestSelectionCatalog.Read(Path.Combine(root, "tests", "test-selection-policy.json"), policy["selectionCatalog"]!["hash"]!.GetValue<string>());
        var inputs = new List<object?>();
        foreach (var set in Items(catalog["sets"]))
        {
            inputs.AddRange(Items(Field(set, "sourceInputs")));
            foreach (var member in Items(Field(set, "members"))) inputs.Add(Field(member, "source"));
        }
        foreach (var input in TestSelectionJson.Sorted(inputs.Where(i => i != null).ToArray()))
            if (input.Length > 0) WriteFixtureFile(root, input, "fixture source");
        FixtureGit(root, "add", "--all");
        FixtureGit(root, "commit", "--quiet", "-m", "fixture");
        var sha = FixtureGit(root, "rev-parse", "HEAD");
        var selection = TestSelection.Select(root, sha, sha, "rc");
        rows.Check(selection != null && PsValue.Str(selection["candidateSha"]) == sha, "C1323 schema4-selector-admitted");
        FrozenLedger(fixture, rows, root, sha, nodes, expectedWindows);
        rows.ShouldAllPass(20, "C1323 windows-retains-69-and-siblings", "C1323 linux-excludes-exactly-69", "C1323 schema4-selector-admitted",
            "C1323 pinned-authority-validates-windows", "C1323 complete-synthetic-ledger-validates", "C1323 missing-windows-uid-refuses");
    }

    // Test-C1323FrozenLedger: the authority frozen at the fixture commit, its plan, a complete synthetic ledger (one TRX per
    // chunk) and the same ledger with one Windows UID missing. Only card I/O is supplied here; every authority, membership,
    // digest and credit decision remains production code. No HTTP or publisher is invoked.
    private static void FrozenLedger(NightlyFixture fixture, NightlyCaseRows rows, string root, string sha, IReadOnlyList<C1323Node> nodes, string[] expectedWindows)
    {
        var candidate = fixture.PathOf("candidate");
        var scripts = Path.Combine(Repo, "scripts");
        var authority = ReleaseAuthority.Freeze(root, candidate, sha, "fixture/c1323", "fixture", "fixture-ref", "fixture-intent", "13231323-1323-1323-1323-132313231323",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), scripts);
        var pinned = ReleaseAuthority.Pinned(candidate, root, sha, "fixture", "fixture-ref", "fixture/c1323", scripts);
        rows.Check(pinned.Ok && SameSet(ReleaseAuthority.RequiredUids(AuthorityNodes(nodes), authority.Exclusions, "e2e"), expectedWindows),
            "C1323 pinned-authority-validates-windows", string.Join(",", pinned.Reasons));
        var discovery = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var suite in authority.RequiredSuites)
        {
            if (suite.Equals("scripts", StringComparison.OrdinalIgnoreCase) || suite.Equals("client", StringComparison.OrdinalIgnoreCase)) continue;
            discovery[suite] = new object[] { new Hashtable { ["uid"] = suite + "-ordinary", ["class"] = "OfflineTests", ["method"] = "Runs" } };
        }
        discovery["e2e"] = AuthorityNodes(nodes);
        var plan = ReleaseAuthority.Plan(candidate, discovery, new Hashtable { ["scripts"] = new object[] { "script" }, ["client"] = new object[] { "client" } });
        var dir = ReleaseAuthority.Dir(candidate);
        var planPath = Path.Combine(dir, "execution-plan.json");
        var ledgerPath = Path.Combine(dir, "ledger.json");
        const string start = "2026-01-01T00:01:00Z", end = "2026-01-01T00:02:00Z";
        var chunks = new List<OrderedDictionary>();
        var rosters = new List<object>();
        var ledger = new OrderedDictionary
        {
            { "intentId", authority.IntentId }, { "candidateId", authority.CandidateId }, { "candidateRef", authority.CandidateRef }, { "sha", sha }, { "runId", "fixture-run" },
            { "authorityDigest", pinned.Digest }, { "executionPlanDigest", ReleaseAuthority.FileSha256(planPath) },
            { "teardownSucceeded", true }, { "seamed", false }, { "noReport", false }, { "diagnostic", false }, { "selection", "full" },
            { "startedAt", start }, { "completedAt", end }, { "chunks", chunks }, { "rosters", rosters },
            { "prerequisites", new object[]
                {
                    new OrderedDictionary { { "id", "build" }, { "exitCode", 0 }, { "succeeded", true } },
                    new OrderedDictionary { { "id", "client-bundle" }, { "exitCode", 0 }, { "succeeded", true } },
                    new OrderedDictionary { { "id", "script-census" }, { "exitCode", 0 }, { "succeeded", true } },
                } },
            { "prepublicationReceipt", new OrderedDictionary
                {
                    { "kind", ReleaseAuthority.ReceiptKind },
                    { "recipient", new OrderedDictionary { { "kind", ReleaseAuthority.RecipientKind }, { "cardId", authority.ReleaseCardId }, { "revision", 1 } } },
                    { "correlation", new OrderedDictionary { { "intentId", authority.IntentId }, { "candidateId", authority.CandidateId }, { "sha", sha }, { "runId", "fixture-run" } } },
                    { "acceptedAt", end },
                } },
        };
        OrderedDictionary Common(string suite) => new()
        {
            { "intentId", authority.IntentId }, { "candidateId", authority.CandidateId }, { "sha", sha }, { "runId", "fixture-run" }, { "startedAt", start },
            { "completedAt", end }, { "exitCode", 0 }, { "suite", suite },
        };
        static NightlyTrxRow[] TrxRows(IDictionary chunk) =>
            ((IEnumerable)chunk["uids"]!).Cast<string>().Select(u => new NightlyTrxRow(u, ((string[])chunk["classes"]!)[0], "Expanded")).ToArray();
        foreach (IDictionary suite in (IEnumerable)plan["suites"]!)
        {
            var id = (string)suite["id"]!;
            if ((string?)suite["kind"] == "roster")
            {
                var row = Common(id);
                row["entries"] = ((IEnumerable)suite["entries"]!).Cast<string>().Select(e => (object)new OrderedDictionary { { "id", e }, { "outcome", "passed" } }).ToArray();
                rosters.Add(row);
                continue;
            }
            foreach (IDictionary chunk in (IEnumerable)suite["chunks"]!)
            {
                var row = Common(id);
                row["chunk"] = chunk["id"];
                row["trx"] = chunk["id"] + ".trx";
                var trx = Path.Combine(candidate, (string)row["trx"]!);
                var trxRows = TrxRows(chunk);
                NightlyFixture.WriteTrx(trx, trxRows);
                row["trxSha256"] = ReleaseAuthority.FileSha256(trx);
                row["executed"] = trxRows.Length;
                row["passed"] = trxRows.Length;
                row["failed"] = 0;
                row["skipped"] = 0;
                chunks.Add(row);
            }
        }
        var correlation = ReleaseAuthority.CorrelationPrefix + " intentId=" + authority.IntentId + " candidateId=" + authority.CandidateId + " sha=" + sha + " runId=fixture-run";
        CardReadback Card(string baseUrl, string cardId) =>
            new() { Ok = true, Card = PlainJson.Parse(JsonSerializer.Serialize(new { id = cardId, revisionCount = 1, description = correlation })) };
        NightlyFixture.WriteJson(ledgerPath, ledger);
        var green = new Hashtable { ["runId"] = "fixture-run" };
        var now = new DateTime(2026, 1, 1, 0, 3, 0, DateTimeKind.Utc);
        var valid = ReleaseAuthority.Ledger(candidate, pinned, green, now, "", Card);
        rows.Check(valid.Ok, "C1323 complete-synthetic-ledger-validates", string.Join(",", valid.Reasons));

        const string missing = "AgentTaskLandDeliveryE2ETests-1";
        var e2e = ((IEnumerable)plan["suites"]!).Cast<IDictionary>().First(s => (string?)s["id"] == "e2e");
        var first = ((IEnumerable)e2e["chunks"]!).Cast<IDictionary>().First();
        first["uids"] = ((IEnumerable)first["uids"]!).Cast<string>().Where(u => u != missing).ToArray();
        PlainJson.WriteAtomic(planPath, plan);
        ledger["executionPlanDigest"] = ReleaseAuthority.FileSha256(planPath);
        var changed = chunks.First(c => Equals(c["chunk"], first["id"]));
        var changedTrx = Path.Combine(candidate, (string)changed["trx"]!);
        NightlyFixture.WriteTrx(changedTrx, TrxRows(first));
        changed["trxSha256"] = ReleaseAuthority.FileSha256(changedTrx);
        changed["executed"] = (int)changed["executed"]! - 1;
        changed["passed"] = (int)changed["passed"]! - 1;
        NightlyFixture.WriteJson(ledgerPath, ledger);
        var refused = ReleaseAuthority.Ledger(candidate, pinned, green, now, "", Card);
        rows.Check(!refused.Ok && SameSet(refused.Reasons, ["plan-uid-missing:" + missing, "uid-missing:" + missing]), "C1323 missing-windows-uid-refuses",
            string.Join(",", refused.Reasons));
    }

    /// <summary>
    /// The tests half: NightlyTests.Run per platform on the offline e2e fixture (one required suite, an owned clone committed
    /// with real git), its launcher answering as TUnit would (discovery, the TRX, execution and discovery documents of the
    /// filtered nodes). Linux launches only the portable cases and its summary is a diagnostic; Windows keeps the delivery
    /// cases and stays Full; an unknown platform launches nothing.
    /// </summary>
    [Test, Timeout(300_000)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C1323_PlatformExecution(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("c1323-execution");
        var rows = new NightlyCaseRows();
        var clone = fixture.NewOwnedClone("https://github.com/fixture/c1323");
        // A one-suite offline policy keeps the executor bounded; the policy case pins the real suites.
        var policy = ShippedPolicyJson();
        policy["suites"] = new JsonObject { ["e2e"] = policy["suites"]!["e2e"]!.DeepClone() };
        policy["profiles"]!["rc"]!["requiredSuites"] = new JsonArray("e2e");
        policy["projects"] = new JsonArray("tests/Antiphon.E2E/Antiphon.E2E.csproj");
        policy["projectDispositions"] = new JsonArray(new JsonObject { ["project"] = "tests/Antiphon.E2E/Antiphon.E2E.csproj", ["role"] = "primary", ["reason"] = "offline fixture" });
        policy["profiles"]!["rc"]!["exclusions"]!.AsArray().Add(LegacyLiveRow());
        WriteFixtureFile(clone, "tests/Antiphon.E2E/Antiphon.E2E.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        foreach (var file in new[] { "global.json", "tests/test-selection-policy.json" }.Concat(TestSelection.SelectorFiles))
            WriteFixtureFile(clone, file, File.ReadAllText(Path.Combine(Repo, file)));
        WritePolicy(policy, Path.Combine(clone, "tests", "test-execution-policy.json"));
        FixtureGit(clone, "init", "--quiet");
        FixtureGit(clone, "config", "user.name", "C1323 fixture");
        FixtureGit(clone, "config", "user.email", "c1323@example.invalid");
        FixtureGit(clone, "config", "core.autocrlf", "false");
        FixtureGit(clone, "add", "--all");
        FixtureGit(clone, "commit", "--quiet", "-m", "offline execution fixture");
        FixtureGit(clone, "config", "--local", "core.longpaths", "true");
        var sha = FixtureGit(clone, "rev-parse", "HEAD");
        var nodes = C1323Nodes();
        // These are synthetic nodes with seamed execution, not the costly native delivery cases sharing their names: the
        // standard fallback node model prices them.
        var costs = fixture.PathOf("class-costs.json");
        NightlyFixture.WriteJson(costs, new { format = "antiphon-nightly-class-costs-v1", classes = new { } });
        var commands = fixture.PathOf("commands");
        WriteFixtureFile(commands, "npm.cmd", "fixture launcher, never executed");

        var linux = RunOn("linux", out var linuxLaunched, out _);
        var portable = SameSet(linuxLaunched, ["unclassified", "legacy-sibling"]);
        rows.Check(portable, "C1323 linux-launches-only-portable-cases", string.Join(",", linuxLaunched));
        if (!portable)
        {
            rows.ShouldAllPass(8);
            return;
        }
        var summary = NightlyTestsWorld.Summary(linux);
        rows.Check(Flag(summary, "testsPassed") && !Flag(summary, "coverageComplete") && Text(summary, "coverageScope") == "PlatformDiagnostic"
            && Text(summary, "platform") == "linux" && Reasons(summary).Contains("windows-rc-obligations-outstanding", StringComparer.OrdinalIgnoreCase),
            "C1323 linux-summary-is-diagnostic", summary.ToString());
        var census = summary.GetProperty("suites").EnumerateArray()
            .Where(s => Number(s, "requiredUidCount") == 2 && Number(s, "excludedUidCount") == 70).ToArray();
        // D-5: the suite's exclusion list is recorded once, in suiteExclusions, when every chunk row's list is the same.
        var rowKeepsList = true;
        var censusExclusions = Array.Empty<JsonElement>();
        if (census.Length > 0)
        {
            rowKeepsList = census[0].TryGetProperty("exclusions", out _);
            if (summary.TryGetProperty("suiteExclusions", out var suiteExclusions) && suiteExclusions.ValueKind == JsonValueKind.Object
                && suiteExclusions.TryGetProperty(Text(census[0], "id"), out var list) && list.ValueKind == JsonValueKind.Array)
                censusExclusions = list.EnumerateArray().ToArray();
        }
        rows.Check(census.Length > 0 && !rowKeepsList && censusExclusions.Count(e => string.Equals(Text(e, "owner"), PolicyFixtureCard, StringComparison.OrdinalIgnoreCase)) == 69,
            "C1323 linux-census-retains-exclusion-attribution");
        rows.Check(linuxLaunched.Contains("unclassified") && linuxLaunched.Contains("legacy-sibling") && !linuxLaunched.Contains("legacy-live"),
            "C1323 optin-and-legacy-method-semantics");

        var windows = RunOn("windows", out var windowsLaunched, out _);
        rows.Check(SameSet(windowsLaunched, nodes.Select(n => n.Uid).Where(u => u != "legacy-live")), "C1323 windows-retains-delivery-cases", string.Join(",", windowsLaunched));
        summary = NightlyTestsWorld.Summary(windows);
        rows.Check(windows.ExitCode == 0 && Flag(summary, "testsPassed") && Flag(summary, "coverageComplete") && Text(summary, "coverageScope") == "Full"
            && Text(summary, "platform") == "windows" && Reasons(summary).Length == 0, "C1323 windows-summary-remains-full", summary.ToString());

        var unknown = RunOn("unknown", out _, out var unknownCommands);
        var record = NightlyRun.RecordLine(new NightlyRunResult { ExitCode = unknown.ExitCode, Refusal = unknown.Refusal });
        const string expectedRefusal = "{\"nativeRunId\":\"\",\"sha\":\"\",\"ref\":\"\",\"trigger\":\"\",\"profile\":\"\",\"candidateId\":\"\",\"creditKind\":\"\",\"localDueDate\":\"\","
            + "\"policyHash\":\"\",\"coverageComplete\":false,\"testsPassed\":false,\"reportDelivered\":false,\"exitCode\":3,\"summaryPath\":\"\",\"refusal\":\"unsupported-host-platform\"}";
        rows.Check(unknown.ExitCode == 3 && unknown.Refusal == "unsupported-host-platform" && unknownCommands == 0 && record == expectedRefusal,
            "C1323 unknown-platform-launches-nothing", record);
        rows.Check(linux.CoverageScope == "PlatformDiagnostic" && linux.Reasons.Contains("windows-rc-obligations-outstanding", StringComparer.OrdinalIgnoreCase),
            "C1323 diagnostic-return-value");
        rows.ShouldAllPass(8, "C1323 linux-launches-only-portable-cases", "C1323 windows-retains-delivery-cases", "C1323 linux-summary-is-diagnostic",
            "C1323 unknown-platform-launches-nothing");

        // One direct run on the platform: the host probe answers it, git is real, PATH leads with the fixture's npm.
        NightlyTestsResult RunOn(string platform, out List<string> launched, out int launchCount)
        {
            var started = new List<string>();
            var launcher = new RecordingLauncher(launch => TestHost(launch, nodes, started));
            var host = new NightlyHost
            {
                Clock = TimeProvider.System, Git = NightlyNativeGit.Run, Launcher = launcher, Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe { PlatformName = platform }, Out = fixture.Out, Error = fixture.Error, Trace = TextWriter.Null,
                Variables = name => string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase)
                    ? commands + Path.PathSeparator + Environment.GetEnvironmentVariable(name)
                    : Environment.GetEnvironmentVariable(name),
            };
            var bindings = NightlyTestsWorld.SeamedBindings();
            bindings.E2EPrerequisites = (_, _, _, _) =>
                new PlainObject(("Ok", true), ("Rows", new object[] { new PlainObject(("name", "offline-browser"), ("exitCode", 0)) }));
            bindings.ClassCostPath = costs;
            var options = new NightlyTestsOptions { RepoRoot = clone, LogRoot = fixture.PathOf(platform + "-direct"), Profile = "rc", Sha = sha };
            var result = NightlyTests.Run(options, host, bindings);
            launched = started;
            launchCount = launcher.Launches.Count;
            return result;
        }
    }

    // The StartProcess seam of the execution fixture: discovery writes every node to log.diag; an execution launch keeps the
    // nodes its --treenode-filter admits (each OR operand with its wildcards, as TUnit matches them), records their UIDs,
    // writes their TRX and execution log.diag, and the child's discovery document of every node beside the TRX.
    private static NightlyLaunchResult TestHost(NightlyLaunch launch, IReadOnlyList<C1323Node> nodes, List<string> launched)
    {
        var argv = (launch.Arguments ?? []).ToArray();
        string After(string flag) => argv[Array.IndexOf(argv, flag) + 1];
        var discovery = new
        {
            format = NightlyDiagnosticLog.DiscoveryFormat, tunitVersion = NightlyDiagnosticLog.SupportedTunit, mtpVersion = NightlyDiagnosticLog.SupportedMtp,
            assemblyHash = "c487-test-hash",
            nodes = nodes.Select(n => new { uid = n.Uid, type = n.Type, className = n.Class, @namespace = "Antiphon.E2E", method = n.Method, categories = n.Categories, state = "Discovered" }).ToArray(),
        };
        if (argv.Contains("--list-tests", StringComparer.OrdinalIgnoreCase))
        {
            NightlyFixture.WriteJson(Path.Combine(After("--diagnostic-output-directory"), "log.diag"), discovery);
        }
        else if (argv.Contains("--report-trx-filename", StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<C1323Node> selected = nodes;
            if (Array.IndexOf(argv, "--treenode-filter") >= 0)
            {
                var parts = After("--treenode-filter").Split('/');
                selected = nodes.Where(n => FilterPart("Antiphon.E2E", parts[2]) && FilterPart(n.Type, parts[3]) && FilterPart(n.Method, parts[4]));
            }
            var run = selected.ToArray();
            lock (launched) launched.AddRange(run.Select(n => n.Uid));
            var trx = Path.Combine(After("--results-directory"), After("--report-trx-filename"));
            NightlyFixture.WriteTrx(trx, run.Select(n => new NightlyTrxRow(n.Uid, n.Class, n.Method)));
            NightlyFixture.WriteExecution(Path.Combine(After("--diagnostic-output-directory"), "log.diag"),
                run.Select(n => new NightlyTestNode(n.Uid, n.Type, n.Method, State: "Passed", ClassName: n.Class)));
            // This is child discovery output, never a precomputed census or verdict.
            NightlyFixture.WriteJson(Path.ChangeExtension(trx, ".discovery.json"), discovery);
        }
        return new NightlyLaunchResult { ExitCode = 0, TimedOut = false, ChildrenExited = true, LogText = "offline child completed" };
    }

    // CARD-1569: each TUnit OR operand of a filter segment, keeping its wildcards (-like: * and ?, ignoring case).
    private static bool FilterPart(string value, string pattern) =>
        pattern.Split('|').Any(operand => Regex.IsMatch(value, "^" + Regex.Escape(operand.Trim('(', ')')).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));

    /// <summary>
    /// The run half (in-process since W2-nightly S20; it ran the PowerShell run core on the platform seams): NightlyRun.Run of a
    /// -NoSync rc run with the selection bound, its tests hop answered with what the tests half writes on each platform. A Linux
    /// run keeps the executor's PlatformDiagnostic scope and reasons in last-run.json and the completion record, earns no credit
    /// (full-credit-scope-required) and leaves both green files as they were; a Windows run stays Full and earns rc credit.
    /// </summary>
    [Test, Timeout(300_000)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C1323_PlatformRunCredit(CancellationToken token)
    {
        const string candidateRef = "rc/2026-10-20";
        using var chain = new Antiphon.Tests.Scripts.Nightly.NightlyRcChain("c1323-run", candidateRef).Publish();
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var releaseRoot = chain.Fixture.PathOf("releases");
        var candidateId = chain.CandidateId();
        var green = Path.Combine(releaseRoot, "candidates", candidateId, "complete-green.json");
        var masterGreen = chain.Fixture.PathOf("master", "last-complete-green.json");
        Directory.CreateDirectory(Path.GetDirectoryName(green)!);
        Directory.CreateDirectory(Path.GetDirectoryName(masterGreen)!);
        File.WriteAllText(green, "candidate sentinel");
        File.WriteAllText(masterGreen, "master sentinel");
        foreach (var platform in new[] { "linux", "windows" })
        {
            var linux = platform == "linux";
            chain.TestsAnswer = options =>
            {
                Directory.CreateDirectory(options.LogRoot);
                var summary = new System.Text.Json.Nodes.JsonObject
                {
                    ["runId"] = options.RunId, ["sha"] = options.Sha, ["profile"] = options.Profile, ["testsPassed"] = true, ["coverageComplete"] = !linux,
                    ["coverageScope"] = linux ? "PlatformDiagnostic" : "Full", ["platform"] = platform,
                    ["reasons"] = linux ? new System.Text.Json.Nodes.JsonArray("windows-rc-obligations-outstanding") : new System.Text.Json.Nodes.JsonArray(),
                    ["policyHash"] = "c1323-policy", ["reportDelivered"] = true,
                };
                File.WriteAllText(Path.Combine(options.LogRoot, "summary.json"), summary.ToJsonString());
                return 0;
            };
            var state = chain.Fixture.PathOf("state-" + platform);
            var run = chain.Run(chain.Options("offline-" + platform, o =>
            {
                o.WhatIf = false; o.NoReport = false; o.CandidateId = candidateId; o.Repository = "fixture/c1323"; o.ReleaseRoot = releaseRoot;
                o.StateRoot = state; o.LogRoot = chain.Fixture.PathOf("run-" + platform); o.CoordinationRoot = chain.Fixture.PathOf("coordination");
            }));
            var last = Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Json(Path.Combine(state, "last-run.json"))!.AsObject();
            string LastText(string name) => last[name]?.GetValue<string>() ?? "";
            bool LastFlag(string name) => last[name]?.GetValue<bool>() == true;
            var reasons = last["reasons"]?.AsArray().Select(r => r?.GetValue<string>() ?? "").ToArray() ?? [];
            if (linux)
            {
                var record = NightlyRunRecord(run);
                rows.Check(LastFlag("testsPassed") && !LastFlag("coverageComplete") && LastText("coverageScope") == "PlatformDiagnostic" && LastText("platform") == "linux"
                    && reasons.Contains("windows-rc-obligations-outstanding") && record.Scope == "PlatformDiagnostic" && record.Reasons.Contains("windows-rc-obligations-outstanding"),
                    "C1323 linux-state-is-diagnostic", last.ToJsonString());
                var verdict = Antiphon.Nightly.ReleaseGateCredit.Verdict(last.ToDictionary(p => p.Key, p => Plain(p.Value)));
                rows.Check(run.CreditKind == "" && verdict.Reasons.Contains("full-credit-scope-required"), "C1323 linux-credit-refuses", string.Join(",", verdict.Reasons));
                rows.Check(File.ReadAllText(green) == "candidate sentinel" && File.ReadAllText(masterGreen) == "master sentinel", "C1323 linux-leaves-green-files-identical");
            }
            else
            {
                rows.Check(run.ExitCode == 0 && LastFlag("coverageComplete") && LastText("coverageScope") == "Full" && LastText("platform") == "windows" && run.CreditKind == "rc-release"
                    && File.ReadAllText(green) != "candidate sentinel", "C1323 windows-state-preserves-full-behavior", last.ToJsonString() + " " + run.Refusal + "\n" + chain.Fixture.Out);
            }
        }
        rows.ShouldAllPass(4, "C1323 linux-state-is-diagnostic", "C1323 linux-credit-refuses", "C1323 linux-leaves-green-files-identical", "C1323 windows-state-preserves-full-behavior");
    }

    private static (string Scope, string[] Reasons) NightlyRunRecord(Antiphon.Nightly.NightlyRunResult run)
    {
        var record = Antiphon.Nightly.NightlyRun.ResultRecord(run);
        var reasons = (record["reasons"] as object[] ?? []).Select(r => Antiphon.Nightly.PsValue.Str(r)).ToArray();
        return (Antiphon.Nightly.PsValue.Str(record["coverageScope"]), reasons);
    }

    private static object? Plain(System.Text.Json.Nodes.JsonNode? node) => node switch
    {
        System.Text.Json.Nodes.JsonValue v when v.TryGetValue<bool>(out var b) => b,
        System.Text.Json.Nodes.JsonValue v when v.TryGetValue<int>(out var i) => i,
        System.Text.Json.Nodes.JsonValue v when v.TryGetValue<string>(out var s) => s,
        System.Text.Json.Nodes.JsonArray a => a.Select(Plain).ToArray(),
        null => null,
        _ => node.ToJsonString(),
    };

    // C1415_SkipExclusions and LinuxSessionRunnerAdmission: ReleaseGatePlatformTests.Plan.cs (in-process since W2-nightly S11).
    // LinuxOnlyWindowsExclusionPolicy and C1686_LinuxOnlyCensus: ReleaseGatePlatformTests.Census.cs.

    /// <summary>
    /// S-A census contract: the compiled S/T/O identities, minus the Windows confinement sibling, are the 37 checkpoint
    /// identities, required on both platforms; a NotExecuted result for any of them keeps coverage incomplete; and the
    /// checkpoint namespace census declares exactly the six unrelated Windows and seven Linux runtime-skip patterns.
    /// </summary>
    [Test]
    public void C1199_WindowsRequiredRoster()
    {
        var rows = new NightlyCaseRows();
        var classes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests",
            "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests",
            "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests",
        };
        const string sibling =
            "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset";
        // The compiled S/T/O identities; test source is never scanned for this roster.
        var roster = CheckpointRoster.CompiledCases(typeof(CheckpointShapeConflictTests).Assembly, "Antiphon.Tests.Checkpoints")
            .Where(row => classes.Contains(row.ClassName))
            .Select(row => row.ClassName + "." + row.Method)
            .Where(name => !string.Equals(name, sibling, StringComparison.Ordinal))
            .ToArray();
        var unique = roster.Distinct(StringComparer.Ordinal).Count();
        var legacy = roster.Count(n => n.EndsWith(".shape_conflict_rule", StringComparison.OrdinalIgnoreCase));
        rows.Check(roster.Length == 37 && unique == 37 && SameSet(roster, ExpectedC1199Roster), "C1323 c1199-roster-37",
            "count=" + roster.Length + " unique=" + unique + " shape_conflict_rule=" + legacy);

        var shipped = Policy();
        bool Excluded(string id, string platform)
        {
            var dot = id.LastIndexOf('.');
            var cls = id.Substring(0, dot);
            var probe = new PlainObject(("ClassName", cls), ("Type", cls), ("Method", id.Substring(dot + 1)));
            return NightlyPolicy.NodeExcluded(probe, Exclusions(shipped, "rc", "antiphon", platform));
        }
        var windowsMiss = ExpectedC1199Roster.Where(id => Excluded(id, "windows")).ToArray();
        var linuxMiss = ExpectedC1199Roster.Where(id => Excluded(id, "linux")).ToArray();
        rows.Check(windowsMiss.Length == 0, "C1323 c1199-windows-required", string.Join(",", windowsMiss));
        rows.Check(linuxMiss.Length == 0, "C1323 c1199-linux-required", string.Join(",", linuxMiss));
        var gapMiss = new List<string>();
        foreach (var id in ExpectedC1199Roster)
        {
            var verdict = CoverageControl(ExpectedC1199Roster.Select(x => (x, x == id ? "NotExecuted" : "Passed")), shipped)[0];
            var reason = string.Join(",", Strings(verdict, "reasons"));
            if (PsValue.IsTrue(PsValue.Prop(verdict, "coverageComplete")) || !reason.Contains(id, StringComparison.OrdinalIgnoreCase)) gapMiss.Add(id);
        }
        rows.Check(gapMiss.Count == 0, "C1323 c1199-each-gap-incomplete", string.Join(",", gapMiss));
        var complete = CoverageControl(ExpectedC1199Roster.Select(x => (x, "Passed")), shipped)[0];
        var completeReason = string.Join(",", Strings(complete, "reasons"));
        rows.Check(PsValue.IsTrue(PsValue.Prop(complete, "coverageComplete")) && Num(complete, "passed") == 37
            && !completeReason.Contains("unexecuted-outcome", StringComparison.OrdinalIgnoreCase), "C1323 c1199-all-passed-complete", completeReason);

        rows.ShouldAllPass(5, "C1323 c1199-roster-37", "C1323 c1199-windows-required", "C1323 c1199-linux-required",
            "C1323 c1199-each-gap-incomplete", "C1323 c1199-all-passed-complete");
    }

    private static readonly string[] ExpectedC1199Roster =
    {
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_directory_on_file",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_file_on_directory",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_symlink_on_file",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_untracked_descendant",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_ignored_descendant",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_staged_untracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_modified_tracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_tracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_clean_build",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_regular_user_move",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_untracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_changed_untracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_stamp",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_output_file",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_output_directory",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_output_symlink",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.missing_receipt_is_not_negative_success",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_fixture_sdk_project_text_count",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.local_output_property_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.intermediate_output_path_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.generated_target_file_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.imported_props_reference_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.transitive_reference_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.dirty_tracked_tree_is_refused_and_unmodified",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.clean_untracked_output_build_exits_zero",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.tracked_snapshot_contains_committed_files",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_untracked_file_is_kept_and_indexed",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_ignored_file_is_kept_and_indexed",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.index_added_file_is_kept_and_indexed",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.passing_script_requires_writable_source_receipt",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.baseline_staged_fixture_sdk_project_text_count",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build",
    };

    // ---- the C1323 fixture ---------------------------------------------------------------------------------------------

    /// <summary>A synthetic e2e node: its UID, the full class name, its simple name (the type), the method and categories.</summary>
    private sealed record C1323Node(string Uid, string Class, string Type, string Method, string[] Categories);

    // New-C1323Nodes: the 69 expanded delivery cases (52, 12 and 5), an unclassified OptIn case and a mixed class whose Live
    // method is a legacy exclusion and whose Offline sibling is required.
    private static List<C1323Node> C1323Nodes()
    {
        var nodes = new List<C1323Node>();
        foreach (var (cls, count) in new[] { ("AgentTaskLandDeliveryE2ETests", 52), ("DispatchBaseWarningDeliveryE2ETests", 12), ("WorktreeRetirementDeliveryE2ETests", 5) })
            for (var i = 1; i <= count; i++) nodes.Add(new C1323Node(cls + "-" + i, "Antiphon.E2E." + cls, cls, "Expanded", ["OptIn"]));
        nodes.Add(new C1323Node("unclassified", "Antiphon.E2E.NewOptInTests", "NewOptInTests", "Runs", ["OptIn"]));
        nodes.Add(new C1323Node("legacy-live", "Antiphon.E2E.MixedTests", "MixedTests", "Live", []));
        nodes.Add(new C1323Node("legacy-sibling", "Antiphon.E2E.MixedTests", "MixedTests", "Offline", []));
        return nodes;
    }

    // Get-C1323Required: the UIDs a profile lane keeps (the census disposition of { ClassName, Type, Method, Categories }).
    private static string[] Required(IEnumerable<C1323Node> nodes, object exclusions) =>
        nodes.Where(n => PsValue.Str(PsValue.Prop(NightlyCensus.Disposition(
                new PlainObject(("ClassName", n.Class), ("Type", n.Class), ("Method", n.Method), ("Categories", n.Categories.Cast<object>().ToArray())), exclusions, true),
            "Disposition")) == "required")
            .Select(n => n.Uid).ToArray();

    // The nodes as the authority reads them (a dictionary per node).
    private static object[] AuthorityNodes(IEnumerable<C1323Node> nodes) =>
        nodes.Select(n => (object)new Hashtable { ["uid"] = n.Uid, ["class"] = n.Class, ["method"] = n.Method, ["categories"] = n.Categories }).ToArray();

    private static JsonObject LegacyLiveRow() =>
        new() { ["suite"] = "e2e", ["class"] = "MixedTests", ["methods"] = new JsonArray("Live"), ["reason"] = "legacy live method", ["owner"] = "fixture" };

    private static JsonObject ShippedPolicyJson() => JsonNode.Parse(File.ReadAllText(Path.Combine(Repo, "tests", "test-execution-policy.json")))!.AsObject();

    private static IEnumerable<JsonObject> RcRows(JsonObject policy) => policy["profiles"]!["rc"]!["exclusions"]!.AsArray().Select(r => r!.AsObject());

    private static bool OwnedBy(JsonObject row, string owner) =>
        row["owner"] is JsonValue v && v.TryGetValue<string>(out var text) && string.Equals(text, owner, StringComparison.OrdinalIgnoreCase);

    // The first CARD-1323 row of the policy (the AgentTaskLandDeliveryE2ETests Linux row).
    private static JsonObject FirstOwned(JsonObject policy) => RcRows(policy).First(r => OwnedBy(r, PolicyFixtureCard));

    private static ExecutionPolicy ParsePolicy(JsonObject policy) => ExecutionPolicy.Parse(policy.ToJsonString());

    private static object[] RcExclusions(ExecutionPolicy policy, string suite, string platform) =>
        NightlyPolicy.ProfileExclusions(policy, "rc", suite, platform).Cast<object>().ToArray();

    // Write-C1323Policy: the policy with its hash restated, written to path.
    private static void WritePolicy(JsonObject policy, string path)
    {
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, policy.ToJsonString());
    }

    // The message of what the action throws ("" when it does not throw).
    private static string Message(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex.Message; }
        return "";
    }

    private static void WriteFixtureFile(string root, string path, string text)
    {
        var file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    // A fixture git command (init, config, add, commit, rev-parse); throws on failure; the trimmed output.
    private static string FixtureGit(string root, params string[] arguments)
    {
        var run = ReleaseAuthority.Git(root, arguments);
        if (run.ExitCode != 0) throw new InvalidOperationException("git-read-failed:" + arguments[0] + ":" + run.ExitCode + " " + run.Error);
        return run.Text.Trim();
    }

    // A catalog value as foreach enumerates it, and a dictionary's field.
    private static IEnumerable<object?> Items(object? value) =>
        value switch { null => [], string or IDictionary => [value], IEnumerable items => items.Cast<object?>(), _ => [value] };

    private static object? Field(object? o, string name) => o is IDictionary d && d.Contains(name) ? d[name] : null;

    private static bool Flag(JsonElement o, string name) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static double Number(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;

    private static string[] Reasons(JsonElement summary) =>
        summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty("reasons", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(r => r.ToString()).ToArray() : [];
}
