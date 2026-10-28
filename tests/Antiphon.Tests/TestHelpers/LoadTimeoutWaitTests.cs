using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class LoadTimeoutWaitTests
{
    [Test]
    [Timeout(10_000)]
    public async Task Late_hook_passes_at_sixty_seconds_and_a_missing_hook_fails_at_the_cap(
        CancellationToken cancellationToken)
    {
        var wall = new FakeTimeProvider();
        var cap = TimeSpan.FromSeconds(61);
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = LoadTimeoutWait.UntilSignaledAsync(
            signal.Task, clock: null, budget: null, wallCap: cap, wallClock: wall,
            cancellationToken: cancellationToken, label: "The hook");

        wall.Advance(TimeSpan.FromSeconds(60));
        var atSixty = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken));
        atSixty.ShouldNotBe(wait, "60s of fake wall delay must not fail a hook that is only late");
        signal.SetResult();
        var passed = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
        passed.ShouldBe(wait, "a hook 60s late must pass");
        await wait;

        var logical = new FakeTimeProvider();
        var logicalWall = new FakeTimeProvider();
        var logicalWait = LoadTimeoutWait.UntilSignaledAsync(
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task,
            logical, TimeSpan.FromSeconds(10), cap, wallClock: logicalWall, label: "The hook");
        logical.Advance(TimeSpan.FromSeconds(10));
        var logicalDone = await Task.WhenAny(logicalWait, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
        logicalDone.ShouldBe(logicalWait, "the test-clock budget fails without spending the wall cap");
        (await Should.ThrowAsync<TimeoutException>(logicalWait)).Message.ShouldContain("test-clock budget");

        var expiredClock = new FakeTimeProvider();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expired = LoadTimeoutWait.UntilSignaledAsync(
            never.Task, clock: null, budget: null, wallCap: cap, wallClock: expiredClock, label: "The hook");
        expiredClock.Advance(cap - TimeSpan.FromSeconds(1));
        var beforeCap = await Task.WhenAny(expired, Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken));
        beforeCap.ShouldNotBe(expired, "the wait fails only once the wall cap elapses");
        expiredClock.Advance(TimeSpan.FromSeconds(1));
        var atCap = await Task.WhenAny(expired, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
        atCap.ShouldBe(expired, "a hook that never runs fails at the wall cap");
        (await Should.ThrowAsync<TimeoutException>(expired)).Message.ShouldContain("wall cap");
    }

    [Test]
    [Timeout(10_000)]
    public async Task Open_wall_cap_is_swallowed_and_an_operation_cancellation_is_rethrown(
        CancellationToken cancellationToken)
    {
        var wall = new FakeTimeProvider();
        var cap = TimeSpan.FromSeconds(45);
        using (var gate = LoadTimeoutWait.Open(
            clock: null, budget: null, wallCap: cap, wallClock: wall, label: "The hook"))
        {
            wall.Advance(cap - TimeSpan.FromSeconds(1));
            gate.WallElapsed.ShouldBeFalse("the Open path stays open until the wall cap elapses");
            wall.Advance(TimeSpan.FromSeconds(1));
            gate.WallElapsed.ShouldBeTrue("Pipeline and Spill read WallElapsed after the cap");
            var pending = Task.Delay(Timeout.Infinite, gate.Token);
            var done = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
            done.ShouldBe(pending, "Open callers observe the wall-cap cancellation");
            var swallowed = false;
            try
            {
                await pending;
            }
            catch (OperationCanceledException) when (gate.WallElapsed)
            {
                swallowed = true;
            }
            swallowed.ShouldBeTrue("Open callers swallow the wall-cap cancellation");
        }

        var live = new FakeTimeProvider();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var thrown = await Should.ThrowAsync<OperationCanceledException>(() =>
            LoadTimeoutWait.UntilSignaledAsync(
                ct => Task.FromCanceled(canceled.Token),
                clock: null, budget: null, wallCap: cap, wallClock: live,
                cancellationToken: cancellationToken, label: "The hook"));
        thrown.Message.ShouldNotContain("wall cap");
    }

    // The six LoadWallCap values. 45s covers Specialist's width-4 row max of
    // 23.21s. 30s covers Pipeline 2.81s, Land 5.61s, Boot 11.33s and Delegation
    // 8.27s. 64s stays for Spill. Review 968eb849 measured those maxima at
    // width 4. The cost-file means are not the basis. The fake clock reaches
    // the cap; this does not wait out 45s.
    [Test]
    [Arguments("SpecialistQualificationTests", 45)]
    [Arguments("AgentTaskPipelineStatusTests", 30)]
    [Arguments("AgentTaskLandSourcePersistenceTests", 30)]
    [Arguments("BootStallDetectionTests", 30)]
    [Arguments("DelegationDispatchRecoveryBoundaryTests", 30)]
    [Arguments("SessionMessageQueueDeliveredSpillRecoveryTests", 64)]
    [Timeout(10_000)]
    public async Task Hook_that_never_runs_fails_at_its_class_wall_cap(
        string className, int capSeconds, CancellationToken cancellationToken)
    {
        var wall = new FakeTimeProvider();
        var cap = TimeSpan.FromSeconds(capSeconds);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = LoadTimeoutWait.UntilSignaledAsync(
            never.Task, clock: null, budget: null, wallCap: cap, wallClock: wall,
            cancellationToken: cancellationToken, label: className);
        wall.Advance(cap - TimeSpan.FromSeconds(1));
        var before = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken));
        before.ShouldNotBe(wait, className + " must not fail before its wall cap");
        wall.Advance(TimeSpan.FromSeconds(1));
        var atCap = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
        atCap.ShouldBe(wait, className + " fails when its wall cap elapses");
        (await Should.ThrowAsync<TimeoutException>(wait)).Message.ShouldContain("wall cap");
    }
}
