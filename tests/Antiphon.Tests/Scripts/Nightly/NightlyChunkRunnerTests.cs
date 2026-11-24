using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyDiagnosticCases;
// The Antiphon.NightlyWatchdog namespace (the independent watchdog) hides the core's NightlyWatchdog here.
using Watchdog = Antiphon.Nightly.NightlyWatchdog;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S12: the chunk runner (Antiphon.Nightly/NightlyChunkRunner.cs, once Invoke-NightlyScheduledChunk) in-process
/// on a NightlyChunkWorld: the owned launch and its argv, the verdicts, the timeout and completed-hang dispositions, the
/// failures that end a chunk, and the timing and observation it writes. The C1419 and Group4 cases of
/// scripts/test-nightly-tests.ps1 moved here with their row names. NotInParallel with QualificationTimingTests: the timing
/// writer's read budget is a static those tests set.
/// </summary>
[Category("Integration")]
[NotInParallel("QualificationTimingTests")]
public sealed class NightlyChunkRunnerTests
{
    private static string Joined(object[] values) => string.Join(",", values.Select(PsValue.Str));

    [Test]
    public void A_native_chunk_launches_its_host_under_owned_custody_and_records_a_complete_row()
    {
        using var world = new NightlyChunkWorld();
        var suite = world.Suite();
        var outcome = NightlyChunkRunner.Run(world.Context(), suite, NightlyChunkWorld.Chunk());

        outcome.Error.ShouldBeNull();
        outcome.CoverageComplete.ShouldBeTrue(Joined(outcome.Reasons));
        outcome.TestsPassed.ShouldBeTrue();
        outcome.OverallFailed.ShouldBeFalse();
        outcome.Reasons.ShouldBeEmpty();
        outcome.TestsOutcome.ShouldBe("green");
        outcome.ActiveNative.ShouldBe(0);
        outcome.NativeActiveMax.ShouldBe(1);
        var row = outcome.Row!;
        row["id"].ShouldBe("antiphon");
        row["chunk"].ShouldBe("all");
        row["result"].ShouldBe("pass");
        row["exitCode"].ShouldBe(0);
        row["timedOut"].ShouldBe(false);
        row["trx"].ShouldBe(world.LogPath("antiphon-all.trx"));
        row.Contains("requiredUidCount").ShouldBeTrue();
        row.Contains("censusDigest").ShouldBeTrue();

        var launch = world.Launcher.Launches.Single();
        launch.Custody.ShouldBe(NightlyCustody.Owned);
        launch.FileName.ShouldBe(world.Fixture.PathOf("bin", "host.exe"));
        launch.WorkingDirectory.ShouldBe(world.Fixture.Root);
        launch.LogPath.ShouldBe(world.LogPath("antiphon-all-tests.log"));
        launch.Arguments.ShouldBe(new[]
        {
            "--no-progress", "--no-ansi", "--report-trx", "--report-trx-filename", "antiphon-all.trx", "--results-directory", world.LogRoot,
            "--diagnostic", "--diagnostic-output-directory", world.LogPath("antiphon-all-execution"),
        });
        var admission = Watchdog.ResolveLaunch("antiphon", null, "default", "", null, null, NightlyChunkWorld.Policy.Object);
        launch.TimeoutMilliseconds.ShouldBe(PsValue.Prop(admission, "WatchdogMilliseconds"));
        launch.Environment!["C487_INVOCATION"].ShouldBe(NightlyChunkWorld.InvocationId);
        launch.Environment["C487_RUN_DIR"].ShouldBe(world.LogRoot);
        launch.Environment["ANTIPHON_TEST_PHASE_ROOT"].ShouldBe(world.TimingRoot);
        launch.Environment["ANTIPHON_TEST_PHASE_GROUP_ID"].ShouldBe("all");
        launch.Environment["ANTIPHON_TEST_PHASE_SUITE"].ShouldBe("antiphon");
        if (OperatingSystem.IsWindows())
        {
            launch.Hooks!.TrxPath.ShouldBe(world.LogPath("antiphon-all.trx"));
            launch.Hooks.DiagnosticDirectory.ShouldBe(world.LogPath("antiphon-all-execution"));
            launch.Hooks.HangCapturePath.ShouldBe(world.LogPath("antiphon-all-tests.log") + ".hang.txt");
            launch.Hooks.ExecutionGraceMilliseconds.ShouldBe(NightlyCompletionGrace.DefaultMilliseconds);
        }
        else launch.Hooks.ShouldBeNull();
        world.Fixture.Trace.ToString().ShouldContain(" proc " + launch.CommandText);

        var seen = world.Published.Single();
        (seen.Suite, seen.Chunk, seen.RunId, seen.Attempt, seen.TrxPath).ShouldBe(("antiphon", "all", NightlyChunkWorld.InvocationId, "1", world.LogPath("antiphon-all.trx")));
        PsValue.Prop(seen.Verdict, "ExitCode").ShouldBe(0);
        PsValue.Prop(seen.Verdict, "coverageComplete").ShouldBe(true);
        PsValue.Prop(seen.Verdict, "childrenExited").ShouldBe(true);
        world.Active.Select(a => a.State).ShouldBe(new[] { "Active", "Idle" });
        world.Active[0].Directory.ShouldBe(world.TimingRoot);
        File.ReadAllText(world.LogPath("antiphon-all-tests.log")).ShouldStartWith("C487_INVOCATION=" + NightlyChunkWorld.InvocationId + Environment.NewLine + "C487_INVOCATION fake");
        var timing = world.Timing("antiphon", "all");
        NightlyChunkWorld.Field(timing, "lastPhase").ShouldBe("complete");
        NightlyChunkWorld.Field(timing, "why").ShouldBe("");
        NightlyChunkWorld.Field(timing, "completedAt").ShouldNotBeEmpty();
        suite.ChunkIndex.ShouldBe(1);
        suite.TerminalNodes.Length.ShouldBe(1);
        suite.DiscoveryNodes.Length.ShouldBe(1);
    }

