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

    [Test]
    public void create_run_sweeps_a_finished_sibling_tool_copy_and_leaves_a_live_one()
    {
        var repo = CheckpointFixtures.TempDir();
        var root = Path.Combine(repo, ".antiphon", "checkpoints");
        var finished = Path.Combine(root, "old-run");
        var live = Path.Combine(root, "live-run");
        Directory.CreateDirectory(Path.Combine(finished, "tool"));
        Directory.CreateDirectory(Path.Combine(live, "tool"));
        File.WriteAllText(Path.Combine(finished, "tool", "Antiphon.Checkpoints.dll"), "old");
        File.WriteAllText(Path.Combine(live, "tool", "Antiphon.Checkpoints.dll"), "live");
        var store = new RunStateStore();
        store.Write(Path.Combine(finished, "state.json"), new RunState
        {
            RunId = "old-run",
            Phase = "done",
            ExecutorPid = 2_100_000_001,
            StartedAt = DateTimeOffset.UtcNow,
        });
        store.Write(Path.Combine(live, "state.json"), new RunState
        {
            RunId = "live-run",
            Phase = "running",
            ExecutorPid = Environment.ProcessId,
            StartedAt = DateTimeOffset.UtcNow,
        });
        var manifest = new CheckpointManifest { ResultsRoot = ".antiphon/checkpoints" };
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "echo hi" });
        CheckpointApp.CreateRun(manifest, new RunRequest(), repo);
        Directory.Exists(Path.Combine(finished, "tool")).ShouldBeFalse();
        Directory.Exists(Path.Combine(live, "tool")).ShouldBeTrue();
    }
}
