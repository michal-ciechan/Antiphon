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
    [Arguments(AgentTaskRole.Merge, AgentModelLevel.High)]
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

    [Test]
    public async Task a_worktree_dir_outside_the_roots_names_the_source_repository_shape()
    {
        using var caller = new TempWorkspace();
        using var stranger = new TempWorkspace();

        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                NewRequest("write the code", role: AgentTaskRole.Code) with
                {
                    Workspace = WorkspaceMode.Worktree,
                    WorkingDirectory = stranger.Path,
                },
                ManualCaller(caller.Path),
                CancellationToken.None));

        var detail = ex.Errors[nameof(CreateAgentTaskRequest.WorkingDirectory)][0];
        detail.ShouldContain("outside the allowed roots");
        detail.ShouldContain("Delegation:AllowedRoots");
        detail.ShouldContain("-Dir <repo> -Worktree");
        detail.ShouldContain(stranger.Path);
    }

    [Test]
    public async Task a_shared_dir_outside_the_roots_does_not_gain_worktree_guidance()
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

        ex.Errors[nameof(CreateAgentTaskRequest.WorkingDirectory)][0]
            .ShouldNotContain("-Dir <repo> -Worktree");
    }

    // ---- CARD-0256: StoppedBeforeFirstPrompt repeat guard ----------------------------------

    [Test]
    public async Task a_second_identical_grok_dispatch_is_blocked_without_starting_work()
    {
        using var workspace = new TempWorkspace();
        var goal = $"CARD-0256 grok repeat {Guid.NewGuid():N}";
        var parentSessionId = Guid.NewGuid();
        var prior = await SeedFailedEmptyStopAsync(
            workspace.Path, goal, AgentKind.Grok, parentSessionId);

        await using var db = CreateContext();
        await SeedParentSessionAsync(db, parentSessionId, workspace.Path);
        var eventBus = new MockEventBus();
        var created = await CreateService(db, eventBus: eventBus).CreateAsync(
            NewRequest(goal, role: AgentTaskRole.Code) with { AgentKind = AgentKind.Grok },
            new AgentTaskService.Caller(null, parentSessionId, workspace.Path),
            CancellationToken.None);

        try
        {
            created.Status.ShouldBe(AgentTaskStatus.Blocked);
            created.Warning.ShouldNotBeNull();
            created.Warning.ShouldContain("StoppedBeforeFirstPrompt");
            created.Warning.ShouldContain(DelegationReportFormatter.Short(prior.Id));

            await using var verify = CreateContext();
            var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
            row.Status.ShouldBe(AgentTaskStatus.Blocked);
            row.AgentSessionId.ShouldBeNull();
            row.WorktreePath.ShouldBeNull();
            row.AgentId.ShouldBeNull();
            row.FailureReason.ShouldContain("StoppedBeforeFirstPrompt");
            row.FailureReason.ShouldContain("no Grok process or worktree was started");

            (await verify.AgentTaskEvents.CountAsync(
                    e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(1);
            (await verify.SessionQueuedMessages.CountAsync(
                    m => m.AgentSessionId == parentSessionId
                         && m.SourceTaskId == created.Id
                         && m.Body.Contains("StoppedBeforeFirstPrompt")))
                .ShouldBe(1);
            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentTaskChanged");

            var attention = new AttentionService(
                CreateContext(), new RefusingSessionRunnerClient(),
                Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()),
                TimeProvider.System,
                NullLogger<AttentionService>.Instance);
            var item = (await attention.GetAsync(CancellationToken.None)).Items
                .Single(i => i.TaskId == created.Id);
            item.Kind.ShouldBe(AttentionKind.BlockedQuestion);
            item.Severity.ShouldBe(AlertSeverity.Critical);
        }
        finally
        {
            await DeleteTaskTreeAsync(created.Id, prior.Id, parentSessionId);
        }
    }

    [Test]
    public async Task an_otherwise_identical_ClaudeCode_dispatch_is_not_blocked()
    {
        using var workspace = new TempWorkspace();
        var goal = $"CARD-0256 claude alternative {Guid.NewGuid():N}";
        var prior = await SeedFailedEmptyStopAsync(workspace.Path, goal, AgentKind.Grok);

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            NewRequest(goal, role: AgentTaskRole.Code) with { AgentKind = AgentKind.ClaudeCode },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        try
        {
            created.Status.ShouldBe(AgentTaskStatus.Queued);
            await using var verify = CreateContext();
            (await verify.AgentTasks.SingleAsync(t => t.Id == created.Id))
                .Status.ShouldBe(AgentTaskStatus.Queued);
        }
        finally
        {
            await DeleteTaskTreeAsync(created.Id, prior.Id);
        }
    }

    [Test]
    public async Task retrying_a_StoppedBeforeFirstPrompt_failure_is_blocked()
    {
        using var workspace = new TempWorkspace();
        var goal = $"CARD-0256 retry block {Guid.NewGuid():N}";
        var prior = await SeedFailedEmptyStopAsync(workspace.Path, goal, AgentKind.Grok);

        await using var db = CreateContext();
        var summary = await CreateService(db).RetryAsync(prior.Id, CancellationToken.None);

        try
        {
            summary.Status.ShouldBe(AgentTaskStatus.Blocked);
            summary.AgentSessionId.ShouldBeNull();
            await using var verify = CreateContext();
            var row = await verify.AgentTasks.SingleAsync(t => t.Id == prior.Id);
            row.Status.ShouldBe(AgentTaskStatus.Blocked);
            row.FailureReason.ShouldContain("StoppedBeforeFirstPrompt");
            (await verify.AgentTaskEvents.CountAsync(
                    e => e.AgentTaskId == prior.Id && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(1);
        }
        finally
        {
            await DeleteTaskTreeAsync(prior.Id);
        }
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

    // ---- completion-note poll provenance ---------------------------------------------------

    [Test]
    public async Task a_parent_session_poll_of_a_settled_task_stamps_the_result_hash()
    {
        using var workspace = new TempWorkspace();
        var parentSessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Succeeded,
            result: "Landed the change.", parentSessionId: parentSessionId);

        await using var db = CreateContext();
        await CreateService(db).GetAsync(task.Id, CancellationToken.None, parentSessionId);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.LastPolledResultHash.ShouldBe(DelegationNoteDigest.Compute(task.Result));
        stored.LastPolledResultAt.ShouldNotBeNull();
    }

    [Test]
    public async Task a_poll_of_an_unsettled_task_stamps_nothing()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Working,
            result: "Still working.", parentSessionId: Guid.NewGuid());

        await using var db = CreateContext();
        await CreateService(db).GetAsync(task.Id, CancellationToken.None, task.ParentSessionId);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.LastPolledResultHash.ShouldBeNull();
        stored.LastPolledResultAt.ShouldBeNull();
    }

    [Test]
    public async Task a_token_less_poll_stamps_nothing()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Succeeded,
            result: "Landed the change.", parentSessionId: Guid.NewGuid());

        await using var db = CreateContext();
        await CreateService(db).GetAsync(task.Id, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.LastPolledResultHash.ShouldBeNull();
        stored.LastPolledResultAt.ShouldBeNull();
    }

    [Test]
    public async Task a_poll_by_a_session_that_is_not_the_parent_stamps_nothing()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Succeeded,
            result: "Landed the change.", parentSessionId: Guid.NewGuid());

        await using var db = CreateContext();
        await CreateService(db).GetAsync(task.Id, CancellationToken.None, Guid.NewGuid());

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.LastPolledResultHash.ShouldBeNull();
        stored.LastPolledResultAt.ShouldBeNull();
    }

    [Test]
    public async Task a_poll_does_not_bump_the_concurrency_token_or_publish()
    {
        using var workspace = new TempWorkspace();
        var parentSessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Succeeded,
            result: "Landed the change.", parentSessionId: parentSessionId);
        var events = new MockEventBus();

        await using var db = CreateContext();
        await CreateService(db, eventBus: events).GetAsync(task.Id, CancellationToken.None, parentSessionId);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.ConcurrencyToken.ShouldBe(task.ConcurrencyToken);
        events.PublishedEvents.Count.ShouldBe(0);
    }

    [Test]
    public async Task a_failed_task_hashes_its_failure_reason()
    {
        using var workspace = new TempWorkspace();
        var parentSessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Failed,
            failureReason: "The test environment did not start.", parentSessionId: parentSessionId);

        await using var db = CreateContext();
        await CreateService(db).GetAsync(task.Id, CancellationToken.None, parentSessionId);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.LastPolledResultHash.ShouldBe(DelegationNoteDigest.Compute(task.FailureReason));
        stored.LastPolledResultAt.ShouldNotBeNull();
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

    // ---- workspace defaults: an orchestrator owns something ---------------------------------

    [Test]
    public async Task an_unspecified_orchestrator_gets_its_own_worktree()
    {
        // It fans out writers, so it must own something — here, a worktree in the caller's repo.
        using var repo = new ScratchGitRepo("antiphon-task-ws");
        await repo.CommitFileAsync("README.md", "base\n");

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            NewRequest("own the upgrade", kind: AgentTaskKind.Orchestrator),
            ManualCaller(repo.Path),
            CancellationToken.None);

        created.Warning.ShouldBeNull();
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .Workspace.ShouldBe(WorkspaceMode.Worktree);
    }

    [Test]
    public async Task an_orchestrator_with_its_own_location_stays_shared()
    {
        // Its own -Dir IS the isolation — a worktree on top would be pure overhead.
        using var callerDir = new TempWorkspace();
        using var ownDir = new TempWorkspace();

        await using var db = CreateContext();
        var created = await CreateService(db, allowedRoots: [ownDir.Path]).CreateAsync(
            NewRequest("own the other repo", kind: AgentTaskKind.Orchestrator) with
            {
                WorkingDirectory = ownDir.Path,
            },
            ManualCaller(callerDir.Path),
            CancellationToken.None);

        created.Warning.ShouldBeNull();
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .Workspace.ShouldBe(WorkspaceMode.Shared);
    }

    [Test]
    public async Task a_worker_still_defaults_to_shared()
    {
        using var repo = new ScratchGitRepo("antiphon-task-ws-worker");
        await repo.CommitFileAsync("README.md", "base\n");

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            NewRequest("fix the typo"), ManualCaller(repo.Path), CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .Workspace.ShouldBe(WorkspaceMode.Shared, "workers run where the work is — that default stands");
    }

    [Test]
    public async Task forcing_an_orchestrator_into_its_callers_directory_is_honoured_but_warned()
    {
        // Explicit choices win — but the caller hears about the risk AT CREATION, when it can
        // still reconsider, not from a timeline after the collision.
        using var workspace = new TempWorkspace();

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            NewRequest("orchestrate in place", kind: AgentTaskKind.Orchestrator) with
            {
                Workspace = WorkspaceMode.Shared,
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.Warning.ShouldNotBeNull();
        created.Warning!.ShouldContain("overwrite each other");
        (await db.AgentTaskEvents.AsNoTracking()
                .AnyAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Warning))
            .ShouldBeTrue("the timeline records what the caller was told");
    }

    [Test]
    public async Task an_orchestrator_in_a_non_git_directory_falls_back_to_shared_with_a_warning()
    {
        // Nothing to branch, so isolation is impossible — say so instead of failing the creation.
        using var workspace = new TempWorkspace();

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            NewRequest("orchestrate the notes", kind: AgentTaskKind.Orchestrator),
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.Warning.ShouldNotBeNull();
        created.Warning!.ShouldContain("not a git repository");
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .Workspace.ShouldBe(WorkspaceMode.Shared);
    }

    [Test]
    public async Task a_child_of_a_worktree_parent_merges_into_the_parents_task_branch()
    {
        // Integration once per level: a sub-orchestrator in a worktree collects its children's work
        // on ITS branch and merges one level up when the subtree is done. If children targeted the
        // parent's TARGET instead, a dozen workers would race to integrate against a moving branch.
        using var repo = new ScratchGitRepo("antiphon-task-inherit");
        await repo.CommitFileAsync("README.md", "base\n");

        var parent = await SeedTaskAsync(AgentTaskKind.Orchestrator, repo.Path);
        await using (var setup = CreateContext())
        {
            var row = await setup.AgentTasks.SingleAsync(t => t.Id == parent.Id);
            row.RepoPath = repo.Path;
            row.WorktreeBranch = "feat/card-task-parent";
            row.MergeTargetRef = "master";
            parent = row;
            await setup.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            NewRequest("write the docs half"),
            new AgentTaskService.Caller(parent, Guid.NewGuid(), repo.Path),
            CancellationToken.None);

        var child = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        child.MergeTargetRef.ShouldBe("feat/card-task-parent", "children integrate into the parent's branch, not past it");
    }

    // ---- follow-up: same agent, same context -------------------------------------------------

    [Test]
    public async Task a_follow_up_pins_the_prior_tasks_agent_and_inherits_its_context()
    {
        // The point of a follow-up is the agent's CONTEXT — so it must land on that agent, in
        // that agent's directory, at that agent's tier (the model is already running; a role
        // policy cannot change it mid-session).
        using var workspace = new TempWorkspace();
        var agentId = await SeedPoolAgentAsync(workspace.Path, AgentModelLevel.Low);
        var prior = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Succeeded);
        await PinTaskAgentAsync(prior.Id, agentId);

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            // Role says Code (fable by policy) — the agent's tier must win anyway.
            NewRequest("now add the edge cases", role: AgentTaskRole.Code) with
            {
                FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        task.AgentId.ShouldBe(agentId);
        task.WorkingDirectory.ShouldBe(workspace.Path);
        task.ModelLevel.ShouldBe(AgentModelLevel.Low, "the running model IS the tier");
        task.Workspace.ShouldBe(WorkspaceMode.Shared);
        task.Ephemeral.ShouldBeFalse("pinned — a requeue must go back to the same agent");
    }

    [Test]
    public async Task a_follow_up_on_a_retired_agent_is_refused_with_guidance()
    {
        using var workspace = new TempWorkspace();
        var prior = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Succeeded);
        await PinTaskAgentAsync(prior.Id, Guid.NewGuid()); // an agent that no longer exists

        await using var db = CreateContext();
        var ex = await Should.ThrowAsync<ConflictException>(
            () => CreateService(db).CreateAsync(
                NewRequest("follow up") with { FollowUpOnTask = prior.Id.ToString("D") },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        ex.Message.ShouldContain("retired");
        ex.Message.ShouldContain("delegate normally", customMessage: "refusals must say what to do instead");
    }

    // ---- short ids: the id the caller actually has -------------------------------------------

    [Test]
    public async Task a_short_id_resolves_to_its_task()
    {
        // The completion note says "[task 7f3a2b91 done]" — that 8-char short id is the ONLY id
        // the calling agent ever sees, so -Reply and -OnAgent must accept it.
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(AgentTaskKind.Worker, workspace.Path);

        await using var db = CreateContext();
        var resolved = await CreateService(db).ResolveTaskIdAsync(
            DelegationReportFormatter.Short(task.Id), CancellationToken.None);

        resolved.ShouldBe(task.Id);
    }

    [Test]
    public async Task an_unknown_short_id_is_a_clean_not_found()
    {
        await using var db = CreateContext();
        await Should.ThrowAsync<NotFoundException>(
            () => CreateService(db).ResolveTaskIdAsync("ffffffff", CancellationToken.None));
    }

    [Test]
    public async Task garbage_is_rejected_as_neither_kind_of_id()
    {
        await using var db = CreateContext();
        await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).ResolveTaskIdAsync("not-an-id", CancellationToken.None));
    }

    private static async Task<Guid> SeedPoolAgentAsync(string directory, AgentModelLevel level)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"pool-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            ModelLevel = level,
            IsPoolDelegate = true,
            PoolIdleSince = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task PinTaskAgentAsync(Guid taskId, Guid agentId)
    {
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.AgentId = agentId;
        await db.SaveChangesAsync();
    }

    // ---- retry and escalation --------------------------------------------------------------

    [Test]
    public async Task retrying_a_failed_task_requeues_it_at_the_same_tier()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker, workspace.Path, status: AgentTaskStatus.Failed, level: AgentModelLevel.Medium);

        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == task.Id)).RecoveredAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var summary = await CreateService(db).RetryAsync(task.Id, CancellationToken.None);

        summary.Status.ShouldBe(AgentTaskStatus.Queued);
        summary.ModelLevel.ShouldBe(AgentModelLevel.Medium, "a retry is the same work, not a bigger model");
        summary.Attempt.ShouldBe(2);
        summary.RecoveredAt.ShouldBeNull("a new attempt cannot inherit the prior settlement's provenance");
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

    private static async Task<AgentTask> SeedFailedEmptyStopAsync(
        string workingDirectory,
        string goal,
        AgentKind agentKind,
        Guid? parentSessionId = null)
    {
        var task = await SeedTaskAsync(
            AgentTaskKind.Worker,
            workingDirectory,
            status: AgentTaskStatus.Failed,
            role: AgentTaskRole.Code,
            parentSessionId: parentSessionId);
        await using var db = CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        row.Goal = goal;
        row.AgentKind = agentKind;
        row.FailureCode = AgentTaskFailureCode.StoppedBeforeFirstPrompt;
        row.FailureReason = "StoppedBeforeFirstPrompt: Antiphon observed no prompt before the session stopped, and the stop origin was not recorded";
        row.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return row;
    }

    private static async Task SeedParentSessionAsync(AppDbContext db, Guid sessionId, string cwd)
    {
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "repeat-guard-parent",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task DeleteTaskTreeAsync(params Guid[] ids)
    {
        var taskIds = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        await using var db = CreateContext();
        await db.AgentTaskEvents.Where(e => taskIds.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
        await db.SessionQueuedMessages.Where(m => m.SourceTaskId != null && taskIds.Contains(m.SourceTaskId.Value))
            .ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => taskIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => taskIds.Contains(s.Id)).ExecuteDeleteAsync();
    }

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
        string? result = null,
        Guid? parentSessionId = null,
        string? failureReason = null)
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
            ParentSessionId = parentSessionId,
            Result = result,
            FailureReason = failureReason,
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
        RecordingSessionStopper? stopper = null,
        MockEventBus? eventBus = null)
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
            eventBus ?? new MockEventBus(),
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
