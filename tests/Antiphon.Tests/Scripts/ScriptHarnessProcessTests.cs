using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessProcessTests
{
    [Test]
    public Task Live_root_timeout_kills_root_child_and_grandchild() =>
        TimeoutTreeAsync("LiveRoot");

    [Test]
    public Task Exited_root_with_stdout_holder_times_out_and_kills_descendant() =>
        TimeoutTreeAsync("ExitedStdout", requireRootExit: true);

    [Test]
    public Task Exited_root_with_stderr_holder_times_out_and_kills_descendant() =>
        TimeoutTreeAsync("ExitedStderr", requireRootExit: true);

    [Test]
    public async Task Caller_cancellation_kills_tree_before_returning()
    {
        ScriptProcessRequest? request = null;
        using var cancel = new CancellationTokenSource();
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "Cancellation",
            ScriptHarnessProcessFixture.ScriptPath,
            ScriptHarnessProcessFixture.Options(observe: value => request = value), cancel.Token);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        cancel.Cancel();
        var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
        (error is OperationCanceledException).ShouldBeTrue(error?.ToString());
        ((OperationCanceledException)error!).CancellationToken.ShouldBe(cancel.Token);
        tree.AssertDeadBeforeEmergencySweep();
    }

    [Test]
    public Task Passing_case_preserves_inventory_and_argument_boundaries()
    {
        const string payload = "empty space \"quoted\" trailing\\ unicode-\u03a9";
        return ScriptHarness.RunHarnessCaseAsync("fixture", "C806", "Passing", 1,
            ["C806 C806 fixture passed"], ScriptHarnessProcessFixture.Options(payload), CancellationToken.None);
    }

    [Test]
    public async Task Nonzero_exit_preserves_output_and_cleans_results()
    {
        var error = await ScriptHarnessProcessFixture.CaptureAsync(
            ScriptHarness.RunHarnessCaseAsync("fixture", "C806", "Nonzero", 1,
                ["C806 C806 fixture passed"], ScriptHarnessProcessFixture.Options(), CancellationToken.None));
        error.ShouldNotBeNull();
        error.Message.ShouldContain("C806 stdout marker");
        error.Message.ShouldContain("C806 stderr marker");
    }

    [Test]
    public async Task Bad_pass_inventory_still_fails()
    {
        var error = await ScriptHarnessProcessFixture.CaptureAsync(
            ScriptHarness.RunHarnessCaseAsync("fixture", "C806", "BadInventory", 1,
                ["C806 C806 fixture passed"], ScriptHarnessProcessFixture.Options(), CancellationToken.None));
        error.ShouldNotBeNull();
        error.Message.ShouldContain("named assertion inventory");
    }

    [Test]
    public async Task Completed_root_with_silent_descendant_releases_owner()
    {
        ScriptProcessRequest? request = null;
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "Silent",
            ScriptHarnessProcessFixture.ScriptPath,
            ScriptHarnessProcessFixture.Options(observe: value => request = value), CancellationToken.None);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(30));
        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldContain("C487 HARNESS EXIT CODE: 0");
        tree.AssertDeadBeforeEmergencySweep();
    }

    [Test]
    public async Task Root_exit_racing_deadline_still_cleans_owner()
    {
        ScriptProcessRequest? request = null;
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "Race",
            ScriptHarnessProcessFixture.ScriptPath,
            ScriptHarnessProcessFixture.Options(observe: value => request = value), CancellationToken.None);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        File.WriteAllText(Path.Combine(tree.ResultsDirectory, "release"), "go");
        var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
        (error is null || error is TimeoutException).ShouldBeTrue(error?.ToString());
        tree.AssertDeadBeforeEmergencySweep();
    }

    [Test]
    public Task High_volume_on_both_streams_completes() =>
        ScriptHarness.RunHarnessCaseAsync("fixture", "C806", "HighVolume", 1,
            ["C806 C806 fixture passed"], ScriptHarnessProcessFixture.Options(), CancellationToken.None);

    [Test]
    public async Task Repeated_timeouts_do_not_leak_owners()
    {
        for (var index = 0; index < 3; index++) await TimeoutTreeAsync("LiveRoot");
    }

    private static async Task TimeoutTreeAsync(string caseName, bool requireRootExit = false)
    {
        ScriptProcessRequest? request = null;
        var clock = Stopwatch.StartNew();
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", caseName,
            ScriptHarnessProcessFixture.ScriptPath,
            ScriptHarnessProcessFixture.Options(observe: value => request = value), CancellationToken.None);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        if (requireRootExit)
        {
            var deadline = Stopwatch.StartNew();
            while (tree.Root.Executing() && deadline.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
            tree.Root.Executing().ShouldBeFalse("The logical PowerShell root must exit before the held pipe times out.");
            (tree.Child.Executing() || tree.Grandchild.Executing()).ShouldBeTrue();
        }
        var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
        error.ShouldBeOfType<TimeoutException>();
        clock.Elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
        tree.AssertDeadBeforeEmergencySweep();
    }
}
