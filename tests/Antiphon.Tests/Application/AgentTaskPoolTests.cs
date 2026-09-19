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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
[NotInParallel]
public class AgentTaskPoolTests
{
    [Test]
    public async Task a_dispatched_pool_delegate_remains_boardless_after_agent_board_backfill()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var worktreePath = Path.Combine(workspace.Path, "worktrees", $"card-task-{shortId}");
        var task = await SeedQueuedTaskAsync(
            workspace.Path,
            AgentModelLevel.Medium,
            worktreePath: worktreePath);

        await dispatcher.TickAsync(CancellationToken.None);

        await using (var db = CreateContext())
        {
            var dispatched = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            dispatched.AgentId.ShouldNotBeNull();
            var poolAgent = await db.Agents.SingleAsync(a => a.Id == dispatched.AgentId!.Value);
            poolAgent.IsPoolDelegate.ShouldBeTrue();
            poolAgent.BoardId.ShouldBeNull();

            await CreateAgentService(db).EnsureAgentBoardsAsync(CancellationToken.None);
        }

        await using var verify = CreateContext();
        var storedTask = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        (await verify.Agents.SingleAsync(a => a.Id == storedTask.AgentId!.Value)).BoardId.ShouldBeNull();
        var expectedBoardName = $"task-{DelegationReportFormatter.Short(task.Id)}";
        (await verify.Boards.AnyAsync(b => b.Name == expectedBoardName)).ShouldBeFalse();
        (await verify.Projects.AnyAsync(p => p.LocalRepositoryPath == worktreePath)).ShouldBeFalse();
    }

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
    public async Task a_fresh_pool_spawn_stamps_inherited_env_onto_the_pool_agent()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var inherited = AgentLaunchEnv.Serialize(new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
            ["ANTHROPIC_BASE_URL"] = "http://localhost:10746",
        });
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, inheritedLaunchEnvJson: inherited);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldNotBeNull();
        var poolAgent = await verify.Agents.SingleAsync(a => a.Id == dispatched.AgentId!.Value);
        poolAgent.IsPoolDelegate.ShouldBeTrue();
        AgentLaunchEnv.Parse(poolAgent.LaunchEnvJson)["X_LLM_PROJECT"].ShouldBe("PredictionMarkets");
        AgentLaunchEnv.Parse(poolAgent.LaunchEnvJson)["ANTHROPIC_BASE_URL"].ShouldBe("http://localhost:10746");
    }

    [Test]
    public async Task a_warm_agent_is_not_reused_when_inherited_env_differs()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (warmAgentId, _) = await SeedWarmAgentAsync(
            workspace.Path,
            AgentModelLevel.Medium,
            idleMinutes: 3,
            launchEnvJson: AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "PredictionMarkets",
            }));
        var task = await SeedQueuedTaskAsync(
            workspace.Path,
            AgentModelLevel.Medium,
            inheritedLaunchEnvJson: AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "Other",
            }));

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldNotBe(warmAgentId, "a process launched for one caller env cannot serve another");
        (await verify.Agents.SingleAsync(a => a.Id == warmAgentId)).Status.ShouldBe(AgentStatus.Idle);
        AgentLaunchEnv.Parse(
            (await verify.Agents.SingleAsync(a => a.Id == dispatched.AgentId)).LaunchEnvJson)
            ["X_LLM_PROJECT"].ShouldBe("Other");
    }

    [Test]
    public async Task unrelated_work_compacts_the_session_first_focused_on_the_new_task()
    {
        // The reused context is only an asset for RELATED work. For unrelated work it is baggage —
        // shrink it down to whatever could still help, before the new brief lands.
        // CARD-0117 S3: pinned to ClaudeCode so this is not silently vacuous when Codex/Grok
        // stop receiving a typed /compact.
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3, kind: AgentKind.ClaudeCode);
        await SeedSettledTaskOnAsync(agentId, sessionId, workspace.Path, tokenHash: null);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, goal: "migrate the compose file to Postgres 18",
            kind: AgentKind.ClaudeCode);

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
        messages[1].Body.ShouldNotContain(DelegationReportFormatter.UnrelatedWorkRefocusLine);
    }

    [Test]
    public async Task unrelated_codex_reuse_enqueues_one_marked_brief_with_the_refocus_line()
    {
        // CARD-0117 S3: Codex records a typed /compact as a work turn (session 51ee57fc). The
        // reuse path must not send one; the refocus note folds into the marked brief instead.
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 3, kind: AgentKind.Codex);
        await SeedSettledTaskOnAsync(agentId, sessionId, workspace.Path, tokenHash: null);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, goal: "migrate the compose file to Postgres 18",
            kind: AgentKind.Codex);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).AgentSessionId.ShouldBe(sessionId);
        var messages = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync();

        messages.Count.ShouldBe(1, "Codex reuse is one marked brief, not compact-then-brief");
        messages[0].Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
        messages[0].Body.ShouldNotStartWith("/");
        messages.ShouldNotContain(m => m.Body.StartsWith("/compact"));
        // D2: the refocus line lives in BuildBrief, never in the pointer. Codex's conservative
        // spill means the queued body is usually the pointer; the spill file is BuildBrief.
        if (messages[0].Body.Contains("YOUR BRIEF IS NOT IN THIS MESSAGE", StringComparison.Ordinal))
        {
            var spill = Path.Combine(
                workspace.Path, ".antiphon",
                $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
            File.Exists(spill).ShouldBeTrue();
            File.ReadAllText(spill).ShouldContain(DelegationReportFormatter.UnrelatedWorkRefocusLine);
        }
        else
        {
            messages[0].Body.ShouldContain(DelegationReportFormatter.UnrelatedWorkRefocusLine);
        }
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
        var pinned = await verify.Agents.SingleAsync(a => a.Id == agentId);
        var held = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync();
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldBe(DispatchHoldDetails.PinnedAgentNoOpenTask(pinned.Name, pinned.Status));
    }

    // ---- CARD-0537: WaitForAgent is a traced hold -------------------------------------------

    [Test]
    public async Task a_follow_up_behind_a_blocked_task_writes_one_held_event_naming_it()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var cs = schema.ConnectionString;
        var (dispatcher, stopper, _) = CreateHarness(connectionString: cs);
        var seeded = await SeedFollowUpBehindBlockedAsync(workspace.Path, cs);

        AgentTaskDispatcher.TickResult? last = null;
        for (var i = 0; i < 3; i++)
            last = await dispatcher.TickAsync(CancellationToken.None);

        last!.Dispatched.ShouldBe(0);
        stopper.Killed.ShouldBeEmpty();
        await using var verify = CreateContext(cs);
        (await verify.AgentTasks.SingleAsync(t => t.Id == seeded.FollowUp.Id)).Status
            .ShouldBe(AgentTaskStatus.Queued);
        var held = await HeldAsync(verify, seeded.FollowUp.Id);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldContain(seeded.AgentName);
        held[0].Detail.ShouldContain(DelegationReportFormatter.Short(seeded.Blocked.Id));
        held[0].Detail.ShouldContain("Blocked");
        held[0].Detail.ShouldContain("-Reply");
        held[0].Detail.ShouldBe(DispatchHoldDetails.PinnedAgentParkedOn(
            seeded.AgentName, DelegationReportFormatter.Short(seeded.Blocked.Id), AgentTaskStatus.Blocked));
    }

    [Test]
    public async Task the_hold_retraces_on_reply_and_releases_on_settle()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var cs = schema.ConnectionString;
        var (dispatcher, _, _) = CreateHarness(connectionString: cs);
        var seeded = await SeedFollowUpBehindBlockedAsync(workspace.Path, cs);

        await dispatcher.TickAsync(CancellationToken.None);

        await using (var db = CreateContext(cs))
        {
            var blocked = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Blocked.Id);
            blocked.Status = AgentTaskStatus.Working;
            await db.SaveChangesAsync();
        }

        await dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(cs))
        {
            var held = await HeldAsync(verify, seeded.FollowUp.Id);
            held.Count.ShouldBe(2);
            held[^1].Detail.ShouldContain("Working");
        }

        await dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(cs))
            (await HeldAsync(verify, seeded.FollowUp.Id)).Count.ShouldBe(2);

        var now = DateTime.UtcNow;
        await using (var db = CreateContext(cs))
        {
            var blocked = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Blocked.Id);
            blocked.Status = AgentTaskStatus.Succeeded;
            var agent = await db.Agents.SingleAsync(a => a.Id == seeded.AgentId);
            agent.Status = AgentStatus.Idle;
            agent.PoolIdleSince = now;
            agent.PoolReservedForRootTaskId = blocked.RootTaskId;
            await db.SaveChangesAsync();
        }

        var tick = await dispatcher.TickAsync(CancellationToken.None);
        tick.Dispatched.ShouldBe(1);
        await using var after = CreateContext(cs);
        (await after.AgentTasks.SingleAsync(t => t.Id == seeded.FollowUp.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched);
        (await HeldAsync(after, seeded.FollowUp.Id)).Count.ShouldBe(2);
        var dispatched = await after.AgentTaskEvents
            .SingleAsync(e => e.AgentTaskId == seeded.FollowUp.Id && e.Type == AgentTaskEventType.Dispatched);
        dispatched.Detail.ShouldStartWith("Reused warm delegate");
    }

    [Test]
    public async Task a_pinned_agent_running_with_no_open_task_is_a_named_hold()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var cs = schema.ConnectionString;
        var (dispatcher, _, _) = CreateHarness(connectionString: cs);
        var (agentId, _) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 0, connectionString: cs);
        string agentName;
        await using (var db = CreateContext(cs))
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.Status = AgentStatus.Running;
            agent.PoolIdleSince = null;
            agentName = agent.Name;
            await db.SaveChangesAsync();
        }

        var followUp = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, pinnedAgentId: agentId, connectionString: cs);

        for (var i = 0; i < 3; i++)
            await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(cs);
        var held = await HeldAsync(verify, followUp.Id);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldBe(DispatchHoldDetails.PinnedAgentNoOpenTask(agentName, AgentStatus.Running));
    }

    [Test]
    public async Task a_busy_standing_agent_writes_one_held_event()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var cs = schema.ConnectionString;
        var (dispatcher, _, _) = CreateHarness(connectionString: cs);
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            workspace.Path, AgentModelLevel.Medium, idleMinutes: 0, connectionString: cs);
        AgentTask busy;
        string agentName;
        await using (var db = CreateContext(cs))
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.IsPoolDelegate = false;
            agent.Status = AgentStatus.Running;
            agent.AlwaysOn = true;
            agentName = agent.Name;
            busy = new AgentTask
            {
                Id = Guid.NewGuid(),
                RootTaskId = Guid.NewGuid(),
                Title = "standing occupant",
                Goal = "standing occupant",
                Role = AgentTaskRole.Docs,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Working,
                DispatchedAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow.AddMinutes(-6),
            };
            busy.RootTaskId = busy.Id;
            db.AgentTasks.Add(busy);
            await db.SaveChangesAsync();
        }

        var followUp = await SeedQueuedTaskAsync(
            workspace.Path, AgentModelLevel.Medium, pinnedAgentId: agentId, connectionString: cs);

        for (var i = 0; i < 3; i++)
            await dispatcher.TickAsync(CancellationToken.None);

        await using (var verify = CreateContext(cs))
        {
            var held = await HeldAsync(verify, followUp.Id);
            held.Count.ShouldBe(1);
            held[0].Detail.ShouldBe(DispatchHoldDetails.StandingAgentBusy(
                agentName, DelegationReportFormatter.Short(busy.Id), AgentTaskStatus.Working));
        }

        await using (var db = CreateContext(cs))
        {
            var occupant = await db.AgentTasks.SingleAsync(t => t.Id == busy.Id);
            occupant.Status = AgentTaskStatus.Succeeded;
            occupant.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var tick = await dispatcher.TickAsync(CancellationToken.None);
        tick.Dispatched.ShouldBe(1);
        await using var after = CreateContext(cs);
        (await after.AgentTasks.SingleAsync(t => t.Id == followUp.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched);
        var dispatched = await after.AgentTaskEvents
            .SingleAsync(e => e.AgentTaskId == followUp.Id && e.Type == AgentTaskEventType.Dispatched);
        dispatched.Detail.ShouldStartWith("Delivered into standing agent");
    }

    [Test]
    public async Task a_pinned_agent_wait_escalates_through_held_aged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var cs = schema.ConnectionString;
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        var (dispatcher, _, _) = CreateHarness(clock, cs);
        var seeded = await SeedFollowUpBehindBlockedAsync(workspace.Path, cs);

        await dispatcher.TickAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(299));
        await dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(cs))
            (await AgedAsync(verify, seeded.FollowUp.Id)).Count.ShouldBe(0);

        clock.Advance(TimeSpan.FromSeconds(1));
        await dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(cs))
        {
            var aged = await AgedAsync(verify, seeded.FollowUp.Id);
            aged.Count.ShouldBe(1);
            aged[0].Detail.ShouldStartWith(DispatchHoldDetails.WarningPrefix);
            aged[0].Detail.ShouldContain(seeded.AgentName);
        }

        clock.Advance(TimeSpan.FromSeconds(600));
        await dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(cs))
        {
            var aged = await AgedAsync(verify, seeded.FollowUp.Id);
            aged.Count.ShouldBe(2);
            aged.ShouldContain(e => e.Detail.StartsWith(DispatchHoldDetails.ErrorPrefix, StringComparison.Ordinal));
        }

        clock.Advance(TimeSpan.FromSeconds(100));
        await dispatcher.TickAsync(CancellationToken.None);
        await using var after = CreateContext(cs);
        (await AgedAsync(after, seeded.FollowUp.Id)).Count.ShouldBe(2);
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

    // ---- CARD-0221: recovered Worktree delegates are Idle for the janitor, never reused ----

    [Test]
    public async Task a_recovered_worktree_task_marks_its_delegate_for_retirement()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, provider) = CreateHarness();
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var worktreePath = Path.Combine(workspace.Path, "worktrees", $"card-task-{shortId}");
        Directory.CreateDirectory(worktreePath);
        var (agentId, sessionId, taskId) = await SeedDispatchedPoolTaskAsync(
            workspace.Path,
            WorkspaceMode.Worktree,
            worktreePath);

        var replies = provider.GetRequiredService<AgentTaskReplyService>();
        await replies.RecoverFromBindRefusalAsync(
            taskId,
            new DelegateBindRefusalEvidence(["abc1234"], null),
            CancellationToken.None);

        await using var verify = CreateContext();
        var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
        agent.Status.ShouldBe(AgentStatus.Idle, "CARD-0221: the janitor query is Idle + PoolIdleSince");
        agent.PoolIdleSince.ShouldNotBeNull();
        agent.PoolReservedForRootTaskId.ShouldBeNull("not claimable — this is retirement, not a warm pool");
        (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId))
            .Status.ShouldBe(SessionStatus.Running, "CARD-0085: do not kill a live unbound worker");
        (await verify.AgentIncidents.AnyAsync(
            i => i.AgentId == agentId && i.Kind == AgentIncidentKind.DelegateBindRefusalRecovered))
            .ShouldBeTrue();
        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("zero ingested transcript rows");
        (await verify.AgentTasks.SingleAsync(t => t.Id == taskId)).Result
            .ShouldContain("unbound session");
        stopper.Killed.ShouldNotContain(sessionId);
        _ = dispatcher;
    }

    [Test]
    public async Task a_direct_recovery_with_ingested_rows_states_the_real_count()
    {
        // R-1 / CARD-0551 D-3: RecoverFromBindRefusalAsync is the public contract; when the
        // session already has rows the warning must not claim "zero ingested".
        using var workspace = new TempWorkspace();
        var (_, stopper, provider) = CreateHarness();
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var worktreePath = Path.Combine(workspace.Path, "worktrees", $"card-task-{shortId}");
        Directory.CreateDirectory(worktreePath);
        var (agentId, sessionId, taskId) = await SeedDispatchedPoolTaskAsync(
            workspace.Path,
            WorkspaceMode.Worktree,
            worktreePath);

        await using (var db = CreateContext())
        {
            var at = DateTime.UtcNow.AddMinutes(-3);
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"c551-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = "the brief",
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 2,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"c551-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = "working",
                    Timestamp = at.AddMinutes(1),
                    CreatedAt = at.AddMinutes(1),
                });
            await db.SaveChangesAsync();
        }

        var replies = provider.GetRequiredService<AgentTaskReplyService>();
        await replies.RecoverFromBindRefusalAsync(
            taskId,
            new DelegateBindRefusalEvidence(["abc1234"], null),
            CancellationToken.None);

        await using var verify = CreateContext();
        var recovered = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        recovered.Status.ShouldBe(AgentTaskStatus.Succeeded);
        recovered.Result.ShouldNotBeNull();
        recovered.Result.ShouldContain("2 ingested transcript row(s)");
        recovered.Result.ShouldNotContain("zero ingested");
        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("2 ingested transcript row(s)");
        warning.Detail.ShouldNotContain("zero ingested");
        stopper.Killed.ShouldNotContain(sessionId);
        _ = agentId;
    }

    [Test]
    public async Task the_janitor_kills_a_recovered_worktree_delegate_after_the_ttl()
    {
        using var workspace = new TempWorkspace();
        var clock = new OffsetTimeProvider(DateTimeOffset.UtcNow);
        var (dispatcher, stopper, _) = CreateHarness(clock);
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var worktreePath = Path.Combine(workspace.Path, "worktrees", $"card-task-{shortId}");
        var (agentId, sessionId) = await SeedRecoveredWorktreeDelegateAsync(
            worktreePath, idleMinutes: 10, withIncident: true);

        // Offset clock is in the graph (CARD-0222). A large Advance would move the cutoff into
        // the future and retire other suites' just-idled rows in the shared fixture DB; the
        // row itself is already older than PoolIdleRetireMinutes (5).
        clock.Advance(TimeSpan.FromSeconds(1));
        var retired = await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        retired.ShouldBeGreaterThanOrEqualTo(1);
        stopper.Killed.ShouldContain(sessionId, "the pool's own KillAsync, after the TTL");
        await using var verify = CreateContext();
        var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
        agent.Status.ShouldBe(AgentStatus.Stopped, "option (a): keep the row when it carries an incident");
        (await verify.AgentIncidents.AnyAsync(i => i.AgentId == agentId)).ShouldBeTrue();
    }

    [Test]
    public async Task a_recovered_worktree_delegate_is_never_reused_for_a_shared_task()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, _) = CreateHarness();
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var worktreePath = Path.Combine(workspace.Path, "worktrees", $"card-task-{shortId}");
        var (warmAgentId, _) = await SeedRecoveredWorktreeDelegateAsync(
            worktreePath, idleMinutes: 3, withIncident: true);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldNotBe(warmAgentId, "a worktree delegate is not a Shared-task warm pool");
        (await verify.Agents.SingleAsync(a => a.Id == warmAgentId)).Status.ShouldBe(AgentStatus.Idle);
    }

    [Test]
    public async Task the_janitor_removes_stale_pool_rows_with_ended_sessions()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, _) = CreateHarness();
        var keep = new List<Guid>();
        var drop = new List<Guid>();
        var staleSessions = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var seeded = await SeedStalePoolDelegateAsync(workspace.Path, withIncident: true);
            keep.Add(seeded.AgentId);
            staleSessions.Add(seeded.SessionId);
        }
        for (var i = 0; i < 4; i++)
        {
            var seeded = await SeedStalePoolDelegateAsync(workspace.Path, withIncident: false);
            drop.Add(seeded.AgentId);
            staleSessions.Add(seeded.SessionId);
        }

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        foreach (var id in drop)
            (await verify.Agents.AnyAsync(a => a.Id == id)).ShouldBeFalse("incident-free stale rows are junk");
        foreach (var id in keep)
        {
            var row = await verify.Agents.SingleAsync(a => a.Id == id);
            row.Status.ShouldBe(AgentStatus.Stopped);
        }

        foreach (var sid in staleSessions)
            stopper.Killed.ShouldNotContain(sid, "stale sessions are already terminal — reconciler owns Failed/Stopped");
    }

    // ---- the janitor -----------------------------------------------------------------------

    [Test]
    public async Task the_janitor_does_not_retire_a_warm_agent_whose_session_is_mid_turn()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, provider) = CreateHarness();
        using var ownedProvider = provider;
        var (agent, session) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, 120);
        await SeedMidTurnTranscriptAsync(session);
        var before = DateTime.UtcNow.AddMinutes(-1);
        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        stopper.Killed.ShouldNotContain(session);
        await using var db = CreateContext();
        (await db.Agents.SingleAsync(a => a.Id == agent)).PoolIdleSince.ShouldNotBeNull()
            .ShouldBeGreaterThanOrEqualTo(before);
    }

    [Test]
    public async Task the_janitor_retires_it_after_the_turn_ends_and_the_ttl_elapses_again()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, provider) = CreateHarness();
        using var ownedProvider = provider;
        var (agent, session) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, 120);
        await SeedMidTurnTranscriptAsync(session);
        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        stopper.Killed.ShouldNotContain(session);
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = session, Sequence = 2,
                Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", CreatedAt = DateTime.UtcNow,
            });
            await db.Agents.Where(a => a.Id == agent).ExecuteUpdateAsync(u =>
                u.SetProperty(a => a.PoolIdleSince, DateTime.UtcNow.AddMinutes(-120)));
            await db.SaveChangesAsync();
        }
        var (next, nextStopper, nextProvider) = CreateHarness();
        using var nextOwnedProvider = nextProvider;
        await next.RetireIdleWarmAgentsAsync(CancellationToken.None);
        nextStopper.Killed.ShouldContain(session);
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agent)).ShouldBeFalse();
    }

    [Test]
    public async Task the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, provider) = CreateHarness();
        using var ownedProvider = provider;
        var (_, session) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, 120);
        await SeedMidTurnTranscriptAsync(session, DateTime.UtcNow.AddMinutes(-10));
        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        stopper.Killed.ShouldContain(session);
    }

    [Test]
    public async Task the_per_directory_cap_skips_a_mid_turn_agent()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper, provider) = CreateHarness();
        using var ownedProvider = provider;
        var agents = new List<(Guid AgentId, Guid SessionId)>();
        for (var i = 0; i < 5; i++)
            agents.Add(await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, i));
        await SeedMidTurnTranscriptAsync(agents[4].SessionId);
        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        stopper.Killed.ShouldNotContain(agents[4].SessionId);
        stopper.Killed.ShouldContain(agents[3].SessionId);
        stopper.Killed.ShouldContain(agents[2].SessionId);
        await using var verify = CreateContext();
        var ids = agents.Select(a => a.AgentId).ToList();
        (await verify.Agents.CountAsync(a => ids.Contains(a.Id))).ShouldBe(3);
    }

    [Test]
    public async Task a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _, provider) = CreateHarness();
        using var ownedProvider = provider;
        var (busy, session) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, 3);
        var (idle, _) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.Medium, 4);
        await SeedMidTurnTranscriptAsync(session);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);
        (await dispatcher.TryReuseWarmAgentAsync(task, DateTime.UtcNow, CancellationToken.None))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
        task.AgentId.ShouldBe(idle);
        var second = await SeedQueuedTaskAsync(workspace.Path, AgentModelLevel.Medium);
        (await dispatcher.TryReuseWarmAgentAsync(second, DateTime.UtcNow, CancellationToken.None))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh);
        second.AgentId.ShouldNotBe(busy);
    }

    private static async Task SeedMidTurnTranscriptAsync(Guid session, DateTime? at = null)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = session, Sequence = 1,
            Kind = TranscriptKinds.UserPrompt, Text = "Keep working", CreatedAt = at ?? DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

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
        CreateHarness(TimeProvider? timeProvider = null, string? connectionString = null)
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString ?? TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(timeProvider ?? TimeProvider.System);
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
            s.Definitions["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-pool-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper, provider);
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(
        string directory, AgentModelLevel level, int idleMinutes, Guid? reservedForRoot = null,
        Guid? projectId = null, AgentKind kind = AgentKind.ClaudeCode,
        string? launchEnvJson = null, string? connectionString = null)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext(connectionString);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = kind,
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
            Kind = kind,
            ModelLevel = level,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-idleMinutes),
            PoolReservedForRootTaskId = reservedForRoot,
            PoolProjectId = projectId,
            PersistentSessionId = sessionId.ToString("D"),
            LaunchEnvJson = launchEnvJson ?? "{}",
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
        Guid? projectId = null,
        AgentKind kind = AgentKind.ClaudeCode,
        string? worktreePath = null,
        string? inheritedLaunchEnvJson = null,
        string? connectionString = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = rootTaskId ?? id,
            Title = goal,
            Goal = goal,
            Role = AgentTaskRole.Docs,
            AgentKind = kind,
            ModelLevel = level,
            Workspace = worktreePath is null ? WorkspaceMode.Shared : WorkspaceMode.Worktree,
            WorkingDirectory = directory,
            WorktreePath = worktreePath,
            ProjectId = projectId,
            AgentId = pinnedAgentId,
            Ephemeral = pinnedAgentId is null,
            Status = AgentTaskStatus.Queued,
            InheritedLaunchEnvJson = inheritedLaunchEnvJson ?? "{}",
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext(connectionString);
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

    private static async Task<(Guid AgentId, Guid SessionId, Guid TaskId)> SeedDispatchedPoolTaskAsync(
        string directory, WorkspaceMode workspace, string? worktreePath)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var agentName = $"task-{agentId:N}"[..13];
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = worktreePath ?? directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = worktreePath ?? directory,
            Details = "CARD-0221 recovery test delegate.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "CARD-0221 recovered worktree",
            Goal = "Do the thing.",
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = workspace,
            WorkingDirectory = directory,
            WorktreePath = worktreePath,
            AgentId = agentId,
            AgentName = agentName,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = now.AddMinutes(-11),
            DispatchedAt = now.AddMinutes(-11),
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId, taskId);
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedRecoveredWorktreeDelegateAsync(
        string worktreePath, int idleMinutes, bool withIncident)
    {
        Directory.CreateDirectory(worktreePath);
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var agentName = $"task-{agentId:N}"[..13];
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = worktreePath,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = worktreePath,
            Details = "CARD-0221 recovered worktree delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-idleMinutes),
            PoolReservedForRootTaskId = null,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        var taskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "recovered worktree",
            Goal = "recovered worktree",
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = worktreePath,
            WorktreePath = worktreePath,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Succeeded,
            CompletedAt = now.AddMinutes(-idleMinutes),
            RecoveredAt = now.AddMinutes(-idleMinutes),
            CreatedAt = now.AddMinutes(-idleMinutes - 10),
            DispatchedAt = now.AddMinutes(-idleMinutes - 10),
        });
        if (withIncident)
        {
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = sessionId,
                Kind = AgentIncidentKind.DelegateBindRefusalRecovered,
                Severity = AlertSeverity.Warning,
                Message = "recovered from an unbound session",
                CreatedAt = now.AddMinutes(-idleMinutes),
            });
        }

        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedStalePoolDelegateAsync(
        string directory, bool withIncident)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var agentName = $"task-{agentId:N}"[..13];
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
            CreatedAt = now.AddDays(-8),
            StartedAt = now.AddDays(-8),
            EndedAt = now.AddDays(-8),
            LastSeenAt = now.AddDays(-8),
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = directory,
            Details = "CARD-0221 stale pool row.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PoolIdleSince = null,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddDays(-8),
            UpdatedAt = now.AddDays(-8),
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = Guid.NewGuid(),
            RootTaskId = Guid.NewGuid(),
            Title = "stale recovered",
            Goal = "stale recovered",
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Succeeded,
            CompletedAt = now.AddDays(-8),
            RecoveredAt = now.AddDays(-8),
            CreatedAt = now.AddDays(-8),
            DispatchedAt = now.AddDays(-8),
        });
        if (withIncident)
        {
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = sessionId,
                Kind = AgentIncidentKind.DelegateBindRefusalRecovered,
                Severity = AlertSeverity.Warning,
                Message = "stale recovered row",
                CreatedAt = now.AddDays(-8),
            });
        }

        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<(Guid AgentId, Guid SessionId, string AgentName, AgentTask Blocked, AgentTask FollowUp)>
        SeedFollowUpBehindBlockedAsync(string directory, string connectionString)
    {
        var (agentId, sessionId) = await SeedWarmAgentAsync(
            directory, AgentModelLevel.Medium, idleMinutes: 0, connectionString: connectionString);
        string agentName;
        AgentTask blocked;
        await using (var db = CreateContext(connectionString))
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.Status = AgentStatus.Running;
            agent.PoolIdleSince = null;
            agentName = agent.Name;
            var now = DateTime.UtcNow;
            var blockedId = Guid.NewGuid();
            blocked = new AgentTask
            {
                Id = blockedId,
                RootTaskId = blockedId,
                Title = "blocked occupant",
                Goal = "blocked occupant",
                Role = AgentTaskRole.Docs,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = directory,
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Blocked,
                DispatchedAt = now.AddMinutes(-5),
                CompletedAt = now.AddMinutes(-1),
                CreatedAt = now.AddMinutes(-6),
            };
            db.AgentTasks.Add(blocked);
            await db.SaveChangesAsync();
        }

        var followUp = await SeedQueuedTaskAsync(
            directory, AgentModelLevel.Medium, pinnedAgentId: agentId, connectionString: connectionString);
        return (agentId, sessionId, agentName, blocked, followUp);
    }

    private static Task<List<AgentTaskEvent>> HeldAsync(AppDbContext db, Guid taskId) =>
        db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Held)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync();

    private static Task<List<AgentTaskEvent>> AgedAsync(AppDbContext db, Guid taskId) =>
        db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.HeldAged)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync();

    private static DateTime UtcMs()
    {
        var now = DateTime.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static AppDbContext CreateContext(string? connectionString = null) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static AgentService CreateAgentService(AppDbContext db) => new(
        db,
        new CardWorkflowRunFactory(db, TimeProvider.System),
        new MockEventBus(),
        TimeProvider.System,
        new NoOpDirectoryWriter(),
        NullLogger<AgentService>.Instance);

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) { }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-pool-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// CARD-0222: an OFFSET over the real clock, not a frozen instant. The janitor cutoff is
    /// <c>UtcNow - PoolIdleRetireMinutes</c>; advancing the offset is how a test crosses the TTL
    /// without wedging any <c>Task.Delay(..., timeProvider)</c> poll loop in the same graph.
    /// </summary>
    private sealed class OffsetTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private TimeSpan _offset = start - DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;

        public void Advance(TimeSpan by) => _offset += by;
    }
}
