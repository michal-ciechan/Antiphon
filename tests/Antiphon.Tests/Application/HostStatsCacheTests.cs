using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class HostStatsCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private static SessionRunnerCatalogueEntryDto Host(string id = "server2", bool available = true) =>
        new(id, id, "linux", Now, available, available, null, 10, 0, "sessions", Now, !available, []);

    internal static RunnerHostStatsDto Sample(DateTimeOffset? at = null, double cpu = 42) =>
        new(at ?? Now, 5, 30, 24,
            new RunnerHostCurrentDto(cpu, 2, 3, 4, 50, 50, 100, null, null, [], 10, []),
            new Dictionary<string, RunnerHostWindowRollups>
            {
                ["1m"] = new(new RunnerHostRollupDto(cpu, cpu), null, null),
            }, null);

    private static (HostStatsCache Cache, FakeTimeProvider Clock) New(bool enabled = true)
    {
        var clock = new FakeTimeProvider(Now);
        return (new HostStatsCache(new HostStatsSettings { Enabled = enabled }, clock), clock);
    }

    [Test]
    public void Recorded_sample_projects_live()
    {
        var (cache, _) = New();
        cache.Record("server2", Sample(Now.AddHours(-1)));
        var row = cache.Project([Host()]).Single();
        row.State.ShouldBe("live");
        row.Current!.CpuPercent.ShouldBe(42);
        row.ObservedAt.ShouldBe(Now);
    }

    [Test]
    public void Old_sample_projects_stale_with_the_same_values()
    {
        var (cache, clock) = New();
        cache.Record("server2", Sample());
        clock.Advance(TimeSpan.FromMilliseconds(15000));
        cache.Project([Host()]).Single().State.ShouldBe("live");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var row = cache.Project([Host()]).Single();
        row.State.ShouldBe("stale");
        row.Current!.CpuPercent.ShouldBe(42);
        row.Rollups!["1m"].CpuPercent!.Avg.ShouldBe(42);
    }

    [Test]
    public void Unrecorded_disconnected_host_projects_offline_with_null_current()
    {
        var (cache, _) = New();
        var row = cache.Project([Host(available: false)]).Single();
        row.State.ShouldBe("offline");
        row.Current.ShouldBeNull();
        row.Rollups.ShouldBeNull();
    }

    [Test]
    public void Unsupported_failure_projects_unsupported()
    {
        var (cache, _) = New();
        cache.RecordFailure("server2", HostStatsFailureKind.Unsupported);
        var row = cache.Project([Host()]).Single();
        row.State.ShouldBe("unsupported");
        row.Current.ShouldBeNull();
    }

    [Test]
    public void Changed_is_true_after_a_new_sample_and_false_after_an_empty_tick()
    {
        var (cache, clock) = New();
        cache.BeginTick();
        cache.Record("server2", Sample());
        cache.Changed.ShouldBeTrue();
        cache.Published();
        cache.BeginTick();
        cache.Record("server2", Sample());
        cache.Changed.ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(15001));
        cache.Project([Host()]).Single().State.ShouldBe("stale");
        cache.Changed.ShouldBeTrue();
    }

    [Test]
    public void Disabled_poll_projects_every_host_offline_disabled()
    {
        var (cache, _) = New(false);
        cache.Record("server2", Sample());
        var rows = cache.Project([Host(), Host("desktop")]);
        rows.Select(r => r.State).ShouldAllBe(s => s == "offline");
        rows.Select(r => r.Reason).ShouldAllBe(s => s == "disabled");
    }
}
