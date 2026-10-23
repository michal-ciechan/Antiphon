using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointOrFilterTests : CheckpointTestBase
{
    [Test]
    [Arguments("(Alpha)|(Beta)", 1, true)]
    [Arguments("(Alpha*)|(Beta)", 2, false)]
    [Arguments("(Alpha)|(Beta*)", 1, true)]
    [Arguments("(Alpha*)|(Beta)|(Gamma*)", 2, false)]
    public void C0931_rejects_method_or_without_each_suffix(string methods, int operand, bool wrapped) =>
        Refused("/*/*/Sample/" + methods, "method", operand, wrapped);

    [Test]
    [Arguments("(Alpha)|(Beta)", 1)]
    [Arguments("(Alpha*)|(Beta)", 2)]
    public void C0931_rejects_class_or_without_each_suffix(string classes, int operand) =>
        Refused("/*/*/" + classes + "/*", "class", operand, true);

    [Test]
    [Arguments("/*/*/Alpha/One|/*/*/Beta/Two")]
    [Arguments("/*/*/Sample/Alpha|Beta")]
    public void C0931_rejects_path_or_shape(string filter)
    {
        var result = PlanTableImporter.ImportMarkdown(Table(filter));
        result.ExitCode.ShouldBe(2);
        result.Manifest.ShouldBeNull();
        result.Error.ShouldBe("refused: checkpoint-filter-or-shape: CP-1: use parenthesized alternatives within one class or method segment");
    }

    [Test]
    [Arguments("import")]
    [Arguments("start")]
    [Arguments("run")]
    public async Task C0931_plan_entrypoints_refuse_before_work(string verb)
    {
        var root = TempDir();
        var plan = Path.Combine(root, "plan.md");
        File.WriteAllText(plan, Table("/*/*/Sample/(Alpha)|(Beta)"));
        var work = new List<string>();
        var driver = new FakeDriver();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = TinyToolDirectory(),
            Driver = driver,
            Slots = new FixedSlotClient("unavailable"),
            CopyToolOutput = (_, _) => work.Add("copy"),
            Launch = _ => { work.Add("launch"); return Environment.ProcessId; },
            Wait = _ => { work.Add("wait"); return Task.FromResult(0); },
        };
        string[] args = verb == "import"
            ? [verb, "--plan", plan, "--repo-root", root, "--out", Path.Combine(root, "import.yaml")]
            : [verb, "--plan", plan, "--repo-root", root];
        (await Antiphon.Checkpoints.Program.MainAsync(args, runtimeFactory: _ => runtime)).ShouldBe(2);
        work.ShouldBeEmpty();
        driver.Calls.ShouldBeEmpty();
        File.Exists(Path.Combine(root, "import.yaml")).ShouldBeFalse();
        Directory.Exists(Path.Combine(root, ".antiphon", "checkpoints")).ShouldBeFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task C0931_zero_execution_has_named_failure(int variant)
    {
        var (result, driver) = await Row(variant == 1 ? 0 : 1,
            variant == 1 ? ["Sample"] : [], variant == 2 ? "skip" : "empty", hostExit: variant == 0 ? 8 : 0);
        AssertZero(result);
        result.Output.ShouldContain(variant == 1 ? "ROSTER MISS Sample" : "MIN EXECUTED");
        result.Trx!.Skipped.ShouldBe(variant == 2 ? 1 : 0);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
    }

    [Test]
    public async Task C0931_shared_build_report_preserves_zero_failure()
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "tests/Antiphon.Tests"));
        var manifest = PlanTableImporter.ImportMarkdown(Table("/*/*/Sample/alpha")).Manifest!;
        manifest.Checkpoints[0].Expect = [];
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-2", After = ["S1"], Build = "bin-example", Filter = "/*/*/Sample/beta",
            MinExecuted = 1, EstimatedMinutes = 1,
        });
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            if (request.Arguments.Contains("/*/*/Sample/alpha"))
                CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request));
            else
                CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Sample.beta", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var state = new RunState { RunId = "c0931", StartedAt = DateTimeOffset.UtcNow };
        var result = await new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest, Rows = manifest.Checkpoints, RunDirectory = root, WorkingDirectory = root,
            State = state, Slots = new FixedSlotClient("unavailable"), Width = 1,
            Commit = new string('a', 40), ExpectedSourceSha = new string('a', 40),
            TotalTimeout = TimeSpan.FromMinutes(1),
        }, CancellationToken.None);
        result.ExitCode.ShouldBe(3);
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(2);
        result.Rows.Single(row => row.Id == "CP-2").ExitCode.ShouldBe(0);
        AssertZero(result.Rows.Single(row => row.Id == "CP-1"));
        File.WriteAllText(Path.Combine(root, "manifest.resolved.yaml"), ManifestLoader.ToYaml(manifest));
        var model = CheckpointApp.BuildReport(root, root, new RunRequest(), manifest, state, result);
        var json = ReportWriter.JsonText(model);
        json.ShouldContain("zero-tests-executed");
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<ReportModel>(json, ReportWriter.Json)!;
        roundtrip.Rows.Single(row => row.Id == "CP-1").State.ShouldBe("zero-tests-executed");
        ReportWriter.Markdown(model).ShouldContain("reason=zero-tests-executed");
    }

    [Test]
    public async Task C0931_nobuild_zero_execution_does_not_rebuild()
    {
        var (result, driver) = await Row(1, [], "empty", noBuild: true);
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(0);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
        result.Line.ShouldContain("build=reused");
        AssertZero(result);
    }

    [Test]
    [Arguments("/*/*/Sample/alpha", "Sample")]
    [Arguments("/*/*/Sample/*", "Sample")]
    [Arguments("/*/*/Sample/(Alpha*)|(Beta*)", "Alpha,Beta")]
    [Arguments("/*/*/(Alpha*)|(Beta*)/*", "Alpha,Beta")]
    [Arguments("/*/*/(Alpha*)|(Beta*)/(One*)|(Two*)[(Category=Unit)]", "Alpha,Beta,One,Two")]
    [Arguments("/*/*/*/*[(Category=Unit)|(Category=Slow)]", "")]
    public void C0931_accepted_filters_are_byte_preserved(string filter, string roster)
    {
        var result = PlanTableImporter.ImportMarkdown(Table(filter));
        result.ExitCode.ShouldBe(0, result.Error);
        var manifest = result.Manifest.ShouldNotBeNull();
        manifest.Checkpoints.Single().Filter.ShouldBe(filter);
        manifest.Checkpoints.Single().Build.ShouldBe("bin-example");
        manifest.Builds.Single().OutputPath.ShouldBe("bin-example/");
        manifest.Checkpoints.Single().Expect.ShouldBe(roster.Split(',', StringSplitOptions.RemoveEmptyEntries));
        ManifestValidator.Validate(manifest, TempDir());
    }

    [Test]
    [Arguments("pass", 0, "green")]
    [Arguments("fail", 1, "red")]
    [Arguments("partial", 3, "roster-miss")]
    [Arguments("absent", 2, "no-trx")]
    [Arguments("malformed", 2, "malformed")]
    [Arguments("skip", 0, "green")]
    public async Task C0931_nonselection_outcomes_keep_their_codes(string kind, int exit, string state)
    {
        var (result, driver) = await Row(kind == "skip" ? 0 : 1,
            kind == "partial" ? ["Missing"] : ["Sample"], kind);
        result.ExitCode.ShouldBe(exit);
        result.State.ShouldBe(state);
        result.Line.ShouldNotContain("reason=zero-tests-executed");
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
        result.Reruns.ShouldBe(0);
    }

    [Test]
    public async Task C0931_absent_inputs_preserve_existing_admission()
    {
        PlanTableImporter.ImportMarkdown("").ExitCode.ShouldBe(2);
        PlanTableImporter.RosterTokens(null).ShouldBeEmpty();
        PlanTableImporter.RosterTokens("  ").ShouldBeEmpty();
        PlanTableImporter.ImportMarkdown(Table("/*/*/Sample/alpha", min: "n/a", expect: ">= 1 executed"))
            .Manifest!.Checkpoints.Single().MinExecuted.ShouldBe(1);
        PlanTableImporter.ImportMarkdown(Table("/*/*/Sample/alpha", min: ""))
            .Manifest!.Checkpoints.Single().MinExecuted.ShouldBeNull();
        var command = Table("echo alpha | cat").Replace("tests/Antiphon.Tests -> bin-example/", "n/a").Replace(" | 1 | 1 |", " | n/a | 1 |");
        PlanTableImporter.ImportMarkdown(command).Manifest!.Checkpoints.Single().Command.ShouldBe("echo alpha | cat");
        var (empty, _) = await Row(0, ["", "  "], "empty");
        empty.ExitCode.ShouldBe(0);
        empty.Line.ShouldNotContain("reason=zero-tests-executed");
    }

    private static void AssertZero(RowRunResult result)
    {
        result.ExitCode.ShouldBe(3);
        result.State.ShouldBe("zero-tests-executed");
        result.Line.ShouldContain("reason=zero-tests-executed");
        result.Line.ShouldContain("executed=0");
        result.Trx!.Executed.ShouldBe(0);
        result.Reruns.ShouldBe(0);
    }

    [Test]
    [Arguments("/*/*/Sample/(Alpha*)|(Beta)", "method", 2)]
    [Arguments("/*/*/(Alpha)|(Beta)/*", "class", 1)]
    public void C0931_manifest_validator_rejects_unsafe_or(string filter, string segment, int operand)
    {
        var manifest = PlanTableImporter.ImportMarkdown(Table("/*/*/Sample/alpha")).Manifest!;
        manifest.Checkpoints.Single().Filter = filter;
        var exception = Should.Throw<ManifestValidationException>(() => ManifestValidator.Validate(manifest, TempDir()));
        exception.ExitCode.ShouldBe(2);
        exception.Message.ShouldContain($"checkpoint-filter-or-wildcard: CP-1: {segment} operand {operand}");
    }

    private void Refused(string filter, string segment, int operand, bool wrapped)
    {
        var result = PlanTableImporter.ImportMarkdown(Table(filter, wrapped));
        result.ExitCode.ShouldBe(2);
        result.Manifest.ShouldBeNull();
        result.Error.ShouldBe($"refused: checkpoint-filter-or-wildcard: CP-1: {segment} operand {operand} requires a trailing '*'; use (NameA*)|(NameB*) or separate exact-method rows sharing one build");
    }

    private static string Table(string filter, bool wrapped = true, string min = "1", string expect = "one test")
    {
        var cell = filter.Replace("|", "\\|");
        if (wrapped) cell = "`" + cell + "`";
        return "### Checkpoints\n| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |\n"
            + "|---|---|---|---|---|---|---|---|---|\n"
            + $"| CP-1 | S1 | `tests/Antiphon.Tests -> bin-example/` | example | {cell} | V-1 | {expect} | {min} | 1 |\n";
    }

    private async Task<(RowRunResult, FakeDriver)> Row(int min, List<string> expect, string kind, bool noBuild = false, int hostExit = 0)
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "tests/Antiphon.Tests"));
        if (noBuild)
        {
            var args = BuildStep.PropertyArguments([], false).ToList();
            var layout = BuildStep.AppendSharedLayout(args, root, "tests/Antiphon.Tests", "bin-example/");
            SafeWrite.Require(SafeWrite.CreateFreshDirectory(layout.Owned, SafeWrite.Under(root)));
            SafeWrite.Require(CheckpointBuildBinding.Expected(root, "tests/Antiphon.Tests", "bin-example/", args, SourceObservation.Unknown("not_observed")).Write());
        }
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var path = CheckpointFixtures.TrxFile(request);
            if (kind == "malformed")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "not xml");
            }
            else if (kind != "absent")
            {
                if (kind == "empty") CheckpointFixtures.WriteResults(path);
                else CheckpointFixtures.WriteResults(path, ("Sample.alpha", kind == "skip" ? "NotExecuted" : kind == "fail" ? "Failed" : "Passed"));
            }
            return Task.FromResult(new DriverResult(hostExit, "", ""));
        });
        var result = await new RowRunner(driver, new FakePlatform()).RunAsync(new RowRequest
        {
            Name = "CP-1", Project = "tests/Antiphon.Tests", OutputPath = "bin-example/", Filter = "/*/*/Sample/alpha",
            WorkingDirectory = root, ResultsDirectory = Path.Combine(root, "row"), MinExecuted = min,
            Expect = expect, NoBuild = noBuild, Commit = new string('a', 40),
        }, TextWriter.Null, CancellationToken.None);
        return (result, driver);
    }
}
