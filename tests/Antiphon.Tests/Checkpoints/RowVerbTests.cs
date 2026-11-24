using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// The row verb, which scripts/run-checkpoint.ps1 launches since unit 20 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the CARD-0585, CARD-0589, CARD-0615 and CARD-0671 cases of
/// the deleted scripts/test-run-checkpoint.ps1 (V-5..V-14 and CARD-0589 V-4), in process. A FakeDriver stands in for dotnet
/// and copies a fixture TRX (scripts/fixtures/c585-*.trx) into the row's results directory, a scripted broker on a manual
/// clock stands in for the session runner's /build-slots, and a fixed source snapshot for git; no build, test host or
/// broker runs. Broker requests and dotnet calls share one timeline, so POST -> build -> run -> DELETE is one order.
/// </summary>
[Category("Unit")]
public sealed class RowVerbTests : CheckpointTestBase
{
    private const string Lease = "c5890000-0000-4000-8000-000000000001";
    private const string Label = "CP-1@tests/Antiphon.Tests";
    private const string Filter = "/*/*/C585SampleTests/*";

    private sealed class Fixture
    {
        public required string Repo { get; init; }
        public string Output { get; init; } = "bin-c585-" + Guid.NewGuid().ToString("N")[..8] + "/";
        public List<string> Timeline { get; } = [];
        public List<string> Labels { get; } = [];
        public string? Trx { get; set; } = "c585-green.trx";
        public int BuildExit { get; set; }
        public int RunExit { get; set; }
        public bool Windows { get; set; } = true;
        public string? Stamp { get; set; }
        public Queue<string> Broker { get; } = new();
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        public string Absolute => Path.GetFullPath(Path.Combine(Repo, "tests/Antiphon.Tests", Output)).Replace('\\', '/').TrimEnd('/') + "/";

        public string Artifacts => Absolute.TrimEnd('/') + "/artifacts";
    }

    private sealed record Result(int Exit, string[] Lines, FakeDriver Driver, Fixture Fixture)
    {
        public string Text => string.Join("\n", Lines);
        public string Line => Lines.Single(line => line.StartsWith("CHECKPOINT CP-1 commit=", StringComparison.Ordinal));
        public string[] Builds => Driver.Calls.Where(CheckpointFixtures.IsBuild).Select(call => string.Join(" ", call.Arguments)).ToArray();
        public string[] Runs => Driver.Calls.Where(CheckpointFixtures.IsRun).Select(call => string.Join(" ", call.Arguments)).ToArray();
        public string Order => string.Join(",", Fixture.Timeline);
    }

