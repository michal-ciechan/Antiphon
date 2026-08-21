using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The warm-agent pool: settled delegates get reused instead of paying a cold start per task.
///
/// The load-bearing behaviours: a reused session's BEARER keeps working (the token hash moves to
/// the new task, because a live process's environment cannot change), unrelated work gets a focused
/// /compact BEFORE its brief, a busy pinned agent is waited for (delivering mid-task would corrupt
/// both correlations), and the janitor keeps "warm Claudes" a bounded trade, not a leak.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskPoolTests
{
    // ---- reuse -----------------------------------------------------------------------------

    [Test]
    public async Task a_queued_task_takes_over_a_warm_agent_in_its_directory()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        // >= 1: the shared fixture DB can carry stale queued tasks from other suites; the tick
        // legitimately dispatches those too. Everything below is scoped to THIS task.
        result.Dispatched.ShouldBeGreaterThanOrEqualTo(1);
        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.AgentId.ShouldBe(agentId, "the warm agent, not a fresh spawn");
        dispatched.AgentSessionId.ShouldBe(sessionId, "the LIVE session — no launch happened");

        var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
        agent.Status.ShouldBe(AgentStatus.Running);
        agent.PoolIdleSince.ShouldBeNull("claimed out of the pool");

        var brief = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId).ToListAsync();
        brief.ShouldContain(
            m => m.Body.Contains(DelegationReportFormatter.TaskMarker(task.Id)),
            "the brief rides the queue into the existing session");
    }

    [Test]
    public async Task a_warm_agent_reuses_only_a_matching_non_null_project_scope()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var projectId = await SeedProjectAsync();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3, projectId: projectId);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, projectId: projectId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldBe(agentId);
        dispatched.AgentSessionId.ShouldBe(sessionId);
    }

    [Test]
    public async Task a_warm_agent_from_project_a_is_not_reused_for_project_b()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var projectA = await SeedProjectAsync();
        var projectB = await SeedProjectAsync();
        var (warmAgentId, _) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3, projectId: projectA);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, projectId: projectB);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldNotBe(warmAgentId, "a project-A environment cannot serve project B");
        (await verify.Agents.SingleAsync(a => a.Id == warmAgentId)).Status.ShouldBe(AgentStatus.Idle);
        (await verify.Agents.SingleAsync(a => a.Id == dispatched.AgentId)).PoolProjectId.ShouldBe(projectB);
    }

    [Test]
    public async Task a_project_scoped_warm_agent_is_not_reused_for_an_unscoped_task()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var projectId = await SeedProjectAsync();
        var (warmAgentId, _) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3, projectId: projectId);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldNotBe(warmAgentId, "a project environment cannot serve global-only work");
        (await verify.Agents.SingleAsync(a => a.Id == dispatched.AgentId)).PoolProjectId.ShouldBeNull();
    }

    [Test]
    public async Task unrelated_work_compacts_the_session_first_focused_on_the_new_task()
    {
        // The reused context is only an asset for RELATED work. For unrelated work it is baggage —
        // shrink it down to whatever could still help, before the new brief lands.
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3);
        await SeedSettledTaskOnAsync(agentId, sessionId, workspace.Path, tokenHash: null);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, goal: "migrate the compose file to Postgres 18");

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var messages = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync();

        messages.Count.ShouldBe(2);
        messages[0].Body.ShouldStartWith("/compact");
        messages[0].Body.ShouldContain("migrate the compose file", customMessage:
            "the compaction is FOCUSED on the incoming work, not a generic squeeze");
        messages[0].Body.Contains('\n').ShouldBeFalse("a slash command must be a single line");
        messages[1].Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
    }

    [Test]
    public async Task a_follow_up_in_the_same_run_keeps_the_context_uncompacted()
    {
        // Same run = the old context IS the value being reused. Compacting it away would defeat
        // the entire point of sending follow-up work to the same agent.
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 0, reservedForRoot: null);
        var prior = await SeedSettledTaskOnAsync(agentId, sessionId, workspace.Path, tokenHash: null);
        await MarkReservedAsync(agentId, prior.RootTaskId);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, rootTaskId: prior.RootTaskId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).AgentId.ShouldBe(agentId);
        var messages = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId).ToListAsync();
        messages.Count.ShouldBe(1, "no /compact — the brief goes straight in");
        messages[0].Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
    }

    [Test]
    public async Task the_reused_sessions_bearer_token_now_resolves_to_the_new_task()
    {
        // A live process's environment cannot change, so the delegate keeps presenting the OLD
        // task's token. Without the rebind, a reused sub-orchestrator's children would all parent
        // to a settled task — silently corrupting lineage, budgets, and reply routing.
        using var workspace = new TempWorkspace();
        var (dispatcher, _, provider) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3);
        const string rawToken = "raw-token-held-by-the-live-session";
        var prior = await SeedSettledTaskOnAsync(
            agentId, sessionId, workspace.Path, tokenHash: AgentTaskService.HashToken(rawToken));
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var caller = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .AuthenticateAsync(rawToken, CancellationToken.None);
        caller.Task!.Id.ShouldBe(task.Id, "the bearer the session holds must mean the CURRENT work");

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == prior.Id)).TokenHash
            .ShouldBeNull("two rows resolving one bearer would make authentication ambiguous");
    }

    // ---- selection guards ------------------------------------------------------------------

    [Test]
    public async Task a_warm_agent_reserved_for_another_run_is_not_taken_inside_the_window()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, _) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 0, reservedForRoot: Guid.NewGuid());
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
        agent.PoolIdleSince.ShouldNotBeNull("still warm — reserved for the run that just used it");
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).AgentId
            .ShouldNotBe(agentId, "the task got a different (fresh) agent instead");
    }

    [Test]
    public async Task the_reservation_expires_and_the_agent_serves_anyone()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, _) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3, reservedForRoot: Guid.NewGuid());
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).AgentId
            .ShouldBe(agentId, "past the reservation window the pool is first come, first served");
    }

    [Test]
    public async Task a_warm_agent_at_the_wrong_tier_is_not_reused()
    {
        // --model was fixed at the warm agent's launch. Handing haiku work a warm fable (or the
        // reverse) would silently break the cost ladder the whole feature exists to enforce.
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, _) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Frontier, idleMinutes: 3);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Low);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).AgentId.ShouldNotBe(agentId);
    }

    [Test]
    public async Task a_pinned_follow_up_waits_while_its_agent_is_still_working()
    {
        // Delivering the follow-up now would land it BETWEEN the running task's turns and corrupt
        // both correlations — the task waits for the settle → pool handshake.
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, idleMinutes: 0);
        await using (var db = CreateContext())
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.Status = AgentStatus.Running;
            agent.PoolIdleSince = null;
            await db.SaveChangesAsync();
        }
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status
            .ShouldBe(AgentTaskStatus.Queued, "still queued — it runs when its agent goes warm");
    }

    [Test]
    public async Task a_pinned_warm_agent_with_a_mismatched_scope_relaunches_fresh_and_restamps()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var projectA = await SeedProjectAsync();
        var projectB = await SeedProjectAsync();
        var (agentId, oldSessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3, projectId: projectA);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, pinnedAgentId: agentId, projectId: projectB);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldBe(agentId, "the pin keeps its pool row");
        dispatched.AgentSessionId.ShouldNotBe(oldSessionId, "a scope mismatch requires a new process");
        (await verify.Agents.SingleAsync(a => a.Id == agentId)).PoolProjectId.ShouldBe(projectB);
        (await verify.SessionQueuedMessages.AnyAsync(m =>
            m.AgentSessionId == oldSessionId && m.Body.Contains(DelegationReportFormatter.TaskMarker(task.Id))))
            .ShouldBeFalse("the stale environment receives no brief");
    }

    // ---- the janitor -----------------------------------------------------------------------

    [Test]
    public async Task the_janitor_retires_agents_idle_past_the_ttl()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, _) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 10);

        var retired = await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        retired.ShouldBeGreaterThanOrEqualTo(1);
        stopper.Killed.ShouldContain(sessionId, "warmth is memory; a forgotten Claude is a leak");
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agentId)).ShouldBeFalse();
    }

    [Test]
    public async Task the_janitor_keeps_a_fresh_agent_warm()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, _) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 1);

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        stopper.Killed.ShouldNotContain(sessionId);
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agentId)).ShouldBeTrue();
    }

    [Test]
    public async Task the_janitor_enforces_the_per_directory_cap_oldest_first()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            // All inside the TTL; idleMinutes spread so "oldest" is unambiguous.
            var (id, _) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, idleMinutes: i);
            ids.Add(id);
        }

        var retired = await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        retired.ShouldBeGreaterThanOrEqualTo(2, "cap is 3 per directory — our two oldest must go");
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == ids[4])).ShouldBeFalse("idle 4 min — oldest");
        (await verify.Agents.AnyAsync(a => a.Id == ids[3])).ShouldBeFalse("idle 3 min");
        (await verify.Agents.AnyAsync(a => a.Id == ids[0])).ShouldBeTrue("freshest stays");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper, ServiceProvider Provider)
        CreateHarness()
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            // The fixture database is shared across suites; leftover Dispatched/Working rows from
            // other tests must never eat this harness's dispatch budget.
            MaxConcurrentTasks = 512,
        }));
        // A resolvable definition so the spawn path works when the pool declines a task.
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-pool-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper, provider);
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(
        string directory, AgentModelLevel level, int idleMinutes, Guid? reservedForRoot = null,
        Guid? projectId = null)
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
            Status = SessionStatus.Running,
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
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            ModelLevel = level,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-idleMinutes),
            PoolReservedForRootTaskId = reservedForRoot,
            PoolProjectId = projectId,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<Guid> SeedProjectAsync()
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"pool-scope-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/pool-scope.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(
        string directory,
        AgentModelLevel level,
        string goal = "do the next piece of work",
        Guid? rootTaskId = null,
        Guid? pinnedAgentId = null,
        Guid? projectId = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = rootTaskId ?? id,
            Title = goal,
            Goal = goal,
            Role = AgentTaskRole.Docs,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            ProjectId = projectId,
            AgentId = pinnedAgentId,
            Ephemeral = pinnedAgentId is null,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    /// <summary>A settled task this session already ran — the "previous work" a reuse compacts away.</summary>
    private static async Task<AgentTask> SeedSettledTaskOnAsync(
        Guid agentId, Guid sessionId, string directory, string? tokenHash)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "the previous work",
            Goal = "the previous work",
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Succeeded,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            DispatchedAt = DateTime.UtcNow.AddMinutes(-19),
            CompletedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task MarkReservedAsync(Guid agentId, Guid rootTaskId)
    {
        await using var db = CreateContext();
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        agent.PoolReservedForRootTaskId = rootTaskId;
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-pool-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
