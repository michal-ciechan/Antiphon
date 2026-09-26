using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class HostStatsStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Empty_store_answers_null_latest_and_null_rollups()
    {
        var store = NewStore();
        store.Latest().ShouldBeNull();
        var rollups = store.Rollups(T0);
        foreach (var window in new[] { "1m", "5m", "15m", "30m" })
        {
            rollups[window].CpuPercent.ShouldBeNull();
            rollups[window].Load1.ShouldBeNull();
            rollups[window].MemoryUsedBytes.ShouldBeNull();
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task Ring_evicts_oldest_at_capacity_and_series_is_time_ordered()
    {
        var store = NewStore();
        for (var i = 0; i < 359; i++)
            store.Add(Sample(i));
        store.Series("cpu", "30m", NowAfter(358)).Count.ShouldBe(359);

        store.Add(Sample(359));
        store.Series("cpu", "30m", NowAfter(359)).Count.ShouldBe(360);

        store.Add(Sample(360));
        var points = store.Series("cpu", "30m", NowAfter(360));
        points.Count.ShouldBe(360);
        points[0].T.ShouldBe(T0.AddSeconds(5));
        for (var i = 1; i < points.Count; i++)
            points[i].T.ShouldBeGreaterThan(points[i - 1].T);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Rollups_match_hand_computed_avg_and_max_for_a_sawtooth()
    {
        var store = NewStore();
        for (var i = 0; i < 360; i++)
            store.Add(Sample(i));
        var rollups = store.Rollups(NowAfter(359));
        rollups["1m"].CpuPercent!.Avg.ShouldBe(13.5);
        rollups["1m"].CpuPercent.Max.ShouldBe(19);
        rollups["5m"].CpuPercent!.Avg.ShouldBe(9.5);
        rollups["5m"].CpuPercent.Max.ShouldBe(19);
        rollups["30m"].CpuPercent!.Avg.ShouldBe(9.5);
        rollups["30m"].CpuPercent.Max.ShouldBe(19);

        var shorter = NewStore();
        for (var i = 0; i < 359; i++)
            shorter.Add(Sample(i));
        shorter.Rollups(NowAfter(358))["5m"].CpuPercent!.Max.ShouldBe(19);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Partial_window_averages_only_the_samples_it_holds()
    {
        var store = NewStore();
        for (var i = 0; i < 24; i++)
            store.Add(Sample(i, cpu: i));
        var rollups = store.Rollups(NowAfter(23));
        rollups["5m"].CpuPercent!.Avg.ShouldBe(11.5);
        rollups["15m"].CpuPercent!.Avg.ShouldBe(11.5);
        rollups["30m"].CpuPercent!.Avg.ShouldBe(11.5);
        rollups["1m"].CpuPercent!.Avg.ShouldBe(17.5);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Window_boundary_includes_sixty_seconds_and_excludes_beyond()
    {
        var now = T0.AddMinutes(2);
        var inside = NewStore();
        inside.Add(Sample(0, cpu: 7) with { At = now.AddSeconds(-60) });
        inside.Rollups(now)["1m"].CpuPercent.ShouldNotBeNull();

        var outside = NewStore();
        outside.Add(Sample(0, cpu: 7) with { At = now.AddSeconds(-60.001) });
        outside.Rollups(now)["1m"].CpuPercent.ShouldBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task Metric_absent_from_samples_yields_null_rollups()
    {
        var store = NewStore();
        store.Add(Sample(0, load: null));
        store.Add(Sample(1, load: null));
        var rollups = store.Rollups(NowAfter(1));
        rollups["1m"].Load1.ShouldBeNull();
        rollups["5m"].Load1.ShouldBeNull();
        rollups["1m"].CpuPercent.ShouldNotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task Series_rejects_unknown_metric_and_window()
    {
        var store = NewStore();
        Should.Throw<ArgumentException>(() => store.Series("bogus", "1m", T0));
        Should.Throw<ArgumentException>(() => store.Series("cpu", "2h", T0));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Snapshot_carries_interval_and_retention_and_copies()
    {
        var store = NewStore();
        store.Add(Sample(0));
        var now = NowAfter(0);
        var snapshot = store.Snapshot(now);
        snapshot.IntervalSeconds.ShouldBe(5);
        snapshot.RetentionMinutes.ShouldBe(30);
        var series = store.Series("cpu", "30m", now);
        var count = series.Count;
        store.Add(Sample(1));
        series.Count.ShouldBe(count);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Writers_and_readers_wait_for_the_store_gate()
    {
        var store = NewStore();
        using var release = new ManualResetEventSlim(false);
        using var held = new ManualResetEventSlim(false);
        var locker = Task.Run(() =>
        {
            lock (store.Gate)
            {
                held.Set();
                release.Wait();
            }
        });
        held.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        var add = Task.Run(() => store.Add(Sample(0)));
        var read = Task.Run(() => store.Rollups(T0));
        await Task.Delay(200);
        add.IsCompleted.ShouldBeFalse();
        read.IsCompleted.ShouldBeFalse();
        release.Set();
        await Task.WhenAll(add, read, locker).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static HostStatsStore NewStore() => new(new HostStatsSettings(), TimeProvider.System);

    private static DateTimeOffset NowAfter(int lastIndex) => T0.AddSeconds(5 * (lastIndex + 1));

    internal static HostSample Sample(int i, double? cpu = null, double? load = 1) => new(
        T0.AddSeconds(5 * i),
        cpu ?? (i % 20),
        8,
        load,
        load,
        load,
        1_000_000,
        400_000,
        100,
        40,
        [],
        10,
        []);
}
