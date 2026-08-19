using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0047 slice 4A — a task pinned to a STANDING agent is delivered into the session that agent
/// already has, instead of spawning a second one.
///
/// <para>The bug this replaces was real and quiet: every pinned agent that was not a pool delegate
/// fell through to <c>SpawnFresh</c>, which creates a new <c>AgentSession</c> and writes its id over
/// <see cref="Agent.PersistentSessionId"/>. For an <see cref="Agent.AlwaysOn"/> agent that points the
/// row at a session the supervisor never started while the supervised one keeps running — two
/// sessions for one agent, each of which the other's owner considers a stray.</para>
///
/// <para>The three properties that make the new path safe, each tested rather than intended:
/// the agent ROW is never written (not <c>PersistentSessionId</c>, not <c>Status</c>, not a Pool*
/// field — those belong to the supervisor and the pool respectively); work SERIALISES on the agent,
/// so a brief never lands between another task's turns; and settlement works exactly as it does for
/// a spawned delegate, driven here through the REAL turn-end trigger
/// (<see cref="AgentSessionRuntime.ObserveTranscriptAsync"/>) rather than by calling
/// <c>OnTurnEndAsync</c> by hand — the point being that the trigger fires at all for a session the
/// dispatcher did not launch.</para>
///
/// <para>Shares <c>[NotInParallel("AgentQueue")]</c> with the pool suite: both drive the global
/// <c>TickAsync</c>, and every assertion here is scoped to rows this suite created.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskStandingAgentDispatchTests
{
    // ---- a live standing agent takes the work ------------------------------------------------

    [Test]
    public async Task a_task_pinned_to_a_live_standing_agent_lands_in_the_session_it_already_has()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateHarness();
        var (agentId, sessionId) = await SeedStandingAgentAsync(workspace.Path, alwaysOn: true);
        var task = await SeedQueuedTaskAsync(workspace.Path, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.AgentId.ShouldBe(agentId);
        dispatched.AgentSessionId.ShouldBe(sessionId, "the LIVE session — no launch happened");
        dispatched.AgentName.ShouldBe("standing-specialist");

        (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace.Path))
            .ShouldBe(1, "a second session for one always-on agent is the whole bug");

        var brief = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId).ToListAsync();
        brief.ShouldContain(
            m => m.Body.Contains(DelegationReportFormatter.TaskMarker(task.Id)),
            "the brief rides the queue into the standing session");

        var events = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Dispatched)
            .ToListAsync();
        events.ShouldContain(e => e.Detail!.Contains("standing agent 'standing-specialist'"));
    }

    [Test]
    public async Task and_it_writes_nothing_at_all_on_the_agent_row()
    {
        // PersistentSessionId belongs to whoever launched the session (here, the supervisor);
        // Status and the Pool* fields belong to the pool. A standing agent is in neither's gift,
        // and a dispatcher that edits them is a dispatcher fighting the supervisor.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateHarness();
        var (agentId, sessionId) = await SeedStandingAgentAsync(workspace.Path, alwaysOn: true);
        var before = await AgentSnapshotAsync(agentId);
        var task = await SeedQueuedTaskAsync(workspace.Path, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        (await ReloadTaskAsync(task.Id)).Status.ShouldBe(
            AgentTaskStatus.Dispatched, "the control: this really did dispatch");
        var after = await AgentSnapshotAsync(agentId);
        after.PersistentSessionId.ShouldBe(sessionId.ToString("D"));
        after.ShouldBe(before, "not one field of the standing agent may move");
    }

    // ---- serialisation -------------------------------------------------------------------------

    [Test]
    public async Task a_busy_standing_agent_makes_the_next_task_wait_and_take_it_after_the_first_settles()
    {
        // One turn at a time. A second brief delivered now would land BETWEEN the running task's
        // turns, and both correlations would then read the wrong turn.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateHarness();
        var (agentId, sessionId) = await SeedStandingAgentAsync(workspace.Path, alwaysOn: true);
        // Explicit ordering: the tick takes queued tasks oldest-first, and "which of the two went"
        // must not depend on how coarse the clock happened to be between two inserts.
        var first = await SeedQueuedTaskAsync(workspace.Path, agentId, createdSecondsAgo: 60);
        var second = await SeedQueuedTaskAsync(workspace.Path, agentId, createdSecondsAgo: 30);

        await dispatcher.TickAsync(CancellationToken.None);

        var firstAfter = await ReloadTaskAsync(first.Id);
        var secondAfter = await ReloadTaskAsync(second.Id);
        firstAfter.Status.ShouldBe(AgentTaskStatus.Dispatched, "one of them goes");
        secondAfter.Status.ShouldBe(AgentTaskStatus.Queued, "and the other waits for the agent");
        secondAfter.AgentSessionId.ShouldBeNull("nothing was placed for it");

        // The first settles; the agent is free again.
        await MarkSettledAsync(first.Id);
        await dispatcher.TickAsync(CancellationToken.None);

        var secondNow = await ReloadTaskAsync(second.Id);
        secondNow.Status.ShouldBe(AgentTaskStatus.Dispatched, "the wait ends when the agent does");
        secondNow.AgentSessionId.ShouldBe(sessionId, "into the SAME standing session");

        await using var verify = CreateContext();
        (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace.Path))
            .ShouldBe(1, "two tasks, one standing session, zero launches");
    }

    [Test]
    public async Task a_dispatched_task_on_a_stopped_previous_session_does_not_occupy_the_live_one()
    {
        // CARD-0079: occupancy is the live composer, not "any Dispatched row on this agent".
        // The check interpreter sat behind a Dispatched task on a dead previous session for two
        // days while every new check waited 60s and fell back to the digest.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateHarness();
        var (agentId, liveSessionId) = await SeedStandingAgentAsync(workspace.Path, alwaysOn: true);
        var previousSessionId = await SeedStoppedSessionAsync(workspace.Path);
        await SeedDispatchedTaskAsync(workspace.Path, agentId, previousSessionId);
        var next = await SeedQueuedTaskAsync(workspace.Path, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        var dispatched = await ReloadTaskAsync(next.Id);
        dispatched.Status.ShouldBe(
            AgentTaskStatus.Dispatched, "a zombie on a previous session must not occupy");
        dispatched.AgentSessionId.ShouldBe(liveSessionId, "the next pin lands in the LIVE session");

        await using var verify = CreateContext();
        (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace.Path))
            .ShouldBe(2, "the stopped previous session is still there — we did not spawn a third");
    }

    // ---- no live session -----------------------------------------------------------------------

    [Test]
    public async Task an_always_on_agent_with_a_dead_session_keeps_the_task_queued_for_the_supervisor()
    {
        // The supervisor's sweep already ensures every AlwaysOn agent that is not user-suspended
        // has a live session. Spawning one here would race it and clobber PersistentSessionId — the
        // exact collision this slice exists to remove.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateHarness();
        var (agentId, deadSessionId) = await SeedStandingAgentAsync(
            workspace.Path, alwaysOn: true, sessionStatus: SessionStatus.Stopped);
        var task = await SeedQueuedTaskAsync(workspace.Path, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        (await ReloadTaskAsync(task.Id)).Status.ShouldBe(
            AgentTaskStatus.Queued, "it waits for the session to come back, it does not make one");

        await using var verify = CreateContext();
        (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace.Path))
            .ShouldBe(1, "no second session was spawned");
        (await verify.Agents.SingleAsync(a => a.Id == agentId)).PersistentSessionId
            .ShouldBe(deadSessionId.ToString("D"), "and the supervisor's pointer is intact");
    }

    [Test]
    public async Task a_standing_agent_that_nothing_supervises_still_gets_a_fresh_session()
    {
        // Regression pin on the OLD behaviour, which is still right here: nobody else is going to
        // bring this agent up, so the dispatcher does what it always did.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateHarness();
        var (agentId, deadSessionId) = await SeedStandingAgentAsync(
            workspace.Path, alwaysOn: false, sessionStatus: SessionStatus.Stopped);
        var task = await SeedQueuedTaskAsync(workspace.Path, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        var dispatched = await ReloadTaskAsync(task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.AgentSessionId.ShouldNotBe(deadSessionId, "a fresh session, as before");
        dispatched.AgentId.ShouldBe(agentId, "still ITS agent — pinning is honoured either way");

        await using var verify = CreateContext();
        (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace.Path)).ShouldBe(2);
    }

    // ---- settlement, through the real turn-end trigger ------------------------------------------

    /// <summary>
    /// The open question the amendment named (§4.3): does the turn-end trigger fire for a standing
    /// agent's session in every path it fires for a spawned delegate? It is keyed on the session and
    /// the task→session mapping, both of which the standing path now sets — so it should, and this
    /// is the evidence. Deliberately driven through <see cref="AgentSessionRuntime.ObserveTranscriptAsync"/>,
    /// the ingestion path the runner's event pump actually uses: calling
    /// <c>AgentTaskReplyService.OnTurnEndAsync</c> directly would prove the settlement logic while
    /// assuming away the only thing in doubt.
    /// </summary>
    [Test]
    public async Task a_marked_turn_on_the_standing_session_settles_the_pinned_task_and_leaves_the_agent_alone()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, provider) = CreateHarness();
        var (agentId, sessionId) = await SeedStandingAgentAsync(workspace.Path, alwaysOn: true);
        var before = await AgentSnapshotAsync(agentId);
        var task = await SeedQueuedTaskAsync(workspace.Path, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);
        (await ReloadTaskAsync(task.Id)).AgentSessionId.ShouldBe(sessionId);

        // The specialist reads its brief and answers, ending a turn — as the tailer emits it.
        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        foreach (var entry in Turn(
            sessionId,
            prompt: DelegationReportFormatter.TaskMarker(task.Id) + "\n\nRead the bundle.",
            reply: "Producing: two commits in the last window. Looks healthy."))
        {
            await runtime.ObserveTranscriptAsync(entry, CancellationToken.None);
        }

        var settled = await ReloadTaskAsync(task.Id);
        settled.Status.ShouldBe(
            AgentTaskStatus.Succeeded,
            "the turn-end trigger fires for a session the dispatcher did not launch");
        settled.Result.ShouldContain("two commits in the last window");
        settled.CompletedAt.ShouldNotBeNull();

        // And no pool handshake: ReleaseDelegateAsync filters on IsPoolDelegate, so a standing
        // agent is never pooled, never reserved, and above all never REMOVED when its task settles.
        var after = await AgentSnapshotAsync(agentId);
        after.ShouldBe(before, "settling someone else's work must not touch a standing agent");
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agentId))
            .ShouldBeTrue("the janitor's delete path must never reach a standing agent");
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Every field the dispatcher or the pool could plausibly write, in one comparison.</summary>
    private sealed record AgentRowSnapshot(
        string? PersistentSessionId, AgentStatus Status, DateTime? PoolIdleSince,
        Guid? PoolReservedForRootTaskId, bool IsPoolDelegate, bool AlwaysOn, string WorkingDirectory);

    private static async Task<AgentRowSnapshot> AgentSnapshotAsync(Guid agentId)
    {
        await using var db = CreateContext();
        var a = await db.Agents.AsNoTracking().SingleAsync(x => x.Id == agentId);
        return new AgentRowSnapshot(
            a.PersistentSessionId, a.Status, a.PoolIdleSince, a.PoolReservedForRootTaskId,
            a.IsPoolDelegate, a.AlwaysOn, a.WorkingDirectory);
    }

    private static async Task<AgentTask> ReloadTaskAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
    }

    private static async Task MarkSettledAsync(Guid taskId)
    {
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Succeeded;
        task.Result = "Done.";
        task.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>One complete turn as the tailer emits it: prompt, the response's text, its TurnEnd.</summary>
    private static SessionRunnerTranscriptEvent[] Turn(Guid sessionId, string prompt, string reply)
    {
        var api = $"msg_{Guid.NewGuid():N}";
        var tag = Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow;
        return
        [
            new(sessionId, 1, TranscriptKinds.UserPrompt, $"{tag}-p", null, at, "user",
                prompt, null, null, null, null, null, null),
            new(sessionId, 2, TranscriptKinds.AssistantText, $"{tag}-t", null, at.AddSeconds(1), "assistant",
                reply, null, null, null, null, null, api),
            new(sessionId, 3, TranscriptKinds.TurnEnd, $"{tag}-t", null, at.AddSeconds(2), "assistant",
                null, null, null, null, null, "end_turn", api),
        ];
    }

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            // The fixture database is shared across suites; leftover Dispatched/Working rows from
            // other tests must never eat this harness's dispatch budget.
            MaxConcurrentTasks = 512,
            // A standing agent is not pool furniture, so the janitor has no business near it — but
            // the sweep is global over the shared database, so keep it away from other suites' rows
            // too rather than letting this harness's tick retire them.
            PoolIdleRetireMinutes = 525_600,
            PoolMaxIdlePerDirectory = int.MaxValue,
            // No stall/timeout policy: this suite's tasks must move only because of what it does.
            RolePolicy = new(StringComparer.OrdinalIgnoreCase),
            FinalMessageGraceSeconds = 0,
            SubagentGraceMinutes = 0,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<ISessionRunnerClient, BridgeQueueHarness.EmptyRunnerClient>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-standing-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        // The settlement half of the end-to-end test: registered so the runtime's turn-end flush
        // can resolve it exactly as the server does.
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }

    private static async Task<Guid> SeedStoppedSessionAsync(string directory)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Stopped,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now.AddDays(-2),
            StartedAt = now.AddDays(-2),
            LastSeenAt = now.AddDays(-2),
            EndedAt = now.AddDays(-2),
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static async Task<AgentTask> SeedDispatchedTaskAsync(
        string directory, Guid agentId, Guid sessionId)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow.AddDays(-2);
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "zombie interpretation",
            Goal = "zombie interpretation",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ReplyTo = AgentTaskReplyTo.None,
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Ephemeral = false,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = now,
            DispatchedAt = now,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedStandingAgentAsync(
        string directory, bool alwaysOn, SessionStatus sessionStatus = SessionStatus.Running)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = sessionStatus,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "standing-specialist",
            // Unique per test, because the slug is uniquely indexed and the fixture DB is shared.
            Slug = $"standing-{agentId:N}"[..20],
            WorkingDirectory = directory,
            Details = "A standing agent nobody pooled.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Low,
            AlwaysOn = alwaysOn,
            RemoteControlEnabled = false,
            IsPoolDelegate = false,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(
        string directory, Guid pinnedAgentId, int createdSecondsAgo = 0)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "interpret this bundle",
            Goal = "interpret this bundle",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ReplyTo = AgentTaskReplyTo.None,
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentId = pinnedAgentId,
            Ephemeral = false,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow.AddSeconds(-createdSecondsAgo),
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-standing-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
