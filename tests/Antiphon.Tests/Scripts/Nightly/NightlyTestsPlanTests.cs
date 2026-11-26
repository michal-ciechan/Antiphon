using System.Collections;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S11: the executor leaves of nightly-tests-impl.ps1 (Antiphon.Nightly/NightlyTestsPlan.cs) in-process. C1397
/// is the scripts/test-nightly-tests.ps1 case of that name (its rows unchanged); the rest pin the small rules the
/// PowerShell bodies held.
/// </summary>
[Category("Integration")]
public sealed class NightlyTestsPlanTests
{
    [Test]
    public void C2082_Full_prefix_precedes_ordinary_work_and_stops_on_failure()
    {
        static object Case(string assembly, string type, string method, string uid, bool invariant) => new PlainObject(
            ("Assembly", assembly), ("Namespace", assembly + (invariant ? ".Invariants" : ".Product")),
            ("Type", type), ("ClassName", assembly + (invariant ? ".Invariants." : ".Product.") + type),
            ("Method", method), ("Uid", uid), ("Categories", invariant ? new object[] { "RepositoryInvariant" } : Array.Empty<object>()));

        foreach (var packed in new[] { false, true })
        {
            var a = new List<object>
            {
                Case("Alpha", "SharedGuardTests", "Check", "alpha-argument-1", true),
                Case("Alpha", "SharedGuardTests", "Check", "alpha-argument-2", true),
                Case("Alpha", "OrdinaryTests", "Run", "alpha-ordinary", false),
            };
            var exclusions = Array.Empty<object>();
            if (packed)
            {
                a.Add(Case("Alpha", "SharedGuardTests", "Paid", "alpha-paid", true));
                exclusions = [new PlainObject(("Class", "SharedGuardTests"), ("Methods", new object[] { "Paid" }))];
            }
            var b = new[] { Case("Beta", "SharedGuardTests", "Check", "beta-argument-1", true) };
            var alpha = NightlyTestsPlan.SuiteChunks(a.ToArray(), AllChunk(), exclusions, true, true, "antiphon", Policy().Object, Costs());
            var beta = NightlyTestsPlan.SuiteChunks(b, AllChunk(), [], true, true, "messaging", Policy().Object, Costs());
            string[] Uids(IEnumerable<object> chunks) => chunks.SelectMany(c =>
                PsValue.AsArray(PsValue.Prop(c, "plannedInvariantUids") ?? Array.Empty<object>()).Select(PsValue.Str)).ToArray();
            Uids(alpha).ShouldBe(["alpha-argument-1", "alpha-argument-2"], ignoreOrder: true);
            Uids(beta).ShouldBe(["beta-argument-1"]);
            var ordinary = new PlainObject(("id", "all"));
            var suites = new Dictionary<string, object>()
            {
                ["client"] = new PlainObject(("id", "client"), ("chunks", new object[] { ordinary })),
                ["antiphon"] = new PlainObject(("id", "antiphon"), ("chunks", alpha)),
                ["messaging"] = new PlainObject(("id", "messaging"), ("chunks", beta)),
            };
            foreach (var order in new[] { "fresh", "history", "previous" })
            foreach (var stop in new[] { "", "antiphon", "messaging", "interrupted" })
            {
                using var fixture = NightlyFixture.Create("c2082-prefix");
                var state = new NightlyRunState
                {
                    Platform = "windows", Profile = "rc", LogRoot = fixture.Root,
                    Sha = new string('e', 40), InvocationId = "candidate", Attempt = "1",
                };
                var started = new List<(string Suite, string Chunk, bool Invariant)>();
                var completed = new List<string>();
                var steps = new NightlyDispatchSteps
                {
                    Prepare = (id, needsDiscovery) =>
                    {
                        needsDiscovery.ShouldBeTrue(id);
                        return suites[id];
                    },
                    RunChunk = (suite, chunk) =>
                    {
                        var id = Str(suite, "id");
                        var invariant = Uids([chunk]).Length > 0;
                        started.Add((id, Str(chunk, "id"), invariant));
                        if (invariant && id == stop) throw new InvalidOperationException("fixture failure " + id);
                        if (invariant && id == "messaging" && stop == "interrupted") throw new OperationCanceledException("fixture interrupted");
                    },
                    Complete = suite => completed.Add(Str(suite, "id")),
                };
                object[] priority = order == "history" ? [new PlainObject(("suite", "client"), ("chunks", new object[] { "all" }))] : [];
                var previous = order == "previous" ? new PlainObject(
                    ("liveness", "closed"), ("schemaVersion", 1), ("sha", state.Sha), ("completedAt", DateTime.UtcNow.AddMinutes(-1)),
                    ("canonicalPlanDigest", Str(NightlyDispatch.Schedule(suites.Values.ToArray()), "canonicalPlanDigest")),
                    ("suites", new object[] { new PlainObject(("id", "client"), ("chunk", "all"), ("result", "FAIL"), ("exitCode", 1)) })) : null;
                var failure = NightlyDispatch.CoordinateOrFailure(state, steps, suites.Keys.Cast<object>().ToArray(), priority, [],
                    "", null!, previous!, "");
                started[0].Invariant.ShouldBeTrue("first runtime start must be a repository invariant");
                using var evidence = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Root, "dispatch-schedule.json")));
                var schedule = evidence.RootElement;
                if (stop.Length > 0)
                {
                    failure.ShouldNotBeNull().Message.ShouldContain(stop == "interrupted" ? "fixture interrupted" : "fixture failure " + stop);
                    started.ShouldAllBe(start => start.Invariant);
                    var prefix = schedule.GetProperty("invariantPrefix");
                    prefix.GetProperty("complete").GetBoolean().ShouldBeFalse(stop);
                    prefix.GetProperty("outstanding").GetArrayLength().ShouldBeGreaterThan(0);
                }
                if (order == "previous") schedule.GetProperty("orderingReason").GetString().ShouldBe("previous-run-order");
                else
                {
                    failure.ShouldBeNull();
                    started.Select(s => s.Invariant).ShouldBe([true, true, false, false]);
                    completed.ShouldBe(["client", "antiphon", "messaging"], ignoreOrder: true);
                    schedule.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("dispatchOrdinal").GetInt32())
                        .ShouldBe([0, 1, 2, 3]);
                    schedule.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("chunk").GetString())
                        .ShouldBe(started.Select(s => s.Chunk));
                }
            }
        }
    }

    [Test]
    public void C1397_RcAntiphonLaunchGroups()
    {
        // RC antiphon discovery is one class launch per fully included class plus one exact launch per remaining method
        // of a partial class. The measured shape is 1339 class launches and 7 exact launches (1346). Grouping must keep
        // that census and cut the class launches to about 40-60.
        var rows = new NightlyCaseRows();
        var detail = "";
        var ok = false;
        try
        {
            var policy = Policy();
            var exclusions = Exclusions(policy, "rc", "antiphon", "windows").ToList();
            exclusions.Add(new PlainObject(("Suite", "antiphon"), ("Class", "C1397HeldTestsExtra"), ("Methods", Array.Empty<object>()),
                ("Reason", "suffix hazard fixture"), ("Owner", "CARD-1397")));
            var exclusionArray = exclusions.ToArray();
            var shapes = new (string Ns, int Count)[] { ("Antiphon.Tests.Application", 1100), ("Antiphon.Tests.TestHelpers", 150), ("Antiphon.Tests.Scripts", 50), ("Antiphon.Tests.Agents", 39) };
            var nodes = new List<object>();
            var typesByNs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (ns, count) in shapes)
            {
                var letter = ns.Substring(ns.LastIndexOf('.') + 1).Substring(0, 1);
                if (!typesByNs.ContainsKey(ns)) typesByNs[ns] = new List<string>();
                for (var n = 0; n < count; n++)
                {
                    var type = "C1397" + letter + n.ToString("0000") + "Tests";
                    var className = ns + "." + type;
                    if (ns == "Antiphon.Tests.Application" && n == 0) { type = "C1397PrefixTests"; className = ns + "." + type; }
                    else if (ns == "Antiphon.Tests.Application" && n == 1) { type = "C1397HeldTests"; className = ns + "." + type; }
                    else if (ns == "Antiphon.Tests.Application" && n == 2) { type = "C1397PrefixTestsExtra"; className = ns + "." + type; }
                    else if (ns == "Antiphon.Tests.Agents" && n == 0) { type = "C1397FixtureTests"; className = ns + "." + type + "(C1397Factory)"; }
                    typesByNs[ns].Add(type);
                    nodes.Add(new PlainObject(("ClassName", className), ("Type", type), ("Method", "Run"), ("Uid", className + ".Run")));
                }
            }
            typesByNs["Antiphon.Tests.Application"].Add("C1397HeldTestsExtra");
            typesByNs["Antiphon.Tests.Application"].Add("Card0168InteractiveProbeTests");
            typesByNs["Antiphon.Tests.Agents"].Add("ClaudeEffortPromptCanaryTests");
            typesByNs["Antiphon.Tests.TestHelpers"].Add("TestDbFixtureLazyInitializationTests");
            nodes.Add(new PlainObject(("ClassName", "Antiphon.Tests.Application.C1397HeldTestsExtra"), ("Type", "C1397HeldTestsExtra"), ("Method", "Run"), ("Uid", "held-extra")));
            nodes.Add(new PlainObject(("ClassName", "Antiphon.Tests.Application.Card0168InteractiveProbeTests"), ("Type", "Card0168InteractiveProbeTests"), ("Method", "Run"), ("Uid", "card0168")));
            nodes.Add(new PlainObject(("ClassName", "Antiphon.Tests.Agents.ClaudeEffortPromptCanaryTests"), ("Type", "ClaudeEffortPromptCanaryTests"),
                ("Method", "Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer"), ("Uid", "claude-effort")));
            const string dbClass = "Antiphon.Tests.TestHelpers.TestDbFixtureLazyInitializationTests";
            string[] dbMethods =
            [
                "A_db_free_exact_method_selection_constructs_and_starts_no_database", "A_class_filtered_child_skips_every_parent_probe",
                "A_child_at_depth_two_refuses_before_any_work", "Mixed_first_consumers_share_one_real_bootstrap",
                "A_post_start_fault_cleans_up_the_owned_container", "A_failing_worker_exits_1_without_running_tests",
                "A_worker_child_exits_before_the_shared_store_warmup", "Child_db_free_selection_touches_no_database",
                "Child_mixed_consumers_initialize_once", "Child_post_start_fault_is_terminal",
            ];
            foreach (var method in dbMethods)
                nodes.Add(new PlainObject(("ClassName", dbClass), ("Type", "TestDbFixtureLazyInitializationTests"), ("Method", method), ("Uid", "db-" + method)));
            var nodeArray = nodes.ToArray();
            var costs = Costs();
            var leaves = LaunchPlanner.ProfileExecutionChunks(nodeArray, AllChunk(), exclusionArray);
            var plan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), exclusionArray, true, true, "antiphon", null, costs);
            var nightlyEx = Exclusions(policy, "nightly", "antiphon", "windows");
            var nightlyPlan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), nightlyEx, false, true, "antiphon", null, costs);
            var emptyPlan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), Array.Empty<object>(), true, true, "antiphon", null, costs);
            var guardedPlan = NightlyTestsPlan.SuiteChunks(nodeArray, AllChunk(), exclusionArray, false, true, "antiphon", null, costs);
            var classChunks = plan.Where(c => string.IsNullOrWhiteSpace(Str(c, "exactFilter"))).ToArray();
            var exactChunks = plan.Where(c => !string.IsNullOrWhiteSpace(Str(c, "exactFilter"))).ToArray();
            var orChunks = classChunks.Where(c => !string.IsNullOrWhiteSpace(Str(c, "groupFilter"))).ToArray();
            var maxOr = orChunks.Select(c => Classes(c).Length).DefaultIfEmpty(0).Max();
            var leafClass = leaves.Where(l => string.IsNullOrWhiteSpace(Str(l, "exactFilter"))).Select(l => Classes(l)[0]).OrderBy(s => s, SortObject).ToArray();
            var planClass = classChunks.SelectMany(Classes).OrderBy(s => s, SortObject).ToArray();
            var leafExact = leaves.Where(l => !string.IsNullOrWhiteSpace(Str(l, "exactFilter"))).Select(l => Str(l, "exactFilter")).OrderBy(s => s, SortObject).ToArray();
            var planExact = exactChunks.Select(c => Str(c, "exactFilter")).OrderBy(s => s, SortObject).ToArray();
            var classKey = string.Join("|", planClass);
            var leafKey = string.Join("|", leafClass);
            var exactKey = string.Join("|", planExact);
            var leafExactKey = string.Join("|", leafExact);
            var seenClass = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dup = planClass.Count(c => !seenClass.Add(c));
            var exactClass = new HashSet<string>(exactChunks.Select(c => Classes(c)[0]), StringComparer.OrdinalIgnoreCase);
            var overlap = planClass.Count(exactClass.Contains);
            var invalid = new List<string>();
            var argvByClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in plan)
            {
                var gf = Str(chunk, "groupFilter");
                try
                {
                    var joined = string.Join(" ", NightlyTestsPlan.NativeExecutionArguments(Path.Combine(Path.GetTempPath(), "c1397.trx"), Path.GetTempPath(),
                        Classes(chunk), Str(chunk, "exactFilter"), gf, ""));
                    foreach (var c in Classes(chunk)) argvByClass[c] = joined;
                    if (joined.Contains('(') && string.IsNullOrWhiteSpace(gf)) invalid.Add("paren-on-class-filter");
                }
                catch (NightlyRefusal ex) { invalid.Add(ex.Message); }
            }
            var rejected = Refusal(() => NightlyTestsPlan.NativeExecutionArguments(Path.Combine(Path.GetTempPath(), "c1397-bad.trx"), Path.GetTempPath(),
                ["Antiphon.Tests.Application.C1397PrefixTests", "Antiphon.Tests.Application.C1397PrefixTestsExtra"], "",
                "/*/Antiphon.Tests.Application/(C1397PrefixTests*)|(C1397PrefixTestsExtra*)/*", "")) == "invalid-group-filter";
            var prefixArg = argvByClass.GetValueOrDefault("Antiphon.Tests.Application.C1397PrefixTests") ?? "";
            var heldArg = argvByClass.GetValueOrDefault("Antiphon.Tests.Application.C1397HeldTests") ?? "";
            var fixtureArg = argvByClass.GetValueOrDefault("Antiphon.Tests.Agents.C1397FixtureTests") ?? "";
            var prefixOk = prefixArg.Contains("/*/Antiphon.Tests.Application/C1397PrefixTests/*") && !prefixArg.Contains("(C1397PrefixTests*)");
            var heldOk = heldArg.Contains("/*/Antiphon.Tests.Application/C1397HeldTests/*") && !heldArg.Contains("(C1397HeldTests*)");
            var fixtureOk = fixtureArg.Contains("C1397FixtureTests") && !fixtureArg.Contains("C1397Factory");
            var unsafeOr = false;
            foreach (var chunk in orChunks)
            {
                var bare0 = NightlyCensus.BareClassIdentity(Classes(chunk)[0]);
                var ns = bare0.Substring(0, bare0.LastIndexOf('.'));
                var types = typesByNs[ns];
                foreach (var c in Classes(chunk))
                {
                    var type = NightlyCensus.BareClassIdentity(c).Substring(ns.Length + 1);
                    if (types.Any(other => other.Length > type.Length && other.StartsWith(type, StringComparison.Ordinal))) unsafeOr = true;
                }
            }
            var excludedKept = dbMethods.Where(m => m.StartsWith("Child_", StringComparison.OrdinalIgnoreCase)).ToArray();
            var kept = dbMethods.Where(m => !m.StartsWith("Child_", StringComparison.OrdinalIgnoreCase)).ToArray();
            var exactText = string.Join(" ", planExact);
            var childrenGone = excludedKept.All(m => !exactText.Contains(m));
            var siblingsKept = kept.All(exactText.Contains);
            var cardGone = !classKey.Contains("Card0168InteractiveProbeTests") && !exactText.Contains("Card0168InteractiveProbeTests");
            var effortGone = !classKey.Contains("ClaudeEffortPromptCanaryTests") && !exactText.Contains("ClaudeEffortPromptCanaryTests");
            var dbNotGrouped = !classKey.Contains("TestDbFixtureLazyInitializationTests");
            var watchdog = Antiphon.Nightly.NightlyWatchdog.PolicyWatchdogMilliseconds(policy.Object, "antiphon", 3600000);
            bool AllOnly(object[] p) => p.Length == 1 && string.Equals(Str(p[0], "id"), "all", StringComparison.OrdinalIgnoreCase) && Classes(p[0]).Length == 0;
            var nightlyOk = nightlyEx.Length == 0 && AllOnly(nightlyPlan);
            var emptyOk = AllOnly(emptyPlan);
            var guardOk = AllOnly(guardedPlan);
            var reasons = new List<string>();
            if (leaves.Length != 1346) reasons.Add("leaf-shape");
            if (leafExact.Length != 7 || exactChunks.Length != 7) reasons.Add("exact-count");
            if (classChunks.Length < 40 || classChunks.Length > 60) reasons.Add("class-launches");
            if (plan.Length >= 100) reasons.Add("total-launches");
            if (classKey != leafKey || dup != 0 || overlap != 0) reasons.Add("class-census");
            if (exactKey != leafExactKey) reasons.Add("exact-census");
            if (!childrenGone || !siblingsKept || !cardGone || !effortGone || !dbNotGrouped) reasons.Add("exclusions");
            if (!prefixOk || !heldOk || !fixtureOk || unsafeOr) reasons.Add("suffix");
            if (orChunks.Length < 1 || maxOr > 40 || maxOr < 2) reasons.Add("or-groups");
            if (invalid.Count != 0 || !rejected) reasons.Add("filters");
            if (!nightlyOk || !emptyOk || !guardOk || watchdog != 3600000) reasons.Add("nightly-watchdog");
            ok = reasons.Count == 0;
            detail = "classLaunches=" + classChunks.Length + " exact=" + exactChunks.Length + " total=" + plan.Length + " leaves=" + leaves.Length + " or=" + orChunks.Length
                + " maxOr=" + maxOr + " invalid=" + invalid.Count + " dup=" + dup + " watchdog=" + watchdog + " reasons=" + string.Join(",", reasons);
        }
        catch (Exception ex)
        {
            detail = ex.Message;
        }
        rows.Check(ok, "C1397 rc antiphon launch groups are far below 1346 and assign every class once", detail);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void The_run_outcome_keeps_the_earliest_failure_class()
    {
        NightlyTestsPlan.Outcome("green", "TESTS").ShouldBe("TESTS");
        NightlyTestsPlan.Outcome("GREEN", "BUILD").ShouldBe("BUILD");
        NightlyTestsPlan.Outcome("TESTS", "BUILD").ShouldBe("BUILD");
        NightlyTestsPlan.Outcome("BUILD", "TESTS").ShouldBe("BUILD");
        NightlyTestsPlan.Outcome("TIMEOUT", "TESTS").ShouldBe("TESTS");
        NightlyTestsPlan.Outcome("PREFLIGHT", "TIMEOUT").ShouldBe("PREFLIGHT");
        NightlyTestsPlan.Outcome("weird", "TIMEOUT").ShouldBe("TIMEOUT");
        NightlyTestsPlan.Outcome("TESTS", "other").ShouldBe("TESTS");
    }

    [Test]
    public void The_platform_is_exactly_windows_or_linux()
    {
        NightlyTestsPlan.ExecutionPlatform("windows").ShouldBe("windows");
        NightlyTestsPlan.ExecutionPlatform("linux").ShouldBe("linux");
        foreach (var observed in new[] { "Windows", "", "macos", null })
            Refusal(() => NightlyTestsPlan.ExecutionPlatform(observed!)).ShouldBe("unsupported-host-platform");
        NightlyTestsPlan.ExecutionPlatform(new NightlyHostProbe().Platform()).ShouldBe(OperatingSystem.IsWindows() ? "windows" : "linux");
    }

    [Test]
    public void The_chunk_discovery_is_its_own_never_the_suite_copied()
    {
        var none = NightlyTestsPlan.ChunkDiscoverySeconds(21.377, null, false);
        none.ChunkDiscoverySeconds.ShouldBeNull();
        none.CopiedFromSuite.ShouldBeFalse();
        none.SuiteDiscoverySeconds.ShouldBe(21.377);
        NightlyTestsPlan.ChunkDiscoverySeconds(21.377, 1.5, false).ChunkDiscoverySeconds.ShouldBe(1.5);
        NightlyTestsPlan.ChunkDiscoverySeconds(21.377, 1.25, true).ChunkDiscoverySeconds.ShouldBe(22.627);
        NightlyTestsPlan.ChunkDiscoverySeconds(3, null, true).ChunkDiscoverySeconds.ShouldBe(3);
    }

    [Test]
    public void The_npm_launch_is_cmd_c_with_the_first_absolute_launcher_on_path()
    {
        using var fixture = NightlyFixture.Create("s11-npm");
        var first = Directory.CreateDirectory(fixture.PathOf("first")).FullName;
        var second = Directory.CreateDirectory(fixture.PathOf("second")).FullName;
        File.WriteAllText(Path.Combine(second, "npm.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(first, "npm"), "#!/bin/sh");
        var path = string.Join(Path.PathSeparator, "", "  ", first, second);
        NightlyTestsPlan.NpmLauncher(path).ShouldBe(Path.Combine(first, "npm"));
        var launch = NightlyTestsPlan.NpmLaunch(["run", "build"], second + Path.PathSeparator + first, "C:\\Windows\\system32\\cmd.exe");
        launch.FilePath.ShouldBe("C:\\Windows\\system32\\cmd.exe");
        launch.ArgumentList.ShouldBe(new[] { "/c", Path.Combine(second, "npm.cmd"), "run", "build" });
        Refusal(() => NightlyTestsPlan.NpmLauncher(fixture.PathOf("missing"))).ShouldBe("npm launcher not found");
        Refusal(() => NightlyTestsPlan.NpmLauncher(" ")).ShouldBe("npm launcher not found");
    }

    [Test]
    public void The_test_host_is_the_exe_then_dotnet_exec_of_the_dll_then_the_legacy_path()
    {
        using var fixture = NightlyFixture.Create("s11-host");
        var dir = Directory.CreateDirectory(fixture.PathOf("tests", "Probe.Tests", "bin", "Debug", "net9.0")).FullName;
        var legacy = NightlyTestsPlan.TestHost(fixture.Root, "Probe.Tests");
        legacy.FilePath.ShouldBe(Path.Combine(fixture.Root, "tests", "Probe.Tests", "bin", "Debug", "net9.0", "Probe.Tests.exe"));
        legacy.Prefix.ShouldBeEmpty();
        File.WriteAllText(Path.Combine(dir, "Probe.Tests.dll"), "");
        var dll = NightlyTestsPlan.TestHost(fixture.Root, "Probe.Tests");
        dll.Prefix.ShouldBe(new[] { "exec", Path.Combine(dir, "Probe.Tests.dll") });
        Path.GetFileNameWithoutExtension(dll.FilePath).ShouldBe("dotnet");
        File.WriteAllText(Path.Combine(dir, "Probe.Tests.exe"), "");
        var exe = NightlyTestsPlan.TestHost(fixture.Root, "Probe.Tests");
        exe.FilePath.ShouldBe(Path.Combine(dir, "Probe.Tests.exe"));
        exe.Prefix.ShouldBeEmpty();
        exe.AssemblyPath.ShouldBe(Path.Combine(dir, "Probe.Tests.dll"));
    }

    [Test]
    public void The_class_cost_table_refuses_what_the_powershell_refused()
    {
        using var fixture = NightlyFixture.Create("s11-costs");
        string Write(string name, string text) { var p = fixture.PathOf(name + ".json"); File.WriteAllText(p, text); return p; }
        const string format = "\"format\":\"antiphon-nightly-class-costs-v1\"";
        NightlyTestsPlan.ClassCostTable(fixture.PathOf("absent.json")).Count.ShouldBe(0);
        Refusal(() => NightlyTestsPlan.ClassCostTable(Write("format", "{\"format\":\"other\",\"classes\":{}}"))).ShouldBe("class-cost-format");
        Refusal(() => NightlyTestsPlan.ClassCostTable(Write("nodes", "{" + format + ",\"classes\":{\"A.B\":{\"nodes\":0,\"seconds\":3}}}"))).ShouldBe("class-cost-row A.B");
        Refusal(() => NightlyTestsPlan.ClassCostTable(Write("seconds", "{" + format + ",\"classes\":{\"A.B\":{\"nodes\":2}}}"))).ShouldBe("class-cost-row A.B");
        Refusal(() => NightlyTestsPlan.ClassCostTable(Write("none", "{" + format + "}"))).ShouldBe("class-cost-row ");
        Refusal(() => NightlyTestsPlan.ClassCostTable(Write("list", "{" + format + ",\"classes\":[1]}"))).ShouldBe("class-cost-row Length");
        var table = NightlyTestsPlan.ClassCostTable(Write("ok", "{" + format + ",\"classes\":{\"A.B\":{\"nodes\":2.5,\"seconds\":10},\"A.C\":{\"nodes\":\"3\",\"seconds\":\"4.5\"}}}"));
        table["a.b"].ShouldBe(5.0);
        table["A.C"].ShouldBe(1.5);
        // Each call gets its own copy.
        table["A.D"] = 1.0;
        NightlyTestsPlan.ClassCostTable(fixture.PathOf("ok.json")).ContainsKey("A.D").ShouldBeFalse();
        var costs = new NightlyClassCosts(fixture.PathOf("ok.json"));
        costs.Loaded.ShouldBeFalse();
        ((IDictionary)costs.Table).Contains("A.B").ShouldBeTrue();
        costs.Loaded.ShouldBeTrue();
    }

    [Test]
    public void A_launch_over_its_ceiling_or_refused_by_the_watchdog_is_refused_before_any_launch()
    {
        var node = Node("Antiphon.Tests.Application.HugeTests", "HugeTests", "Indivisible", "huge");
        var over = NightlyClassCosts.Of(new Hashtable(StringComparer.OrdinalIgnoreCase) { ["Antiphon.Tests.Application.HugeTests"] = 2401.0 });
        var ceiling = Refusal(() => NightlyTestsPlan.ProfileLaunchPlan([node], AllChunk(), Array.Empty<object>(), "antiphon", null, over));
        ceiling.ShouldNotBeNull();
        ceiling.ShouldStartWith("launch-estimate-ceiling id=");
        ceiling.ShouldEndWith(" seconds=2401 ceiling=2400");
        var pty = Node("Antiphon.PtyHost.Tests.PricedTests", "PricedTests", "Run", "priced");
        var priced = NightlyClassCosts.Of(new Hashtable(StringComparer.OrdinalIgnoreCase) { ["Antiphon.PtyHost.Tests.PricedTests"] = 1500.0 });
        var refusal = Refusal(() => NightlyTestsPlan.ProfileLaunchPlan([pty], AllChunk(), Array.Empty<object>(), "pty-host", Policy().Object, priced));
        refusal.ShouldNotBeNull();
        refusal.ShouldStartWith("watchdog-admission-refused id=all-group-1 reason=");
        var admitted = NightlyTestsPlan.ProfileLaunchPlan([pty], AllChunk(), Array.Empty<object>(), "antiphon", null, priced);
        admitted.Length.ShouldBe(1);
        PsValue.IsTrue(PsValue.Prop(admitted[0], "watchdogAdmitted")).ShouldBeTrue();
        Num(admitted[0], "watchdogSeconds").ShouldBe(3600);
    }

    [Test]
    public void Native_argv_refuses_ambiguous_and_unsafe_filters()
    {
        var trx = Path.Combine(Path.GetTempPath(), "s11", "x.trx");
        var argv = NightlyTestsPlan.NativeExecutionArguments(trx, "diag", ["Antiphon.Tests.Agents.AgentTuiApiTests(AgentTuiApiWebAppFactory)"], "", "", "");
        argv.ShouldBe(new[] { "--no-progress", "--no-ansi", "--report-trx", "--report-trx-filename", "x.trx", "--results-directory", Path.GetDirectoryName(trx)!,
            "--diagnostic", "--diagnostic-output-directory", "diag", "--treenode-filter", "/*/Antiphon.Tests.Agents/AgentTuiApiTests/*" });
        Refusal(() => NightlyTestsPlan.NativeExecutionArguments(trx, "diag", ["A.B"], "/*/A/B/M", "/*/A/(B*)|(C*)/*", "")).ShouldBe("ambiguous-chunk-filter");
        Refusal(() => NightlyTestsPlan.NativeExecutionArguments(trx, "diag", ["A.B"], " ", "", "")).ShouldBe("invalid-exact-filter");
        Refusal(() => NightlyTestsPlan.NativeExecutionArguments(trx, "diag", ["A.B", "A.C"], "", "", "")).ShouldBe("multiple classes require split exact roster chunks");
        Refusal(() => NightlyTestsPlan.NativeExecutionArguments(trx, "diag", ["Bad-Name.X"], "", "", "")).ShouldBe("invalid-class-filter");
        Refusal(() => NightlyTestsPlan.NativeExecutionArguments(trx, "diag", ["A.B"], "", "", "/*/A/B/(Run*)|(RunLater*)")).ShouldBe("invalid-method-filter");
        Refusal(() => NightlyTestsPlan.NativeExecutionArguments("x.trx", "diag", [], "", "", "")).ShouldBe("missing trx results directory");
        Filter(NightlyTestsPlan.NativeExecutionArguments(trx, "diag", [""], "", "", "")).ShouldBeNull();
        Filter(NightlyTestsPlan.NativeExecutionArguments(trx, "diag", null!, "/*/A/B/M", "", "")).ShouldBe("/*/A/B/M");
    }
}
