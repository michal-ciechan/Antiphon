using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointImportTests : CheckpointTestBase
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
    public void warns_when_estimate_exceeds_advisory_threshold()
    {
        foreach (var (estimate, timeout, expectedWarnings) in new (int, int, string[])[]
        {
            (15, 45, []),
            (16, 48, ["CP-1: derived row timeout (48 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)"]),
            (20, 60, ["CP-1: derived row timeout (60 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)"]),
        })
        {
            var markdown = $"""
                ### Checkpoints

                | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
                |---|---|---|---|---|---|---|---:|---:|
                | CP-1 | S1 | `tests/Antiphon.Tests -> bin-ex/` | slow | `/*/*/SlowTests/*` | V-1 | all listed, 0 failed | 1 | {estimate} |
                """;
            var imported = PlanTableImporter.ImportMarkdown(markdown);
            imported.ExitCode.ShouldBe(0, imported.Error);
            imported.Manifest.ShouldNotBeNull().Checkpoints.Single().TimeoutMinutes.ShouldBe(timeout);
            imported.Warnings.ShouldBe(expectedWarnings);
        }
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
        var root = TempDir();
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
        var reloaded = ManifestLoader.LoadYaml(ManifestLoader.ToYaml(manifest), TempDir());
        reloaded.Checkpoints[0].Serial.ShouldBeTrue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, _) => { entered.TrySetResult(); await release.Task; return new DriverResult(0, "", ""); });
        var run = new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest, Rows = manifest.Checkpoints, RunDirectory = TempDir(),
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
        var reload = ManifestLoader.LoadYaml(ManifestLoader.ToYaml(imported.Manifest), TempDir());
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
        var root = TempDir();
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
        // CARD-1267: a no-build row needs the build-source receipt of the output it reuses, or it
        // stops as a mismatch before the rerun decision.
        Directory.CreateDirectory(Path.Combine(root, "tests/Antiphon.Tests"));
        var reused = BuildStep.PropertyArguments([], isWindows: false).ToList();
        var layout = BuildStep.AppendSharedLayout(reused, root, "tests/Antiphon.Tests", "bin-e/");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(layout.Owned, SafeWrite.Under(root)));
        SafeWrite.Require(CheckpointBuildBinding.Expected(root, "tests/Antiphon.Tests", "bin-e/", reused,
            SourceObservation.Unknown("not_observed")).Write());
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
        const string name = "C760_SERIAL_ROW_VALUE";
        var manifest = new CheckpointManifest();
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 });
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-2", After = ["S1"], Command = "true", EstimatedMinutes = 1, Serial = true,
            Environment = new Dictionary<string, string> { [name] = "7" },
        });
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-3", After = ["S1"], Command = "true", EstimatedMinutes = 1 });
        var factory = new CapturingFactory();
        var root = TempDir();
        var result = await new RunScheduler(new ProcessDriver(factory), new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest, Rows = manifest.Checkpoints, RunDirectory = root, WorkingDirectory = root,
            State = new RunState(), Slots = new FixedSlotClient("off"), Width = 2,
        }, CancellationToken.None);
        result.ExitCode.ShouldBe(0);
        factory.Starts.Count.ShouldBe(3);
        foreach (var index in new[] { 0, 2 })
            factory.Starts[index].Environment.ContainsKey(name).ShouldBeFalse();
        factory.Starts[1].Environment[name].ShouldBe("7");
        Environment.GetEnvironmentVariable(name).ShouldBeNull();
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
        Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml(yaml, TempDir()));
        var duplicateYaml = yaml.Replace("1BAD: x", "A: x\n    a: y");
        Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml(duplicateYaml, TempDir()));
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
        var root = TempDir();
        var plan = Path.Combine(CheckpointFixtures.RepoRoot, "docs", "superpowers", "plans", "2026-09-26-checkpoint-tool-hardening-plan.md");
        var output = Path.Combine(root, "import.yaml");
        (await Antiphon.Checkpoints.Program.RunAsync(["import", "--plan", plan, "--repo-root", root, "--out", output])).ShouldBe(0);
        var imported = ManifestLoader.LoadFile(output, root);
        string? runDirectory = null;
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = TinyToolDirectory(),
            Launch = _ => Environment.ProcessId,
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

    [Test]
    public void C1098_card_1087_wrapper_census_row_is_split_under_the_same_cap()
    {
        var path = Path.Combine(
            CheckpointFixtures.RepoRoot,
            "docs", "superpowers", "plans", "2026-10-06-card-1087-recycle-census-plan.md");
        var text = File.ReadAllText(path);
        var imported = PlanTableImporter.ImportFile(path);
        imported.ExitCode.ShouldBe(0, imported.Error);
        foreach (var warning in imported.Warnings)
            warning.StartsWith("CP-3", StringComparison.Ordinal).ShouldBeFalse(warning);

        var manifest = imported.Manifest.ShouldNotBeNull();
        manifest.Checkpoints.Count.ShouldBe(9);
        var census = manifest.Checkpoints.Single(row => row.Id == "CP-3");
        var rest = manifest.Checkpoints.Single(row => row.Id == "CP-3b");
        census.Build.ShouldBe("bin-c1087-b");
        rest.Build.ShouldBe(census.Build);
        string.Join(",", census.After).ShouldBe("all");
        string.Join(",", rest.After).ShouldBe("all");
        census.Serial.ShouldBeTrue();
        rest.Serial.ShouldBeTrue();
        census.Group.ShouldBe("wrapper-census");
        rest.Group.ShouldBe("wrapper-census-rest");
        census.MinExecuted.ShouldBe(4);
        rest.MinExecuted.ShouldBe(16);
        (census.EstimatedMinutes + rest.EstimatedMinutes).ShouldBe(14);
        census.EstimatedMinutes!.Value.ShouldBeLessThanOrEqualTo(15);
        rest.EstimatedMinutes!.Value.ShouldBeLessThanOrEqualTo(15);
        manifest.Checkpoints.Sum(row => row.EstimatedMinutes ?? 0).ShouldBe(72);
        census.ExpectText.ShouldContain("0 failed/skipped");
        rest.ExpectText.ShouldContain("0 failed/skipped");
        manifest.Checkpoints.Single(row => row.Id == "CP-4").Build.ShouldBe(census.Build);
        manifest.Checkpoints.Any(row => row.Filter == "/*/*/RollingVolumeRecycleScriptTests/*").ShouldBeFalse();

        var censusMethods = FilterMethods(census.Filter);
        var restMethods = FilterMethods(rest.Filter);
        censusMethods.Length.ShouldBe(4);
        restMethods.Length.ShouldBe(16);
        censusMethods.ShouldAllBe(name => name.StartsWith("C1087_", StringComparison.Ordinal));
        restMethods.ShouldAllBe(name => name.StartsWith("C1008_", StringComparison.Ordinal));
        var selected = censusMethods.Concat(restMethods).Order(StringComparer.Ordinal).ToArray();
        var declared = DeclaredWrapperCensusMethods();
        declared.Length.ShouldBe(20);
        selected.ShouldBe(declared);
        censusMethods.Intersect(restMethods, StringComparer.Ordinal).ShouldBeEmpty();

        const string rows = "--rows CP-3,CP-3b,CP-4,CP-5,CP-5b,CP-6,CP-7";
        text.Split(rows).Length.ShouldBe(3);
        text.Contains("--rows CP-3,CP-4,CP-5,CP-5b,CP-6,CP-7", StringComparison.Ordinal).ShouldBeFalse();
        text.ShouldContain("--row-timeout 20m --total-timeout 70m");
    }

    [Test]
    public void C1493_DuplicateBuild_second_equivalent_build_warns_once()
    {
        var imported = PlanTableImporter.ImportMarkdown(Table("",
            FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-a/`"),
            FilterRow("CP-2", "`tests/Antiphon.Tests -> bin-b/`"),
            FilterRow("CP-3", "`tests/Antiphon.Tests -> bin-b/`"),
            FilterRow("CP-4", "CP-2")));

        imported.ExitCode.ShouldBe(0, imported.Error);
        imported.Warnings.ShouldBe(["CP-2: Build 'bin-b' duplicates 'bin-a' from CP-1 (same project 'tests/Antiphon.Tests', After 'S1', Repeat 1); write 'CP-1' in the Build cell to reuse it and save one build"]);
        var manifest = imported.Manifest.ShouldNotBeNull();
        manifest.Builds.Select(build => build.Id).ShouldBe(["bin-a", "bin-b"]);
        manifest.Checkpoints.Select(row => row.Build).ShouldBe(["bin-a", "bin-b", "bin-b", "bin-b"]);
    }

    [Test]
    public void C1493_DuplicateBuild_project_spellings_normalise()
    {
        var imported = PlanTableImporter.ImportMarkdown(Table("| Repeat",
            FilterRow("CP-1", @"`tests\Antiphon.Tests\Antiphon.Tests.csproj -> bin-a/`", "S1", "| 3"),
            FilterRow("CP-2", "`./tests/Antiphon.Tests/ -> bin-b/`", "S1", "| 3")));

        imported.ExitCode.ShouldBe(0, imported.Error);
        imported.Warnings.ShouldBe(["CP-2: Build 'bin-b' duplicates 'bin-a' from CP-1 (same project 'tests/Antiphon.Tests', After 'S1', Repeat 3); write 'CP-1' in the Build cell to reuse it and save one build"]);
    }

    [Test]
    public void C1493_distinct_after_repeat_or_project_do_not_warn()
    {
        var imported = PlanTableImporter.ImportMarkdown(Table("| Repeat",
            FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-a/`", "S1", "|"),
            FilterRow("CP-2", "`tests/Antiphon.Tests -> bin-b/`", "S2", "|"),
            FilterRow("CP-3", "`tests/Antiphon.Tests -> bin-c/`", "S1", "| 2"),
            FilterRow("CP-4", "`tests/Antiphon.SessionRunner.Tests -> bin-d/`", "S1", "|"),
            FilterRow("CP-5", "`tests/Antiphon.Tests/Other.csproj -> bin-e/`", "S1", "|"),
            FilterRow("CP-6", "CP-1", "S1", "|"),
            FilterRow("CP-7", "`tests/Antiphon.Tests -> bin-a/`", "S1", "|")));

        imported.ExitCode.ShouldBe(0, imported.Error);
        imported.Warnings.ShouldBeEmpty();
        imported.Manifest.ShouldNotBeNull().Builds.Count.ShouldBe(5);
    }

    [Test]
    public async Task C1493_warning_keeps_import_exit_and_output()
    {
        var root = TempDir();
        var plan = Path.Combine(root, "plan.md");
        var output = Path.Combine(root, "import.yaml");
        File.WriteAllText(plan, Table("",
            FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-a/`"),
            FilterRow("CP-2", "`tests/Antiphon.Tests -> bin-b/`")));
        (await Antiphon.Checkpoints.Program.RunAsync(["import", "--plan", plan, "--repo-root", root, "--out", output])).ShouldBe(0);
        var manifest = ManifestLoader.LoadFile(output, root);
        manifest.Builds.Select(build => build.Id).ShouldBe(["bin-a", "bin-b"]);
        manifest.Checkpoints.Select(row => row.Build).ShouldBe(["bin-a", "bin-b"]);

        var refused = PlanTableImporter.ImportMarkdown(Table("",
            FilterRow("CP-1", "`tests/Antiphon.Tests -> bin-a/`"),
            FilterRow("CP-2", "`tests/Antiphon.Tests -> bin-b/`"),
            FilterRow("CP-3", "CP-9")));
        refused.ExitCode.ShouldBe(2);
        refused.Error.ShouldBe("CP-3: reuses CP-9 which is not an earlier row");
        refused.Manifest.ShouldBeNull();
    }

    [Test]
    public void C1493_DuplicateBuild_recommendation_imports_for_non_numeric_row_id()
    {
        var rows = new[]
        {
            FilterRow("CP-1-first", "`tests/Antiphon.Tests -> bin-a/`"),
            FilterRow("CP-2", "`tests/Antiphon.Tests -> bin-b/`"),
            FilterRow("CP-3", "`tests/Antiphon.Tests -> bin-c/`", "S2"),
            FilterRow("CP-4", "`tests/Antiphon.Tests -> bin-d/`", "S2"),
        };
        var imported = PlanTableImporter.ImportMarkdown(Table("", rows));

        imported.ExitCode.ShouldBe(0, imported.Error);
        imported.Warnings.ShouldBe([
            "CP-2: Build 'bin-b' duplicates 'bin-a' from CP-1-first (same project 'tests/Antiphon.Tests', After 'S1', Repeat 1); write 'tests/Antiphon.Tests -> bin-a/' in the Build cell to reuse it and save one build",
            "CP-4: Build 'bin-d' duplicates 'bin-c' from CP-3 (same project 'tests/Antiphon.Tests', After 'S2', Repeat 1); write 'CP-3' in the Build cell to reuse it and save one build",
        ]);

        // Apply each recommendation to its row and re-import the amended plan text.
        var amended = rows.ToArray();
        foreach (var warning in imported.Warnings)
        {
            var rowId = warning[..warning.IndexOf(':')];
            var cell = System.Text.RegularExpressions.Regex.Match(warning, @"write '(?<cell>[^']+)' in the Build cell").Groups["cell"].Value;
            cell.ShouldNotBeEmpty();
            var index = Array.FindIndex(amended, row => row.StartsWith($"| {rowId} |", StringComparison.Ordinal));
            amended[index] = FilterRow(rowId, cell, rowId == "CP-4" ? "S2" : "S1");
        }
        var reimported = PlanTableImporter.ImportMarkdown(Table("", amended));

        reimported.ExitCode.ShouldBe(0, reimported.Error);
        reimported.Warnings.ShouldBeEmpty();
        var manifest = reimported.Manifest.ShouldNotBeNull();
        manifest.Builds.Select(build => build.Id).ShouldBe(["bin-a", "bin-c"]);
        manifest.Checkpoints.Select(row => row.Build).ShouldBe(["bin-a", "bin-a", "bin-c", "bin-c"]);
    }

    private static string[] FilterMethods(string? filter)
    {
        filter.ShouldNotBeNull();
        return filter.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var open = part.LastIndexOf('(');
                var star = part.IndexOf('*', open + 1);
                (open >= 0 && star > open).ShouldBeTrue(part);
                return part[(open + 1)..star];
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] DeclaredWrapperCensusMethods()
    {
        var source = File.ReadAllText(Path.Combine(
            CheckpointFixtures.RepoRoot,
            "tests", "Antiphon.Tests", "Scripts", "RollingVolumeRecycleScriptTests.cs"));
        return System.Text.RegularExpressions.Regex.Matches(source, @"public (?:async Task|void) ((?:C1087|C1008)_\w+)\(")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
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
