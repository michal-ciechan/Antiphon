using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class ScaledTimeProviderTests
{
    [Test]
    public async Task Speed_10_advances_ten_times_real_time()
    {
        var clock = new ScaledTimeProvider(10);
        var before = clock.GetUtcNow();
        await Task.Delay(100);
        var moved = (clock.GetUtcNow() - before).TotalSeconds;
        moved.ShouldBeGreaterThanOrEqualTo(0.8);
        moved.ShouldBeLessThanOrEqualTo(3.0);
    }

    [Test]
    public async Task Delay_on_the_clock_completes_speed_times_sooner()
    {
        var clock = new ScaledTimeProvider(10);
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(1), clock);
        elapsed.Stop();
        elapsed.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(50);
        elapsed.ElapsedMilliseconds.ShouldBeLessThanOrEqualTo(500);
    }

    [Test]
    public async Task Advance_jumps_now_without_firing_a_pending_delay()
    {
        var clock = new ScaledTimeProvider(10);
        using var cts = new CancellationTokenSource();
        var pending = Task.Delay(TimeSpan.FromSeconds(10), clock, cts.Token);
        var before = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(1));
        (clock.GetUtcNow() - before).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
        await Task.Delay(50);
        pending.IsCompleted.ShouldBeFalse();
        cts.Cancel();
    }

    [Test]
    public async Task Speed_one_is_an_offset_clock()
    {
        var clock = new ScaledTimeProvider(1);
        clock.Advance(TimeSpan.FromSeconds(31));
        var drift = clock.GetUtcNow() - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(31);
        drift.Duration().ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(1));

        var elapsed = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromMilliseconds(100), clock);
        elapsed.Stop();
        elapsed.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(90);
    }

    [Test]
    public async Task CancelAfter_on_the_clock_is_scaled()
    {
        var clock = new ScaledTimeProvider(10);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1), clock);
        var wait = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        var finished = await Task.WhenAny(wait, Task.Delay(500));
        finished.ShouldBe(wait);
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    public void Non_positive_speed_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ScaledTimeProvider(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new ScaledTimeProvider(-1));
    }
}
