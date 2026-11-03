using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1663. The SessionRunnerStates-backed alarm exclusion: a retired or draining runner raises
/// no RunnerUnavailable alarm and its open alarm resolves, while an active runner still alarms.
/// CARD-1666: a draining runner offline past <c>Alarms:RunnerDrainMaxMinutes</c> alarms again.
/// </summary>
[Category("Integration")]
public sealed class RunnerStateAlarmExclusionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 1, 2, 3, TimeSpan.Zero);

    [Test]
    public async Task C1663_an_active_runner_that_goes_down_still_raises()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Settings("fresh", "cleared"));
        // A retirement that drain/clear lifted is an active runner again.
        host.Directory.ApplyState("cleared", State(draining: true, retired: true));
        host.Directory.ApplyState("cleared", State(draining: false, retired: false));
        await using var db = Context(schema);
        var state = new RunnerAlarmState();
        var exclusion = Exclusion(host);
        var coordinator = Build(host, db, state, exclusion);

        exclusion.Excluded("fresh").ShouldBeNull();
        exclusion.Excluded("cleared").ShouldBeNull();
        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);

        state.Current.Episodes.Select(episode => episode.RunnerId).ShouldBe(["fresh", "cleared"], ignoreOrder: true);
        state.Current.Episodes.ShouldAllBe(episode => episode.RaisedAt == T0.AddSeconds(180));
    }

    [Test]
    public async Task C1663_a_retired_or_draining_runner_raises_no_alarm()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using (var seed = Context(schema))
        {
            // The live server2-temp row: drained, then retired when idle.
            seed.SessionRunnerStates.Add(new SessionRunnerState
            {
                RunnerId = "server2-temp", Draining = true, DrainedAt = T0, DrainReason = "rolling upgrade complete",
                RedirectTo = "server2", RetireWhenIdle = true, RetiredAt = T0.AddMinutes(51), RetireReason = "idle",
                UpdatedAt = T0.AddMinutes(51),
            });
            seed.SessionRunnerStates.Add(new SessionRunnerState
            {
                RunnerId = "draining", Draining = true, DrainedAt = T0, DrainReason = "rolling", UpdatedAt = T0,
            });
            await seed.SaveChangesAsync();
        }

        await using var host = await PhoneHomeTestHost.StartAsync(
            connectionString: schema.ConnectionString, configured: Settings("server2-temp", "draining"));
        await new RunnerStateLoader(host.App.Services.GetRequiredService<IServiceScopeFactory>(), host.Directory)
            .StartAsync(CancellationToken.None);
        await using var db = Context(schema);
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        var exclusion = Exclusion(host);
        var coordinator = Build(host, db, state, exclusion, notifier);

        exclusion.Excluded("server2-temp").ShouldBe("retired");
        exclusion.Excluded("draining").ShouldBe("draining");
        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddMinutes(29), CancellationToken.None);

        state.Current.Episodes.ShouldBeEmpty();
        notifier.Notes.ShouldBeEmpty();

        // CARD-1666: a retired runner has no bound; a drain this old has overrun its bound.
        await coordinator.EvaluateRunnersAsync(T0.AddDays(3), CancellationToken.None);
        state.Current.Episodes.ShouldNotContain(episode => episode.RunnerId == "server2-temp");
    }

    [Test]
    public async Task C1666_a_draining_runner_offline_past_the_bound_raises_with_a_drain_overran_note()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Settings("stuck"));
        host.Directory.ApplyState("stuck", State(draining: true, retired: false));
        await using var db = Context(schema);
        var caller = await SeedPinnedAsync(db, "stuck");
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        var coordinator = Build(host, db, state, Exclusion(host), notifier);

        // Within the 30 min default bound (inclusive) the offline draining runner stays silent.
        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddMinutes(20), CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddMinutes(30), CancellationToken.None);
        state.Current.Episodes.ShouldBeEmpty();
        notifier.Notes.ShouldBeEmpty();

        // Past the bound the ordinary outage episode opens and raises after the usual grace.
        await coordinator.EvaluateRunnersAsync(T0.AddMinutes(31), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldBeNull();
        await coordinator.EvaluateRunnersAsync(T0.AddMinutes(31).AddSeconds(180), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldBe(T0.AddMinutes(31).AddSeconds(180));
        var note = notifier.Notes.ShouldHaveSingleItem();
        note.SessionId.ShouldBe(caller);
        note.Header.ShouldBe("[runner stuck unavailable]");
        note.Body.ShouldContain("drain overran");
        note.Body.ShouldContain("Alarms:RunnerDrainMaxMinutes");
        note.Body.ShouldContain(T0.ToString("O"));
    }

    [Test]
    public async Task C1666_a_draining_runner_with_no_drain_start_fails_safe_and_raises()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Settings("unknown"));
        host.Directory.ApplyState("unknown", new RunnerState(true, null, "rolling", null, false, null, null, null));
        await using var db = Context(schema);
        await SeedPinnedAsync(db, "unknown");
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        var coordinator = Build(host, db, state, Exclusion(host), notifier);

        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);

        state.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldBe(T0.AddSeconds(180));
        var note = notifier.Notes.ShouldHaveSingleItem();
        note.Body.ShouldContain("drain overran");
        note.Body.ShouldContain("drain start is unknown");
    }

    [Test]
    public void C1671_a_drain_start_in_the_future_fails_safe_and_counts_as_overrun()
    {
        // CARD-1671 (b): a drain start after now (clock skew, bad row) cannot be aged, so it alarms.
        var future = new RunnerState(true, T0.AddMinutes(5), "rolling", null, false, null, null, null);
        var note = RunnerStateAlarmExclusion.Overran(future, T0, 30).ShouldNotBeNull();
        note.ShouldContain("drain overran");
        note.ShouldContain("drain start is unknown");
        RunnerStateAlarmExclusion.Overran(future, T0.AddMinutes(5), 30).ShouldBeNull("a start equal to now is a fresh drain");
        RunnerStateAlarmExclusion.Overran(State(draining: true, retired: false), T0.AddMinutes(30), 30).ShouldBeNull();
    }

    [Test]
    public void C1671_a_drain_expires_at_its_start_plus_the_bound_and_a_retirement_never_expires()
    {
        // CARD-1671 (c): the instant the alarm loop must wake for; retired or unstarted drains have none.
        RunnerStateAlarmExclusion.ExpiresAt(State(draining: true, retired: false), 30).ShouldBe(T0.AddMinutes(30));
        RunnerStateAlarmExclusion.ExpiresAt(State(draining: true, retired: false), 45).ShouldBe(T0.AddMinutes(45));
        RunnerStateAlarmExclusion.ExpiresAt(State(draining: true, retired: true), 30).ShouldBeNull();
        RunnerStateAlarmExclusion.ExpiresAt(State(draining: false, retired: false), 30).ShouldBeNull();
        RunnerStateAlarmExclusion.ExpiresAt(new RunnerState(true, null, "rolling", null, false, null, null, null), 30).ShouldBeNull();
        RunnerStateAlarmExclusion.ExpiresAt(null, 30).ShouldBeNull();
    }

    [Test]
    public async Task C1663_an_open_alarm_resolves_when_the_runner_retires()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Settings("server2-temp", "server2"));
        await using var db = Context(schema);
        var state = new RunnerAlarmState();
        var coordinator = Build(host, db, state, Exclusion(host));
        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        state.Current.Episodes.Count.ShouldBe(2);
        state.Current.Episodes.ShouldAllBe(episode => episode.RaisedAt != null);

        host.Directory.ApplyState("server2-temp", State(draining: true, retired: true));
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(181), CancellationToken.None);

        state.Current.Episodes.ShouldHaveSingleItem().RunnerId.ShouldBe("server2", "the active runner keeps its alarm");
    }

    private static RunnerAlarmCoordinator Build(PhoneHomeTestHost host, AppDbContext db, RunnerAlarmState state,
        RunnerStateAlarmExclusion exclusion, RecordingNotifier? notifier = null) =>
        new(host.Directory, exclusion, state, new RepositoryChildJournalInspector(new LandingGit()), db,
            notifier ?? new RecordingNotifier(), new CompletionNoteFlushQueue(), Options.Create(new AlarmSettings()),
            new ListLogger<RunnerAlarmCoordinator>());

    private static RunnerStateAlarmExclusion Exclusion(PhoneHomeTestHost host) =>
        new(host.Directory, Options.Create(new AlarmSettings()));

    private static async Task<Guid> SeedPinnedAsync(AppDbContext db, string runnerId)
    {
        var caller = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = caller, Status = SessionStatus.Running, RunnerId = runnerId, RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work", Cwd = "/work", DefinitionName = "test", CreatedAt = now, StartedAt = now, LastSeenAt = now,
        });
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "pinned", Goal = "goal", Role = AgentTaskRole.Code,
            Status = AgentTaskStatus.Working, RunnerId = runnerId, ParentSessionId = caller,
            ReplyTo = AgentTaskReplyTo.Session, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared, WorkingDirectory = Path.GetTempPath(), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return caller;
    }

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static RunnerState State(bool draining, bool retired) =>
        new(draining, draining ? T0 : null, draining ? "rolling" : null, null, false, null,
            retired ? T0.AddMinutes(51) : null, retired ? "idle" : null);

    private static PhoneHomeRunnerSettings Settings(params string[] ids) => new()
    {
        Enabled = true,
        Runners = ids.ToDictionary(id => id, id => new PhoneHomeRunnerEntry
        {
            Enabled = true,
            AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\work",
            RunnerWorkspace = "/work/" + id,
            RunnerRepository = "/work/repos/antiphon",
            CallbackOrigin = "https://antiphon.test",
            SharedSecret = "secret-" + id,
            MaxCapacity = 3,
        }, StringComparer.OrdinalIgnoreCase),
    };
}
