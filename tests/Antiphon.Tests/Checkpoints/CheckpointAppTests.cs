using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointAppTests
{
    [Test]
    public void create_run_uses_the_manifest_results_root()
    {
        var repo = CheckpointFixtures.TempDir();
        var manifest = new CheckpointManifest { ResultsRoot = ".antiphon/custom-root" };
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-1",
            After = ["S1"],
            Command = "echo hi",
        });
        var directory = CheckpointApp.CreateRun(manifest, new RunRequest(), repo);
        directory.ShouldContain("custom-root");
        File.Exists(Path.Combine(directory, "manifest.resolved.yaml")).ShouldBeTrue();
        File.Exists(Path.Combine(directory, "request.json")).ShouldBeTrue();
        Directory.Exists(Path.Combine(repo, ".antiphon", "checkpoints")).ShouldBeFalse();
    }

    [Test]
    public void phase_done_is_published_before_cleanup_and_cleanup_failures_are_kept()
    {
        var order = new List<string>();
        var state = new RunState { Phase = "running" };
        CheckpointApp.Finish(
            state,
            0,
            () => order.Add("publish:" + state.Phase),
            () =>
            {
                order.Add("cleanup");
                throw new UnauthorizedAccessException("tool");
            },
            message => order.Add("note:" + message));
        state.Phase.ShouldBe("done");
        state.ExitCode.ShouldBe(0);
        order[0].ShouldBe("publish:done");
        order.ShouldContain("cleanup");
        order.ShouldContain(item => item.Contains("UnauthorizedAccessException", StringComparison.Ordinal));
    }
}
