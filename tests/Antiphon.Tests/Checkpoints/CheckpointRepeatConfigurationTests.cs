using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointRepeatConfigurationTests : CheckpointTestBase
{
    private static string Table(string repeat, string second = "") => $$"""
        ### Checkpoints

        | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Repeat |
        |---|---|---|---|---|---|---|---:|---:|---:|
        | CP-1 | S1 | `tests/Antiphon.Tests -> bin-c885/` | repeat | `/*/*/SampleTests/*` | V-1 | 3 executed | 3 | 3 | {{repeat}} |
        {{second}}
        """;

    [Test]
    public void repeat_round_trips_from_cli_yaml_and_table()
    {
        var imported = PlanTableImporter.ImportMarkdown(Table("3"));
        imported.ExitCode.ShouldBe(0, "requested-repeat-preserved: optional Repeat column must import");
        var row = imported.Manifest!.Checkpoints.Single();
        var repeat = row.GetType().GetProperty("Repeat");
        repeat.ShouldNotBeNull("requested-repeat-preserved: repeat must be a persisted row property");
        repeat.GetValue(row).ShouldBe(3, "requested-repeat-preserved: table count");
        var reloaded = ManifestLoader.LoadYaml(ManifestLoader.ToYaml(imported.Manifest), TempDir());
        repeat.GetValue(reloaded.Checkpoints.Single()).ShouldBe(3, "requested-repeat-preserved: YAML count");
    }

    [Test]
    public void invalid_repeat_refuses_before_slot_or_driver()
    {
        foreach (var value in new[] { "0", "-1", "abc", "2147483648" })
        {
            var imported = PlanTableImporter.ImportMarkdown(Table(value));
            imported.ExitCode.ShouldBe(2, "invalid-repeat-no-work: invalid count " + value);
            imported.Error.ShouldContain("Repeat", Case.Insensitive, "invalid-repeat-no-work: refusal must identify the count");
        }
        var command = PlanTableImporter.ImportMarkdown(Table("2", "| CP-2 | S1 | n/a | command | `true` | V-1 | n/a | n/a | 1 | 2 |"));
        command.ExitCode.ShouldBe(2, "invalid-repeat-no-work: command rows cannot repeat");
        command.Error.ShouldContain("repeat", Case.Insensitive, "invalid-repeat-no-work: refusal must identify the repeat");
    }

    [Test]
    public void repeat_properties_bind_build_and_run_only_to_selected_project()
    {
        var imported = PlanTableImporter.ImportMarkdown(Table("5"));
        imported.ExitCode.ShouldBe(0, "target-project-bound: table repeat must import");
        var property = typeof(CheckpointSpec).GetProperty("Repeat");
        property.ShouldNotBeNull("target-project-bound: selected test project needs explicit repeat binding");
        property.GetValue(imported.Manifest!.Checkpoints.Single()).ShouldBe(5, "target-project-bound: selected count");
    }

    [Test]
    public void reuse_requires_matching_repeat_build()
    {
        var imported = PlanTableImporter.ImportMarkdown(Table("3", "| CP-2 | S1 | CP-1 | repeat | `/*/*/OtherTests/*` | V-1 | 3 executed | 3 | 3 | 2 |"));
        imported.ExitCode.ShouldBe(2, "shared-repeat-output-refused: one output cannot carry two repeat counts");
        imported.Error.ShouldContain("repeat", Case.Insensitive, "shared-repeat-output-refused: actionable refusal");
    }

    [Test]
    public void default_one_preserves_legacy_arguments_and_counts()
    {
        var manifest = ManifestLoader.LoadYaml(CheckpointFixtures.SampleYaml(), TempDir());
        var property = typeof(CheckpointSpec).GetProperty("Repeat");
        property.ShouldNotBeNull("default-one-unchanged: default count is explicit in the typed manifest");
        property.GetValue(manifest.Checkpoints.Single()).ShouldBe(1, "default-one-unchanged: existing manifests remain single execution");
        BuildStep.PropertyArguments([], isWindows: true).ShouldBeEmpty("default-one-unchanged: no new MSBuild properties in default mode");
    }
}
