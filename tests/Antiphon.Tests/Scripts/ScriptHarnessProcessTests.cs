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
        ScriptHarnessProcessFixture.WithInvocationAsync(fixture => TimeoutTreeAsync(fixture, "LiveRoot"));

    [Test]
    public Task Exited_root_with_stdout_holder_times_out_and_kills_descendant() =>
        ScriptHarnessProcessFixture.WithInvocationAsync(fixture => TimeoutTreeAsync(fixture, "ExitedStdout", "stdout"));

    [Test]
    public Task Exited_root_with_stderr_holder_times_out_and_kills_descendant() =>
        ScriptHarnessProcessFixture.WithInvocationAsync(fixture => TimeoutTreeAsync(fixture, "ExitedStderr", "stderr"));

    [Test]
    public Task Caller_cancellation_kills_tree_before_returning() => ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
    {
        using var cancel = new CancellationTokenSource();
        var run = fixture.Start("Cancellation", token: cancel.Token);
        await fixture.WaitReadyAsync(run);
        cancel.Cancel();
        var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
        error.ShouldBeOfType<OperationCanceledException>().CancellationToken.ShouldBe(cancel.Token);
        fixture.AssertClean();
    });

    [Test]
    public Task Passing_case_preserves_inventory_and_argument_boundaries() => ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
    {
        const string payload = "empty space \"quoted\" trailing\\ unicode-\u03a9";
        var validated = false;
        var result = await fixture.Start("Passing", payload, validate: receipt =>
        {
            // The actual argument receipt is read before harness-owned paths vanish.
            File.ReadAllText(Path.Combine(fixture.Request!.ResultsDirectory, "payload")).ShouldBe(payload);
            receipt.ExitCode.ShouldBe(0);
            var lines = receipt.Stdout.ReplaceLineEndings("\n").Split('\n');
            lines.Count(line => line.StartsWith("PASS ", StringComparison.Ordinal)).ShouldBe(1);
            lines.ShouldContain("PASS C806 C806 fixture passed");
            lines.ShouldContain("C487 HARNESS EXIT CODE: 0");
            lines.ShouldContain("C487: 1 passed, 0 failed, 1 rows");
            validated = true;
        });
        result.ExitCode.ShouldBe(0);
        validated.ShouldBeTrue();
        fixture.AssertClean();
    });

    [Test]
    public Task Nonzero_exit_preserves_output_and_cleans_results() => ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
    {
        var error = await ScriptHarnessProcessFixture.CaptureAsync(fixture.StartHarness("Nonzero"));
        error.ShouldNotBeNull();
        error.Message.ShouldContain("C806 stdout marker");
        error.Message.ShouldContain("C806 stderr marker");
        fixture.AssertClean();
    });

    [Test]
    public Task Bad_pass_inventory_still_fails() => ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
    {
        var error = await ScriptHarnessProcessFixture.CaptureAsync(fixture.StartHarness("BadInventory"));
        error.ShouldNotBeNull();
        error.Message.ShouldContain("named assertion inventory");
        fixture.AssertClean();
    });

    [Test]
    public Task Completed_root_with_silent_descendant_releases_owner() => ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
    {
        var run = fixture.Start("Silent");
        await fixture.WaitReadyAsync(run);
        var result = await run;
        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldContain("C487 HARNESS EXIT CODE: 0");
        fixture.AssertClean();
        if (fixture.Windows is { } windows)
        {
            windows.Hooks.StdoutEof.ShouldBeTrue();
            windows.Hooks.StderrEof.ShouldBeTrue();
        }
    });

    [Test]
    public Task Root_exit_racing_deadline_still_cleans_owner() => ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
    {
        var run = fixture.Start("Race");
        var tree = await fixture.WaitReadyAsync(run);
        if (fixture.Windows is { } windows) windows.ReleaseRace();
        else File.WriteAllText(Path.Combine(tree.ResultsDirectory, "release"), "go");
        var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
        (error is null || error is TimeoutException).ShouldBeTrue(error?.ToString());
        fixture.AssertClean();
    });

    [Test]
    public Task High_volume_on_both_streams_completes() => ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
    {
        await fixture.StartHarness("HighVolume");
        fixture.AssertClean();
    });

    [Test]
    public async Task Repeated_timeouts_do_not_leak_owners()
    {
        // One independent outer watchdog covers all three sequential invocations.
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        for (var index = 0; index < 3; index++)
            await ScriptHarnessProcessFixture.WithInvocationAsync(fixture => TimeoutTreeAsync(fixture, "LiveRoot"),
                watchdogToken: watchdog.Token);
    }

    private static async Task TimeoutTreeAsync(ScriptHarnessProcessFixture.Invocation fixture, string caseName, string? held = null)
    {
        var clock = Stopwatch.StartNew();
        var run = fixture.Start(caseName);
        var tree = await fixture.WaitReadyAsync(run);
        if (held is not null)
        {
            var deadline = Stopwatch.StartNew();
            while (tree.Root.Executing() && deadline.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
            tree.Root.Executing().ShouldBeFalse("The logical PowerShell root must exit before the held pipe times out.");
            tree.Child.Executing().ShouldBeTrue();
            tree.Grandchild.Executing().ShouldBeTrue();
            if (fixture.Windows is { } windows)
            {
                while (!(held == "stdout" ? windows.Hooks.StderrEof : windows.Hooks.StdoutEof) &&
                       deadline.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
                (held == "stdout" ? windows.Hooks.StderrEof : windows.Hooks.StdoutEof)
                    .ShouldBeTrue("The independently observed opposite stream must reach EOF before termination.");
                (held == "stdout" ? windows.Hooks.StdoutEof : windows.Hooks.StderrEof)
                    .ShouldBeFalse("A live descendant must still hold the selected stream.");
                tree.Child.Executing().ShouldBeTrue();
                tree.Grandchild.Executing().ShouldBeTrue();
            }
        }
        var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
        error.ShouldBeOfType<TimeoutException>();
        clock.Elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
        fixture.AssertClean();
    }
}
