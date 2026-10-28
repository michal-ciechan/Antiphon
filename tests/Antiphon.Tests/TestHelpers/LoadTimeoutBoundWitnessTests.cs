using Antiphon.Checkpoints;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.Checkpoints;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Each shared cap accepts a hook that arrives after the old bound and rejects a hook
/// that never runs. Clocks are fake. No case waits out the real cap.
/// </summary>
[Category("Unit")]
public sealed class LoadTimeoutBoundWitnessTests
{
    [Test]
    public async Task Checkpoint_positive_wait_accepts_a_late_death_and_rejects_a_hook_that_never_dies()
    {
        var run = Directory.CreateTempSubdirectory("load-timeout-wait").FullName;
        try
        {
            CheckpointFixtures.MarkRun(run, alive: true);
            var late = await WaitAsync(run, LoadTimeoutBounds.CheckpointPositiveWait, TimeSpan.FromSeconds(2));
            late.Exit.ShouldBe(ExitCodes.ExecutorCrashed);
            late.Elapsed.ShouldBe(TimeSpan.FromSeconds(2));

            var oldBound = await WaitAsync(run, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            oldBound.Exit.ShouldBe(ExitCodes.StillRunning);
            oldBound.Elapsed.ShouldBe(TimeSpan.FromSeconds(1));

            var wall = System.Diagnostics.Stopwatch.StartNew();
            var never = await WaitAsync(run, LoadTimeoutBounds.CheckpointPositiveWait, dieAfter: null);
            wall.Stop();
            never.Exit.ShouldBe(ExitCodes.StillRunning);
            never.Elapsed.ShouldBe(LoadTimeoutBounds.CheckpointPositiveWait);
            wall.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        }
        finally
        {
            Directory.Delete(run, true);
        }
    }

    [Test]
    public async Task Launch_queue_idle_wait_accepts_a_late_completion_and_rejects_a_hook_that_never_runs()
    {
        var oldClock = new FakeTimeProvider();
        var oldHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldWait = AgentSessionLaunchQueue.WaitForPendingAsync(
            () => [oldHook.Task], TimeSpan.FromSeconds(15), CancellationToken.None, oldClock);
        oldClock.Advance(TimeSpan.FromSeconds(16));
        await Should.ThrowAsync<OperationCanceledException>(() => oldWait.WaitAsync(TimeSpan.FromSeconds(2)));
        oldHook.TrySetResult();

        var lateClock = new FakeTimeProvider();
        var lateHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = AgentSessionLaunchQueue.WaitForPendingAsync(
            () => lateHook.Task.IsCompleted ? [] : [lateHook.Task],
            LoadTimeoutBounds.LaunchQueueIdleWait, CancellationToken.None, lateClock);
        lateClock.Advance(TimeSpan.FromSeconds(16));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        late.IsCompleted.ShouldBeFalse("16s is past the old 15s bound and inside the 45s cap");
        lateHook.TrySetResult();
        await late.WaitAsync(TimeSpan.FromSeconds(2));

        var neverClock = new FakeTimeProvider();
        var neverHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wall = System.Diagnostics.Stopwatch.StartNew();
        var never = AgentSessionLaunchQueue.WaitForPendingAsync(
            () => [neverHook.Task], LoadTimeoutBounds.LaunchQueueIdleWait, CancellationToken.None, neverClock);
        neverClock.Advance(LoadTimeoutBounds.LaunchQueueIdleWait);
        await Should.ThrowAsync<OperationCanceledException>(() => never.WaitAsync(TimeSpan.FromSeconds(2)));
        wall.Stop();
        wall.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        neverHook.TrySetResult();
    }

    [Test]
    public async Task Phone_home_signal_wait_accepts_a_late_hook_and_rejects_one_that_never_runs()
    {
        var oldClock = new FakeTimeProvider();
        var oldHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldWait = WaitHookAsync(oldHook.Task, TimeSpan.FromSeconds(5), oldClock);
        oldClock.Advance(TimeSpan.FromSeconds(6));
        await Should.ThrowAsync<OperationCanceledException>(() => oldWait.WaitAsync(TimeSpan.FromSeconds(2)));
        oldHook.TrySetResult();

        var lateClock = new FakeTimeProvider();
        var lateHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = WaitHookAsync(lateHook.Task, LoadTimeoutBounds.PhoneHomeSignalWait, lateClock);
        lateClock.Advance(TimeSpan.FromSeconds(6));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        late.IsCompleted.ShouldBeFalse("6s is past the old 5s bound and inside the 45s cap");
        lateHook.TrySetResult();
        await late.WaitAsync(TimeSpan.FromSeconds(2));

        var neverClock = new FakeTimeProvider();
        var neverHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wall = System.Diagnostics.Stopwatch.StartNew();
        var never = WaitHookAsync(neverHook.Task, LoadTimeoutBounds.PhoneHomeSignalWait, neverClock);
        neverClock.Advance(LoadTimeoutBounds.PhoneHomeSignalWait);
        await Should.ThrowAsync<OperationCanceledException>(() => never.WaitAsync(TimeSpan.FromSeconds(2)));
        wall.Stop();
        wall.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        neverHook.TrySetResult();
    }

    [Test]
    public async Task Session_runtime_page_yield_accepts_a_twenty_second_delay_and_fails_at_the_cap()
    {
        var lateClock = new FakeTimeProvider();
        var lateHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = LoadTimeoutWait.UntilSignaledAsync(
            lateHook.Task, clock: null, budget: null,
            wallCap: LoadTimeoutBounds.SessionRuntimeActionWait, wallClock: lateClock,
            label: "The hosted page yield");
        lateClock.Advance(TimeSpan.FromSeconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        late.IsCompleted.ShouldBeFalse("a page yield 20s late stays inside the 70s cap");
        lateHook.TrySetResult();
        await late.WaitAsync(TimeSpan.FromSeconds(2));

        var neverClock = new FakeTimeProvider();
        var neverHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wall = System.Diagnostics.Stopwatch.StartNew();
        var never = LoadTimeoutWait.UntilSignaledAsync(
            neverHook.Task, clock: null, budget: null,
            wallCap: LoadTimeoutBounds.SessionRuntimeActionWait, wallClock: neverClock,
            label: "The hosted page yield");
        neverClock.Advance(LoadTimeoutBounds.SessionRuntimeActionWait - TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        never.IsCompleted.ShouldBeFalse("the page yield stays open until the wall cap elapses");
        neverClock.Advance(TimeSpan.FromSeconds(1));
        var atCap = await Task.WhenAny(never, Task.Delay(TimeSpan.FromSeconds(2)));
        atCap.ShouldBe(never, "a page yield that never arrives fails at the cap");
        (await Should.ThrowAsync<TimeoutException>(never)).Message.ShouldContain("wall cap");
        wall.Stop();
        wall.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        neverHook.TrySetResult();
    }

    private static async Task WaitHookAsync(Task hook, TimeSpan cap, TimeProvider clock)
    {
        using var budget = new CancellationTokenSource(cap, clock);
        await hook.WaitAsync(budget.Token);
    }

    private static async Task<(int Exit, TimeSpan Elapsed)> WaitAsync(string run, TimeSpan maxWait, TimeSpan? dieAfter)
    {
        var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        var born = now;
        DateTimeOffset Clock() => now;
        Task Delay(TimeSpan slice, CancellationToken _)
        {
            now += slice;
            return Task.CompletedTask;
        }

        var exit = await new WaitCommand(clock: Clock, delay: Delay, probe: new ScriptedProbe(Clock, born, dieAfter))
            .WaitAsync(run, maxWait, TimeSpan.FromHours(1), TextWriter.Null, CancellationToken.None);
        return (exit, now - born);
    }

    private sealed class ScriptedProbe(Func<DateTimeOffset> clock, DateTimeOffset born, TimeSpan? dieAfter) : ProcessIdentityProbe
    {
        public override ProcessObservation Observe(ProcessIdentity? expected) =>
            dieAfter is TimeSpan die && clock() - born >= die
                ? new ProcessObservation(ProcessVerdict.Dead, "identity-dead")
                : new ProcessObservation(ProcessVerdict.AliveSame, "identity-alive");
    }
}
