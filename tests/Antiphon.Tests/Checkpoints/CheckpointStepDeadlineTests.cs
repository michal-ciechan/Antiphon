using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointStepDeadlineTests : CheckpointTestBase
{
    // rc run 97150a0b g109: CheckpointSourceExecutionTests.validators_agree_on_xml_character_references ran
    // ~326 validator calls in one pwsh under one fixed 30-s deadline and was cancelled at 36.8 s on a
    // saturated Windows host while still progressing. A child that starts slowly and keeps finishing steps
    // must not expire; one that stops must, naming the phase.
    [Test]
    public void step_deadline_keeps_a_slow_starting_child_that_keeps_progressing()
    {
        var time = new FakeTimeProvider();
        var deadline = new CheckpointStepDeadline(40, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), time);
        time.Advance(TimeSpan.FromSeconds(25)); // delayed start: process start and script load, no step yet
        deadline.Observe(0);
        deadline.Expired.ShouldBeFalse("startup within its budget");
        for (var step = 1; step <= 40; step++)
        {
            time.Advance(TimeSpan.FromSeconds(2));
            deadline.Observe(step);
            deadline.Expired.ShouldBeFalse("step " + step + " finished 2 s after the previous one, " + (25 + 2 * step) + " s in");
        }
    }

    [Test]
    public void step_deadline_expires_a_stalled_step_and_names_it()
    {
        var time = new FakeTimeProvider();
        var steps = new[] { "a", "b", "c", "stuck-case", "e" };
        var deadline = new CheckpointStepDeadline(steps.Length, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), time);
        time.Advance(TimeSpan.FromSeconds(1));
        deadline.Observe(3);
        time.Advance(TimeSpan.FromSeconds(10));
        deadline.Observe(3);
        deadline.Expired.ShouldBeFalse("exactly at the per-step budget");
        time.Advance(TimeSpan.FromMilliseconds(1));
        deadline.Expired.ShouldBeTrue("no step finished for longer than the per-step budget");
        var text = deadline.Describe(steps);
        text.ShouldContain("after 3/5 steps", Case.Sensitive);
        text.ShouldContain("stuck-case", Case.Sensitive);
    }

    [Test]
    public void step_deadline_expires_a_silent_startup_as_startup()
    {
        var time = new FakeTimeProvider();
        var deadline = new CheckpointStepDeadline(2, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), time);
        time.Advance(TimeSpan.FromSeconds(29));
        deadline.Observe(0);
        deadline.Expired.ShouldBeFalse("a step budget never applies before the first step");
        time.Advance(TimeSpan.FromSeconds(2));
        deadline.Expired.ShouldBeTrue();
        deadline.Describe(["only", "two"]).ShouldContain("before the first step finished", Case.Sensitive);
    }

    // Real child, any platform: a pwsh that never finishes a step is cancelled at the startup budget and
    // the failure says so, instead of a bare OperationCanceledException from the runner.
    [Test]
    [Timeout(60_000)]
    public async Task step_child_that_never_finishes_a_step_times_out_naming_startup(CancellationToken cancellationToken)
    {
        _ = cancellationToken.CanBeCanceled;
        var root = TempDir();
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root };
        foreach (var token in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 600" })
            start.ArgumentList.Add(token);
        var clock = Stopwatch.StartNew();
        var error = await Thrown(CheckpointFixtureProcess.RunStepsAsync(start, Path.Combine(root, "progress"), ["first", "second"],
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)));
        error.ShouldBeOfType<TimeoutException>(error?.ToString());
        error!.Message.ShouldContain("before the first step finished", Case.Sensitive);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), "cancelled near the 3-s startup budget, not after the child's 600 s");
    }

    private static async Task<Exception?> Thrown(Task call)
    {
        try { await call; return null; }
        catch (Exception ex) { return ex; }
    }
}
