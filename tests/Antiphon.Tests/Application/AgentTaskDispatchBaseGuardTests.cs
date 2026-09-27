using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0215: a card-bound Worktree task must not branch from master while a same-card
/// kept sibling is still off to the side. Hold while that sibling's land is in flight;
/// warn (and still dispatch) when the branch is simply not landed; stay silent when the
/// branch is already gone.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class AgentTaskDispatchBaseGuardTests
{
    [Test]
    [Arguments("new_descendant")]
    [Arguments("landed_in_queue")]
    [Arguments("unchanged")]
    [Timeout(90_000)]
    public async Task T0442_V19_dispatch_rechecks_the_create_preview(string scenario, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-v19");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var a = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        await db.SaveChangesAsync(ct);
        var aSha = (await repo.GitReadAsync("rev-parse", a.WorktreeBranch!)).Trim();
        AgentTaskCreatedDto created;
        await using (var previewProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var previewScope = previewProvider.CreateAsyncScope())
        {
            created = await previewScope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(new CreateAgentTaskRequest("Continue", Title: "CARD-0442 Code",
                    Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                    Card: card.Id.ToString("D")),
                    new AgentTaskService.Caller(null, null, repo.Path), ct);
        }
        created.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        created.WorktreeBase.SourceTaskId.ShouldBe(a.Id);
        created.WorktreeBase.SourceSha.ShouldBe(aSha);

        AgentTask? b = null;
        if (scenario == "new_descendant")
            b = await SeedKeptSiblingAsync(db, repo, card.Id, "B", startRef: a.WorktreeBranch);
        else if (scenario == "landed_in_queue")
        {
            await repo.GitAsync("merge", "--ff-only", a.WorktreeBranch!);
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = a.Id, Type = AgentTaskEventType.Landed,
                Detail = "fixture landed", At = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
        var expectedSha = scenario == "new_descendant"
            ? (await repo.GitReadAsync("rev-parse", b!.WorktreeBranch!)).Trim()
            : aSha;

        await using (var launchProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var launchScope = launchProvider.CreateAsyncScope())
            await launchScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var launched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        launched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        launched.WorktreePath.ShouldNotBeNull();
        (await ScratchGitRepo.GitInAsync(launched.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim().ShouldBe(expectedSha);
        launched.WorktreeBaseSha.ShouldBe(expectedSha);
        launched.WorktreeBaseTaskId.ShouldBe(scenario switch
        {
            "new_descendant" => b!.Id,
            "landed_in_queue" => null,
            _ => a.Id,
        });
        launched.MergeTargetRef.ShouldBeNull();
        launched.WorktreeBasePreviewJson.ShouldContain(aSha);
        var changed = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == created.Id && i.WarningKey == "worktree-base-preview-changed")
            .ToListAsync(ct);
        changed.Count.ShouldBe(scenario == "unchanged" ? 0 : 1);
        if (changed.Count == 1)
        {
            changed[0].Detail.ShouldContain(aSha);
            changed[0].Detail.ShouldContain(scenario == "landed_in_queue" ? "Target master" : expectedSha);
        }
    }

    [Test]
    [Arguments("auto_two")]
    [Arguments("task_two")]
    [Arguments("fresh_two")]
    [Arguments("auto_four")]
    [Arguments("task_four")]
    [Arguments("fresh_four")]
    [Timeout(90_000)]
    public async Task T0442_V20_pending_land_precedes_source_selection_in_every_mode(
        string scenario, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-v20");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var a = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        var x = await SeedKeptSiblingAsync(db, repo, card.Id, "X", startRef: "master");
        AgentTask chosen = a;
        AgentTask pending = x;
        if (scenario.EndsWith("four", StringComparison.Ordinal))
        {
            chosen = await SeedKeptSiblingAsync(db, repo, card.Id, "B", startRef: a.WorktreeBranch);
            pending = await SeedKeptSiblingAsync(db, repo, card.Id, "Y", startRef: x.WorktreeBranch);
        }
        pending.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedPendingSiblingLandAsync(db, repo, pending);
        await db.SaveChangesAsync(ct);
        var request = new CreateAgentTaskRequest("Continue", Title: "CARD-0442 Code",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            Card: card.Id.ToString("D"));
        if (scenario.StartsWith("task", StringComparison.Ordinal))
            request = request with { WorktreeBaseTask = DelegationReportFormatter.Short(chosen.Id) };
        else if (scenario.StartsWith("fresh", StringComparison.Ordinal))
            request = request with { FreshWorktree = true };

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .CreateAsync(request, new AgentTaskService.Caller(null, null, repo.Path), ct);
        created.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.WaitForLand);
        created.WorktreeBase.SourceTaskId.ShouldBe(pending.Id);
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        held.WorktreeBranch.ShouldBeNull();
        held.AgentSessionId.ShouldBeNull();
        (await db.AgentSessions.AsNoTracking().CountAsync(s => s.Id == held.AgentSessionId, ct)).ShouldBe(0);
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>
            e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(1);
        (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet",
            $"refs/heads/feat/card-task-{DelegationReportFormatter.Short(created.Id)}")).Ok.ShouldBeFalse();
    }

    [Test]
    [Arguments("all_contained")]
    [Arguments("still_divergent")]
    [Arguments("landed_merge")]
    [Arguments("landed_residue_merge")]
    [Timeout(120_000)]
    public async Task T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips(
        string scenario, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-v21");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var source = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        AgentTask? other = null;
        AgentTask? b = null;
        AgentTask? tail = null;
        if (scenario is "all_contained" or "still_divergent")
        {
            other = await SeedKeptSiblingAsync(db, repo, card.Id, "X", startRef: "master");
            if (scenario == "still_divergent")
            {
                b = await SeedKeptSiblingAsync(db, repo, card.Id, "B", startRef: source.WorktreeBranch);
                tail = await SeedKeptSiblingAsync(db, repo, card.Id, "Y", startRef: other.WorktreeBranch);
            }
        }
        else
        {
            var right = await SeedKeptSiblingAsync(db, repo, card.Id, "R", startRef: "master");
            db.AgentTasks.Remove(right);
            await repo.GitAsync("checkout", source.WorktreeBranch!);
            await repo.GitAsync("merge", "--no-ff", right.WorktreeBranch!, "-m", "merge fixture");
            await repo.GitAsync("checkout", "master");
            await repo.CommitFileAsync("target.txt", "T\n");
            if (scenario == "landed_residue_merge")
                other = await SeedKeptSiblingAsync(db, repo, card.Id, "remaining", startRef: "master");
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = source.Id,
                Type = scenario == "landed_merge"
                    ? AgentTaskEventType.Landed : AgentTaskEventType.LandedWithResidue,
                Detail = "fixture completion", At = DateTime.UtcNow,
            });
        }
        source.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedPendingSiblingLandAsync(db, repo, source);
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = source.Id,
            Type = AgentTaskEventType.LandRequested, Detail = "historic request",
            At = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync(ct);
        var request = new CreateAgentTaskRequest("Continue", Title: "CARD-0442 Code",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            Card: card.Id.ToString("D"));
        AgentTaskCreatedDto created;
        await using (var previewProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var previewScope = previewProvider.CreateAsyncScope())
            created = await previewScope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(request, new AgentTaskService.Caller(null, null, repo.Path), ct);
        if (scenario is "landed_merge" or "landed_residue_merge")
        {
            var completedRequest = await db.AgentTaskLandRequests.SingleAsync(
                r => r.Id == source.CurrentLandRequestId, ct);
            completedRequest.IsPending = false;
            completedRequest.State = LandRequestState.Completed;
            await db.SaveChangesAsync(ct);
            created.WorktreeBase!.Decision.ShouldBe(other is null
                ? CardWorktreeBaseDecision.Target : CardWorktreeBaseDecision.Continue);
        }
        else
            created.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.WaitForLand);

        if (scenario is "all_contained" or "still_divergent")
        {
            await repo.GitAsync("merge", "--no-ff", source.WorktreeBranch!, "-m", "integrate A");
            if (scenario == "all_contained")
                await repo.GitAsync("merge", "--no-ff", other!.WorktreeBranch!, "-m", "integrate X");
            source.LandRequestedAt = null;
            var landRequest = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == source.CurrentLandRequestId, ct);
            landRequest.IsPending = false;
            landRequest.State = LandRequestState.Completed;
            await db.SaveChangesAsync(ct);
        }
        await using (var launchProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var launchScope = launchProvider.CreateAsyncScope())
            await launchScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        var result = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        if (scenario == "still_divergent")
        {
            result.Status.ShouldBe(AgentTaskStatus.Blocked);
            result.FailureReason.ShouldContain("worktree_base_ambiguous");
            result.WorktreePath.ShouldBeNull();
            await repo.GitAsync("merge", "--no-ff", tail!.WorktreeBranch!, "-m", "integrate Y");
            // B is still on the other chain and must also be integrated before Retry.
            await repo.GitAsync("merge", "--no-ff", b!.WorktreeBranch!, "-m", "integrate B");
            await using (var retryProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
            await using (var retryScope = retryProvider.CreateAsyncScope())
                await retryScope.ServiceProvider.GetRequiredService<AgentTaskService>()
                    .RetryAsync(created.Id, ct);
            await using (var resumedProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
            await using (var resumedScope = resumedProvider.CreateAsyncScope())
                await resumedScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            db.ChangeTracker.Clear();
            result = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        }
        result.Status.ShouldBe(AgentTaskStatus.Dispatched);
        result.WorktreePath.ShouldNotBeNull();
        result.WorktreeBaseTaskId.ShouldBe(scenario == "landed_residue_merge" ? other!.Id : null);
        if (scenario is "landed_merge" or "landed_residue_merge")
            (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>
                e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
    }

    [Test]
    [Arguments("auto_diverges")]
    [Arguments("task_deleted")]
    [Arguments("task_dirty")]
    [Arguments("task_active")]
    [Timeout(90_000)]
    public async Task T0442_V22_launch_blocks_when_preview_inputs_worsen(string scenario, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-v22");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var source = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        await db.SaveChangesAsync(ct);
        var sourceSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        var request = new CreateAgentTaskRequest("Continue", Title: "CARD-0442 Code",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            Card: card.Id.ToString("D"));
        if (scenario != "auto_diverges")
            request = request with { WorktreeBaseTask = DelegationReportFormatter.Short(source.Id) };
        AgentTaskCreatedDto created;
        await using (var previewProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var previewScope = previewProvider.CreateAsyncScope())
            created = await previewScope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(request, new AgentTaskService.Caller(null, null, repo.Path), ct);
        created.WorktreeBase!.SourceSha.ShouldBe(sourceSha);

        if (scenario == "auto_diverges")
            await SeedKeptSiblingAsync(db, repo, card.Id, "X", startRef: "master");
        else if (scenario == "task_deleted")
            await repo.GitAsync("branch", "-D", source.WorktreeBranch!);
        else
        {
            var checkout = Path.Combine(repo.WorktreeRoot, source.Id.ToString("N"));
            await repo.GitAsync("worktree", "add", checkout, source.WorktreeBranch!);
            source.WorktreePath = checkout;
            if (scenario == "task_dirty")
                await File.WriteAllTextAsync(Path.Combine(checkout, "untracked.txt"), "operator work\n", ct);
            else
            {
                var writerId = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = writerId, RootTaskId = source.Id, ParentTaskId = source.Id,
                    FollowUpOfTaskId = source.Id, Title = "shared writer", Goal = "shared writer",
                    Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
                    ModelLevel = AgentModelLevel.Low, Workspace = WorkspaceMode.Shared,
                    WorkingDirectory = checkout, WorktreePath = checkout, RepoPath = repo.Path,
                    CardId = card.Id, Status = AgentTaskStatus.Working,
                    ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
                });
            }
        }
        await db.SaveChangesAsync(ct);

        await using (var launchProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var launchScope = launchProvider.CreateAsyncScope())
            await launchScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        var blocked = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.WorktreePath.ShouldBeNull();
        blocked.WorktreeBranch.ShouldBeNull();
        blocked.AgentSessionId.ShouldBeNull();
        blocked.WorktreeBasePreviewJson.ShouldContain(sourceSha);
        blocked.FailureReason.ShouldContain(scenario == "auto_diverges"
            ? "worktree_base_ambiguous" : "requested_source_invalid");
        blocked.FailureReason.ShouldContain("-BaseTask");
        blocked.FailureReason.ShouldContain("-FreshWorktree");
        (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet",
            $"refs/heads/feat/card-task-{DelegationReportFormatter.Short(created.Id)}")).Ok.ShouldBeFalse();
        if (scenario == "task_deleted")
            (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet",
                "refs/heads/" + source.WorktreeBranch)).Ok.ShouldBeFalse();
        else
            (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim().ShouldBe(sourceSha);
    }

    [Test]
    [Timeout(90_000)]
    public async Task T0442_V28_reply_cannot_select_a_blocked_task_source(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-v28");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var source = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        await db.SaveChangesAsync(ct);
        AgentTaskCreatedDto created;
        await using (var createProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = createProvider.CreateAsyncScope())
            created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(new CreateAgentTaskRequest("Continue", Title: "CARD-0442 Code",
                    Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                    Card: card.Id.ToString("D")),
                    new AgentTaskService.Caller(null, null, repo.Path), ct);
        var originalPreview = (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct))
            .WorktreeBasePreviewJson;
        var divergent = await SeedKeptSiblingAsync(db, repo, card.Id, "X", startRef: "master");
        await db.SaveChangesAsync(ct);
        await using (var launchProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = launchProvider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct))
            .Status.ShouldBe(AgentTaskStatus.Blocked);

        await using (var replyProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = replyProvider.CreateAsyncScope())
            await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<AgentTaskReplyService>()
                .AnswerAsync(created.Id, $"Use {divergent.WorktreeBranch} or {source.Id:D}", ct));

        db.ChangeTracker.Clear();
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        after.Status.ShouldBe(AgentTaskStatus.Blocked);
        after.RequestedWorktreeBaseMode.ShouldBe(RequestedWorktreeBaseMode.Auto);
        after.RequestedWorktreeBaseTaskId.ShouldBeNull();
        after.WorktreeBasePreviewJson.ShouldBe(originalPreview);
        after.WorktreePath.ShouldBeNull();
        after.WorktreeBranch.ShouldBeNull();
        after.AgentSessionId.ShouldBeNull();
    }

    [Test]
    [Arguments("deadline")]
    [Arguments("candidate_cap")]
    [Arguments("pending_land_budget")]
    [Timeout(120_000)]
    public async Task T0442_V30_incomplete_dispatch_inspection_keeps_safe_base_and_land_hold(
        string scenario, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-v30");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var a = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        await db.SaveChangesAsync(ct);
        var request = new CreateAgentTaskRequest("Continue", Title: "CARD-0442 Code",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            Card: card.Id.ToString("D"));
        AgentTaskCreatedDto created;
        await using (var createProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = createProvider.CreateAsyncScope())
            created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(request, new AgentTaskService.Caller(null, null, repo.Path), ct);
        created.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        AgentTask? b = null;
        if (scenario != "deadline")
            b = await SeedKeptSiblingAsync(db, repo, card.Id, "B", startRef: "master");
        if (scenario == "pending_land_budget")
        {
            a.LandRequestedAt = DateTime.UtcNow;
            await SeedPendingSiblingLandAsync(db, repo, a);
        }
        await db.SaveChangesAsync(ct);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        ILandingGit? git = scenario == "deadline" ? new CandidateDeadlineGit(clock, a.WorktreeBranch!) : null;
        await using (var launchProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot,
            git: git, clock: scenario == "deadline" ? clock : null,
            maxCandidates: scenario == "deadline" ? null : 1))
        await using (var scope = launchProvider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        var result = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        if (scenario == "pending_land_budget")
        {
            result.Status.ShouldBe(AgentTaskStatus.Queued);
            result.WorktreePath.ShouldBeNull();
            var pending = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == a.CurrentLandRequestId, ct);
            pending.IsPending = false;
            pending.State = LandRequestState.Completed;
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = a.Id, Type = AgentTaskEventType.Landed,
                Detail = "fixture completed", At = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await using (var resumeProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot,
                maxCandidates: 1))
            await using (var scope = resumeProvider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            db.ChangeTracker.Clear();
            result = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
            result.Status.ShouldBe(AgentTaskStatus.Dispatched);
            result.WorktreeBaseTaskId.ShouldBe(b!.Id);
            return;
        }

        result.Status.ShouldBe(AgentTaskStatus.Dispatched);
        result.WorktreeBaseTaskId.ShouldBeNull();
        result.WorktreeBaseSha.ShouldBe((await repo.GitReadAsync("rev-parse", "master")).Trim());
        result.WorktreeBasePreviewJson.ShouldContain(a.Id.ToString("D"));
        var warnings = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == created.Id && i.WarningKey == "worktree-base-preview-changed")
            .ToListAsync(ct);
        warnings.Count.ShouldBe(1);
        warnings[0].Detail.ShouldContain(scenario == "deadline" ? "inspection_timeout" : "candidate_limit");
        result.FailureReason.ShouldBeNull();
    }

    private sealed class CandidateDeadlineGit(FakeTimeProvider clock, string branch) : LandingGit
    {
        public override async Task<LandingGitResult> RunAsync(string repository,
            IReadOnlyList<string> args, CancellationToken ct)
        {
            if (args is ["rev-parse", "--verify", "--quiet", var reference]
                && reference == $"refs/heads/{branch}^{{commit}}")
                clock.Advance(TimeSpan.FromSeconds(2));
            return await base.RunAsync(repository, args, ct);
        }
    }

    [Test]
    [Arguments("auto")]
    [Arguments("target")]
    [Arguments("task")]
    [Timeout(90_000)]
    public async Task T0442_V26_retry_retains_requested_base_intent(string scenario, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-v26");
        await repo.CommitFileAsync("seed.txt", "M\n");
        var masterSha = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var a = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        await db.SaveChangesAsync(ct);
        var aSha = (await repo.GitReadAsync("rev-parse", a.WorktreeBranch!)).Trim();
        var request = new CreateAgentTaskRequest("Continue", Title: "CARD-0442 Code",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            Card: card.Id.ToString("D"));
        if (scenario == "target") request = request with { FreshWorktree = true };
        if (scenario == "task")
            request = request with { WorktreeBaseTask = DelegationReportFormatter.Short(a.Id) };
        AgentTaskCreatedDto created;
        await using (var previewProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var previewScope = previewProvider.CreateAsyncScope())
            created = await previewScope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(request, new AgentTaskService.Caller(null, null, repo.Path), ct);
        var storedPreview = (await db.AgentTasks.AsNoTracking()
            .SingleAsync(t => t.Id == created.Id, ct)).WorktreeBasePreviewJson;
        storedPreview.ShouldNotBeNull();

        // The preceding launch boundary owns this Blocked state. Retry must not treat the
        // operator's retry as a fresh Auto request or silently discard an explicit base.
        await db.AgentTasks.Where(t => t.Id == created.Id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, AgentTaskStatus.Blocked)
            .SetProperty(t => t.FailureReason, "fixture prelaunch refusal"), ct);
        var b = await SeedKeptSiblingAsync(db, repo, card.Id, "B", startRef: a.WorktreeBranch);
        await db.SaveChangesAsync(ct);
        var bSha = (await repo.GitReadAsync("rev-parse", b.WorktreeBranch!)).Trim();
        await using (var retryProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var retryScope = retryProvider.CreateAsyncScope())
        {
            var service = retryScope.ServiceProvider.GetRequiredService<AgentTaskService>();
            await service.RetryAsync(created.Id, ct);
            var retry = await retryScope.ServiceProvider.GetRequiredService<AppDbContext>()
                .AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
            retry.Status.ShouldBe(AgentTaskStatus.Queued);
            retry.Attempt.ShouldBe(2);
            retry.RequestedWorktreeBaseMode.ShouldBe(scenario switch
            {
                "target" => RequestedWorktreeBaseMode.Target,
                "task" => RequestedWorktreeBaseMode.Task,
                _ => RequestedWorktreeBaseMode.Auto,
            });
            retry.RequestedWorktreeBaseTaskId.ShouldBe(scenario == "task" ? a.Id : null);
            retry.WorktreeBasePreviewJson.ShouldBe(storedPreview);
            retry.CardId.ShouldBe(card.Id);
            retry.MergeTargetRef.ShouldBeNull();
        }
        await using (var launchProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var launchScope = launchProvider.CreateAsyncScope())
            await launchScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        var launched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        launched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        launched.WorktreeBaseSha.ShouldBe(scenario switch
        {
            "auto" => bSha,
            "task" => aSha,
            _ => masterSha,
        });
        launched.WorktreeBaseTaskId.ShouldBe(scenario switch
        {
            "auto" => b.Id,
            "task" => a.Id,
            _ => null,
        });
    }

    [Test]
    [Timeout(60_000)]
    public async Task T0442_V14_divergent_create_refuses_and_both_recovery_modes_work(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-divergent-create");
        await repo.CommitFileAsync("README.md", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var a = await SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        var x = await SeedKeptSiblingAsync(db, repo, card.Id, "X", startRef: "master");
        await db.SaveChangesAsync(ct);
        var before = await db.AgentTasks.CountAsync(t => t.CardId == card.Id, ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var request = new CreateAgentTaskRequest("Build CARD-0442", Title: "CARD-0442 code",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            Card: card.Id.ToString("D"));
        var caller = new AgentTaskService.Caller(null, null, repo.Path);
        var conflict = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(request, caller, ct));
        conflict.Code.ShouldBe("worktree_base_ambiguous");
        conflict.Message.ShouldContain(DelegationReportFormatter.Short(a.Id));
        conflict.Message.ShouldContain(DelegationReportFormatter.Short(x.Id));
        conflict.Message.ShouldContain("-BaseTask");
        conflict.Message.ShouldContain("-FreshWorktree");
        (await db.AgentTasks.CountAsync(t => t.CardId == card.Id, ct)).ShouldBe(before);

        var selected = await service.CreateAsync(request with
        {
            WorktreeBaseTask = DelegationReportFormatter.Short(a.Id),
        }, caller, ct);
        selected.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        selected.WorktreeBase.SourceTaskId.ShouldBe(a.Id);
        var fresh = await service.CreateAsync(request with { FreshWorktree = true }, caller, ct);
        fresh.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        fresh.WorktreeBase.CandidateWarnings.ShouldContain(w => w.Contains(a.WorktreeBranch!, StringComparison.Ordinal));
        fresh.WorktreeBase.CandidateWarnings.ShouldContain(w => w.Contains(x.WorktreeBranch!, StringComparison.Ordinal));
    }

    [Test]
    [Timeout(60_000)]
    public async Task T0442_V10_empty_card_preview_names_explicit_destination(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-empty-destination");
        await repo.CommitFileAsync("README.md", "M\n");
        await repo.GitAsync("branch", "release");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var selection = await scope.ServiceProvider.GetRequiredService<AgentTaskWorktreeBaseResolver>()
            .ResolveAsync(new AgentTask
            {
                Id = Guid.NewGuid(), CardId = Guid.NewGuid(), RepoPath = repo.Path,
                Workspace = WorkspaceMode.Worktree, MergeTargetRef = "release",
            }, ct);
        selection.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        selection.FallbackRef.ShouldBe("release");
        selection.LandingTarget.ShouldBe("release");
    }

    [Test]
    [Timeout(60_000)]
    public async Task T0442_V08_missing_original_directory_uses_surviving_checkout(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-surviving-checkout");
        await repo.CommitFileAsync("README.md", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var source = await SeedKeptSiblingAsync(db, repo, card.Id, "A");
        source.RepoPath = Path.Combine(repo.Path, "removed-original");
        source.WorktreePath = repo.Path;
        await db.SaveChangesAsync(ct);
        var sha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var selection = await scope.ServiceProvider.GetRequiredService<AgentTaskWorktreeBaseResolver>()
            .ResolveAsync(new AgentTask
            {
                Id = Guid.NewGuid(), CardId = card.Id, RepoPath = repo.Path,
                Workspace = WorkspaceMode.Worktree,
            }, ct);
        selection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        selection.SourceTaskId.ShouldBe(source.Id);
        selection.SourceSha.ShouldBe(sha);
    }

    [Test]
    [Timeout(60_000)]
    public async Task T0442_V01_real_create_continues_clean_same_card_tip(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-create-continue");
        await repo.CommitFileAsync("README.md", "M\n");
        var target = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0442");
        var source = await SeedKeptSiblingAsync(db, repo, card.Id, "A");
        await db.SaveChangesAsync(ct);
        var sourceSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest("Review A", Title: "CARD-0442 review", Role: AgentTaskRole.Review,
                Workspace: WorkspaceMode.Worktree, Card: card.Id.ToString("D")),
            new AgentTaskService.Caller(null, null, repo.Path), ct);
        created.CardId.ShouldBe(card.Id);
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var next = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        next.WorktreePath.ShouldNotBeNull();
        var actual = (await ScratchGitRepo.GitInAsync(next.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim();
        actual.ShouldBe(sourceSha);
        next.WorktreeBaseSha.ShouldBe(sourceSha);
        next.MergeTargetRef.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(target);
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_sibling_land_in_flight_holds_until_the_base_contains_it(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-hold");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        sibling.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedPendingSiblingLandAsync(db, repo, sibling);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.TickAsync(ct);

        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        var heldEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync(ct);
        heldEvents.ShouldHaveSingleItem();
        heldEvents[0].Detail.ShouldContain(sibling.WorktreeBranch!);
        heldEvents[0].Detail.ShouldContain(DelegationReportFormatter.Short(sibling.Id));
        heldEvents[0].Detail.ShouldContain("is landing");

        var heldSibling = await db.AgentTasks.SingleAsync(t => t.Id == sibling.Id, ct);
        heldSibling.LandRequestedAt = null;
        var landedRequest = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == sibling.CurrentLandRequestId, ct);
        landedRequest.IsPending = false;
        landedRequest.State = LandRequestState.Completed;
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("merge", "--ff-only", sibling.WorktreeBranch!);

        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreePath.ShouldNotBeNull();
        Directory.Exists(dispatched.WorktreePath).ShouldBeTrue();
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(1);
        (await ScratchGitRepo.GitInAsync(
            dispatched.WorktreePath!, "merge-base", "--is-ancestor", sibling.WorktreeBranch!, "HEAD"))
            .Ok.ShouldBeTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_test_design_worktree_is_held_while_its_card_plan_land_is_in_flight(
        CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0146-td-hold");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0146");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0146");
        sibling.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedPendingSiblingLandAsync(db, repo, sibling);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(
            db, repo.Path, card.Id, parentSessionId, role: AgentTaskRole.TestDesign);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.TickAsync(ct);

        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        var heldEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync(ct);
        heldEvents.ShouldHaveSingleItem();
        heldEvents[0].Detail.ShouldContain(sibling.WorktreeBranch!);
        heldEvents[0].Detail.ShouldContain("is landing");
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_kept_sibling_with_no_land_dispatches_with_a_warning_and_whenidle_note(
        CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-warn");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreePath.ShouldNotBeNull();

        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();

        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
        var tip = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "--short", sibling.WorktreeBranch!))
            .StdOut.Trim();
        warning.Detail.ShouldContain(tip);
        warning.Detail.ShouldContain("Land " + DelegationReportFormatter.Short(sibling.Id));

        var notes = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync(ct);
        notes.ShouldHaveSingleItem();
        notes[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        notes[0].Body.ShouldContain(sibling.WorktreeBranch!);
        notes[0].Body.ShouldContain(tip);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C499_V06_ARepairIsHeldWhileItsOwnerIsLanding(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c499-v06");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        // Different card so CARD-0215 sibling-base hold cannot match.
        var ownerCard = await SeedCardAsync(db, "CARD-0499");
        var repairCard = await SeedCardAsync(db, "CARD-0499b");
        var owner = await SeedKeptSiblingAsync(db, repo, ownerCard.Id, "owner work");
        owner.Role = AgentTaskRole.Code;
        owner.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var repair = await SeedQueuedWorktreeTaskAsync(db, repo.Path, repairCard.Id, parentSessionId);
        repair.RepairSourceTaskId = owner.Id;
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        var heldEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync(ct);
        heldEvents.ShouldHaveSingleItem();
        heldEvents[0].Detail.ShouldBe($"{DelegationReportFormatter.Short(owner.Id)} is landing");
        heldEvents[0].Detail.ShouldNotContain("kept branch");

        var liveOwner = await db.AgentTasks.SingleAsync(t => t.Id == owner.Id, ct);
        liveOwner.LandRequestedAt = null;
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("checkout", owner.WorktreeBranch!);
        await dispatcher.TickAsync(ct);
        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_sibling_whose_branch_was_deleted_is_silent(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-gone");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        await repo.GitAsync("branch", "-D", sibling.WorktreeBranch!);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId: null);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_stranded_request_row_with_a_null_column_only_warns(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-stranded");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = sibling.Id,
            Type = AgentTaskEventType.LandRequested,
            Detail = "Land requested",
            At = DateTime.UtcNow.AddMinutes(-1),
        });
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C508_MissingDefaultWarns(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-missing-default");
        await repo.CommitFileAsync("README.md", "base\n");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        var missing = "missing-" + Guid.NewGuid().ToString("N")[..8];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = repo.WorktreeRoot,
            WorktreeAddTimeoutSeconds = 180,
            DefaultBranch = missing,
        });
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddSingleton<LandDeliveryBoundary>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.RepoHead);
        dispatched.WorktreeBaseRef.ShouldBe("HEAD");
        (await ScratchGitRepo.GitInAsync(dispatched.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(head);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var intent = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .SingleAsync(i => i.TaskId == task.Id && i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey, ct);
        intent.Detail.ShouldContain(missing);
        var warning = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == intent.Id, ct);
        warning.Detail.ShouldContain(missing);
        warning.Type.ShouldBe(AgentTaskEventType.Warning);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C508_RepairRecordsOwnerAndSkipsSiblings(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-repair-skip");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var ownerCard = await SeedCardAsync(db, "CARD-0499");
        var repairCard = await SeedCardAsync(db, "CARD-0508");
        var owner = await SeedKeptSiblingAsync(db, repo, ownerCard.Id, "owner work");
        owner.Role = AgentTaskRole.Code;
        await repo.GitAsync("checkout", owner.WorktreeBranch!);
        var ownerSha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var other = await SeedKeptSiblingAsync(db, repo, repairCard.Id, "other sibling");
        other.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var repair = await SeedQueuedWorktreeTaskAsync(db, repo.Path, repairCard.Id, parentSessionId);
        repair.RepairSourceTaskId = owner.Id;
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("checkout", owner.WorktreeBranch!);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Repair);
        dispatched.WorktreeBaseTaskId.ShouldBe(owner.Id);
        dispatched.WorktreeBaseRef.ShouldBe(ownerSha);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, repair.Id, ct);
        db.ChangeTracker.Clear();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == repair.Id, ct)).ShouldBe(0);
        (await db.AgentTaskLandNotifications.CountAsync(
            n => n.TaskId == repair.Id && n.Kind == LandNotificationKind.DispatchBase, ct)).ShouldBe(0);
        // The repair path still carries its OWN pre-existing prep warning (the owner branch is
        // checked out, so the snapshot is routed to an isolated branch). That is not a
        // dispatch-base warning: assert on the shape, not on a bare Warning-event count, or this
        // guard silently also asserts unrelated repair behaviour.
        var warnings = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning)
            .ToListAsync(ct);
        foreach (var warning in warnings)
        {
            warning.Detail.ShouldNotContain(other.WorktreeBranch!);
            warning.Detail.ShouldNotContain("sibling containment was evaluated against");
            warning.Detail.ShouldNotContain("configured default branch");
        }

        warnings.ShouldContain(w => w.Detail.Contains("occupied source", StringComparison.Ordinal));
    }

    /// <summary>
    /// V-16 / G-48, G-49, G-65, G-116: one <c>base-observation-stale</c> intent per successful
    /// claim whose observed base differs from the base the worktree was actually cut from, with
    /// zero, one and two divergent siblings, and none at all when the two refs agree.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(1, false)]
    public async Task C508_GuardRefMismatchWarnedOnce(int divergentSiblings, bool moveTheDefault, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-mismatch");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (card, project) = await SeedCardWithProjectAsync(db, "CARD-0508");
        // M -> P: the project's configured default is observed before the lease, then edited to a
        // different real branch while the lease is held. The locked claim resolves the new one.
        project.BaseBranch = "master";
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("branch", "project-default", "master");
        const string observedRef = "master";
        var expectedRef = moveTheDefault ? "project-default" : "master";

        var siblings = new List<AgentTask>();
        for (var i = 0; i < divergentSiblings; i++)
            siblings.Add(await SeedKeptSiblingAsync(db, repo, card.Id, $"divergent sibling {i}"));

        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        task.ProjectId = project.Id;
        await db.SaveChangesAsync(ct);

        var projectId = project.Id;
        var connection = schema.ConnectionString;
        await using var provider = CreateProvider(
            connection, repo.WorktreeRoot,
            onLeaseAcquired: moveTheDefault
                ? async () =>
                {
                    await using var edit = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
                    await edit.Projects.Where(p => p.Id == projectId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.BaseBranch, "project-default"), ct);
                }
            : null);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseRef.ShouldBe(expectedRef);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);

        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var intents = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == task.Id).ToListAsync(ct);
        var mismatches = intents
            .Where(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey).ToList();
        if (moveTheDefault)
        {
            mismatches.ShouldHaveSingleItem();
            mismatches[0].Detail.ShouldContain(observedRef);
            mismatches[0].Detail.ShouldContain(expectedRef);
            // The pair exists once, under the intent's own preallocated identity.
            var note = await db.AgentTaskLandNotifications.AsNoTracking()
                .SingleAsync(n => n.Id == mismatches[0].NotificationId, ct);
            note.SourceEventId.ShouldBe(mismatches[0].Id);
            note.Kind.ShouldBe(LandNotificationKind.DispatchBase);
            (await db.AgentTaskEvents.CountAsync(e => e.Id == mismatches[0].Id, ct)).ShouldBe(1);
        }
        else
        {
            mismatches.ShouldBeEmpty();
        }

        var siblingIntents = intents
            .Where(i => i.WarningKey.StartsWith(
                DispatchBaseNotificationPayload.SiblingKeyPrefix, StringComparison.Ordinal))
            .ToList();
        siblingIntents.Count.ShouldBe(divergentSiblings);
        foreach (var sibling in siblings)
        {
            siblingIntents.ShouldContain(
                i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(sibling.Id));
        }

        intents.Count.ShouldBe(divergentSiblings + (moveTheDefault ? 1 : 0));
    }

    /// <summary>
    /// V-31 / G-22: containment is evaluated against the ACTUAL configured default, not a hard
    /// <c>master</c>. A sibling rebased (cherry-picked, so a different sha and the same patch) onto
    /// the configured default is contained and stays silent, even though <c>master</c> lacks it.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_RebasedSiblingUsesActualDefault(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-rebased-default");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (card, project) = await SeedCardWithProjectAsync(db, "CARD-0508");
        project.BaseBranch = "release";
        await db.SaveChangesAsync(ct);

        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, "rebased sibling work");
        var siblingTip = (await repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim();
        await repo.GitAsync("checkout", "-b", "release", "master");
        // A commit of its own first, so the replay is a genuinely different sha and not the
        // identical object git produces when the cherry-pick parent is the original parent.
        await repo.CommitFileAsync("release-only.md", "release diverges\n");
        await repo.GitAsync("cherry-pick", siblingTip);
        var releaseTip = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        releaseTip.ShouldNotBe(siblingTip);
        await repo.GitAsync("checkout", "master");

        // The control: plain master does NOT carry the patch, so a hard-coded master base would warn.
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", "master", sibling.WorktreeBranch!)).StdOut)
            .ShouldBeFalse();
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", "release", sibling.WorktreeBranch!)).StdOut)
            .ShouldBeTrue();

        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        task.ProjectId = project.Id;
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseRef.ShouldBe("release");
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);
        dispatched.WorktreeBaseSha.ShouldBe(releaseTip);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id, ct)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(0);
    }

    /// <summary>
    /// V-30 / G-104: the guard observes HEAD independently once the chosen default fails to
    /// resolve — it does not cascade to a lower-priority setting and it does not silently fall
    /// back to <c>master</c>. Here master EXISTS and differs from HEAD, and the sibling is
    /// contained in master but not in HEAD, so a base evaluated against master would be silent.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_GuardPreservesFailedDefault(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-failed-default");
        await repo.CommitFileAsync("README.md", "base\n");
        var basement = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, "sibling above the basement");
        var siblingTip = (await repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim();
        // master carries the sibling's patch; the detached HEAD the guard must observe does not.
        await repo.CommitFileAsync("master-only.md", "master diverges\n");
        await repo.GitAsync("cherry-pick", siblingTip);
        var masterTip = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        masterTip.ShouldNotBe(basement);
        // master must contain the patch, so only a base evaluated at HEAD can warn.
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", "master", sibling.WorktreeBranch!)).StdOut)
            .ShouldBeTrue();
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", basement, sibling.WorktreeBranch!)).StdOut)
            .ShouldBeFalse();
        await repo.GitAsync("checkout", "--detach", basement);

        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        var missing = "missing-" + Guid.NewGuid().ToString("N")[..8];
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, defaultBranch: missing);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.RepoHead);
        dispatched.WorktreeBaseRef.ShouldBe("HEAD");
        dispatched.WorktreeBaseSha.ShouldBe(basement);

        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var intents = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == task.Id).ToListAsync(ct);
        intents.Count.ShouldBe(2);
        var unresolved = intents.Single(i => i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey);
        unresolved.Detail.ShouldContain(missing);
        unresolved.Detail.ShouldNotContain("master");
        var siblingIntent = intents.Single(
            i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(sibling.Id));
        siblingIntent.Detail.ShouldContain(sibling.WorktreeBranch!);
        intents.ShouldNotContain(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey);
    }

    private static async Task MaterializeAndDeliverAsync(IServiceProvider services, Guid taskId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var materializer = services.GetRequiredService<DispatchBaseWarningIntentService>();
        var intents = await db.AgentTaskDispatchWarningIntents.Where(i => i.TaskId == taskId).ToListAsync(ct);
        foreach (var intent in intents)
            await materializer.MaterializeAsync(intent.Id, ct);
        var notifier = services.GetRequiredService<AgentTaskLandNotificationService>();
        var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == taskId).ToListAsync(ct);
        foreach (var note in notes)
            await notifier.ReconcileAsync(note.Id, ct);
    }

    internal static async Task<AgentTask> SeedKeptSiblingAsync(
        AppDbContext db, ScratchGitRepo repo, Guid cardId, string commitMessage,
        string? startRef = null, bool alias = false)
    {
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        await repo.GitAsync("checkout", "-b", branch, startRef ?? "HEAD");
        var file = $"plan-{DelegationReportFormatter.Short(id)}.md";
        if (!alias)
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, file), commitMessage + "\n");
            await repo.GitAsync("add", file);
            await repo.GitAsync("commit", "-m", commitMessage);
        }
        await repo.GitAsync("checkout", "master");

        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-0215 plan",
            Goal = "Write the plan.",
            Role = AgentTaskRole.Plan,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            CardId = cardId,
            WorktreeBranch = branch,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static async Task<AgentTask> SeedQueuedWorktreeTaskAsync(
        AppDbContext db, string repoPath, Guid cardId, Guid? parentSessionId,
        AgentTaskRole role = AgentTaskRole.Code)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = role == AgentTaskRole.TestDesign ? "CARD-0146 test design" : "CARD-0215 execute",
            Goal = role == AgentTaskRole.TestDesign ? "Write the verification section." : "Build the plan.",
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            // The pre-CARD-0442 guard fixtures assert the deliberate target-base path.
            RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target,
            WorkingDirectory = repoPath,
            RepoPath = repoPath,
            CardId = cardId,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static async Task SeedParentSessionAsync(AppDbContext db, Guid parentSessionId)
    {
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentSessionId,
            DefinitionName = "card0215-parent",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            StartedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow,
        });
        await Task.CompletedTask;
    }

    /// <summary>
    /// The same predicate the production guard uses: <c>git cherry base branch</c> means contained
    /// when it prints nothing, or only '-' lines. A '+' line is a patch the base does not carry.
    /// </summary>
    private static bool CherryContains(string cherryStdOut)
    {
        var lines = cherryStdOut.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 || Array.TrueForAll(lines, static line => line.StartsWith('-'));
    }

    internal static async Task<Card> SeedCardAsync(AppDbContext db, string identifier) =>
        (await SeedCardWithProjectAsync(db, identifier)).Card;

    private static async Task<(Card Card, Project Project)> SeedCardWithProjectAsync(
        AppDbContext db, string identifier)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"card0215-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/card0215.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"CARD-0215 {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"{identifier} ancestry",
            Description = "CARD-0215.",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return (card, project);
    }

    /// <summary>
    /// Runs <paramref name="onAcquired"/> once, the first time the repository mutation lease is
    /// taken. That is exactly the window between the dispatcher's pre-lease base observation and
    /// the locked claim, so a test can move the base under the claim deterministically.
    /// </summary>
    private sealed class LeaseHook(IRepositoryMutationLease inner, Func<Task> onAcquired)
        : IRepositoryMutationLease
    {
        private int _fired;

        public async Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
                await onAcquired();
            return await inner.TryAcquireAsync(repository, ct);
        }

        public bool Owns(RepositoryLease lease, string commonDirectory) =>
            inner.Owns(lease, commonDirectory);
    }

    internal static async Task SeedPendingSiblingLandAsync(AppDbContext db, ScratchGitRepo repo, AgentTask sibling)
    {
        var request = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), TaskId = sibling.Id, RequestedAt = sibling.LandRequestedAt!.Value,
            LastEvaluatedAt = sibling.LandRequestedAt.Value,
            LastProgressAt = sibling.LandRequestedAt.Value,
            State = LandRequestState.Queued, IsPending = true,
            ExpectedSourceSha = (await repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim(),
        };
        db.AgentTaskLandRequests.Add(request);
        sibling.CurrentLandRequestId = request.Id;
    }

    internal static ServiceProvider CreateProvider(
        string connectionString,
        string worktreeBase,
        string defaultBranch = "master",
        Func<Task>? onLeaseAcquired = null,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null,
        LandDeliveryBoundary? boundary = null,
        ILandingGit? git = null,
        TimeProvider? clock = null,
        int? maxCandidates = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (git is not null) services.AddSingleton(git);
        if (onLeaseAcquired is not null)
        {
            // Registered BEFORE the graph so its TryAdd keeps this decorator. The hook fires once,
            // after the pre-lease base observation and before the locked claim reads the route.
            services.AddSingleton<IRepositoryMutationLease>(sp =>
                new LeaseHook(new RepositoryMutationLease(sp.GetRequiredService<ILandingGit>()), onLeaseAcquired));
            services.TryAddSingleton<ILandingGit, LandingGit>();
        }

        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            if (interceptor is not null) o.AddInterceptors(interceptor);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = worktreeBase,
            WorktreeAddTimeoutSeconds = 180,
            DefaultBranch = defaultBranch,
            WorktreeBaseMaxCandidates = maxCandidates ?? 16,
        });
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddSingleton<LandDeliveryBoundary>(boundary ?? new LandDeliveryBoundary());
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    internal static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
