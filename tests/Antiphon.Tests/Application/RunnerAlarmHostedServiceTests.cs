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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerAlarmHostedServiceTests
{
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
        await UntilAsync(() => source.Calls == snapshots + 1 && inspector.Calls == inspections + 1, "sweep did not run at 15 minutes");
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
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
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
        var services = Provider(schema.ConnectionString, host.Directory, exclusion, state, wake, new AlarmSettings(), clock);
        var service = new RunnerAlarmHostedService(services.GetRequiredService<IServiceScopeFactory>(), wake, state,
            Options.Create(new AlarmSettings()), clock, NullLogger<RunnerAlarmHostedService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        peerA.Socket.Abort();
        peerB.Socket.Abort();
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "runner-a")
            && state.Current.Episodes.All(episode => episode.RunnerId != "runner-b"), "directory signal did not open runner-a");
        clock.Advance(TimeSpan.FromSeconds(180));
        await UntilAsync(() => state.Current.Episodes.Any(episode => episode.RunnerId == "runner-a" && episode.RaisedAt is not null)
            && state.Current.Episodes.All(episode => episode.RunnerId != "runner-b"), "drained runner raised");
        await using var again = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        host.Directory.MarkRecovered(host.Directory.SnapshotLive("runner-a"));
        await UntilAsync(() => state.Current.Episodes.All(episode => episode.RunnerId != "runner-a"), "recovery did not close runner-a");
        await service.StopAsync(CancellationToken.None);
    }

    private static (RunnerAlarmHostedService Service, RunnerAlarmState State, AlarmWakeQueue Wake) Build(
        string connection, FakeEligibilitySource source, AlarmClock clock, AlarmSettings settings,
        RepositoryChildJournalInspector? journals = null, ListLogger<RunnerAlarmHostedService>? logger = null)
    {
        var state = new RunnerAlarmState();
        var wake = new AlarmWakeQueue();
        var services = Provider(connection, source, new FakeExclusion(), state, wake, settings, clock, journals);
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
        var deadline = DateTime.UtcNow.AddSeconds(5);
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
        public List<TimerMark> Timers { get; } = [];
        public TaskCompletionSource FirstTimer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AlarmClock(DateTimeOffset start) => Inner = new FakeTimeProvider(start);
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timers.Add(new TimerMark(dueTime, period));
            FirstTimer.TrySetResult();
            return Inner.CreateTimer(callback, state, dueTime, period);
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
