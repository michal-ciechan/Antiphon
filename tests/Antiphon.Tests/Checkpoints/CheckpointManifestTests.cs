using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointManifestTests
{
    [Test]
    public void parses_yaml_and_defaults()
    {
        var root = CheckpointFixtures.TempDir();
        var manifest = ManifestLoader.LoadYaml(CheckpointFixtures.SampleYaml(), root);
        manifest.SchemaVersion.ShouldBe(1);
        manifest.ResultsRoot.ShouldBe(".antiphon/checkpoints");
        manifest.Build.Slots.ShouldBe("auto");
        manifest.Build.SlotWaitMinutes.ShouldBe(45);
        manifest.Build.MaxCpuCount.ShouldBe(0);
        manifest.Timeouts.RowMinutes.ShouldBe(15);
        manifest.Timeouts.TotalMinutes.ShouldBe(90);
        manifest.Rerun.KnownFlaky.ShouldBeEmpty();
        manifest.Parallel.MaxRows.ShouldBeNull();
        manifest.EffectiveMaxRows(isWindows: false).ShouldBe(2);
        manifest.EffectiveMaxRows(isWindows: true).ShouldBe(1);
        manifest.Checkpoints[0].Filter.ShouldBe("/*/*/ExampleSurfaceTests/*");
    }

    [Test]
    public void rejects_backslash_or_trailing_space_output_path()
    {
        var root = CheckpointFixtures.TempDir();
        foreach (var path in new[] { @"bin-ex\", "bin-ex/ ", "bin-ex", " bin-ex/" })
        {
            var manifest = ManifestLoader.LoadYaml(CheckpointFixtures.SampleYaml(), root, validate: false);
            manifest.Builds[0].OutputPath = path;
            var ex = Should.Throw<ManifestValidationException>(() => ManifestValidator.Validate(manifest, root));
            ex.ExitCode.ShouldBe(2);
            ex.Message.ShouldContain("outputPath");
        }
    }

    [Test]
    public void rejects_duplicate_or_unknown_build_ids()
    {
        var root = CheckpointFixtures.TempDir();
        var duplicate = ManifestLoader.LoadYaml(CheckpointFixtures.SampleYaml(), root, validate: false);
        duplicate.Builds.Add(new BuildSpec { Id = "bin-ex", Project = "tests/Antiphon.Tests", OutputPath = "bin-ex/" });
        var duplicateError = Should.Throw<ManifestValidationException>(() => ManifestValidator.Validate(duplicate, root));
        duplicateError.ExitCode.ShouldBe(2);
        duplicateError.Message.ShouldContain("id");

        var unknown = ManifestLoader.LoadYaml(CheckpointFixtures.SampleYaml(), root, validate: false);
        unknown.Checkpoints[0].Build = "bin-missing";
        var unknownError = Should.Throw<ManifestValidationException>(() => ManifestValidator.Validate(unknown, root));
        unknownError.ExitCode.ShouldBe(2);
        unknownError.Message.ShouldContain("build");
    }

    [Test]
    public void rejects_reuse_across_different_after()
    {
        var root = CheckpointFixtures.TempDir();
        var manifest = ManifestLoader.LoadYaml(CheckpointFixtures.SampleYaml(), root, validate: false);
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-2",
            After = ["S2"],
            Build = "bin-ex",
            Filter = "/*/*/OtherTests/*",
        });
        var ex = Should.Throw<ManifestValidationException>(() => ManifestValidator.Validate(manifest, root));
        ex.ExitCode.ShouldBe(2);
        ex.Field.ShouldBe("after");
    }

    [Test]
    public void after_selector_parses_ranges_and_all()
    {
        AfterSelector.Expand("S1-S3").ShouldBe(["S1", "S2", "S3"]);
        AfterSelector.Expand("S1,S3").ShouldBe(["S1", "S3"]);
        AfterSelector.Expand("all").ShouldBe(["all"]);
        AfterSelector.Expand("S2").ShouldBe(["S2"]);
        AfterSelector.IsSelected(["S2"], ["S1", "S2", "S3"]).ShouldBeTrue();
        AfterSelector.IsSelected(["S4"], ["S1", "S2", "S3"]).ShouldBeFalse();
        AfterSelector.IsSelected(["all"], ["S1"]).ShouldBeTrue();
        AfterSelector.IsSelected(["S1"], ["all"]).ShouldBeTrue();
    }

    [Test]
    public void results_root_must_be_under_worktree()
    {
        var root = CheckpointFixtures.TempDir();
        var manifest = ManifestLoader.LoadYaml(CheckpointFixtures.SampleYaml(), root, validate: false);
        manifest.ResultsRoot = Path.Combine(Path.GetTempPath(), "c723-outside-" + Guid.NewGuid().ToString("N"));
        var ex = Should.Throw<ManifestValidationException>(() => ManifestValidator.Validate(manifest, root));
        ex.ExitCode.ShouldBe(2);
        ex.Message.ShouldContain("resultsRoot");
    }
}
