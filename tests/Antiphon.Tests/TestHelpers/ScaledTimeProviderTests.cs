using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class ScaledTimeProviderTests
{
    [Test]
    public void Speed_10_advances_ten_times_real_time()
    {
        var source = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new ScaledTimeProvider(10, source: source);
        var beforeUtc = clock.GetUtcNow();
        var beforeTimestamp = clock.GetTimestamp();

        source.Advance(TimeSpan.FromMilliseconds(100));

        (clock.GetUtcNow() - beforeUtc).ShouldBe(TimeSpan.FromSeconds(1), "scaled-utc-100ms-is-1s");
        clock.GetElapsedTime(beforeTimestamp, clock.GetTimestamp())
            .ShouldBe(TimeSpan.FromSeconds(1), "scaled-timestamp-100ms-is-1s");
    }

    [Test]
    public async Task Delay_on_the_clock_completes_speed_times_sooner()
    {
        var source = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new ScaledTimeProvider(10, source: source);
        using var cts = new CancellationTokenSource();
        var pending = Task.Delay(TimeSpan.FromSeconds(1), clock, cts.Token);
        try
        {
            pending.IsCompleted.ShouldBeFalse("scaled-timer-before-source-advance");
            source.Advance(TimeSpan.FromMilliseconds(99));
            pending.IsCompleted.ShouldBeFalse("scaled-timer-before-100ms");
            source.Advance(TimeSpan.FromMilliseconds(1));
            pending.IsCompleted.ShouldBeTrue("scaled-timer-due-at-100ms");
            await pending;
        }
        finally
        {
            cts.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task Advance_jumps_now_without_firing_a_pending_delay()
    {
        var source = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new ScaledTimeProvider(10, source: source);
        using var cts = new CancellationTokenSource();
        var pending = Task.Delay(TimeSpan.FromSeconds(10), clock, cts.Token);
        var beforeUtc = clock.GetUtcNow();
        var beforeTimestamp = clock.GetTimestamp();
        try
        {
            clock.Advance(TimeSpan.FromHours(1));
            (clock.GetUtcNow() - beforeUtc).ShouldBe(TimeSpan.FromHours(1), "offset-utc-exact");
            clock.GetElapsedTime(beforeTimestamp, clock.GetTimestamp())
                .ShouldBe(TimeSpan.FromHours(1), "offset-timestamp-exact");
            pending.IsCompleted.ShouldBeFalse("offset-does-not-fire-timer");
            source.Advance(TimeSpan.FromMilliseconds(999));
            pending.IsCompleted.ShouldBeFalse("offset-timer-before-source-due");
            source.Advance(TimeSpan.FromMilliseconds(1));
            pending.IsCompleted.ShouldBeTrue("offset-timer-at-source-due");
            await pending;
        }
        finally
        {
            cts.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task Speed_one_is_an_offset_clock()
    {
        var source = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new ScaledTimeProvider(1, source: source);
        clock.Advance(TimeSpan.FromSeconds(31));
        (clock.GetUtcNow() - source.GetUtcNow()).ShouldBe(TimeSpan.FromSeconds(31), "speed-one-offset-exact");

        using var cts = new CancellationTokenSource();
        var pending = Task.Delay(TimeSpan.FromMilliseconds(100), clock, cts.Token);
        try
        {
            source.Advance(TimeSpan.FromMilliseconds(99));
            pending.IsCompleted.ShouldBeFalse("speed-one-timer-before-100ms");
            source.Advance(TimeSpan.FromMilliseconds(1));
            pending.IsCompleted.ShouldBeTrue("speed-one-timer-at-100ms");
            await pending;
        }
        finally
        {
            cts.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task CancelAfter_on_the_clock_is_scaled()
    {
        var source = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new ScaledTimeProvider(10, source: source);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1), clock);
        var wait = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        try
        {
            source.Advance(TimeSpan.FromMilliseconds(99));
            cts.IsCancellationRequested.ShouldBeFalse("scaled-cancel-at-100ms: before");
            wait.IsCompleted.ShouldBeFalse("scaled-cancel-wait-before-100ms");
            source.Advance(TimeSpan.FromMilliseconds(1));
            cts.IsCancellationRequested.ShouldBeTrue("scaled-cancel-at-100ms: at due");
            wait.IsCanceled.ShouldBeTrue("scaled-cancel-wait-observed");
        }
        finally
        {
            cts.Cancel();
            try { await wait; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    public void Non_positive_speed_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ScaledTimeProvider(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new ScaledTimeProvider(-1));
    }
}
