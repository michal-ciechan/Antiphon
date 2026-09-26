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
        var path = Path.Combine(CheckpointFixtures.RepoRoot, "docs", "superpowers", "plans", "2026-09-25-card-0723-checkpoint-runner-plan.md");
        var imported = PlanTableImporter.ImportFile(path);
        imported.ExitCode.ShouldBe(0, imported.Error);
        var manifest = imported.Manifest!;
        manifest.Checkpoints.Count.ShouldBe(8);
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
        roundTrip.Checkpoints.Count.ShouldBe(8);
        roundTrip.Checkpoints.Single(row => row.Id == "CP-6").Filter.ShouldBe("/*/*/*/*[Category=Unit]");
    }
}
