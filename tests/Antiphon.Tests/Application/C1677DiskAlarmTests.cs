using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1677 S3: the <c>WorkspaceDiskLow</c> attention row from the runner host-stats cache
/// (test design V-60..V-67). Samples go through <see cref="HostStatsCache.Record"/>, the same call the
/// host-stats poller makes, and the row is read through the real <see cref="AttentionService.GetAsync"/>.
/// The alarm deletes nothing, so no test here touches a real volume or worktree.
/// </summary>
[Category("Integration")]
// Test design §Performance budget: the RunnerAlarmAttentionTests analog p95 5.3 s, ~8.7 s loaded; 2x.
[Timeout(20_000)]
public sealed class C1677DiskAlarmTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private const long GiB = 1L << 30;
    private const string Server2 = "server2";

    // V-60. The S-7 integer boundary: 100 * (100 - 15) >= 85 * 100 exactly.
    [Test]
    public async Task Work_at_85_percent_raises_one_warning()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/work", 15 * GiB, 100 * GiB), ("/state", 90 * GiB, 100 * GiB));

        var row = (await world.ReadAsync(schema)).ShouldHaveSingleItem("boundary-85-one-row");
        row.Kind.ShouldBe(AttentionKind.WorkspaceDiskLow);
        row.Severity.ShouldBe(AlertSeverity.Warning, "boundary-85-warning");
        row.ConditionKey.ShouldBe("workspace-disk-low:server2", "boundary-85-key");
        row.Title.ShouldContain(Server2);
        row.Actions.ShouldBe([AttentionAction.OpenDrawer]);
        row.TaskId.ShouldBeNull();
        row.SessionId.ShouldBeNull();
        row.AgentId.ShouldBeNull();
        row.SinceUtc.ShouldBe(Now.UtcDateTime);
    }

    // V-61. 84.9 percent: 100 * 849 < 85 * 1000.
    [Test]
    public async Task Work_below_85_percent_raises_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/work", 151 * GiB, 1000 * GiB));

        (await world.ReadAsync(schema)).ShouldBeEmpty("below-85-no-row");
    }

    // V-62. Another volume over the threshold does not count; the configured one counts wherever it is listed.
    [Test]
    public async Task Only_the_configured_volume_counts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/state", 5 * GiB, 100 * GiB), ("/work", 50 * GiB, 100 * GiB));
        (await world.ReadAsync(schema)).ShouldBeEmpty("other-volume-no-row");

        world.Clock.Advance(TimeSpan.FromSeconds(5));
        world.Record(Server2, ("/state", 50 * GiB, 100 * GiB), ("/work", 10 * GiB, 100 * GiB));
        var row = (await world.ReadAsync(schema)).ShouldHaveSingleItem("work-listed-second-one-row");
        row.Evidence.ShouldContain("usedPercent=90");
        row.Evidence.ShouldContain("path=/work");
    }

    // V-63. A sample the server first observed more than SampleFreshMinutes ago raises nothing.
    [Test]
    public async Task Stale_sample_raises_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World(start: Now.AddMinutes(-16));
        world.Record(Server2, ("/work", 1 * GiB, 100 * GiB));

        world.Clock.SetUtcNow(Now.AddMinutes(-2));
        (await world.ReadAsync(schema)).ShouldHaveSingleItem("fresh-control-14-min-raises");

        world.Clock.SetUtcNow(Now);
        (await world.ReadAsync(schema)).ShouldBeEmpty("stale-16-min-no-row");

        // A runner that keeps answering with the SAME sample time is still stale: ObservedAt is when
        // the server first saw that sample, not when it last polled.
        world.Record(Server2, ("/work", 1 * GiB, 100 * GiB), at: Now.AddMinutes(-16));
        (await world.ReadAsync(schema)).ShouldBeEmpty("same-sample-repeated-stays-stale");
    }

    // CARD-1683 (sweep 974b0c17 C7). S-7 stays exact for a raising huge reading too: 100 * (Total - Free)
    // overflows a long, and only the exact compare sees 90 percent used.
    [Test]
    public async Task Huge_reading_at_90_percent_raises_one_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/work", long.MaxValue / 10, long.MaxValue));

        (await world.ReadAsync(schema)).ShouldHaveSingleItem("huge-90-one-row")
            .Evidence.ShouldContain("usedPercent=90");
    }

    // CARD-1683 (sweep 974b0c17 C8). The freshness bound is inclusive: a sample first observed exactly
    // SampleFreshMinutes ago still raises.
    [Test]
    public async Task Sample_observed_exactly_at_the_freshness_bound_raises()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World(start: Now.AddMinutes(-15));
        world.Record(Server2, ("/work", 1 * GiB, 100 * GiB));
        world.Clock.SetUtcNow(Now);

        (await world.ReadAsync(schema)).ShouldHaveSingleItem("fresh-15-min-inclusive");
    }

    // CARD-1683 (sweep 974b0c17 C10). Freshness is the server's first-observed time, not the runner's clock:
    // a sample stamped 30 minutes behind the server but first seen now is fresh.
    [Test]
    public async Task Skewed_runner_clock_with_a_freshly_observed_sample_raises()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/work", 1 * GiB, 100 * GiB), at: Now.AddMinutes(-30));

        (await world.ReadAsync(schema)).ShouldHaveSingleItem("skewed-runner-clock-fresh");
    }

    // CARD-1683 (sweep 974b0c17 C19). The row starts when the server first observed the sample, not when
    // the feed is read.
    [Test]
    public async Task Row_start_is_the_observed_time_not_the_read_time()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/work", 1 * GiB, 100 * GiB));
        world.Clock.Advance(TimeSpan.FromMinutes(5));

        (await world.ReadAsync(schema)).ShouldHaveSingleItem().SinceUtc
            .ShouldBe(Now.UtcDateTime, "since-is-observed-at");
    }

    // CARD-1683 (sweep 974b0c17 C13). The configured volume matches exactly: a volume whose path only
    // starts with it does not count, wherever it is listed.
    [Test]
    public async Task A_volume_that_only_shares_the_prefix_does_not_count()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/workspace", 1 * GiB, 100 * GiB), ("/work", 50 * GiB, 100 * GiB));

        (await world.ReadAsync(schema)).ShouldBeEmpty("prefix-volume-no-row");
    }

    // Brief: a missing or unusable reading raises nothing and never throws out of the feed read.
    [Test]
    [Arguments("no-cache")]
    [Arguments("no-entry")]
    [Arguments("failure-only")]
    [Arguments("no-current")]
    [Arguments("no-volume")]
    [Arguments("zero-total")]
    [Arguments("free-above-total")]
    [Arguments("negative-free")]
    [Arguments("huge-total")]
    public async Task Missing_or_unusable_reading_raises_nothing(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World(withCache: shape != "no-cache");
        switch (shape)
        {
            case "no-cache":
            case "no-entry":
                break;
            case "failure-only":
                world.Cache!.RecordFailure(Server2, HostStatsFailureKind.Offline);
                break;
            case "no-current":
                world.Cache!.Record(Server2, new RunnerHostStatsDto(
                    Now, 5, 60, 4, null, new Dictionary<string, RunnerHostWindowRollups>(), null));
                break;
            case "no-volume":
                world.Record(Server2, ("/state", 1 * GiB, 100 * GiB));
                break;
            case "zero-total":
                world.Record(Server2, ("/work", 0, 0));
                break;
            case "free-above-total":
                world.Record(Server2, ("/work", 200 * GiB, 100 * GiB));
                break;
            case "negative-free":
                world.Record(Server2, ("/work", -1, 100 * GiB));
                break;
            case "huge-total":
                // 100 * Total overflows a long; the compare must stay exact and must not throw.
                world.Record(Server2, ("/work", long.MaxValue / 2, long.MaxValue));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        (await world.ReadAsync(schema)).ShouldBeEmpty($"{shape}-no-row");
    }

    // V-64 and D-11. Only allow-listed runners are alarmed, and the desktop never is, even when listed.
    [Test]
    public async Task Unlisted_runner_raises_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(RunnerPlatformWire.DesktopId, ("/work", 1 * GiB, 100 * GiB));
        world.Record("server2-temp", ("/work", 1 * GiB, 100 * GiB));
        (await world.ReadAsync(schema)).ShouldBeEmpty("unlisted-runners-no-row");

        world.Settings.Runners[RunnerPlatformWire.DesktopId] = "/work";
        (await world.ReadAsync(schema)).ShouldBeEmpty("desktop-listed-still-no-row");

        // CARD-1683 (sweep 974b0c17 V6): desktop matching ignores case.
        world.Record("Desktop", ("/work", 1 * GiB, 100 * GiB));
        world.Settings.Runners["Desktop"] = "/work";
        (await world.ReadAsync(schema)).ShouldBeEmpty("desktop-other-case-no-row");

        world.Record(Server2, ("/work", 1 * GiB, 100 * GiB));
        (await world.ReadAsync(schema)).ShouldHaveSingleItem("listed-control-one-row")
            .ConditionKey.ShouldBe("workspace-disk-low:server2");
    }

    // V-65.
    [Test]
    public async Task Evidence_names_bytes_and_retention_mode()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/work", 15 * GiB, 100 * GiB));

        var evidence = (await world.ReadAsync(schema)).ShouldHaveSingleItem().Evidence;
        evidence.ShouldContain("path=/work");
        evidence.ShouldContain("usedPercent=85");
        evidence.ShouldContain($"freeBytes={15 * GiB}");
        evidence.ShouldContain($"totalBytes={100 * GiB}");
        evidence.ShouldContain($"observedAt={Now:O}");
        evidence.ShouldContain("threshold=85");
        evidence.ShouldContain("retention=Off");
    }

    // The slice reads the retention mode from configuration, not from S1's settings type (test design
    // §Slices). Absent or anything but true is Off.
    [Test]
    [Arguments(null, null, "Off")]
    [Arguments("false", "true", "Off")]
    [Arguments("not-a-bool", "true", "Off")]
    [Arguments("true", null, "DryRun")]
    [Arguments("true", "false", "DryRun")]
    [Arguments("True", "true", "Execute")]
    public async Task Retention_mode_is_read_from_configuration(string? enabled, string? execute, string mode)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var config = new Dictionary<string, string?>();
        if (enabled is not null) config["RunnerWorktreeRetention:Enabled"] = enabled;
        if (execute is not null) config["RunnerWorktreeRetention:Execute"] = execute;
        var world = new World(configuration: config);
        world.Record(Server2, ("/work", 5 * GiB, 100 * GiB));

        (await world.ReadAsync(schema)).ShouldHaveSingleItem().Evidence
            .ShouldEndWith($"retention={mode}", customMessage: $"retention-mode-{mode}");
    }

    // V-66. Computed per read: recovery below the threshold closes the row.
    [Test]
    public async Task Row_clears_below_the_threshold()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Record(Server2, ("/work", 10 * GiB, 100 * GiB));
        (await world.ReadAsync(schema)).ShouldHaveSingleItem("90-percent-raises");

        world.Clock.Advance(TimeSpan.FromSeconds(5));
        world.Record(Server2, ("/work", 50 * GiB, 100 * GiB));
        (await world.ReadAsync(schema)).ShouldBeEmpty("50-percent-clears");
    }

    // Brief: one open row per runner, not one per poll tick or per read.
    [Test]
    public async Task Repeated_samples_keep_one_row_per_runner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Settings.Runners["server2-temp"] = "/work";
        foreach (var free in new[] { 10L, 9L, 8L })
        {
            world.Record(Server2, ("/work", free * GiB, 100 * GiB));
            world.Clock.Advance(TimeSpan.FromSeconds(5));
        }
        world.Record("server2-temp", ("/work", 1 * GiB, 100 * GiB));

        var first = await world.ReadAsync(schema);
        var second = await world.ReadAsync(schema);
        foreach (var rows in new[] { first, second })
        {
            rows.Count.ShouldBe(2, "one-row-per-listed-runner");
            rows.Select(row => row.ConditionKey).ShouldBe(
                ["workspace-disk-low:server2", "workspace-disk-low:server2-temp"], ignoreOrder: true);
        }
        first.Single(row => row.ConditionKey == "workspace-disk-low:server2").Evidence
            .ShouldContain("usedPercent=92", customMessage: "latest-sample-wins");
    }

    [Test]
    public async Task Disabled_alarm_raises_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Settings.Enabled = false;
        world.Record(Server2, ("/work", 1 * GiB, 100 * GiB));

        (await world.ReadAsync(schema)).ShouldBeEmpty("disabled-no-row");
    }

    // V-67.
    [Test]
    [Arguments(49, false)]
    [Arguments(50, true)]
    [Arguments(99, true)]
    [Arguments(100, false)]
    public void Used_percent_bounds(int value, bool valid)
    {
        var settings = Listed();
        settings.UsedPercent = value;
        new WorkspaceDiskAlarmSettingsValidator().Validate(null, settings).Succeeded
            .ShouldBe(valid, $"used-percent-{value}");
    }

    [Test]
    [Arguments("fresh-0", false)]
    [Arguments("fresh-1", true)]
    [Arguments("fresh-1440", true)]
    [Arguments("fresh-1441", false)]
    [Arguments("desktop-listed", false)]
    [Arguments("desktop-other-case", false)]
    [Arguments("blank-runner", false)]
    [Arguments("relative-path", false)]
    [Arguments("blank-path", false)]
    [Arguments("empty-runners", true)]
    public void Settings_validator_refuses_unsafe_values(string shape, bool valid)
    {
        var settings = Listed();
        switch (shape)
        {
            case "fresh-0": settings.SampleFreshMinutes = 0; break;
            case "fresh-1": settings.SampleFreshMinutes = 1; break;
            case "fresh-1440": settings.SampleFreshMinutes = 1440; break;
            case "fresh-1441": settings.SampleFreshMinutes = 1441; break;
            case "desktop-listed": settings.Runners[RunnerPlatformWire.DesktopId] = "/work"; break;
            case "desktop-other-case": settings.Runners["Desktop"] = "/work"; break;
            case "blank-runner": settings.Runners[" "] = "/work"; break;
            case "relative-path": settings.Runners[Server2] = "work"; break;
            case "blank-path": settings.Runners[Server2] = ""; break;
            case "empty-runners": settings.Runners.Clear(); break;
            default: throw new ArgumentOutOfRangeException(nameof(shape));
        }
        new WorkspaceDiskAlarmSettingsValidator().Validate(null, settings).Succeeded
            .ShouldBe(valid, shape);
    }

    // CARD-1794 (5). server2's /work reached 1.8 GB free with no alert: a free-space floor raises an Error row
    // even below UsedPercent, and a Warning row turns Error once the free bytes are under the floor.
    [Test]
    [Arguments(45L, 250L, 50, "Error")]    // 82 percent used: below UsedPercent, under the floor
    [Arguments(5L, 100L, 10, "Error")]     // 95 percent used and under the floor
    [Arguments(15L, 100L, 10, "Warning")]  // 85 percent used, above the floor
    [Arguments(50L, 250L, 50, "none")]     // 80 percent used, exactly at the floor
    public async Task C1794_free_space_floor_raises_an_error(long freeGiB, long totalGiB, int minFreeGiB, string expected)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new World();
        world.Settings.MinFreeGiB = minFreeGiB;
        world.Record(Server2, ("/work", freeGiB * GiB, totalGiB * GiB));

        var rows = await world.ReadAsync(schema);
        if (expected == "none")
        {
            rows.ShouldBeEmpty("above-floor-below-percent-no-row");
            return;
        }
        var row = rows.ShouldHaveSingleItem("floor-one-row");
        row.Severity.ShouldBe(Enum.Parse<AlertSeverity>(expected), $"floor-{freeGiB}-of-{totalGiB}");
        row.ConditionKey.ShouldBe("workspace-disk-low:server2");
        if (expected == "Error")
        {
            row.Evidence.ShouldContain($"minFreeBytes={world.Settings.MinFreeGiB * GiB}");
            row.Headline.ShouldContain("below the " + world.Settings.MinFreeGiB + " GiB floor");
        }
    }

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(50, true)]
    [Arguments(100000, true)]
    [Arguments(100001, false)]
    public void C1794_min_free_bounds(int value, bool valid)
    {
        var settings = Listed();
        settings.MinFreeGiB = value;
        new WorkspaceDiskAlarmSettingsValidator().Validate(null, settings).Succeeded
            .ShouldBe(valid, $"min-free-{value}");
    }

    private static WorkspaceDiskAlarmSettings Listed() =>
        new() { Runners = new Dictionary<string, string>(StringComparer.Ordinal) { [Server2] = "/work" } };

    private sealed class World
    {
        public FakeTimeProvider Clock { get; }
        public HostStatsCache? Cache { get; }
        public WorkspaceDiskAlarmSettings Settings { get; } = Listed();
        private readonly IConfiguration _configuration;

        public World(DateTimeOffset? start = null, bool withCache = true,
            IReadOnlyDictionary<string, string?>? configuration = null)
        {
            Clock = new FakeTimeProvider(start ?? Now);
            Cache = withCache ? new HostStatsCache(new HostStatsSettings(), Clock) : null;
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
                .Build();
        }

        public void Record(string runnerId, params (string Path, long Free, long Total)[] disks) =>
            Record(runnerId, disks, at: null);

        public void Record(string runnerId, (string Path, long Free, long Total) disk, DateTimeOffset at) =>
            Record(runnerId, [disk], at);

        private void Record(string runnerId, (string Path, long Free, long Total)[] disks, DateTimeOffset? at) =>
            Cache!.Record(runnerId, new RunnerHostStatsDto(
                at ?? Clock.GetUtcNow(), 5, 60, 4,
                new RunnerHostCurrentDto(null, null, null, null, null, null, null, null, null,
                    disks.Select(disk => new RunnerHostDiskDto(disk.Path, disk.Free, disk.Total)).ToArray(),
                    null, []),
                new Dictionary<string, RunnerHostWindowRollups>(), null));

        public async Task<List<AttentionItemDto>> ReadAsync(IsolatedTestSchema schema)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var dto = await new AttentionService(
                db, new AttentionServiceTests.FakeRunnerClient(),
                Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
                Clock, NullLogger<AttentionService>.Instance,
                hostStats: Cache, workspaceDiskAlarm: Options.Create(Settings),
                configuration: _configuration).GetAsync(CancellationToken.None, includeProgressProbe: false);
            return dto.Items.Where(item => item.Kind == AttentionKind.WorkspaceDiskLow).ToList();
        }
    }
}
