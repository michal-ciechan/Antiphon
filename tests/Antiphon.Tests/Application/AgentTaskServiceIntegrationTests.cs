using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Delegated-task creation against a real database: the tier ladder, the recursion boundary
/// (a worker cannot delegate — the only thing keeping a recursive tree bounded), cross-repo
/// targeting, and the fan-out caps.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskServiceIntegrationTests
{
    // ---- the tier ladder ------------------------------------------------------------------

    [Test]
    [Arguments(AgentTaskRole.Plan, AgentModelLevel.Frontier)]
    [Arguments(AgentTaskRole.Code, AgentModelLevel.Frontier)]
    [Arguments(AgentTaskRole.Review, AgentModelLevel.Frontier)]
    [Arguments(AgentTaskRole.Debug, AgentModelLevel.High)]
    [Arguments(AgentTaskRole.Coverage, AgentModelLevel.High)]
    [Arguments(AgentTaskRole.Docs, AgentModelLevel.Medium)]
    [Arguments(AgentTaskRole.Commit, AgentModelLevel.Medium)]
    [Arguments(AgentTaskRole.Test, AgentModelLevel.Low)]
    [Arguments(AgentTaskRole.Deploy, AgentModelLevel.Low)]
    public async Task each_role_resolves_to_its_configured_tier(AgentTaskRole role, AgentModelLevel expected)
    {
        // The user-facing promise of the whole design: plan/code on frontier, debug on opus,
        // docs/git on sonnet, run-the-tests on haiku.
        await using var db = CreateContext();
        var service = CreateService(db);

        service.ResolveLevel(AgentTaskKind.Worker, role, null).ShouldBe(expected);
    }

    [Test]
    public async Task an_explicit_level_overrides_the_role_policy_and_is_recorded()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest("run the suite", role: AgentTaskRole.Test, level: AgentModelLevel.Frontier),
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);

        await using var verify = CreateContext();
        var events = await verify.AgentTaskEvents.Where(e => e.AgentTaskId == created.Id).ToListAsync();
        events.ShouldContain(e => e.Detail.Contains("explicit override"));
    }

    [Test]
    public async Task a_sub_orchestrator_is_never_dispatched_below_the_orchestrator_floor()
    {
        // Decomposition is the expensive kind of thinking; a cheap orchestrator produces a bad tree
        // and every delegate under it pays for that.
        await using var db = CreateContext();
        var service = CreateService(db);

        var level = service.ResolveLevel(AgentTaskKind.Orchestrator, AgentTaskRole.Test, null);

        ((int)level).ShouldBeLessThanOrEqualTo((int)AgentModelLevel.High);
    }

    // ---- the recursion boundary -----------------------------------------------------------

    [Test]
    public async Task a_worker_cannot_delegate()
    {
        // THE test that must never be skipped. A worker's token carries no create scope, so a
        // worker cannot start a fan-out even if it decides it wants to. If this leaks, the cost
        // ceiling is the only thing between the fleet and a fork bomb.
        using var workspace = new TempWorkspace();
        var worker = await SeedTaskAsync(AgentTaskKind.Worker, workspace.Path);

        await using var db = CreateContext();
        var service = CreateService(db);
        var caller = new AgentTaskService.Caller(worker, Guid.NewGuid(), workspace.Path);

        caller.MayDelegate.ShouldBeFalse();
        var ex = await Should.ThrowAsync<ForbiddenException>(
            () => service.CreateAsync(NewRequest("do more work"), caller, CancellationToken.None));
        ex.Message.ShouldContain("Workers cannot delegate");

        await using var verify = CreateContext();
        (await verify.AgentTasks.CountAsync(t => t.ParentTaskId == worker.Id))
            .ShouldBe(0, "the rejected task must not exist at all");
        (await verify.AgentTaskEvents.CountAsync(
                e => e.AgentTaskId == worker.Id && e.Type == AgentTaskEventType.Rejected))
            .ShouldBe(1, "a refused delegation is worth an audit row");
    }

    [Test]
    public async Task a_sub_orchestrator_can_delegate_and_its_child_carries_the_lineage()
    {
        using var workspace = new TempWorkspace();
        var orchestrator = await SeedTaskAsync(AgentTaskKind.Orchestrator, workspace.Path);

        await using var db = CreateContext();
        var service = CreateService(db);
        var parentSession = Guid.NewGuid();

        var child = await service.CreateAsync(
            NewRequest("write the code", role: AgentTaskRole.Code),
            new AgentTaskService.Caller(orchestrator, parentSession, workspace.Path),
            CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == child.Id);
        stored.ParentTaskId.ShouldBe(orchestrator.Id);
        stored.RootTaskId.ShouldBe(orchestrator.RootTaskId, "the whole run shares one root");
        stored.Depth.ShouldBe(orchestrator.Depth + 1);
        stored.ParentSessionId.ShouldBe(parentSession, "the report goes to the sub-orchestrator, not the root");
        stored.ReplyTo.ShouldBe(AgentTaskReplyTo.Session);
    }

    [Test]
    public async Task an_unknown_token_is_refused()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        await Should.ThrowAsync<ForbiddenException>(
            () => service.AuthenticateAsync("not-a-real-token", CancellationToken.None));
    }

    [Test]
    public async Task a_missing_token_is_refused()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        await Should.ThrowAsync<ForbiddenException>(
            () => service.AuthenticateAsync(null, CancellationToken.None));
    }

    [Test]
    public async Task the_raw_token_is_never_stored()
    {
        // A leaked database row must not be replayable as a credential.
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest("do a thing"), ManualCaller(workspace.Path), CancellationToken.None);
        AgentTaskService.RawTokens.TryGetValue(created.Id, out var raw).ShouldBeTrue();

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == created.Id);
        stored.TokenHash.ShouldNotBeNull();
        stored.TokenHash.ShouldNotBe(raw);

        // ...and the hash is what authentication matches on.
        var caller = await CreateService(verify).AuthenticateAsync(raw!, CancellationToken.None);
        caller.Task!.Id.ShouldBe(created.Id);
    }

    // ---- workspace defaults and cross-repo targeting ---------------------------------------

    [Test]
    public async Task workspace_defaults_to_shared()
    {
        // Isolation is opt-in: most delegated work either must see live state or is small enough
        // that a branch + merge-back + conflict path is pure overhead.
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest("run the tests"), ManualCaller(workspace.Path), CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == created.Id)).Workspace.ShouldBe(WorkspaceMode.Shared);
    }

    [Test]
    public async Task a_task_inherits_the_callers_directory_when_none_is_given()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest("do a thing"), ManualCaller(workspace.Path), CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == created.Id))
            .WorkingDirectory.ShouldBe(workspace.Path);
    }

    [Test]
    public async Task a_task_can_target_a_different_repo_when_its_root_is_allowed()
    {
        // Agent-per-repo orchestration: the directory is a property of the TASK.
        using var caller = new TempWorkspace();
        using var otherRoot = new TempWorkspace();
        var otherRepo = Directory.CreateDirectory(Path.Combine(otherRoot.Path, "am-service"));

        await using var db = CreateContext();
        var service = CreateService(db, allowedRoots: [otherRoot.Path]);

        var created = await service.CreateAsync(
            NewRequest("roll out the gateway build", role: AgentTaskRole.Deploy) with
            {
                WorkingDirectory = otherRepo.FullName,
            },
            ManualCaller(caller.Path),
            CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == created.Id))
            .WorkingDirectory.ShouldBe(otherRepo.FullName);
    }

    [Test]
    public async Task a_directory_outside_the_allowed_roots_is_refused()
    {
        using var caller = new TempWorkspace();
        using var stranger = new TempWorkspace();

        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                NewRequest("do a thing") with { WorkingDirectory = stranger.Path },
                ManualCaller(caller.Path),
                CancellationToken.None));

        ex.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.WorkingDirectory));
    }

    [Test]
    public async Task a_worktree_task_in_a_non_git_directory_is_refused_with_a_usable_message()
    {
        // There is nothing to branch. Fail at creation with an explanation rather than crashing
        // in the dispatcher.
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                NewRequest("write the code", role: AgentTaskRole.Code) with { Workspace = WorkspaceMode.Worktree },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        ex.Errors[nameof(CreateAgentTaskRequest.Workspace)][0].ShouldContain("not a git repository");
    }

    // ---- fan-out caps ----------------------------------------------------------------------

    [Test]
    public async Task a_task_past_the_depth_limit_is_refused()
    {
        using var workspace = new TempWorkspace();
        var deep = await SeedTaskAsync(AgentTaskKind.Orchestrator, workspace.Path, depth: 5);

        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ConflictException>(
            () => service.CreateAsync(
                NewRequest("go deeper"),
                new AgentTaskService.Caller(deep, Guid.NewGuid(), workspace.Path),
                CancellationToken.None));

        ex.Message.ShouldContain("depth limit");
    }

    [Test]
    public async Task a_run_that_has_burned_its_budget_stops_accepting_new_tasks()
    {
        // The REAL runaway guard: a recursive tree can only run away by spending, so spend is what
        // is bounded. Depth is only a backstop.
        using var workspace = new TempWorkspace();
        var orchestrator = await SeedTaskAsync(AgentTaskKind.Orchestrator, workspace.Path, costUsd: 6.00m);

        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ConflictException>(
            () => service.CreateAsync(
                NewRequest("more work"),
                new AgentTaskService.Caller(orchestrator, Guid.NewGuid(), workspace.Path),
                CancellationToken.None));

        ex.Message.ShouldContain("cost ceiling");
    }

    [Test]
    public async Task a_goal_is_required()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "   "), ManualCaller(workspace.Path), CancellationToken.None));
    }

    // ---- projection ------------------------------------------------------------------------

    [Test]
    public async Task subtree_cost_rolls_up_through_the_whole_tree()
    {
        // The board chip shows a sub-orchestrator's whole subtree spend — a grandchild's cost must
        // reach the root, not stop one level up.
        using var workspace = new TempWorkspace();
        var root = await SeedTaskAsync(AgentTaskKind.Orchestrator, workspace.Path, costUsd: 0.01m);
        var mid = await SeedTaskAsync(
            AgentTaskKind.Orchestrator, workspace.Path, costUsd: 0.02m, parent: root);
        await SeedTaskAsync(AgentTaskKind.Worker, workspace.Path, costUsd: 0.04m, parent: mid);

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(root.Id, CancellationToken.None);

        detail.Summary.CostUsd.ShouldBe(0.01m, "the row's own cost is unchanged");
        detail.Summary.SubtreeCostUsd.ShouldBe(0.07m, "0.01 + 0.02 + 0.04 across two levels");
        detail.Summary.ChildCount.ShouldBe(1, "child count is direct children only");
    }

    [Test]
    public async Task cancelling_a_queued_task_settles_it()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(AgentTaskKind.Worker, workspace.Path);

        await using var db = CreateContext();
        var summary = await CreateService(db).CancelAsync(task.Id, CancellationToken.None);

        summary.Status.ShouldBe(AgentTaskStatus.Canceled);
    }

    [Test]
    public async Task a_settled_task_cannot_be_cancelled_twice()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Succeeded);

        await using var db = CreateContext();

        await Should.ThrowAsync<ConflictException>(
            () => CreateService(db).CancelAsync(task.Id, CancellationToken.None));
    }

    [Test]
    public async Task cancelling_a_running_task_stops_its_delegate()
    {
        // A cancel that only relabels the row leaves a Claude running against the run's cost
        // ceiling while the board claims the work stopped.
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Working, sessionId: sessionId);

        await using var db = CreateContext();
        var stopper = new RecordingSessionStopper();
        await CreateService(db, stopper: stopper).CancelAsync(task.Id, CancellationToken.None);

        stopper.Killed.ShouldBe([sessionId]);
    }

    [Test]
    public async Task a_task_still_cancels_when_its_session_is_already_gone()
    {
        // The runner losing a session must not strand the task as un-cancellable.
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Working, sessionId: Guid.NewGuid());

        await using var db = CreateContext();
        var stopper = new RecordingSessionStopper { Throws = new InvalidOperationException("session gone") };
        var summary = await CreateService(db, stopper: stopper).CancelAsync(task.Id, CancellationToken.None);

        summary.Status.ShouldBe(AgentTaskStatus.Canceled);
    }

    // ---- retry and escalation --------------------------------------------------------------

    [Test]
    public async Task retrying_a_failed_task_requeues_it_at_the_same_tier()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Failed, level: AgentModelLevel.Medium);

        await using var db = CreateContext();
        var summary = await CreateService(db).RetryAsync(task.Id, CancellationToken.None);

        summary.Status.ShouldBe(AgentTaskStatus.Queued);
        summary.ModelLevel.ShouldBe(AgentModelLevel.Medium, "a retry is the same work, not a bigger model");
        summary.Attempt.ShouldBe(2);
    }

    [Test]
    public async Task a_requeued_task_gets_a_fresh_token()
    {
        // The raw token is consumed by the dispatch that injects it into the delegate's env, so a
        // requeued task without a new one launches a delegate that cannot call back at all — and a
        // sub-orchestrator that cannot call back cannot delegate.
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Orchestrator, workspace.Path, status: AgentTaskStatus.Failed);

        await using var db = CreateContext();
        var service = CreateService(db);
        await service.RetryAsync(task.Id, CancellationToken.None);

        AgentTaskService.RawTokens.TryGetValue(task.Id, out var raw).ShouldBeTrue();
        var stored = await db.AgentTasks.AsNoTracking().FirstAsync(t => t.Id == task.Id);
        stored.TokenHash.ShouldBe(AgentTaskService.HashToken(raw!));

        // And it authenticates as this task, which is what the delegate's callback depends on.
        var caller = await service.AuthenticateAsync(raw, CancellationToken.None);
        caller.Task!.Id.ShouldBe(task.Id);
    }

    [Test]
    public async Task a_queued_task_cannot_be_retried()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(AgentTaskKind.Worker, workspace.Path);

        await using var db = CreateContext();

        var ex = await Should.ThrowAsync<ConflictException>(
            () => CreateService(db).RetryAsync(task.Id, CancellationToken.None));
        ex.Message.ShouldContain("has not run yet");
    }

    [Test]
    public async Task retrying_a_running_task_stops_the_delegate_first()
    {
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Working, sessionId: sessionId);

        await using var db = CreateContext();
        var stopper = new RecordingSessionStopper();
        var summary = await CreateService(db, stopper: stopper).RetryAsync(task.Id, CancellationToken.None);

        stopper.Killed.ShouldBe([sessionId], "two delegates on one task would both report back");
        summary.AgentSessionId.ShouldBeNull("the next dispatch assigns a fresh session");
    }

    [Test]
    [Arguments(AgentModelLevel.Low, AgentModelLevel.Medium)]
    [Arguments(AgentModelLevel.Medium, AgentModelLevel.High)]
    [Arguments(AgentModelLevel.High, AgentModelLevel.Frontier)]
    public async Task escalating_moves_one_rung_up_the_ladder(AgentModelLevel from, AgentModelLevel to)
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Failed, level: from);

        await using var db = CreateContext();
        var summary = await CreateService(db).EscalateAsync(task.Id, null, CancellationToken.None);

        summary.ModelLevel.ShouldBe(to);
        summary.EscalatedFrom.ShouldBe(from, "the chip shows the ladder, not just where it ended up");
        summary.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task escalating_from_the_top_of_the_ladder_is_refused()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path,
            status: AgentTaskStatus.Failed, level: AgentModelLevel.Frontier);

        await using var db = CreateContext();

        var ex = await Should.ThrowAsync<ConflictException>(
            () => CreateService(db).EscalateAsync(task.Id, null, CancellationToken.None));
        ex.Message.ShouldContain("top of the ladder");
    }

    [Test]
    public async Task escalating_a_queued_task_changes_its_tier_without_spending_an_attempt()
    {
        // Bumping a task before it starts is not a second try — it is the first one, at a
        // different tier.
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, level: AgentModelLevel.Medium);

        await using var db = CreateContext();
        var summary = await CreateService(db).EscalateAsync(task.Id, null, CancellationToken.None);

        summary.ModelLevel.ShouldBe(AgentModelLevel.High);
        summary.Attempt.ShouldBe(1);
        summary.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task an_explicit_escalation_target_must_be_a_higher_tier()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Failed, level: AgentModelLevel.High);

        await using var db = CreateContext();

        // Downgrading through "escalate" would silently make work cheaper than the caller asked for.
        await Should.ThrowAsync<ConflictException>(
            () => CreateService(db).EscalateAsync(task.Id, AgentModelLevel.Low, CancellationToken.None));
    }

    [Test]
    public async Task the_next_attempt_carries_what_the_last_one_found()
    {
        // Escalation that restarts cold just pays a higher tier to rediscover the same dead end.
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path,
            status: AgentTaskStatus.Failed, level: AgentModelLevel.High,
            result: "Could not reproduce; the failure only appears under load.");

        await using var db = CreateContext();
        await CreateService(db).EscalateAsync(task.Id, null, CancellationToken.None);

        var requeued = await db.AgentTasks.AsNoTracking().FirstAsync(t => t.Id == task.Id);
        var brief = DelegationReportFormatter.BuildBrief(requeued, new DelegationSettings());

        brief.ShouldContain("previous attempt");
        brief.ShouldContain("only appears under load");
        brief.ShouldContain("opus", Case.Insensitive);
        brief.ShouldContain("fable", Case.Insensitive);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static CreateAgentTaskRequest NewRequest(
        string goal,
        AgentTaskKind kind = AgentTaskKind.Worker,
        AgentTaskRole role = AgentTaskRole.Custom,
        AgentModelLevel? level = null) =>
        new(Goal: goal, Kind: kind, Role: role, ModelLevel: level);

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static async Task<AgentTask> SeedTaskAsync(
        AgentTaskKind kind,
        string workingDirectory,
        int depth = 0,
        decimal costUsd = 0m,
        AgentTaskStatus status = AgentTaskStatus.Queued,
        AgentTask? parent = null,
        AgentModelLevel level = AgentModelLevel.High,
        AgentTaskRole role = AgentTaskRole.Custom,
        Guid? sessionId = null,
        string? result = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = parent?.RootTaskId ?? id,
            ParentTaskId = parent?.Id,
            Depth = parent is null ? depth : parent.Depth + 1,
            Title = $"Seeded {kind}",
            Goal = "Seeded goal.",
            Kind = kind,
            Role = role,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            Status = status,
            CostUsd = costUsd,
            AgentSessionId = sessionId,
            Result = result,
            CreatedAt = DateTime.UtcNow,
        };

        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskService CreateService(
        AppDbContext db,
        IReadOnlyList<string>? allowedRoots = null,
        RecordingSessionStopper? stopper = null)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = allowedRoots?.ToList() ?? [],
        };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            stopper ?? new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>A real directory on disk — the resolver verifies existence, so a fake path won't do.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-task-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a delegate's stray file lock must not fail the test */ }
        }
    }
}
