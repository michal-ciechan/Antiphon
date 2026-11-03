using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0726 V-14, V-15, V-26, V-29, V-35. The alarm loop's timers and wakes.</summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class RunnerAlarmHostedServiceTests
{
    [Test]
    public void a_timer_is_registered_before_the_clock_reports_it_ready()
    {
        var clock = new AlarmClock(DateTimeOffset.UtcNow);
        var fired = false;
        clock.AfterFirstTimerSignal = () => clock.Inner.Advance(TimeSpan.FromSeconds(180));

        using var timer = clock.CreateTimer(_ => fired = true, null,
            TimeSpan.FromSeconds(180), Timeout.InfiniteTimeSpan);

        fired.ShouldBeTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task the_loop_raises_on_the_grace_timer_and_wakes_on_a_fence_signal()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var repo = new ScratchGitRepo("c726-loop-fence");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        await PlantDeadAsync(common);
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("server2", "server2", true, false, "transport_abort", null, 0));
        var clock = new AlarmClock(DateTimeOffset.UtcNow);
        var (service, state, wake) = Build(schema.ConnectionString, source, clock, new AlarmSettings());
        await service.StartAsync(CancellationToken.None);
        await clock.FirstTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Inner.Advance(TimeSpan.FromSeconds(179));
        await Task.Delay(100);
        state.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldBeNull();
        clock.Inner.Advance(TimeSpan.FromSeconds(1));
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RaisedAt is not null), "grace timer did not raise");

        wake.Fenced(common);
        await UntilAsync(() => state.Current.Journals.Any(finding => LandingGit.PathsEqual(finding.CommonDirectory, common)),
            "fence signal did not publish a finding");
        var stop = service.StopAsync(CancellationToken.None);
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    [Timeout(60_000)]
    public async Task the_sweep_runs_at_the_period_and_no_other_timer_exists()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var repo = new ScratchGitRepo("c726-loop-sweep");
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.Projects.Add(new Antiphon.Server.Domain.Entities.Project
        {
            Id = Guid.NewGuid(), Name = "sweep", GitRepositoryUrl = "https://example.test/sweep",
            LocalRepositoryPath = repo.Path, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("server2", "server2", true, true, null, null, 0));
        var inspector = new CountingInspector(new LandingGit());
        var clock = new AlarmClock(DateTimeOffset.UtcNow);
        var (service, _, _) = Build(schema.ConnectionString, source, clock, new AlarmSettings(), inspector);
        await service.StartAsync(CancellationToken.None);
        await clock.FirstTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshots = source.Calls;
        var inspections = inspector.Calls;
        snapshots.ShouldBeGreaterThanOrEqualTo(1);
        inspections.ShouldBeGreaterThanOrEqualTo(1);

        clock.Inner.Advance(TimeSpan.FromMinutes(14));
        await Task.Delay(100);
        source.Calls.ShouldBe(snapshots);
        inspector.Calls.ShouldBe(inspections);
        clock.Inner.Advance(TimeSpan.FromMinutes(1));
        await UntilAsync(() => source.Calls == snapshots + 1 && inspector.Calls == inspections + 1,
            $"sweep did not run at 15 minutes (snapshots {snapshots}->{source.Calls}, inspections {inspections}->{inspector.Calls})");
        clock.Timers.ShouldAllBe(timer => timer.Period == Timeout.InfiniteTimeSpan && timer.Due >= TimeSpan.FromMinutes(1));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(30_000)]
    public async Task disabled_alarms_start_no_loop_and_publish_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("server2", "server2", true, false, "transport_abort", null, 0));
        var clock = new AlarmClock(DateTimeOffset.UtcNow);
        var (service, state, _) = Build(schema.ConnectionString, source, clock, new AlarmSettings { Enabled = false });
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
        source.Calls.ShouldBe(0);
        clock.Timers.ShouldBeEmpty();
        clock.Inner.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(100);
        state.Current.Episodes.ShouldBeEmpty();
        state.Current.Journals.ShouldBeEmpty();
    }

    [Test]
    [Timeout(60_000)]
    public async Task a_failing_pass_is_logged_and_retried_without_waiting_for_the_sweep()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var source = new FakeEligibilitySource { ThrowOnCall = 2, IneligibleAfterThrow = true };
        source.Rows.Add(new RunnerEligibilitySnapshot("server2", "server2", true, true, null, null, 0));
        var clock = new AlarmClock(DateTimeOffset.UtcNow);
        var logger = new ListLogger<RunnerAlarmHostedService>();
        var (service, state, wake) = Build(schema.ConnectionString, source, clock, new AlarmSettings(), logger: logger);
        await service.StartAsync(CancellationToken.None);
        await clock.FirstTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = clock.Inner.GetUtcNow();
        wake.Signal("server2");
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "server2"), "failing pass was not retried");
        (clock.Inner.GetUtcNow() - before).ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        logger.Entries.ShouldContain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        service.ExecuteTask.IsFaulted.ShouldBeFalse();
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task the_real_directory_drives_the_loop_and_a_drained_runner_never_raises()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        var clock = new AlarmClock(DateTimeOffset.UtcNow);
        var wake = new AlarmWakeQueue();
        await using var host = await PhoneHomeTestHost.StartAsync(clock, configured: Pair(secretA, secretB), observer: wake);
        var storeA = Guid.NewGuid();
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", storeId: Guid.NewGuid(), secret: secretB);
        host.Directory.MarkRecovered(host.Directory.SnapshotLive("runner-a"));
        host.Directory.MarkRecovered(host.Directory.SnapshotLive("runner-b"));
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var exclusion = new FakeExclusion();
        exclusion.Reasons["runner-b"] = "draining";
        var state = new RunnerAlarmState();
        var graceTimerRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.AfterTimerRegistered = timer =>
        {
            if (timer.Due == TimeSpan.FromSeconds(180)
                && state.Current.Episodes.Any(episode => episode.RunnerId == "runner-a"))
                graceTimerRegistered.TrySetResult();
        };
        var services = Provider(schema.ConnectionString, host.Directory, exclusion, state, wake, new AlarmSettings(), clock);
        var service = new RunnerAlarmHostedService(services.GetRequiredService<IServiceScopeFactory>(), wake, state,
            Options.Create(new AlarmSettings()), clock, NullLogger<RunnerAlarmHostedService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        peerA.Socket.Abort();
        peerB.Socket.Abort();
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "runner-a")
            && state.Current.Episodes.All(episode => episode.RunnerId != "runner-b"), "directory signal did not open runner-a");
        await graceTimerRegistered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Inner.Advance(TimeSpan.FromSeconds(180));
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "runner-a" && episode.RaisedAt is not null)
            && state.Current.Episodes.All(episode => episode.RunnerId != "runner-b"), "drained runner raised");
        await using var again = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        host.Directory.MarkRecovered(host.Directory.SnapshotLive("runner-a"));
        await UntilAsync(() => state.Current.Episodes.All(episode => episode.RunnerId != "runner-a"), "recovery did not close runner-a");
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1671_the_loop_wakes_when_a_drain_passes_its_bound_and_raises_then_not_a_sweep_later()
    {
        // CARD-1671 (c): an offline draining runner must not stay hidden past the drain bound
        // until the next sweep; the loop wakes when the drain expires.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var start = DateTimeOffset.UtcNow;
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("drainer", "drainer", true, false, "socket_closed", start, 0));
        var exclusion = new FakeExclusion();
        exclusion.Reasons["drainer"] = "draining";
        exclusion.Expires["drainer"] = start.AddMinutes(30);
        var clock = new AlarmClock(start);
        var (service, state, _) = Build(schema.ConnectionString, source, clock, new AlarmSettings { SweepMinutes = 60 },
            exclusion: exclusion);
        await service.StartAsync(CancellationToken.None);
        await clock.FirstTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.Current.Episodes.ShouldBeEmpty();

        clock.Inner.Advance(TimeSpan.FromMinutes(30));
        await Task.Delay(100);
        state.Current.Episodes.ShouldBeEmpty("within the bound (inclusive) the drain still hides the outage");
        clock.Inner.Advance(TimeSpan.FromSeconds(1));
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "drainer" && episode.RaisedAt is not null),
            "the loop did not wake when the drain passed its bound");
        var raised = state.Current.Episodes.ShouldHaveSingleItem();
        raised.DownSince.ShouldBe(start);
        raised.RaisedAt!.Value.ShouldBeLessThanOrEqualTo(start.AddMinutes(30).AddSeconds(1));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1674_a_reachable_drain_past_its_bound_publishes_no_expiry_and_the_loop_does_not_wake_again()
    {
        // CARD-1674 A-1: a drain that overran must not keep publishing its (past) expiry, or the
        // loop wakes on it again and again; after the wake at the bound the next wake is the sweep.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var start = DateTimeOffset.UtcNow;
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("drainer", "drainer", true, true, null, null, 0));
        var exclusion = new FakeExclusion();
        exclusion.Reasons["drainer"] = "draining";
        exclusion.Expires["drainer"] = start.AddMinutes(30);
        var clock = new AlarmClock(start);
        var (service, state, _) = Build(schema.ConnectionString, source, clock, new AlarmSettings { SweepMinutes = 60 },
            exclusion: exclusion);
        await service.StartAsync(CancellationToken.None);
        await clock.FirstTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.Current.DrainExpiresAt.ShouldContainKey("drainer");
        var passes = source.Calls;

        clock.Inner.Advance(TimeSpan.FromMinutes(30).Add(TimeSpan.FromSeconds(1)));
        await UntilAsync(() => source.Calls == passes + 1, "the loop did not wake when the drain passed its bound");
        await UntilAsync(() => clock.Timers.Count >= 2, "the loop did not wait again after the wake");
        state.Current.DrainExpiresAt.ShouldBeEmpty("a drain past its bound has no expiry left to wake on");
        state.Current.Episodes.ShouldBeEmpty("a reachable draining runner never raises");
        await Task.Delay(200);
        source.Calls.ShouldBe(passes + 1, "the loop woke again without the clock moving");

        clock.Inner.Advance(TimeSpan.FromMinutes(29));
        await Task.Delay(200);
        source.Calls.ShouldBe(passes + 1, "the loop woke again before the sweep");
        clock.Timers.Last().Due.ShouldBe(TimeSpan.FromMinutes(30).Subtract(TimeSpan.FromSeconds(1)));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public void C1674_a_drain_expiry_not_after_the_last_pass_never_schedules_a_wake()
    {
        // CARD-1674 A-1: whatever the snapshot carries, the loop never waits less than its floor:
        // an expiry whose wake is at or before the last pass is stale and is ignored.
        var lastPass = DateTimeOffset.UtcNow;
        var nextSweep = lastPass.AddMinutes(15);
        var grace = TimeSpan.FromSeconds(180);
        var past = RunnerAlarmSnapshot.Empty with
        {
            DrainExpiresAt = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
            {
                ["long-gone"] = lastPass.AddMinutes(-10),
                ["at-the-pass"] = lastPass.AddSeconds(-1),
            },
        };

        var due = RunnerAlarmHostedService.NextDue(past, nextSweep, lastPass, grace);

        (due - lastPass).ShouldBe(TimeSpan.FromMinutes(15), "a stale drain expiry must not make the loop spin");

        var future = RunnerAlarmSnapshot.Empty with
        {
            DrainExpiresAt = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
            {
                ["stale"] = lastPass.AddMinutes(-10),
                ["live"] = lastPass.AddMinutes(5),
            },
        };
        RunnerAlarmHostedService.NextDue(future, nextSweep, lastPass, grace)
            .ShouldBe(lastPass.AddMinutes(5).AddSeconds(1), "a drain still within its bound wakes just past it");
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1678_one_pass_arms_one_timer_exactly_one_second_past_the_drain_bound()
    {
        // CARD-1678 (sweep 2deb2419 M71-6): the bound is inclusive, so the one wake is one second
        // past it. A wake at the bound itself finds the drain still hiding the outage and passes
        // again; the eventual raise alone cannot tell the two apart.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var start = DateTimeOffset.UtcNow;
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("drainer", "drainer", true, false, "socket_closed", start, 0));
        var exclusion = new FakeExclusion();
        exclusion.Reasons["drainer"] = "draining";
        exclusion.Expires["drainer"] = start.AddMinutes(30);
        var clock = new AlarmClock(start);
        var (service, state, _) = Build(schema.ConnectionString, source, clock, new AlarmSettings { SweepMinutes = 60 },
            exclusion: exclusion);
        await service.StartAsync(CancellationToken.None);
        await clock.FirstTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var passes = source.Calls;
        passes.ShouldBe(1, "the start pass runs once before the loop waits");
        clock.Timers.ShouldHaveSingleItem().Due.ShouldBe(TimeSpan.FromMinutes(30).Add(TimeSpan.FromSeconds(1)),
            "the timer is due exactly one second past the drain bound");

        clock.Inner.Advance(TimeSpan.FromMinutes(30));
        await Task.Delay(200);
        source.Calls.ShouldBe(passes, "no pass at the bound itself");
        clock.Timers.Count.ShouldBe(1, "no timer re-armed at the bound");
        state.Current.Episodes.ShouldBeEmpty();

        clock.Inner.Advance(TimeSpan.FromSeconds(1));
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "drainer" && episode.RaisedAt is not null),
            "the loop did not wake one second past the drain bound");
        source.Calls.ShouldBe(passes + 1, "one pass raises the overrun drain");
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1678_the_loop_wakes_one_second_past_the_earliest_of_several_drains()
    {
        // CARD-1678 (sweep 2deb2419 M71-8): with two drains the wake follows the one that ends
        // first; the later drain is still hidden when the earlier one raises.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var start = DateTimeOffset.UtcNow;
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("late", "late", true, false, "socket_closed", start, 0));
        source.Rows.Add(new RunnerEligibilitySnapshot("early", "early", true, false, "socket_closed", start, 0));
        var exclusion = new FakeExclusion();
        exclusion.Reasons["late"] = "draining";
        exclusion.Expires["late"] = start.AddMinutes(60);
        exclusion.Reasons["early"] = "draining";
        exclusion.Expires["early"] = start.AddMinutes(30);
        var clock = new AlarmClock(start);
        var (service, state, _) = Build(schema.ConnectionString, source, clock, new AlarmSettings { SweepMinutes = 120 },
            exclusion: exclusion);
        await service.StartAsync(CancellationToken.None);
        await clock.FirstTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.Current.DrainExpiresAt.Count.ShouldBe(2);
        clock.Timers.ShouldHaveSingleItem().Due.ShouldBe(TimeSpan.FromMinutes(30).Add(TimeSpan.FromSeconds(1)),
            "the timer is due one second past the earliest drain expiry");

        clock.Inner.Advance(TimeSpan.FromMinutes(30).Add(TimeSpan.FromSeconds(1)));
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "early" && episode.RaisedAt is not null),
            "the loop did not wake when the earliest drain passed its bound");
        state.Current.Episodes.ShouldNotContain(episode => episode.RunnerId == "late", "the later drain is still within its bound");
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public void C1678_NextDue_is_one_second_past_the_earliest_drain_bound()
    {
        // CARD-1678 (M71-6, M71-8): the pure schedule. One drain wakes exactly one second past its
        // bound; of several, the earliest wins whatever the dictionary order.
        var lastPass = DateTimeOffset.UtcNow;
        var nextSweep = lastPass.AddMinutes(120);
        var grace = TimeSpan.FromSeconds(180);
        var one = RunnerAlarmSnapshot.Empty with
        {
            DrainExpiresAt = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { ["only"] = lastPass.AddMinutes(30) },
        };
        RunnerAlarmHostedService.NextDue(one, nextSweep, lastPass, grace).ShouldBe(lastPass.AddMinutes(30).AddSeconds(1));

        foreach (var order in new[] { new[] { 60, 30, 45 }, new[] { 30, 60, 45 }, new[] { 45, 60, 30 } })
        {
            var several = RunnerAlarmSnapshot.Empty with
            {
                DrainExpiresAt = order.ToDictionary(minutes => $"drain-{minutes}", minutes => lastPass.AddMinutes(minutes),
                    StringComparer.Ordinal),
            };
            RunnerAlarmHostedService.NextDue(several, nextSweep, lastPass, grace)
                .ShouldBe(lastPass.AddMinutes(30).AddSeconds(1), $"order {string.Join(",", order)}");
        }
    }

    private static (RunnerAlarmHostedService Service, RunnerAlarmState State, AlarmWakeQueue Wake) Build(
        string connection, FakeEligibilitySource source, AlarmClock clock, AlarmSettings settings,
        RepositoryChildJournalInspector? journals = null, ListLogger<RunnerAlarmHostedService>? logger = null,
        FakeExclusion? exclusion = null)
    {
        var state = new RunnerAlarmState();
        var wake = new AlarmWakeQueue();
        var services = Provider(connection, source, exclusion ?? new FakeExclusion(), state, wake, settings, clock, journals);
        var service = new RunnerAlarmHostedService(services.GetRequiredService<IServiceScopeFactory>(), wake, state,
            Options.Create(settings), clock, logger ?? (Microsoft.Extensions.Logging.ILogger<RunnerAlarmHostedService>)NullLogger<RunnerAlarmHostedService>.Instance);
        return (service, state, wake);
    }

    private static ServiceProvider Provider(string connection, IRunnerEligibilitySnapshotSource source,
        FakeExclusion exclusion, RunnerAlarmState state, AlarmWakeQueue wake, AlarmSettings settings, TimeProvider clock,
        RepositoryChildJournalInspector? journals = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton(source);
        services.AddSingleton(exclusion);
        services.AddSingleton<IRunnerAlarmExclusion>(exclusion);
        services.AddSingleton(state);
        services.AddSingleton(wake);
        services.AddSingleton<IRunnerAlarmNotifier>(new RecordingNotifier());
        services.AddSingleton(new CompletionNoteFlushQueue());
        services.AddSingleton(journals ?? new RepositoryChildJournalInspector(new LandingGit()));
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton(clock);
        services.AddLogging();
        services.AddScoped<RunnerAlarmCoordinator>();
        return services.BuildServiceProvider();
    }

    private static async Task PlantDeadAsync(string common)
    {
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + 1;
        var path = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new RepositoryChildJournal.ChildRecord(1, common, Environment.ProcessId, ticks)));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
    }

    private static async Task UntilAsync(Func<bool> ready, string why)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (ready()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException(why);
    }

    private static PhoneHomeRunnerSettings Pair(string secretA, string secretB) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, Antiphon.Server.Application.Settings.PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = Entry(secretA),
            ["runner-b"] = Entry(secretB),
        },
    };

    private static Antiphon.Server.Application.Settings.PhoneHomeRunnerEntry Entry(string secret) => new()
    {
        Enabled = true, DisplayName = "runner", AllowDelegatedTasks = true, HostWorkspaceRoot = @"C:\work",
        RunnerWorkspace = "/work", RunnerRepository = "/work/repos/antiphon", CallbackOrigin = "https://antiphon.test",
        SharedSecret = secret, MaxCapacity = 4, ChildGrokHome = "/state/grok", ChildClaudeHome = "/state/claude",
        ChildCodexHome = "/state/codex",
    };

    private sealed class AlarmClock : TimeProvider
    {
        public FakeTimeProvider Inner { get; }
        public ConcurrentQueue<TimerMark> Timers { get; } = new();
        public TaskCompletionSource FirstTimer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<TimerMark>? AfterTimerRegistered { get; set; }
        public Action? AfterFirstTimerSignal { get; set; }
        public AlarmClock(DateTimeOffset start) => Inner = new FakeTimeProvider(start);
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = Inner.CreateTimer(callback, state, dueTime, period);
            var mark = new TimerMark(dueTime, period);
            Timers.Enqueue(mark);
            AfterTimerRegistered?.Invoke(mark);
            FirstTimer.TrySetResult();
            AfterFirstTimerSignal?.Invoke();
            return timer;
        }
        internal sealed record TimerMark(TimeSpan Due, TimeSpan Period);
    }

    private sealed class CountingInspector(ILandingGit git) : RepositoryChildJournalInspector(git)
    {
        public int Calls;
        public override async Task<JournalInspection> InspectAsync(string repository, TimeSpan staleAfter, DateTimeOffset now, CancellationToken ct)
        {
            Calls++;
            return await base.InspectAsync(repository, staleAfter, now, ct);
        }
    }
}
