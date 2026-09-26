using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointImportTests
{
    [Test]
    public void imports_the_card_0688_table()
    {
        var imported = PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-0688.md"));
        imported.ExitCode.ShouldBe(0, imported.Error);
        var manifest = imported.Manifest.ShouldNotBeNull();
        manifest.Checkpoints.Count.ShouldBe(13);
        manifest.Builds.Select(build => build.Id).OrderBy(id => id).ShouldBe(["bin-c688a", "bin-c688b", "bin-c688c", "bin-c688e"]);
        manifest.Checkpoints.Single(row => row.Id == "CP-11").Command.ShouldNotBeNull();
        manifest.Checkpoints.Single(row => row.Id == "CP-11").MinExecuted.ShouldBeNull();
        manifest.Checkpoints.Single(row => row.Id == "CP-13").Command.ShouldNotBeNull();
        manifest.Checkpoints.Single(row => row.Id == "CP-13").MinExecuted.ShouldBeNull();
        manifest.Checkpoints.Single(row => row.Id == "CP-2").Build.ShouldBe("bin-c688a");
        manifest.Checkpoints.Single(row => row.Id == "CP-1").Expect.ShouldContain("LandingGitTests");
        manifest.Checkpoints.Single(row => row.Id == "CP-1").Expect.ShouldContain("LandWorkspaceTests");
    }

    [Test]
    public void unescapes_pipes_in_filters()
    {
        var manifest = PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-0688.md")).Manifest!;
        var filter = manifest.Checkpoints.Single(row => row.Id == "CP-1").Filter!;
        filter.ShouldContain("|");
        filter.ShouldNotContain("\\|");
    }

    [Test]
    public void maps_cp_reuse_to_the_same_build()
    {
        var manifest = PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-0688.md")).Manifest!;
        manifest.Checkpoints.Single(row => row.Id == "CP-7").Build.ShouldBe("bin-c688a");
        manifest.Checkpoints.Single(row => row.Id == "CP-10").Build.ShouldBe(
            manifest.Checkpoints.Single(row => row.Id == "CP-9").Build);
    }

    [Test]
    public void command_rows_become_command_checkpoints()
    {
        var manifest = PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-0688.md")).Manifest!;
        manifest.Checkpoints.Single(row => row.Id == "CP-11").Command.ShouldContain("git grep");
        manifest.Checkpoints.Single(row => row.Id == "CP-11").Filter.ShouldBeNull();
        manifest.Checkpoints.Single(row => row.Id == "CP-13").IsCommand.ShouldBeTrue();
        var illustrative = PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-illustrative.md")).Manifest!;
        illustrative.Checkpoints.Single(row => row.Id == "CP-2").Command.ShouldContain("test-client.ps1");
    }

    [Test]
    public void derives_roster_tokens_from_the_filter()
    {
        var illustrative = PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-illustrative.md"));
        illustrative.ExitCode.ShouldBe(0, illustrative.Error);
        illustrative.Manifest!.Checkpoints.Single(row => row.Id == "CP-1").Expect.ShouldBe(["ExampleSurfaceTests"]);
        PlanTableImporter.RosterTokens("/*/*/*/*[Category=Unit]").ShouldBeEmpty();
        PlanTableImporter.RosterTokens("/*/*/AgentTaskLandDeliveryE2ETests/*").ShouldBe(["AgentTaskLandDeliveryE2ETests"]);
    }

    [Test]
    public void refuses_the_legacy_eight_column_table()
    {
        var imported = PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-legacy-min.md"));
        imported.ExitCode.ShouldBe(2);
        imported.Error.ShouldContain("CARD-0617");
        imported.Error.ShouldContain("EstimatedMinutes");
    }

    [Test]
    public void warns_when_estimate_exceeds_row_timeout()
    {
        var markdown = """
            ### Checkpoints

            | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
            |---|---|---|---|---|---|---|---:|---:|
            | CP-1 | S1 | `tests/Antiphon.Tests -> bin-ex/` | slow | `/*/*/SlowTests/*` | V-1 | all listed, 0 failed | 1 | 20 |
            """;
        var imported = PlanTableImporter.ImportMarkdown(markdown);
        imported.ExitCode.ShouldBe(0, imported.Error);
        imported.Warnings.ShouldContain(warning => warning.Contains("45", StringComparison.Ordinal));
        imported.Manifest!.Checkpoints[0].TimeoutMinutes.ShouldBe(60);
    }

    [Test]
    public void imports_the_card_0723_table()
    {
        var path = CheckpointFixtures.Fixture("plan-table-0723-same-after.md");
        var imported = PlanTableImporter.ImportFile(path);
        imported.ExitCode.ShouldBe(0, imported.Error);
        var manifest = imported.Manifest!;
        manifest.Checkpoints.Count.ShouldBe(9);
        var first = manifest.Checkpoints.Single(row => row.Id == "CP-1");
        first.Build.ShouldBe("bin-c723a");
        manifest.Builds.Single(build => build.Id == "bin-c723a").Project.ShouldBe("tests/Antiphon.Tests");
        manifest.Builds.Single(build => build.Id == "bin-c723a").OutputPath.ShouldBe("bin-c723a/");
        manifest.Checkpoints.Single(row => row.Id == "CP-2").Build.ShouldBe("bin-c723a");
        manifest.Checkpoints.Single(row => row.Id == "CP-6").Filter.ShouldBe("/*/*/*/*[Category=Unit]");
        manifest.Checkpoints.Single(row => row.Id == "CP-6").MinExecuted.ShouldBe(1000);
        manifest.Checkpoints.Single(row => row.Id == "CP-6").Expect.ShouldBeEmpty();
        manifest.Checkpoints.Single(row => row.Id == "CP-2").MinExecuted.ShouldBe(OperatingSystem.IsWindows() ? 15 : 16);
        manifest.Checkpoints.Single(row => row.Id == "CP-7").IsCommand.ShouldBeTrue();
        manifest.Checkpoints.Single(row => row.Id == "CP-7").Command.ShouldContain("dotnet exec");
        manifest.Checkpoints.Single(row => row.Id == "CP-7").Command!.ShouldNotContain(".antiphon/c723-pack/tp/antiphon-checkpoints --version");
        manifest.Checkpoints.Single(row => row.Id == "CP-8").IsCommand.ShouldBeTrue();
        PlanTableImporter.TryParseMin("16 linux / 15 windows", isWindows: false, out var linux, out var error).ShouldBeTrue(error);
        linux.ShouldBe(16);
        PlanTableImporter.TryParseMin("16 linux / 15 windows", isWindows: true, out var windows, out _).ShouldBeTrue();
        windows.ShouldBe(15);
        first.Filter.ShouldContain("|");
        var root = CheckpointFixtures.TempDir();
        var yaml = ManifestLoader.ToYaml(manifest);
        var roundTrip = ManifestLoader.LoadYaml(yaml, root);
        roundTrip.Checkpoints.Count.ShouldBe(9);
        roundTrip.Checkpoints.Single(row => row.Id == "CP-5b").Filter.ShouldContain("C544_DailyValidity");
        roundTrip.Checkpoints.Single(row => row.Id == "CP-5b").Expect.ShouldContain("C544_DailyValidity");
        roundTrip.Checkpoints.Single(row => row.Id == "CP-6").Filter.ShouldBe("/*/*/*/*[Category=Unit]");
    }

    private static string Table(string optionalHeaders, params string[] rows) =>
        "### Checkpoints\n\n| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes "
        + optionalHeaders + " |\n|---|---|---|---|---|---|---|---:|---:|\n"
        + string.Join('\n', rows) + "\n";

    private static string FilterRow(string id, string build, string after = "S1", string suffix = "") =>
        $"| {id} | {after} | {build} | {id} | `/*/*/{id}Tests/*` | V-1 | all listed | 1 | 1 {suffix} |";

    [Test]
    public async Task serial_import_round_trips_and_excludes_other_rows()
    {
        var markdown = Table("| Serial",
            "| CP-1 | S1 | n/a | first | `true` | V-1 | exit 0 | n/a | 1 | true |",
            "| CP-2 | S1 | n/a | second | `true` | V-2 | exit 0 | n/a | 1 | false |");
        var imported = PlanTableImporter.ImportMarkdown(markdown);
        imported.ExitCode.ShouldBe(0, imported.Error);
        var manifest = imported.Manifest!;
        manifest.Checkpoints[0].Serial.ShouldBeTrue();
        manifest.Checkpoints[1].Serial.ShouldBeFalse();
        var reloaded = ManifestLoader.LoadYaml(ManifestLoader.ToYaml(manifest), CheckpointFixtures.TempDir());
        reloaded.Checkpoints[0].Serial.ShouldBeTrue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, _) => { entered.TrySetResult(); await release.Task; return new DriverResult(0, "", ""); });
        var run = new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest, Rows = manifest.Checkpoints, RunDirectory = CheckpointFixtures.TempDir(),
            WorkingDirectory = CheckpointFixtures.RepoRoot, State = new RunState(), Slots = new FixedSlotClient("off"), Width = 2,
        }, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            driver.Count(_ => true).ShouldBe(1);
        }
        finally { release.TrySetResult(); }
        (await run).State.MaxConcurrentRows.ShouldBe(1);
        driver.Count(_ => true).ShouldBe(2);
    }

    [Test]
    public void environment_map_round_trips_without_changing_serial()
    {
        var imported = PlanTableImporter.ImportMarkdown(Table("| Environment",
            FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`", suffix: "| `TUNIT_MAX_PARALLEL_TESTS=1;C760_VALUE=a=b;C760_EMPTY=`")));
        imported.ExitCode.ShouldBe(0, imported.Error);
        var row = imported.Manifest!.Checkpoints.Single();
        row.Serial.ShouldBeFalse();
        row.Environment.Count.ShouldBe(3);
        row.Environment["TUNIT_MAX_PARALLEL_TESTS"].ShouldBe("1");
        row.Environment["C760_VALUE"].ShouldBe("a=b");
        row.Environment["C760_EMPTY"].ShouldBe("");
        var reload = ManifestLoader.LoadYaml(ManifestLoader.ToYaml(imported.Manifest), CheckpointFixtures.TempDir());
        reload.Checkpoints.Single().Environment.ShouldBe(row.Environment);
        PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-0688.md")).Manifest!.Checkpoints[0].Environment.ShouldBeEmpty();
        var trimmed = PlanTableImporter.ImportMarkdown(Table("| Environment",
            FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`", suffix: "| A=one  ; B=two")));
        trimmed.ExitCode.ShouldBe(0, trimmed.Error);
        trimmed.Manifest!.Checkpoints[0].Environment["A"].ShouldBe("one");
    }

    [Test]
    public async Task environment_reaches_only_its_row_and_its_reruns()
    {
        var name = "C760_TEST_VALUE";
        var original = Environment.GetEnvironmentVariable(name);
        var factory = new CapturingFactory();
        var runner = new RowRunner(new ProcessDriver(factory), new FakePlatform());
        var root = CheckpointFixtures.TempDir();
        await runner.RunAsync(new RowRequest { Name = "A", Command = "true", ResultsDirectory = Path.Combine(root, "a"),
            WorkingDirectory = root, Environment = new Dictionary<string, string> { [name] = "a=b" } }, TextWriter.Null, CancellationToken.None);
        await runner.RunAsync(new RowRequest { Name = "B", Command = "true", ResultsDirectory = Path.Combine(root, "b"),
            WorkingDirectory = root }, TextWriter.Null, CancellationToken.None);
        factory.Starts.Count.ShouldBe(2);
        factory.Starts[0].Environment[name].ShouldBe("a=b");
        factory.Starts[1].Environment.TryGetValue(name, out var siblingValue).ShouldBe(original is not null);
        siblingValue.ShouldBe(original);
        Environment.GetEnvironmentVariable(name).ShouldBe(original);

        const string flaky = "Antiphon.Tests.FlakyTests.flaky";
        var retryDriver = new FakeDriver();
        retryDriver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var first = request.Arguments.Contains("run.trx");
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), (flaky, first ? "Failed" : "Passed"));
            return Task.FromResult(new DriverResult(first ? 1 : 0, "", ""));
        });
        var retryEnvironment = new Dictionary<string, string> { [name] = "target-only" };
        await new RowRunner(retryDriver, new FakePlatform()).RunAsync(new RowRequest
        {
            Name = "retry", Project = "tests/Antiphon.Tests", OutputPath = "bin-e/", Filter = "/*/*/FlakyTests/*",
            ResultsDirectory = Path.Combine(root, "retry"), WorkingDirectory = root, NoBuild = true,
            Expect = ["FlakyTests"], MinExecuted = 1, KnownFlaky = [flaky], Environment = retryEnvironment,
        }, TextWriter.Null, CancellationToken.None);
        retryDriver.Count(CheckpointFixtures.IsRun).ShouldBe(2);
        retryDriver.Calls.Where(CheckpointFixtures.IsRun)
            .All(call => call.Environment![name] == "target-only").ShouldBeTrue();
        retryEnvironment[name].ShouldBe("target-only");
    }

    [Test]
    public async Task scheduler_passes_test_parallel_limit_only_to_serial_rows_process()
    {
        const string name = "TUNIT_MAX_PARALLEL_TESTS";
        var inherited = Environment.GetEnvironmentVariable(name);
        var manifest = new CheckpointManifest();
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 });
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-2", After = ["S1"], Command = "true", EstimatedMinutes = 1, Serial = true,
            Environment = new Dictionary<string, string> { [name] = "1" },
        });
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-3", After = ["S1"], Command = "true", EstimatedMinutes = 1 });
        var factory = new CapturingFactory();
        var root = CheckpointFixtures.TempDir();
        var result = await new RunScheduler(new ProcessDriver(factory), new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest, Rows = manifest.Checkpoints, RunDirectory = root, WorkingDirectory = root,
            State = new RunState(), Slots = new FixedSlotClient("off"), Width = 2,
        }, CancellationToken.None);
        result.ExitCode.ShouldBe(0);
        factory.Starts.Count.ShouldBe(3);
        foreach (var index in new[] { 0, 2 })
        {
            factory.Starts[index].Environment.TryGetValue(name, out var sibling).ShouldBe(inherited is not null);
            sibling.ShouldBe(inherited);
        }
        factory.Starts[1].Environment[name].ShouldBe("1");
        Environment.GetEnvironmentVariable(name).ShouldBe(inherited);
    }

    [Test]
    public void invalid_serial_is_refused_before_execution()
    {
        foreach (var value in new[] { "yes", "1", "n/a" })
        {
            var result = PlanTableImporter.ImportMarkdown(Table("| Serial", FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`", suffix: "| " + value)));
            result.ExitCode.ShouldBe(2);
            result.Error.ShouldContain("Serial");
        }
        PlanTableImporter.ImportMarkdown(Table("| Serial", FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`", suffix: "| TRUE"))).Manifest!.Checkpoints[0].Serial.ShouldBeTrue();
    }

    [Test]
    public void unknown_duplicate_and_malformed_columns_are_refused()
    {
        var baseRow = FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`");
        foreach (var header in new[] { "| Serail", "| Serial | Serial", "| Environment | Environment" })
        {
            var extra = header.Count(c => c == '|');
            var result = PlanTableImporter.ImportMarkdown(Table(header, baseRow[..^1] + string.Concat(Enumerable.Repeat(" false |", extra))));
            result.ExitCode.ShouldBe(2);
        }
        var shortRow = PlanTableImporter.ImportMarkdown(Table("| Serial", baseRow));
        shortRow.ExitCode.ShouldBe(2);
        shortRow.Error.ShouldContain("columns");
        var extraRow = PlanTableImporter.ImportMarkdown(Table("", baseRow[..^1] + " extra |"));
        extraRow.ExitCode.ShouldBe(2);
        var barePipe = PlanTableImporter.ImportMarkdown(Table("", baseRow.Replace("`/*/*/CP-1Tests/*`", "`/*/*/A|B/*`")));
        barePipe.ExitCode.ShouldBe(2);
        barePipe.Error.ShouldContain("escape");
    }

    [Test]
    public void invalid_environment_names_and_duplicates_are_refused()
    {
        foreach (var value in new[] { "1BAD=x", "BAD-NAME=x", "=x", "BAD", "A=x;A=y", "A=x;a=y", "A=bad\0value" })
        {
            var result = PlanTableImporter.ImportMarkdown(Table("| Environment",
                FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`", suffix: "| " + value)));
            result.ExitCode.ShouldBe(2, value);
            result.Error.ShouldContain("Environment");
        }
        var yaml = ManifestLoader.ToYaml(PlanTableImporter.ImportMarkdown(Table("| Environment",
            FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`", suffix: "| A=x"))).Manifest!);
        yaml = yaml.Replace("A: x", "1BAD: x");
        Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml(yaml, CheckpointFixtures.TempDir()));
        var duplicateYaml = yaml.Replace("1BAD: x", "A: x\n    a: y");
        Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml(duplicateYaml, CheckpointFixtures.TempDir()));
    }

    [Test]
    public void annotated_reuse_resolves_only_an_earlier_filter_build()
    {
        var first = FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`");
        var second = FilterRow("CP-2", "CP-1 (-NoBuild)");
        var imported = PlanTableImporter.ImportMarkdown(Table("", first, second));
        imported.ExitCode.ShouldBe(0, imported.Error);
        imported.Manifest!.Builds.Count.ShouldBe(1);
        imported.Manifest.Checkpoints[1].Build.ShouldBe("bin-e");
        foreach (var invalid in new[] { "CP-99", "CP-1 (anything-else)" })
            PlanTableImporter.ImportMarkdown(Table("", first, FilterRow("CP-2", invalid))).ExitCode.ShouldBe(2);
    }

    [Test]
    public void filter_shorthand_and_trailing_environment_prose_are_refused()
    {
        var first = FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`");
        var shorthand = FilterRow("CP-2", "CP-1").Replace("`/*/*/CP-2Tests/*`", "same as CP-1").Replace(" | 1 | 1 |", " | n/a | 1 |");
        var result = PlanTableImporter.ImportMarkdown(Table("", first, shorthand));
        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("Filter");
        var trailing = first.Replace("`/*/*/CP-1Tests/*`", "`/*/*/CP-1Tests/*` TUNIT_MAX_PARALLEL_TESTS=1");
        result = PlanTableImporter.ImportMarkdown(Table("", trailing));
        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("Environment");
    }

    [Test]
    public void cross_after_reuse_is_refused_without_a_relaxation_flag()
    {
        var first = FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`");
        foreach (var build in new[] { "CP-1", "CP-1 (-NoBuild)", "`tests/Antiphon.Tests -> bin-e/`" })
        {
            var result = PlanTableImporter.ImportMarkdown(Table("", first, FilterRow("CP-2", build, after: "S2")));
            result.ExitCode.ShouldBe(2, build);
            result.Error.ShouldContain("After");
        }
        var historical = Path.Combine(CheckpointFixtures.RepoRoot, "docs", "superpowers", "plans", "2026-09-25-card-0723-checkpoint-runner-plan.md");
        PlanTableImporter.ImportFile(historical).ExitCode.ShouldBe(2);
    }

    [Test]
    public void conflicting_projects_cannot_share_an_output_identity()
    {
        var first = FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-e/`");
        var conflict = FilterRow("CP-2", "`tests/Other.Tests -> bin-e/`");
        var result = PlanTableImporter.ImportMarkdown(Table("", first, conflict));
        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("bin-e");
        PlanTableImporter.ImportMarkdown(Table("", first, FilterRow("CP-2", "`tests/Antiphon.Tests -> bin-e/`"))).Manifest!.Builds.Count.ShouldBe(1);
    }

    [Test]
    public async Task run_plan_and_import_resolve_the_committed_table_identically()
    {
        var root = CheckpointFixtures.TempDir();
        var plan = Path.Combine(CheckpointFixtures.RepoRoot, "docs", "superpowers", "plans", "2026-09-26-checkpoint-tool-hardening-plan.md");
        var output = Path.Combine(root, "import.yaml");
        (await Antiphon.Checkpoints.Program.RunAsync(["import", "--plan", plan, "--repo-root", root, "--out", output])).ShouldBe(0);
        var imported = ManifestLoader.LoadFile(output, root);
        string? runDirectory = null;
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            Launch = _ => 123,
            Wait = path => { runDirectory = path; return Task.FromResult(0); },
        };
        foreach (var (slice, id, floor) in new[] { ("S1", "CP-1", 17), ("S2", "CP-2", 19), ("S3", "CP-3", 3) })
        {
            (await Antiphon.Checkpoints.Program.RunAsync(["run", "--plan", plan, "--repo-root", root, "--after", slice], runtime)).ShouldBe(0);
            var resolved = ManifestLoader.LoadFile(Path.Combine(runDirectory!, "manifest.resolved.yaml"), root);
            resolved.Checkpoints.Single(row => row.Id == id).MinExecuted.ShouldBe(floor);
            ManifestLoader.ToYaml(resolved).ShouldBe(ManifestLoader.ToYaml(imported));
            var request = System.Text.Json.JsonSerializer.Deserialize<RunRequest>(File.ReadAllText(Path.Combine(runDirectory!, "request.json")), CheckpointApp.Json)!;
            request.Rows.ShouldBe([id]);
        }
    }

    private sealed class CapturingFactory : IProcessHandleFactory
    {
        public List<System.Diagnostics.ProcessStartInfo> Starts { get; } = [];
        public IProcessHandle Create(System.Diagnostics.ProcessStartInfo startInfo)
        {
            Starts.Add(startInfo);
            return new ImmediateHandle();
        }
    }

    private sealed class ImmediateHandle : IProcessHandle
    {
        public bool Start() => true;
        public void BeginRead(Action<string?> stdout, Action<string?> stderr) { }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public bool HasExited => true;
        public int ExitCode => 0;
        public void Kill(bool entireProcessTree) { }
        public void Dispose() { }
    }
}