    [Test]
    public void A_watchdog_timeout_is_a_timeout_outcome_and_an_incomplete_run()
    {
        using var world = new NightlyChunkWorld(NightlyChunkWorld.NativeThen(_ => new NightlyLaunchResult { ExitCode = 1, TimedOut = true, Pid = 7, ChildrenExited = true }));
        var outcome = NightlyChunkRunner.Run(world.Context(), world.Suite(), NightlyChunkWorld.Chunk());
        outcome.Error.ShouldBeNull();
        outcome.Reasons.Select(PsValue.Str).ShouldContain("timeout antiphon");
        outcome.TestsOutcome.ShouldBe("TIMEOUT");
        outcome.CoverageComplete.ShouldBeFalse();
        outcome.TestsPassed.ShouldBeFalse();
        outcome.OverallFailed.ShouldBeTrue();
        outcome.HarnessHangReason.ShouldBe("");
        outcome.Row!.Contains("hangCapture").ShouldBeFalse();
        var timing = world.Timing("antiphon", "all");
        NightlyChunkWorld.Field(timing, "lastPhase").ShouldBe("timeout");
        NightlyChunkWorld.Field(timing, "why").ShouldBe("timeout");
        NightlyChunkWorld.Field(timing, "completedAt").ShouldBe("");
    }

    [Test]
    public void A_failing_exit_is_a_tests_outcome_with_a_failed_row()
    {
        using var world = new NightlyChunkWorld(NightlyChunkWorld.NativeThen(_ => new NightlyLaunchResult { ExitCode = 3, Pid = 7, ChildrenExited = true }));
        var outcome = NightlyChunkRunner.Run(world.Context(), world.Suite(), NightlyChunkWorld.Chunk());
        outcome.Error.ShouldBeNull();
        outcome.Row!["result"].ShouldBe("FAIL");
        outcome.Row["exitCode"].ShouldBe(3);
        Items(outcome.Row, "evidenceReasons").Select(PsValue.Str).ShouldContain("process-exit-3");
        outcome.TestsOutcome.ShouldBe("TESTS");
        outcome.TestsPassed.ShouldBeFalse();
        outcome.OverallFailed.ShouldBeTrue();
    }

    [Test]
    public void An_unjoined_owned_child_ends_the_chunk_after_its_observation()
    {
        using var world = new NightlyChunkWorld();
        var waited = 0;
        world.Launcher.Cleanup = _ => { waited++; return false; };
        var outcome = NightlyChunkRunner.Run(world.Context(), world.Suite(), NightlyChunkWorld.Chunk());
        outcome.Error!.Message.ShouldBe("owned-child still running");
        outcome.Row.ShouldBeNull();
        outcome.ActiveNative.ShouldBe(0);
        waited.ShouldBe(1);
        world.Published.Count.ShouldBe(1);
        world.Active.Select(a => a.State).ShouldBe(new[] { "Active", "Idle" });
    }

    [Test]
    public void A_native_chunk_refuses_to_overlap_another()
    {
        using var world = new NightlyChunkWorld();
        var context = world.Context();
        context.ActiveNative = 1;
        var suite = world.Suite();
        var outcome = NightlyChunkRunner.Run(context, suite, NightlyChunkWorld.Chunk());
        outcome.Error!.Message.ShouldBe("native overlap");
        outcome.ActiveNative.ShouldBe(1);
        world.Launcher.Launches.ShouldBeEmpty();
        suite.ChunkIndex.ShouldBe(1);
        world.Active.Select(a => a.State).ShouldBe(new[] { "Idle" });
    }

    [Test]
    public void A_launch_that_throws_records_launch_failed_and_ends_the_chunk()
    {
        using var world = new NightlyChunkWorld(_ => throw new InvalidOperationException("no host"));
        var outcome = NightlyChunkRunner.Run(world.Context(), world.Suite(), NightlyChunkWorld.Chunk());
        outcome.Error!.Message.ShouldBe("no host");
        outcome.Row.ShouldBeNull();
        // As the PowerShell left its caller: the count taken for the launch stays taken.
        outcome.ActiveNative.ShouldBe(1);
        var timing = world.Timing("antiphon", "all");
        NightlyChunkWorld.Field(timing, "lastPhase").ShouldBe("launch");
        NightlyChunkWorld.Field(timing, "why").ShouldBe("launch-failed");
        world.Active.Select(a => a.State).ShouldBe(new[] { "Active", "Idle" });
    }

    [Test]
    public void A_client_chunk_runs_the_cli_test_client_command_and_needs_its_json()
    {
        using (var missing = new NightlyChunkWorld(_ => RecordingLauncher.Passed()))
        {
            // Unit 32 of the 2026-11-22 plan: the CLI that runs nightly-tests (its entry assembly, unless the context names
            // one) runs test-client on the checkout under test; no PowerShell wrapper.
            var outcome = NightlyChunkRunner.Run(missing.Context(), missing.Suite("client", native: false), NightlyChunkWorld.Chunk());
            outcome.Error.ShouldBeNull();
            var launch = missing.Launcher.Launches.Single();
            launch.FileName.ShouldBe("dotnet");
            launch.Arguments.ShouldBe(new[] { "exec", System.Reflection.Assembly.GetEntryAssembly()!.Location, "test-client", "--repo-root", missing.Fixture.Root,
                "--json-result-path", missing.LogPath("client-all.json") });
            launch.WorkingDirectory.ShouldBe(missing.Fixture.Root);
            launch.Custody.ShouldBe(NightlyCustody.Owned);
            launch.LogPath.ShouldBe(missing.LogPath("client-all-tests.log"));
            launch.TimeoutMilliseconds.ShouldBe(1200000);
            ClientTestsOptions.Parse(launch.Arguments.Skip(3).ToArray(), "cli-checkout").ShouldSatisfyAllConditions(
                o => o.RepoRoot.ShouldBe(missing.Fixture.Root), o => o.JsonResultPath.ShouldBe(missing.LogPath("client-all.json")), o => o.Forward.ShouldBeEmpty());
            outcome.Reasons.Select(PsValue.Str).ShouldBe(new[] { "missing-client-json" });
            outcome.Row!["coverageComplete"].ShouldBe(false);
            outcome.TestsOutcome.ShouldBe("TESTS");
            NightlyChunkWorld.Field(missing.Timing("client", "all"), "why").ShouldBe("missing-client-json");
            missing.Active.ShouldBeEmpty();
            missing.Published.ShouldBeEmpty();
        }
        using var written = new NightlyChunkWorld();
        var context = written.Context();
        context.CliPath = written.Fixture.PathOf("cli", "Antiphon.Scripts.dll");
        var passed = NightlyChunkRunner.Run(context, written.Suite("client", native: false), NightlyChunkWorld.Chunk());
        passed.Reasons.ShouldBeEmpty();
        passed.Row!["result"].ShouldBe("pass");
        NightlyChunkWorld.Field(written.Timing("client", "all"), "why").ShouldBe("no-trx:client");
        written.Launcher.Launches.Single().Arguments[1].ShouldBe(context.CliPath);
    }

