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
}