    private Fixture New()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, "tests", "Antiphon.Tests"));
        return new Fixture { Repo = repo };
    }

    private async Task<Result> Row(Fixture fixture, params string[] flags)
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) =>
        {
            lock (fixture.Timeline) fixture.Timeline.Add("build");
            return Task.FromResult(new DriverResult(fixture.BuildExit, "", ""));
        });
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            lock (fixture.Timeline) fixture.Timeline.Add("run");
            if (fixture.Trx is not null)
            {
                Directory.CreateDirectory(CheckpointFixtures.ResultsDirectory(request));
                File.Copy(CheckpointFixtures.Trx(fixture.Trx), CheckpointFixtures.TrxFile(request));
            }
            return Task.FromResult(new DriverResult(fixture.RunExit, "", ""));
        });
        var output = new StringWriter();
        var arguments = new List<string>
        {
            "row", "--repo-root", fixture.Repo, "--name", "CP-1", "--project", "tests/Antiphon.Tests", "--output-path", fixture.Output,
            "--filter", Filter, "--results-root", "results",
        };
        arguments.AddRange(flags);
        var exit = await Antiphon.Checkpoints.Program.RunAsync(arguments.ToArray(), new CheckpointApp.Runtime
        {
            Output = output,
            Driver = driver,
            Platform = new FakePlatform { IsWindows = fixture.Windows },
            SourceCapture = _ => CheckpointFixtures.CleanSource(),
            EnvironmentLookup = _ => null,
            RowStamp = fixture.Stamp is null ? null : () => fixture.Stamp,
            SlotHandler = new Broker(fixture),
            SlotClock = () => fixture.Now,
            SlotDelay = (span, _) => { fixture.Now += span; return Task.CompletedTask; },
            SlotHolders = new ThisProcess(),
        });
        var lines = output.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        return new Result(exit, lines, driver, fixture);
    }

    // ---- CARD-0585 V-5..V-13 ----

    [Test]
    public async Task C585_green_row_reports_the_fresh_trx_and_the_class_qualified_roster()
    {
        var row = await Row(New(), "--no-slot");
        row.Exit.ShouldBe(0, row.Text);
        row.Line.ShouldContain("build=ok ");
        row.Line.ShouldContain("executed=3 passed=3 failed=0 skipped=0");
        Regex.IsMatch(row.Line, @"trx=.+run\.trx slot=skipped waited=0s dirty=").ShouldBeTrue(row.Line);
        var executed = row.Lines.Where(line => line.StartsWith("EXECUTED ", StringComparison.Ordinal)).ToArray();
        executed.Length.ShouldBe(3);
        executed.ShouldContain("EXECUTED Antiphon.Tests.Scripts.C585SampleTests.alpha_is_green");
        row.Lines[^1].ShouldBe("CHECKPOINT CP-1 EXIT CODE: 0");
    }

    [Test]
    public async Task C585_failures_are_exit_1_and_named_by_class_not_display_name()
    {
        var fixture = New();
        fixture.Trx = "c585-failures.trx";
        fixture.RunExit = 1;
        var row = await Row(fixture, "--no-slot");
        row.Exit.ShouldBe(1, row.Text);
        row.Line.ShouldContain("executed=3 passed=2 failed=1 skipped=0");
        row.Lines.Where(line => line.StartsWith("FAILED ", StringComparison.Ordinal))
            .ShouldBe(new[] { "FAILED Antiphon.Tests.Scripts.C585SampleTests.delta_is_red" });
    }

    [Test]
    public async Task C585_zero_executed_is_exit_3_with_no_roster()
    {
        var fixture = New();
        fixture.Trx = "c585-zero.trx";
        fixture.RunExit = 8;
        var row = await Row(fixture, "--no-slot");
        row.Exit.ShouldBe(3, row.Text);
        row.Line.ShouldContain("executed=0 passed=0 failed=0 skipped=0");
        row.Lines.ShouldNotContain(line => line.StartsWith("EXECUTED ", StringComparison.Ordinal));
    }

    [Test]
    public async Task C585_roster_miss_is_exit_3_and_a_comma_separated_expect_splits()
    {
        var miss = await Row(New(), "--no-slot", "--expect", "NotInRoster");
        miss.Exit.ShouldBe(3, miss.Text);
        miss.Lines.ShouldContain("ROSTER MISS NotInRoster");
        var control = await Row(New(), "--no-slot", "--expect", "C585SampleTests");
        control.Exit.ShouldBe(0, control.Text);
        control.Text.ShouldNotContain("ROSTER MISS");
        var comma = await Row(New(), "--no-slot", "--expect", "C585SampleTests,C585OtherTests");
        comma.Exit.ShouldBe(0, comma.Text);
        var commaMiss = await Row(New(), "--no-slot", "--expect", "C585SampleTests,NotInRoster");
        commaMiss.Exit.ShouldBe(3, commaMiss.Text);
        commaMiss.Lines.ShouldContain("ROSTER MISS NotInRoster");
    }

    // CARD-0615 V-1: the edge quotes of a quoted roster are wrapper syntax; an interior quote stays significant.
    [Test]
    [Arguments("'C585SampleTests','C585OtherTests'", 0, null)]
    [Arguments("\"C585SampleTests\",\"C585OtherTests\"", 0, null)]
    [Arguments("  ' C585SampleTests ' , \" C585OtherTests \"  ", 0, null)]
    [Arguments("'C585SampleTests',\"NotInRoster\"", 3, "ROSTER MISS NotInRoster")]
    [Arguments("'C585Sam'pleTests','C585OtherTests'", 3, "ROSTER MISS C585Sam'pleTests")]
    [Arguments("'','C585SampleTests',\"\",'C585OtherTests'", 0, null)]
    public async Task C585_quoted_expect_normalizes_only_its_edges(string expect, int exit, string? miss)
    {
        var row = await Row(New(), "--no-slot", "--min-executed", "3", "--expect", expect);
        row.Exit.ShouldBe(exit, row.Text);
        row.Line.ShouldContain("executed=3 passed=3 failed=0 skipped=0");
        row.Lines.Where(line => line.StartsWith("ROSTER MISS ", StringComparison.Ordinal)).ShouldBe(miss is null ? Array.Empty<string>() : new[] { miss });
    }

    [Test]
    [Arguments("bin-x\\")]
    [Arguments("bin-x/ ")]
    [Arguments("x/")]
    public async Task C585_bad_output_path_is_refused_before_any_dotnet_call(string output)
    {
        var fixture = new Fixture { Repo = New().Repo, Output = output };
        var row = await Row(fixture, "--no-slot");
        row.Exit.ShouldBe(2, row.Text);
        row.Text.ShouldContain("forward slash");
        row.Text.ShouldContain("CARD-0448");
        row.Driver.Calls.ShouldBeEmpty();
        Directory.Exists(Path.Combine(fixture.Repo, "results")).ShouldBeFalse("nothing is created before the inputs pass");
        row.Lines[^1].ShouldBe("CHECKPOINT CP-1 EXIT CODE: 2");
    }

    [Test]
    public async Task C585_failed_build_is_exit_2_and_never_runs_the_tests()
    {
        var fixture = New();
        fixture.BuildExit = 1;
        var row = await Row(fixture, "--no-slot");
        row.Exit.ShouldBe(2, row.Text);
        row.Text.ShouldContain("CHECKPOINT CP-1 build=failed");
        row.Line.ShouldContain(" build=failed ");
        (row.Builds.Length, row.Runs.Length).ShouldBe((1, 0));
        row.Lines[^1].ShouldBe("CHECKPOINT CP-1 EXIT CODE: 2");
    }

    [Test]
    public async Task C585_an_existing_results_directory_is_refused_and_another_stamp_runs()
    {
        var fixture = New();
        fixture.Stamp = "20260920-101010-abcd";
        Directory.CreateDirectory(Path.Combine(fixture.Repo, "results", "CP-1-" + fixture.Stamp));
        var refused = await Row(fixture, "--no-slot");
        refused.Exit.ShouldBe(2, refused.Text);
        refused.Text.ShouldContain("results directory exists");
        refused.Driver.Calls.ShouldBeEmpty();
        fixture.Stamp = "20260920-101011-abce";
        var second = await Row(fixture, "--no-slot");
        second.Exit.ShouldBe(0, second.Text);
    }

    [Test]
    public async Task C585_no_build_reuses_a_verified_build_and_never_builds()
    {
        var fixture = New();
        (await Row(fixture, "--no-slot")).Exit.ShouldBe(0, "seed build");
        var row = await Row(fixture, "--no-slot", "--no-build");
        row.Builds.ShouldBeEmpty();
        row.Line.ShouldContain("build=reused ");
        row.Line.ShouldContain("buildSource=verified");
        row.Exit.ShouldBe(0, row.Text);
    }

    [Test]
    public async Task C585_line_format_is_pinned_between_the_slot_line_and_the_trailer()
    {
        var row = await Row(New(), "--no-slot");
        row.Lines[0].ShouldBe("BUILD SLOT skipped by -NoSlot");
        Regex.IsMatch(row.Line, @"^CHECKPOINT CP-1 commit=[0-9a-f]{40} build=(ok|reused) filter=.+ executed=\d+ passed=\d+ failed=\d+ skipped=\d+ trx=.+ slot=(granted|unleased|unlimited|skipped) waited=\d+s dirty=\d+ source=[0-9a-f]{40}(\+dirty:[0-9a-f]{64})? sourceState=(clean|dirty) buildSource=(verified|unknown|mismatch)$")
            .ShouldBeTrue(row.Line);
        Regex.IsMatch(row.Lines[^1], @"^CHECKPOINT CP-1 EXIT CODE: \d$").ShouldBeTrue(row.Lines[^1]);
    }

    [Test]
    public async Task C585_every_outcome_writes_its_report_and_ends_with_the_trailer()
    {
        var fixture = New();
        fixture.BuildExit = 1;
        var row = await Row(fixture, "--no-slot");
        var report = Directory.GetFiles(Path.Combine(fixture.Repo, "results"), "report.json", SearchOption.AllDirectories).Single();
        var model = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(report), ReportWriter.Json)!;
        model.ExitCode.ShouldBe(2);
        model.Rows.Single().Line.ShouldBe(row.Line);
        model.Rows.Single().Line!.ShouldContain(" build=failed ");
    }

    // ---- CARD-0671: MSBuild properties reach the build and the dotnet run ----

    private static string ExpectedBuild(Fixture fixture, params string[] properties) =>
        string.Join(" ", new[] { "build", "tests/Antiphon.Tests", "--property:OutputPath=" + fixture.Output }
            .Concat(properties.Select(property => "--property:" + property))
            .Concat(["--property:ArtifactsPath=" + fixture.Artifacts, "--property:UseArtifactsOutput=true", "-nodeReuse:false"]));

    private static string ExpectedRun(Fixture fixture, params string[] properties) =>
        string.Join(" ", new[] { "run", "--project", "tests/Antiphon.Tests", "--no-build", "--property:OutputPath=" + fixture.Absolute }
            .Concat(properties.Select(property => "--property:" + property))
            .Concat(["--property:ArtifactsPath=" + fixture.Artifacts, "--property:UseArtifactsOutput=true", "--", "--treenode-filter", Filter]));

    [Test]
    public async Task C671_a_property_reaches_the_build_the_run_and_a_no_build_run()
    {
        var fixture = New();
        var row = await Row(fixture, "--no-slot", "--msbuild-property", "UseAppHost=false");
        row.Exit.ShouldBe(0, row.Text);
        row.Builds.Single().ShouldBe(ExpectedBuild(fixture, "UseAppHost=false") + " --nologo");
        row.Runs.Single().ShouldStartWith(ExpectedRun(fixture, "UseAppHost=false") + " --report-trx --report-trx-filename run.trx --results-directory ");
        row.Lines.Count(line => line == "MSBUILD PROPERTY UseAppHost=false").ShouldBe(1);

        var reused = await Row(fixture, "--no-slot", "--no-build", "--msbuild-property", "UseAppHost=false");
        reused.Exit.ShouldBe(0, reused.Text);
        reused.Builds.ShouldBeEmpty();
        reused.Runs.Single().ShouldStartWith(ExpectedRun(fixture, "UseAppHost=false") + " ");

        var comma = New();
        var both = await Row(comma, "--no-slot", "--msbuild-property", "UseAppHost=false, C671Probe=yes");
        both.Exit.ShouldBe(0, both.Text);
        both.Builds.Single().ShouldBe(ExpectedBuild(comma, "UseAppHost=false", "C671Probe=yes") + " --nologo");
        both.Runs.Single().ShouldStartWith(ExpectedRun(comma, "UseAppHost=false", "C671Probe=yes") + " ");
    }

    [Test]
    public async Task C671_off_windows_use_app_host_false_is_the_default_unless_named()
    {
        var linux = New();
        linux.Windows = false;
        var row = await Row(linux, "--no-slot");
        row.Exit.ShouldBe(0, row.Text);
        row.Builds.Single().ShouldBe(ExpectedBuild(linux, "UseAppHost=false") + " --nologo");
        row.Runs.Single().ShouldStartWith(ExpectedRun(linux, "UseAppHost=false") + " ");

        var explicitHost = New();
        explicitHost.Windows = false;
        var named = await Row(explicitHost, "--no-slot", "--msbuild-property", "UseAppHost=true");
        named.Exit.ShouldBe(0, named.Text);
        named.Driver.Calls.ShouldNotContain(call => call.Arguments.Contains("--property:UseAppHost=false"));
        named.Builds.Single().ShouldBe(ExpectedBuild(explicitHost, "UseAppHost=true") + " --nologo");

        var other = New();
        other.Windows = false;
        var probe = await Row(other, "--no-slot", "--msbuild-property", "C671Probe=yes");
        probe.Builds.Single().ShouldBe(ExpectedBuild(other, "C671Probe=yes", "UseAppHost=false") + " --nologo");
    }

    [Test]
    public async Task C671_windows_forwards_only_the_output_properties_in_order()
    {
        var fixture = New();
        var row = await Row(fixture, "--no-slot");
        row.Builds.Single().ShouldBe(ExpectedBuild(fixture) + " --nologo");
        row.Runs.Single().ShouldStartWith(ExpectedRun(fixture) + " --report-trx ");
        row.Lines.Where(line => line.StartsWith("MSBUILD PROPERTY ", StringComparison.Ordinal))
            .ShouldBe(new[] { "MSBUILD PROPERTY ArtifactsPath=" + fixture.Artifacts, "MSBUILD PROPERTY UseArtifactsOutput=true" });
    }

    [Test]
    [Arguments("NotAProperty", "must be Name=Value")]
    [Arguments("OutputPath=bin-y/", "use -OutputPath")]
    [Arguments("UseAppHost=false;OutDir=x/", "use -OutputPath")]
    [Arguments("AntiphonCheckpointRepeat=3", "reserved repeat properties")]
    public async Task C671_an_invalid_property_is_refused_before_any_dotnet_call(string property, string message)
    {
        var fixture = New();
        fixture.Windows = false;
        var row = await Row(fixture, "--no-slot", "--msbuild-property", property);
        row.Exit.ShouldBe(2, row.Text);
        row.Text.ShouldContain(message);
        row.Driver.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Inputs_are_checked_before_the_slot_and_a_strict_reuse_checks_its_stamp_first()
    {
        var repeat = await Row(New(), "--repeat", "0");
        repeat.Exit.ShouldBe(2, repeat.Text);
        repeat.Text.ShouldContain("Repeat must be a positive integer");
        var product = await Row(New(), "--repeat", "3", "--min-executed", int.MaxValue.ToString());
        product.Text.ShouldContain("Repeat x MinExecuted must fit an integer");
        var sha = await Row(New(), "--expected-source-sha", "ABC");
        sha.Text.ShouldContain("ExpectedSourceSha must be a normalized full Git object ID");
        foreach (var refused in new[] { repeat, product, sha })
        {
            refused.Exit.ShouldBe(2);
            refused.Fixture.Timeline.ShouldBeEmpty("an invalid row asks the broker nothing");
            refused.Driver.Calls.ShouldBeEmpty();
        }

        // CARD-0835: a strict --no-build row without a matching stamp stops before it waits for a slot.
        var unstamped = await Row(New(), "--no-build", "--expected-source-sha", new string('a', 40));
        unstamped.Exit.ShouldBe(2, unstamped.Text);
        unstamped.Line.ShouldEndWith(" reason=build_source_mismatch");
        unstamped.Fixture.Timeline.ShouldBeEmpty();
        var mismatch = await Row(New(), "--no-slot", "--expected-source-sha", new string('b', 40));
        mismatch.Exit.ShouldBe(2);
        mismatch.Line.ShouldEndWith(" reason=source_mismatch");
        mismatch.Driver.Calls.ShouldBeEmpty();
    }

    // ---- CARD-0589 V-4: the row takes a host build slot ----

    [Test]
    public async Task C589_a_granted_slot_caps_the_build_and_is_released_after_the_run()
    {
        var fixture = New();
        fixture.Broker.Enqueue("granted");
        var row = await Row(fixture);
        row.Exit.ShouldBe(0, row.Text);
        row.Builds.Single().ShouldBe(ExpectedBuild(fixture) + " -maxcpucount:3 --nologo");
        row.Runs.Single().ShouldNotContain("maxcpucount");
        row.Lines.Count(line => Regex.IsMatch(line, "^BUILD SLOT granted lease=" + Lease + @" label=.+ waited=\d+s maxcpucount=3$")).ShouldBe(1, row.Text);
        row.Order.ShouldBe("GET,POST,build,run,DELETE");
        fixture.Labels.ShouldBe(new[] { Label });
        row.Line.ShouldContain(" slot=granted waited=0s dirty=");
        row.Lines.Count(line => line.StartsWith("BUILD SLOT released lease=" + Lease, StringComparison.Ordinal)).ShouldBe(1);
        row.Lines[^1].ShouldBe("CHECKPOINT CP-1 EXIT CODE: 0", "the trailer follows the release");
    }

    [Test]
    public async Task C589_a_busy_broker_is_waited_for_in_order_with_visible_lines()
    {
        var fixture = New();
        foreach (var answer in new[] { "busy", "memory_floor", "granted" }) fixture.Broker.Enqueue(answer);
        var row = await Row(fixture);
        row.Exit.ShouldBe(0, row.Text);
        row.Lines.ShouldContain(line => line.StartsWith("BUILD SLOT waiting label=\"" + Label + "\" reason=build_slot_busy elapsed=", StringComparison.Ordinal));
        row.Lines.ShouldNotContain(line => line.Contains(" status=409 ", StringComparison.Ordinal), "a busy answer is not printed on every retry");
        row.Order.ShouldBe("GET,POST,POST,POST,build,run,DELETE");
        row.Line.ShouldContain(" slot=granted waited=");
    }

    [Test]
    public async Task C589_no_slot_within_the_wait_is_exit_4_and_nothing_runs()
    {
        var fixture = New();
        for (var i = 0; i < 100; i++) fixture.Broker.Enqueue("busy");
        var row = await Row(fixture, "--slot-wait-minutes", "0.05");
        row.Exit.ShouldBe(4, row.Text);
        row.Driver.Calls.ShouldBeEmpty();
        row.Lines.ShouldContain(line => line.StartsWith("BUILD SLOT timeout label=\"" + Label + "\" after ", StringComparison.Ordinal));
        row.Lines.ShouldContain(line => Regex.IsMatch(line, @"^CHECKPOINT CP-1 slot=timeout waited=\d+s: no build slot within 0\.05 minutes; nothing was built or run$"));
        fixture.Timeline.ShouldNotContain("DELETE");
        fixture.Timeline.Count(step => step == "POST").ShouldBeGreaterThanOrEqualTo(2);
        row.Line.ShouldContain(" slot=timeout ");
        row.Lines[^1].ShouldBe("CHECKPOINT CP-1 EXIT CODE: 4");
    }

    [Test]
    public async Task C589_an_unreachable_runner_falls_back_to_an_unleased_build_at_four_cpus()
    {
        var fixture = New();
        fixture.Broker.Enqueue("unreachable");
        var row = await Row(fixture);
        row.Exit.ShouldBe(0, row.Text);
        row.Lines.ShouldContain(line => line.StartsWith("BUILD SLOT ", StringComparison.Ordinal) && line.EndsWith(" fallback=unleased", StringComparison.Ordinal));
        row.Builds.Single().ShouldBe(ExpectedBuild(fixture) + " -maxcpucount:4 --nologo");
        row.Line.ShouldContain(" slot=unleased waited=");
        fixture.Timeline.ShouldNotContain("DELETE");

        // A runner without /build-slots (404) is unavailable at once, still at the fallback -maxcpucount:4.
        var old = New();
        old.Broker.Enqueue("notfound");
        var gone = await Row(old);
        gone.Exit.ShouldBe(0, gone.Text);
        gone.Builds.Single().ShouldBe(ExpectedBuild(old) + " -maxcpucount:4 --nologo");
        gone.Line.ShouldContain(" slot=unavailable waited=0s");

        // The exit code follows the run.
        var red = New();
        red.Broker.Enqueue("unreachable");
        red.Trx = "c585-failures.trx";
        red.RunExit = 1;
        (await Row(red)).Exit.ShouldBe(1);
    }

    [Test]
    public async Task C589_a_no_build_row_still_leases_around_its_run()
    {
        var fixture = New();
        (await Row(fixture, "--no-slot")).Exit.ShouldBe(0, "seed build");
        fixture.Timeline.Clear();
        fixture.Broker.Enqueue("granted");
        var row = await Row(fixture, "--no-build");
        row.Order.ShouldBe("GET,POST,run,DELETE");
        row.Exit.ShouldBe(0, row.Text);
        row.Line.ShouldContain("build=reused ");
        row.Line.ShouldContain(" slot=granted waited=");
    }

    [Test]
    public async Task C589_no_slot_asks_the_broker_nothing_and_passes_no_cpu_cap()
    {
        var fixture = New();
        var row = await Row(fixture, "--no-slot");
        row.Lines[0].ShouldBe("BUILD SLOT skipped by -NoSlot");
        fixture.Timeline.ShouldBe(new[] { "build", "run" });
        row.Builds.Single().ShouldBe(ExpectedBuild(fixture) + " --nologo");
        row.Line.ShouldContain(" slot=skipped waited=0s dirty=");
    }

    [Test]
    public async Task A_definitive_broker_refusal_is_exit_2_before_any_driver()
    {
        var fixture = New();
        fixture.Broker.Enqueue("refused");
        var row = await Row(fixture);
        row.Exit.ShouldBe(2, row.Text);
        row.Driver.Calls.ShouldBeEmpty();
        row.Line.ShouldContain(" slot=refused ");
        row.Line.ShouldContain(" slot-reason=build_slot_invalid");
    }

    // The children see the row's output as it arrives, and inherit no phase-journal names (CARD-1748).
    [Test]
    public async Task Child_output_reaches_the_console_and_the_phase_family_is_cleared()
    {
        var fixture = New();
        var output = new StringWriter();
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (request, _) =>
        {
            request.OnOutput.ShouldNotBeNull();
            request.OnOutput!("Build succeeded.");
            request.ClearedEnvironmentPrefixes.ShouldBe(new[] { "ANTIPHON_TEST_PHASE_" });
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            request.ClearedEnvironmentPrefixes.ShouldBe(new[] { "ANTIPHON_TEST_PHASE_" });
            request.Environment!["ANTIPHON_TEST_PHASE_ROOT"].ShouldBe(Path.GetFullPath(Path.Combine(CheckpointFixtures.ResultsDirectory(request), "phase")));
            File.Copy(CheckpointFixtures.Trx("c585-green.trx"), CheckpointFixtures.TrxFile(request));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", fixture.Repo, "--name", "CP-1", "--project", "tests/Antiphon.Tests", "--output-path", fixture.Output,
            "--filter", Filter, "--no-slot",
        ], new CheckpointApp.Runtime
        {
            Output = output, Driver = driver, Platform = new FakePlatform { IsWindows = true },
            SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null,
        });
        exit.ShouldBe(0, output.ToString());
        output.ToString().ShouldContain("Build succeeded.");
        Directory.GetDirectories(Path.Combine(fixture.Repo, ".antiphon", "checkpoints"), "CP-1-*").Length.ShouldBe(1,
            "the default results root is the repository's .antiphon/checkpoints");
    }

    [Test]
    public async Task A_repeat_row_writes_a_schema_3_report_with_its_evidence()
    {
        var fixture = New();
        File.WriteAllText(Path.Combine(fixture.Repo, "tests", "Antiphon.Tests", "Antiphon.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.44.0\" /></ItemGroup></Project>");
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var template = File.ReadAllText(CheckpointRepeatFixture.WriteTemplate(TempDir(), 2, methods: ["alpha"]))
                .Replace("__NONCE__", request.Environment!["ANTIPHON_CHECKPOINT_NONCE"], StringComparison.Ordinal);
            File.WriteAllText(CheckpointFixtures.TrxFile(request), template);
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", fixture.Repo, "--name", "CP-1", "--project", "tests/Antiphon.Tests", "--output-path", fixture.Output,
            "--filter", "/*/*/*/alpha", "--no-slot", "--repeat", "2", "--results-root", "results",
        ], new CheckpointApp.Runtime
        {
            Output = output, Driver = driver, Platform = new FakePlatform { IsWindows = true },
            SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null,
        });
        exit.ShouldBe(0, output.ToString());
        var line = output.ToString().Split('\n').Single(text => text.StartsWith("CHECKPOINT CP-1 commit=", StringComparison.Ordinal));
        line.ShouldContain(" repeat=2 repetitions=2/2 hostInvocations=1");
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(
            Directory.GetFiles(Path.Combine(fixture.Repo, "results"), "report.json", SearchOption.AllDirectories).Single()), ReportWriter.Json)!;
        report.SchemaVersion.ShouldBe(3);
        report.Rows.Single().Repeat.ShouldNotBeNull().Requested.ShouldBe(2);
    }

    // ---- the broker stand-in ----

    private sealed class ThisProcess : ILeaseHolderSource
    {
        public ILeaseHolder Open() => new Holder();

        private sealed class Holder : ILeaseHolder
        {
            public int Pid => Environment.ProcessId;
            public string? ProcessStartUtc => "2026-11-24T12:00:00.0000000Z";
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Broker(Fixture fixture) : HttpMessageHandler
    {
        private string _mode = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.Method.Method;
            lock (fixture.Timeline) fixture.Timeline.Add(method);
            if (method == "DELETE") return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (method == "GET")
            {
                _mode = fixture.Broker.Count > 0 ? fixture.Broker.Peek() : "unreachable";
                return _mode switch
                {
                    "unreachable" => throw new HttpRequestException("connection refused"),
                    "notfound" => Json(HttpStatusCode.NotFound, "{}"),
                    _ => Json(HttpStatusCode.OK, """{"enabled":true,"budget":2,"maxCpuCount":3}"""),
                };
            }
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using (var document = JsonDocument.Parse(body))
                fixture.Labels.Add(document.RootElement.GetProperty("label").GetString()!);
            var answer = fixture.Broker.Count > 0 ? fixture.Broker.Dequeue() : "unreachable";
            return answer switch
            {
                "granted" => Json(HttpStatusCode.OK, "{\"leaseId\":\"" + Lease + "\",\"maxCpuCount\":3}"),
                "busy" => Json(HttpStatusCode.Conflict, """{"type":"build_slot_busy","occupied":2,"budget":2,"queuePosition":1,"retryAfterMs":100}"""),
                "memory_floor" => Json(HttpStatusCode.Conflict, """{"type":"build_slot_memory_floor","availableMb":1000,"floorMb":6144,"queuePosition":1,"retryAfterMs":100}"""),
                "refused" => Json(HttpStatusCode.BadRequest, """{"type":"build_slot_invalid"}"""),
                _ => throw new HttpRequestException("connection refused"),
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