    [Test]
    public void A_scripts_chunk_runs_each_unattended_census_script()
    {
        var unattended = NightlyPolicy.ScriptCensus(NightlyChunkWorld.Policy.Object).Where(r => string.Equals(r.Disposition, "unattended", StringComparison.OrdinalIgnoreCase)).ToArray();
        unattended.ShouldNotBeEmpty();
        var failing = unattended[0].Id;
        using var world = new NightlyChunkWorld(launch => launch.LogPath!.EndsWith("script-" + failing + ".log", StringComparison.Ordinal)
            ? new NightlyLaunchResult { ExitCode = 1, ChildrenExited = true } : RecordingLauncher.Passed());
        var outcome = NightlyChunkRunner.Run(world.Context(), world.Suite("scripts", native: false), NightlyChunkWorld.Chunk());
        outcome.Error.ShouldBeNull();
        world.Launcher.Launches.Count.ShouldBe(unattended.Length);
        var first = world.Launcher.Launches[0];
        first.FileName.ShouldBe("pwsh");
        first.Arguments.Take(4).ShouldBe(new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(world.Fixture.Root, unattended[0].Path.Replace('/', Path.DirectorySeparatorChar)) });
        first.TimeoutMilliseconds.ShouldBe(600000);
        outcome.Row!["exitCode"].ShouldBe(1);
        outcome.Row["result"].ShouldBe("FAIL");
        outcome.TestsOutcome.ShouldBe("TESTS");
        NightlyChunkWorld.Field(world.Timing("scripts", "all"), "why").ShouldBe("script-failed");
    }

    [Test]
    public void A_chunk_without_discovery_asks_the_producer_and_a_missing_producer_is_a_reason()
    {
        var host = FakeTestHost.NativePass(NightlyChunkWorld.Rows, NightlyChunkWorld.Nodes, omitDiscovery: true);
        using var world = new NightlyChunkWorld(host);
        var outcome = NightlyChunkRunner.Run(world.Context(), world.Suite(), NightlyChunkWorld.Chunk());
        var ask = world.Discoveries.Single();
        ask.OutputPath.ShouldBe(world.LogPath("antiphon-all.discovery.json"));
        ask.DiagnosticDirectory.ShouldBe(world.LogPath("antiphon-all-discovery"));
        ask.Environment["C487_INVOCATION"].ShouldBe(NightlyChunkWorld.InvocationId);
        // The fake producer wrote nothing: the verdict says so.
        Items(outcome.Row, "evidenceReasons").Select(PsValue.Str).ShouldContain("missing-discovery");

        using var bare = new NightlyChunkWorld(host);
        var context = bare.Context();
        context.ProduceDiscovery = null;
        var refused = NightlyChunkRunner.Run(context, bare.Suite(), NightlyChunkWorld.Chunk());
        refused.Reasons.Select(PsValue.Str).ShouldContain("discovery-produce-failed discovery-producer-unavailable");
    }

    [Test]
    public void The_observation_verdict_carries_the_owner_fields_a_record_has()
    {
        var owner = new NativeProcessResult { ExitCode = 0, RootExitedAtUtc = "2026-10-29T00:00:02.0000000Z", JobEmptyObserved = true, DrainMilliseconds = 7 };
        var verdict = NightlyChunkRunner.ObservationVerdict(new NightlyLaunchResult { Owner = owner }, true, null);
        PsValue.Prop(verdict, "coverageComplete").ShouldBe(true);
        PsValue.Prop(verdict, "testsPassed").ShouldBe(false);
        PsValue.Prop(verdict, "rootExitedAtUtc").ShouldBe("2026-10-29T00:00:02.0000000Z");
        PsValue.Prop(verdict, "jobEmpty").ShouldBe(true);
        PsValue.Prop(verdict, "drainMilliseconds").ShouldBe(7.0);
        PsValue.PropertyNames(verdict).Count(n => n == "rootStartedAtUtc").ShouldBe(1);
        // A dictionary's keys are not properties (the StartProcess seam's @{} records): only the defaults.
        var seam = NightlyChunkRunner.ObservationVerdict(new System.Collections.Hashtable { ["ExitCode"] = 4, ["JobEmptyObserved"] = true }, false, false);
        PsValue.PropertyNames(seam).ShouldBe(new[] { "ExitCode", "coverageComplete", "testsPassed" });
        PsValue.Prop(seam, "ExitCode").ShouldBe(0);
    }

    [Test]
    public void The_measured_phases_land_in_the_timing_directory()
    {
        using var fixture = NightlyFixture.Create("s12-phase");
        var started = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
        NightlyChunkRunner.WritePrerequisitePhases(fixture.Root, "", started, 1.0, started.AddSeconds(1), 6.0, started.AddSeconds(7), 2.0, null, null);
        NightlyChunkRunner.WriteMeasuredPhase(fixture.Root, "", "Not A Phase", started, 1.0);
        var text = File.ReadAllText(fixture.PathOf("timing", "setup-phases.json"));
        foreach (var name in new[] { "npm-ci", "npm-build", "lint" }) text.ShouldContain("\"" + name + "\"");
        text.ShouldNotContain("dotnet-build");
        text.ShouldNotContain("Not A Phase");
        text.ShouldContain("\"startedAt\":\"2026-10-29T00:00:00.0000000Z\"");
    }

    // ---- C1419 -------------------------------------------------------------------------------------------------------

    private sealed record C1419Launch(string Trx, string Exec, string Class, string Dir);

    private static C1419Launch NewC1419Launch(string root, string name, string cls, string[] methods, string extraClass = "", string type = "")
    {
        // TRX keeps the bare class; discovery and execution carry TUnit's type name.
        if (type.Length == 0) type = cls;
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var rows = methods.Select(m => new NightlyTrxRow(Guid.NewGuid().ToString(), "Ns." + cls, m)).ToList();
        var exec = methods.Select(m => new NightlyTestNode(type + "." + m, type, m, "Ns", "Passed", "Ns." + type)).ToList();
        if (extraClass.Length > 0) rows.Add(new NightlyTrxRow(Guid.NewGuid().ToString(), "Ns." + extraClass, "One"));
        var trx = Path.Combine(dir, "launch.trx");
        NightlyFixture.WriteTrx(trx, rows);
        var execPath = Path.Combine(dir, "launch.execution.json");
        NightlyFixture.WriteExecution(execPath, exec);
        return new C1419Launch(trx, execPath, cls, dir);
    }

    private static object C1419Verdict(C1419Launch launch, string discovery, string root, NightlyDiscoveryIndex? index = null, object[]? exclusions = null, bool aware = false)
    {
        var sha = new string('a', 40);
        return NightlyChunkVerdict.NativeSuiteVerdict(launch.Trx, discovery, launch.Exec, root, DateTime.UtcNow.AddMinutes(-5), 0, sha, sha, "origin/master", "origin/master",
            "p", "p", ["Ns." + launch.Class], exclusions, aware, index, "", null);
    }

    private static string C1419Text(object verdict)
    {
        var census = PsValue.Prop(verdict, "census");
        return "cov=" + B(verdict, "coverageComplete") + " pass=" + B(verdict, "testsPassed") + " req=" + S(census, "RequiredCount") + " exc=" + S(census, "ExcludedCount")
            + " digest=" + S(census, "Digest") + " nodes=" + Items(verdict, "discoveryNodes").Length + " reasons=" + string.Join(";", Items(verdict, "reasons").Select(PsValue.Str));
    }

    /// <summary>
    /// CARD-1419: every launch of a suite is judged against the same discovery, parsed and censused once per distinct
    /// discovery, each verdict reconciling only its own classes and equal to an unindexed one. A parse is an entry of the
    /// launch's index (the index parses only content it has not seen), so the counts below are the index's own.
    /// </summary>
    [Test]
    public void C1419_DiscoveryParsedOncePerSuite()
    {
        using var fixture = NightlyFixture.Create("c1419");
        var root = fixture.Root;
        var rows = new NightlyCaseRows();
        var nodes = new List<NightlyTestNode>();
        foreach (var c in new[] { "ClassA", "ClassB", "ClassC", "ClassF(Factory)" })
            foreach (var m in new[] { "One", "Two" }) nodes.Add(new NightlyTestNode(c + "." + m, c, m, "Ns", "Discovered"));
        var disc = Path.Combine(root, "suite.discovery.json");
        NightlyFixture.WriteDiscovery(disc, nodes);
        var launches = new[]
        {
            NewC1419Launch(root, "a", "ClassA", ["One", "Two"]), NewC1419Launch(root, "b", "ClassB", ["One", "Two"]), NewC1419Launch(root, "c", "ClassC", ["One", "Two"]),
            NewC1419Launch(root, "d", "ClassB", ["One"]), NewC1419Launch(root, "e", "ClassC", ["One", "Two"], extraClass: "ClassA"),
            NewC1419Launch(root, "f", "ClassF", ["One", "Two"], type: "ClassF(Factory)"), NewC1419Launch(root, "g", "ClassF", ["One"], type: "ClassF(Factory)"),
        };
        // Shared path (the rc profile shape): one parse for seven launches, each verdict equal to the unindexed one.
        var index = new NightlyDiscoveryIndex();
        var same = true;
        var text = new List<string>();
        var indexed = new List<object>();
        foreach (var l in launches)
        {
            var v = C1419Verdict(l, disc, root, index);
            var plain = C1419Verdict(l, disc, root);
            if (C1419Text(v) != C1419Text(plain)) same = false;
            text.Add(l.Dir + " " + C1419Text(v));
            indexed.Add(v);
        }
        rows.Check(index.Entries.Count == 1, "C1419 one discovery parse for seven launches on a shared path", "reads=" + index.Entries.Count);
        rows.Check(same, "C1419 indexed verdicts equal unindexed verdicts", string.Join(" || ", text));
        var clean = new[] { indexed[0], indexed[1], indexed[2], indexed[5] }.Count(v => B(v, "coverageComplete") && B(v, "testsPassed") && Items(v, "reasons").Length == 0);
        // A constructor-suffixed type is found from its bare chunk class name.
        var suffixed = string.Join(";", Items(indexed[6], "reasons").Select(PsValue.Str));
        var missing = string.Join(";", Items(indexed[3], "reasons").Select(PsValue.Str));
        var extra = string.Join(";", Items(indexed[4], "reasons").Select(PsValue.Str));
        rows.Check(clean == 4 && suffixed == "missing-expanded-row ClassF(Factory).Two" && !B(indexed[3], "coverageComplete") && missing == "missing-expanded-row ClassB.Two"
            && !B(indexed[4], "coverageComplete") && Regex.IsMatch(extra, @"extra-class-in-chunk Ns\.ClassA\.One", RegexOptions.IgnoreCase),
            "C1419 launch verdicts reconcile only their own classes", string.Join(" || ", text));

        // The same bytes at a new path per launch (the per-chunk discovery shape).
        var copyIndex = new NightlyDiscoveryIndex();
        foreach (var l in launches.Take(3))
        {
            var copy = Path.Combine(l.Dir, "launch.discovery.json");
            File.Copy(disc, copy, overwrite: true);
            C1419Verdict(l, copy, root, copyIndex);
        }
        rows.Check(copyIndex.Entries.Count == 1, "C1419 identical discovery bytes at three paths parse once", "reads=" + copyIndex.Entries.Count);

        // Profile lane: the exclusion census and its required set come from the index.
        object[] hold = [new PlainObject(("Class", "ClassC"), ("Methods", new object[] { "Two" }), ("Reason", "held"), ("Owner", "C1419"))];
        var profileIndex = new NightlyDiscoveryIndex();
        var held = NewC1419Launch(root, "held", "ClassC", ["One"]);
        var pv = new[] { launches[0], held, launches[1] }.Select(l => C1419Verdict(l, disc, root, profileIndex, hold, aware: true)).ToArray();
        var pPlain = C1419Verdict(held, disc, root, null, hold, aware: true);
        var pText = C1419Text(pv[1]);
        var census = PsValue.Prop(pv[1], "census");
        rows.Check(profileIndex.Entries.Count == 1 && pText == C1419Text(pPlain) && B(pv[1], "coverageComplete") && S(census, "RequiredCount") == "7" && S(census, "ExcludedCount") == "1",
            "C1419 profile exclusions census once and match the unindexed verdict", "reads=" + profileIndex.Entries.Count + " " + pText);

        // A rewritten discovery is new bytes: the index parses it again.
        nodes.Add(new NightlyTestNode("ClassD.One", "ClassD", "One", "Ns", "Discovered"));
        NightlyFixture.WriteDiscovery(disc, nodes);
        var after = C1419Verdict(launches[0], disc, root, index);
        rows.Check(index.Entries.Count == 2 && S(PsValue.Prop(after, "census"), "RequiredCount") == "9" && B(after, "coverageComplete"),
            "C1419 rewritten discovery is parsed again", "reads=" + (index.Entries.Count - 1) + " " + C1419Text(after));

        // The suite's one index (NightlySuiteRunner.Prepare makes it before any launch) is the one every chunk's verdict uses.
        using var world = new NightlyChunkWorld();
        var suite = world.Suite();
        var suiteIndex = suite.DiscoveryIndex;
        foreach (var id in new[] { "first", "second", "third" }) NightlyChunkRunner.Run(world.Context(), suite, NightlyChunkWorld.Chunk(id));
        var prepare = File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "scripts", "lib", "Antiphon.Nightly", "NightlySuiteRunner.cs"));
        var prepareBody = prepare[prepare.IndexOf("public NightlyPreparedSuite Prepare(", StringComparison.Ordinal)..prepare.IndexOf("private bool E2EReady(", StringComparison.Ordinal)];
        var makers = Regex.Matches(prepareBody, @"new NightlyDiscoveryIndex\(\)").Count;
        var siteOk = ReferenceEquals(suite.DiscoveryIndex, suiteIndex) && suiteIndex!.Entries.Count == 1 && world.Launcher.Launches.Count == 3 && makers == 1;
        rows.Check(siteOk, "C1419 suite loop builds one discovery index before its launches",
            "entries=" + suiteIndex!.Entries.Count + " launches=" + world.Launcher.Launches.Count + " makers=" + makers);
        rows.ShouldAllPass(7);
    }

    // ---- Group4 ------------------------------------------------------------------------------------------------------

    private static void WriteSummaryTrx(string path, string outcome, int total, int executed, int notExecuted, int failed = 0, bool passedIsExecuted = false)
    {
        var passed = passedIsExecuted ? executed : Math.Max(0, executed - failed);
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><ResultSummary outcome=\"" + outcome
            + "\"><Counters total=\"" + total + "\" executed=\"" + executed + "\" passed=\"" + passed + "\" failed=\"" + failed + "\" notExecuted=\"" + notExecuted + "\" /></ResultSummary></TestRun>");
    }

    private static void WriteFinishedDiag(string path, int ran, bool stop = true) =>
        File.WriteAllText(path, new string('x', 20000) + "\ntotal ran: " + ran + "\n" + (stop ? "request execute stop\n" : ""));

    /// <summary>
    /// session-runner all-group-4: TRX Completed and "total ran" matched the selection, then the process tree lived until
    /// the launch watchdog. The chunk's classification rows (once source-text checks of nightly-tests-impl.ps1) now run the
    /// chunk runner on a completed hang.
    /// </summary>
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Group4CompletedHang()
    {
        using var fixture = NightlyFixture.Create("group4-hang");
        var rows = new NightlyCaseRows();
        var matchTrx = fixture.PathOf("match.trx");
        var matchDiag = fixture.PathOf("match.diag");
        WriteSummaryTrx(matchTrx, "Completed", 119, 119, 0, passedIsExecuted: true);
        WriteFinishedDiag(matchDiag, 119);
        var finished = false;
        var finishedError = Refusal(() => finished = Watchdog.ExecutionFinished(matchTrx, matchDiag));
        rows.Check(finished && finishedError == "", "group4 completed trx plus matching total ran is finished", finishedError);
        var lowDiag = fixture.PathOf("low.diag");
        File.WriteAllText(lowDiag, "total ran: 118\nrequest execute stop\n");
        rows.Check(!Watchdog.ExecutionFinished(matchTrx, lowDiag), "group4 total ran that misses the trx total is not finished");
        var noStop = fixture.PathOf("nostop.diag");
        File.WriteAllText(noStop, "total ran: 119\n");
        rows.Check(!Watchdog.ExecutionFinished(matchTrx, noStop), "group4 total ran without request execute stop is not finished");
        var bare = fixture.PathOf("bare.trx");
        NightlyFixture.WriteTrx(bare, [new NightlyTrxRow("1", "A", "M")]);
        rows.Check(!Watchdog.ExecutionFinished(bare, matchDiag), "group4 trx without ResultSummary is not finished");
        var partial = fixture.PathOf("partial.trx");
        WriteSummaryTrx(partial, "Completed", 119, 118, 1, passedIsExecuted: true);
        rows.Check(!Watchdog.ExecutionFinished(partial, matchDiag), "group4 notExecuted row is not a completed hang");
        var failedTrx = fixture.PathOf("failed.trx");
        WriteSummaryTrx(failedTrx, "Failed", 119, 119, 0, passedIsExecuted: true);
        rows.Check(!Watchdog.ExecutionFinished(failedTrx, matchDiag), "group4 Failed outcome is not a completed hang");

        var hung = Watchdog.CompletedHang(true, true, "session-runner", "pid=42/image=git");
        rows.Check(B(hung, "CompletedButHung") && S(hung, "Reason") == "completed-but-hung session-runner children=pid=42/image=git",
            "group4 finished timeout records completed-but-hung and the child list");
        var openHang = Watchdog.CompletedHang(true, false, "session-runner", "pid=42/image=git");
        rows.Check(!B(openHang, "CompletedButHung") && S(openHang, "Reason") == "watchdog-timeout", "group4 unfinished timeout stays watchdog-timeout");
        rows.Check(!B(Watchdog.CompletedHang(false, true, "session-runner", "pid=42/image=git"), "CompletedButHung"), "group4 a finished run that exited is not a hang");
        var disposition = Watchdog.TimeoutDisposition(true, false, true);
        rows.Check(S(disposition, "Reason") == "completed-but-hung" && !B(disposition, "CoverageComplete") && !B(disposition, "TestsPassed") && B(disposition, "Incomplete")
            && !B(disposition, "Pass"), "group4 completed hang stays a red test gate");
        var still = Watchdog.TimeoutDisposition(true, false, false);
        rows.Check(S(still, "Reason") == "watchdog-timeout" && !B(still, "TestsPassed") && !B(still, "CoverageComplete"), "group4 generic watchdog disposition is unchanged");
        var formatted = Watchdog.HangChildren(new object[] { "42:2026-11-09T22:33:18.0000000Z" }, new object[] { "git" }, 7);
        rows.Check(formatted == "pid=42/image=git", "group4 hang children format pid and image", formatted);
        var fallback = Watchdog.HangChildren(Array.Empty<object>(), Array.Empty<object>(), 9);
        rows.Check(fallback == "pid=9/image=", "group4 hang with no descendants names the root", fallback);
        var capture = fixture.PathOf("capture.hang.txt");
        var captureError = Refusal(() => Watchdog.WriteHangCapture(capture, "session-runner", "pid=42/image=git", 7));
        var captureText = File.Exists(capture) ? File.ReadAllText(capture) : "";
        rows.Check(captureText.Contains("completed-hang-capture") && captureText.Contains("handles=not-enumerated") && captureText.Contains("pid=42/image=git")
            && captureText.Contains("suite=session-runner"), "group4 powershell hang capture names the tree and skips handle names", captureError);

        // A real child that outlives the grace: the grace classifies it hung once the finish markers are reported.
        var sleeper = new ProcessStartInfo(OperatingSystem.IsWindows() ? "ping" : "/bin/sleep") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in OperatingSystem.IsWindows() ? new[] { "-n", "31", "127.0.0.1" } : new[] { "30" }) sleeper.ArgumentList.Add(argument);
        var sleep = Process.Start(sleeper)!;
        try
        {
            var grace = NightlyCompletionGrace.Wait(() => sleep.HasExited, () => true, 300, 5000, 10);
            var alive = !sleep.HasExited;
            rows.Check(grace.CompletedButHung && grace.TimedOut && alive && grace.ElapsedMilliseconds >= 250 && grace.ElapsedMilliseconds < 2000,
                "group4 host must be classified hung inside the grace once total ran is reported", "elapsed=" + grace.ElapsedMilliseconds + " hung=" + grace.CompletedButHung + " alive=" + alive);
        }
        finally
        {
            try { sleep.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            sleep.Dispose();
        }
        var openGrace = NightlyCompletionGrace.Wait(() => false, () => false, 5000, 250, 10);
        rows.Check(openGrace.TimedOut && !openGrace.CompletedButHung && openGrace.ElapsedMilliseconds < 1000, "group4 unfinished run still uses the launch watchdog",
            "elapsed=" + openGrace.ElapsedMilliseconds + " hung=" + openGrace.CompletedButHung);

        var ownerText = File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "scripts", "lib", "Antiphon.Nightly", "NativeProcessOwner.cs"));
        var captureAt = ownerText.IndexOf("WriteHangCapture(job, root, logPath)", StringComparison.Ordinal);
        var killAt = ownerText.IndexOf("TerminateJobObject(job, 1)", StringComparison.Ordinal);
        rows.Check(captureAt >= 0 && killAt > captureAt, "group4 hang capture is written before the job is killed", "capture=" + captureAt + " kill=" + killAt);
        rows.Check(ownerText.Contains("Observation.CompletedButHung = true"), "group4 native owner records CompletedButHung");
        rows.Check(ownerText.Contains("NtQuerySystemInformation"), "group4 hang capture lists open handles");
        rows.Check(!ownerText.Contains("NtQueryObject"), "group4 hang capture does not query handle names");
        rows.Check(ownerText.Contains("total ran:") && ownerText.Contains("request execute stop"), "group4 native grace uses the same finish markers");

        // The chunk on a completed hang: the native owner reports the hang; the kill's exit reason is dropped (from the row,
        // and its last copy from the run's reasons), and the outcome is TESTS, not TIMEOUT.
        var owner = new NativeProcessResult
        {
            ExitCode = 1, TimedOut = true, Pid = 7, CompletedButHung = true, ChildrenExited = true, OutputDrained = true,
            DescendantIdentities = ["42:2026-11-09T22:33:18.0000000Z"], DescendantImages = ["git"],
        };
        using var world = new NightlyChunkWorld(NightlyChunkWorld.NativeThen(_ => new NightlyLaunchResult { ExitCode = 1, TimedOut = true, Pid = 7, ChildrenExited = true, Owner = owner }));
        var context = world.Context();
        context.Reasons = ["earlier", "process-exit-1"];
        var outcome = NightlyChunkRunner.Run(context, world.Suite("session-runner"), NightlyChunkWorld.Chunk("all-group-4"));
        var row = outcome.Row;
        var reasons = outcome.Reasons.Select(PsValue.Str).ToArray();
        rows.Check(outcome.Error == null && row != null && (string?)row["result"] == "FAIL" && reasons.LastOrDefault() == "completed-but-hung session-runner children=pid=42/image=git"
            && outcome.HarnessHangReason == "completed-but-hung", "group4 chunk classifies a completed hang", string.Join(",", reasons) + " " + outcome.Error?.Message);
        var launch = world.Launcher.Launches.SingleOrDefault();
        var log = world.LogPath("session-runner-all-group-4-tests.log");
        var armed = launch != null && (OperatingSystem.IsWindows()
            ? launch.Hooks != null && launch.Hooks.HangCapturePath == log + ".hang.txt" && launch.Hooks.ExecutionGraceMilliseconds == context.CompletionGraceMilliseconds
            : launch.Hooks == null);
        var hangCapture = row == null ? "" : PsValue.Str(row["hangCapture"]);
        rows.Check(armed && hangCapture == log + ".hang.txt" && File.Exists(hangCapture), "group4 chunk arms the native grace and hang capture", hangCapture);
        rows.Check(row != null && !Items(row, "evidenceReasons").Select(PsValue.Str).Any(r => r.StartsWith("process-exit-", StringComparison.OrdinalIgnoreCase))
            && reasons.SequenceEqual(new[] { "earlier", "completed-but-hung session-runner children=pid=42/image=git" }), "group4 completed hang drops the kill exit code reason",
            string.Join(",", reasons));
        var timing = world.Timing("session-runner", "all-group-4");
        rows.Check(outcome.TestsOutcome == "TESTS" && NightlyChunkWorld.Field(timing, "why") == "completed-but-hung" && NightlyChunkWorld.Field(timing, "lastPhase") == "timeout",
            "group4 completed hang is a test outcome rather than a generic timeout", outcome.TestsOutcome + " " + NightlyChunkWorld.Field(timing, "why"));
        rows.ShouldAllPass(25);
    }

    /// <summary>
    /// The native owner's TRX completion reader (ExecutionEvidenceFinished, private, compiled into the core since S04) on
    /// opening-tag TRX shapes, its process-start age check, the completion grace constant and the stop-to-exit line.
    /// </summary>
    [Test]
    public void Group4NativeTrxReader()
    {
        using var fixture = NightlyFixture.Create("group4-reader");
        var rows = new NightlyCaseRows();
        var type = typeof(NativeProcessOwner);
        var assembly = type.Assembly.GetName().Name;
        rows.Check(assembly == "Antiphon.Nightly", "group4 native owner is part of the compiled core", assembly == "Antiphon.Nightly" ? "" : "NativeProcessOwner comes from " + assembly);
        MethodInfo? Find(int count) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "ExecutionEvidenceFinished" && m.GetParameters().Length == count);
        var reader = Find(2);
        var aged = Find(3);
        rows.Check(reader != null, "group4 native reader method is loaded");
        (bool Finished, string Error) Call(MethodInfo? method, string trx, string diag, DateTime? notBefore = null)
        {
            if (method == null) return (false, "method-missing");
            try
            {
                var args = method.GetParameters().Length >= 3 ? new object[] { trx, diag, notBefore ?? default } : new object[] { trx, diag };
                return ((bool)method.Invoke(null, args)!, "");
            }
            catch (TargetInvocationException ex) { return (false, ex.InnerException?.Message ?? ex.Message); }
        }
        var matchTrx = fixture.PathOf("match.trx");
        var matchDiag = fixture.PathOf("match.diag");
        WriteSummaryTrx(matchTrx, "Completed", 119, 119, 0);
        WriteFinishedDiag(matchDiag, 119);
        var shape = Call(reader, matchTrx, matchDiag);
        rows.Check(shape.Finished && shape.Error == "", "group4 native reader accepts the case trx shape", "finished=" + shape.Finished + " error=" + shape.Error);
        var realText = File.ReadAllText(Path.Combine(ProbeRoot, "all.trx"));
        var shaped = realText.Replace("total=\"8\"", "total=\"119\"").Replace("executed=\"8\"", "executed=\"119\"").Replace("passed=\"8\"", "passed=\"119\"");
        rows.Check(shaped.Contains("total=\"119\"") && shaped.Contains("executed=\"119\"") && shaped.Contains("outcome=\"Completed\"") && !shaped.Contains("total=\"8\""),
            "group4 real c487-probe shape was rewritten to 119/119");
        var realTrx = fixture.PathOf("real-shape.trx");
        File.WriteAllText(realTrx, shaped);
        var real = Call(reader, realTrx, matchDiag);
        rows.Check(real.Finished && real.Error == "", "group4 native reader accepts the real c487-probe trx shape", "finished=" + real.Finished + " error=" + real.Error);
        var nsPath = fixture.PathOf("ns.trx");
        File.WriteAllText(nsPath, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns:t=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><t:ResultSummary outcome='Completed'><t:Counters notExecuted='0' failed='0' executed='119' total='119' /></t:ResultSummary></TestRun>");
        var ns = Call(reader, nsPath, matchDiag);
        rows.Check(ns.Finished && ns.Error == "", "group4 native reader accepts a prefixed summary and single quotes", "finished=" + ns.Finished + " error=" + ns.Error);
        var notExec = fixture.PathOf("not-executed.trx");
        WriteSummaryTrx(notExec, "Completed", 119, 119, 1);
        rows.Check(!Call(reader, notExec, matchDiag).Finished, "group4 native reader rejects notExecuted above zero");
        var lowDiag = fixture.PathOf("low.diag");
        WriteFinishedDiag(lowDiag, 118);
        rows.Check(!Call(reader, matchTrx, lowDiag).Finished, "group4 native reader rejects total ran below the trx total");
        var failedCount = fixture.PathOf("failed-count.trx");
        WriteSummaryTrx(failedCount, "Completed", 119, 119, 0, 3);
        rows.Check(!Call(reader, failedCount, matchDiag).Finished, "group4 native reader rejects failed above zero");
        var failedOutcome = fixture.PathOf("failed-outcome.trx");
        WriteSummaryTrx(failedOutcome, "Failed", 119, 119, 0);
        rows.Check(!Call(reader, failedOutcome, matchDiag).Finished, "group4 native reader rejects outcome Failed");
        var missing = fixture.PathOf("missing.trx");
        File.WriteAllText(missing, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"></TestRun>");
        rows.Check(!Call(reader, missing, matchDiag).Finished, "group4 native reader rejects a trx with no ResultSummary");
        var truncated = fixture.PathOf("truncated.trx");
        File.WriteAllText(truncated, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"119\" executed=\"119\" failed=\"0\" notExecuted=\"0\"");
        rows.Check(!Call(reader, truncated, matchDiag).Finished, "group4 native reader rejects a truncated trx");

        rows.Check(aged != null, "group4 native reader has a process-start age check");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 11, 10, 11, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(matchTrx, old);
        File.SetLastWriteTimeUtc(matchDiag, newer);
        var staleTrx = Call(aged, matchTrx, matchDiag, before);
        rows.Check(!staleTrx.Finished, "group4 native reader ignores a trx older than the process start", "finished=" + staleTrx.Finished + " error=" + staleTrx.Error);
        File.SetLastWriteTimeUtc(matchTrx, newer);
        File.SetLastWriteTimeUtc(matchDiag, old);
        var staleDiag = Call(aged, matchTrx, matchDiag, before);
        rows.Check(!staleDiag.Finished, "group4 native reader ignores a diagnostic older than the process start", "finished=" + staleDiag.Finished + " error=" + staleDiag.Error);
        File.SetLastWriteTimeUtc(matchTrx, newer);
        File.SetLastWriteTimeUtc(matchDiag, newer);
        var sameAge = Call(aged, matchTrx, matchDiag, newer);
        rows.Check(!sameAge.Finished, "group4 native reader ignores evidence timestamped at the process start", "finished=" + sameAge.Finished + " error=" + sameAge.Error);
        var freshAge = Call(aged, matchTrx, matchDiag, before);
        rows.Check(freshAge.Finished && freshAge.Error == "", "group4 native reader accepts evidence newer than the process start", "finished=" + freshAge.Finished + " error=" + freshAge.Error);

        var contextGrace = new NightlyChunkContext().CompletionGraceMilliseconds;
        rows.Check(NightlyCompletionGrace.DefaultMilliseconds == 60000 && contextGrace == NightlyCompletionGrace.DefaultMilliseconds,
            "group4 grace constant is the unmeasured 60000 ms placeholder", "csharp=" + NightlyCompletionGrace.DefaultMilliseconds + " chunk=" + contextGrace);
        var lib = Path.Combine(NightlyPlanCases.Repo, "scripts", "lib");
        var ownerText = File.ReadAllText(Path.Combine(lib, "Antiphon.Nightly", "NativeProcessOwner.cs"));
        rows.Check(ownerText.Contains("NightlyCompletionGrace.Wait(") && ownerText.Contains("2x") && ownerText.Contains("p95"), "group4 native loop waits on the named grace constant");
        // W2-nightly S17: no PowerShell library keeps a grace copy (nightly-tests-impl.ps1, which once did, is deleted).
        var graceCopies = Directory.GetFiles(lib, "*.ps1").Where(f => File.ReadAllText(f).Contains("function Invoke-NightlyCompletionGrace")).Select(Path.GetFileName).ToArray();
        rows.Check(graceCopies.Length == 0, "group4 powershell grace copy is not a second implementation", string.Join(",", graceCopies));

        var stamp = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(matchDiag, stamp);
        var measured = NightlyCompletionGrace.MeasureFileStopToExitSeconds(matchDiag, stamp.AddSeconds(5));
        rows.Check(Math.Abs(measured - 5.0) < 0.05, "group4 stop-to-exit seconds use the diagnostic write and the process exit", "seconds=" + measured);
        var linePath = fixture.PathOf("chunk.log");
        File.WriteAllText(linePath, "host-output\n");
        var lineError = Refusal(() => NightlyCompletionGrace.AppendStopToExitLine(linePath, 5.0));
        var lineText = File.ReadAllText(linePath);
        rows.Check(lineError == "" && lineText.Contains("execution-stop-to-exit-seconds=5.000"), "group4 chunk log records stop-to-exit seconds", lineError + lineText);
        var skipPath = fixture.PathOf("skip.log");
        File.WriteAllText(skipPath, "host-output\n");
        Refusal(() => NightlyCompletionGrace.AppendStopToExitLine(skipPath, -1));
        var skipText = File.ReadAllText(skipPath);
        rows.Check(!skipText.Contains("execution-stop-to-exit-seconds="), "group4 chunk log omits stop-to-exit when the markers were absent", skipText);
        rows.Check(Watchdog.HarnessHangField(true) == "completed-but-hung", "group4 summary harnessHang reason is completed-but-hung");
        rows.Check(Watchdog.HarnessHangField(false) == "", "group4 summary harnessHang is empty when the chunk is not a hang");
        var runText = File.ReadAllText(Path.Combine(lib, "Antiphon.Nightly", "NightlyTests.cs"));
        rows.Check(runText.Contains("{ \"harnessHang\", NightlyWatchdog.HarnessHangField("), "group4 summary records harnessHang beside the red result");
        var noteText = File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "docs", "investigations", "2026-11-10-why-missed-session-runner-group4-post-completion-hang.md"));
        rows.Check(noteText.Contains("publicationTerminal") && noteText.Contains("failed-run") && noteText.Contains("<ResultSummary"),
            "group4 note records the opening-tag reader and publicationTerminal failed-run");
        rows.ShouldAllPass(27);
    }
}
