using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// CARD-0215: a card-bound Worktree task must not branch from master while a same-card
/// kept sibling is still off to the side. Hold while that sibling's land is in flight;
/// warn (and still dispatch) when the branch is simply not landed; stay silent when the
/// branch is already gone.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentTaskDispatchBaseGuardTests
{
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

        var tip = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", sibling.WorktreeBranch!))
            .StdOut.Trim();
        dispatched.WorktreeBaseSha.ShouldBe(tip);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning
                && e.Detail!.Contains("Land "), ct)).ShouldBe(0);
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("new_descendant")]
    [Arguments("landed_in_queue")]
    [Arguments("unchanged")]
    public async Task T0442_V19(string name, CancellationToken ct)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var created = await world.Services.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest("next", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442"),
            world.Caller(), ct);
        Guid? expectedSource = a.Id;
        if (name == "new_descendant")
        {
            var b = await world.SeedSucceededAsync("B", "code-b.txt", "B\n", fromBranch: a.WorktreeBranch);
            expectedSource = b.Id;
        }
        else if (name == "landed_in_queue")
        {
            await world.Repo.GitAsync("merge", "--ff-only", a.WorktreeBranch!);
            await using var seed = world.CreateDb();
            seed.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = a.Id,
                Type = AgentTaskEventType.Landed,
                Detail = "landed",
                At = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync(ct);
            expectedSource = null;
        }

        await using var scope = world.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        await using var db = world.CreateDb();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id, ct);
        row.Status.ShouldBe(AgentTaskStatus.Dispatched);
        row.WorktreeBaseTaskId.ShouldBe(expectedSource);
        if (name != "unchanged")
        {
            (await db.AgentTaskEvents.CountAsync(
                e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Warning, ct))
                .ShouldBeGreaterThan(0);
        }
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("auto_two")]
    [Arguments("task_two")]
    [Arguments("fresh_two")]
    [Arguments("auto_four")]
    [Arguments("task_four")]
    [Arguments("fresh_four")]
    public async Task T0442_V20(string name, CancellationToken ct)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        AgentTask source;
        if (name.Contains("four", StringComparison.Ordinal))
        {
            var graph = await world.SeedFourTipsAsync();
            source = graph.B;
        }
        else
        {
            var graph = await world.SeedTwoTipsAsync();
            source = graph.A;
        }

        await world.SetLandRequestedAsync(source.Id);
        var mode = name.StartsWith("task", StringComparison.Ordinal) ? AgentTaskWorktreeBaseMode.Task
            : name.StartsWith("fresh", StringComparison.Ordinal) ? AgentTaskWorktreeBaseMode.Target
            : AgentTaskWorktreeBaseMode.Auto;
        var created = await world.CreateNextAsync(mode, source.Id, ct: ct);
        created.WorktreeBase!.Decision.ShouldBe("WaitForLand");
        await world.TickAsync(ct);
        await world.TickAsync(ct);
        var row = await world.ReloadAsync(created.Id);
        row.Status.ShouldBe(AgentTaskStatus.Queued);
        row.WorktreePath.ShouldBeNull();
        await world.AssertNoLaunchAsync(created.Id);
        await using var scope = world.Services.CreateAsyncScope();
        var pipeline = await scope.ServiceProvider.GetRequiredService<AgentTaskPipelineStatusService>().GetAsync(ct);
        pipeline.Stages.SelectMany(s => s.Queued).ShouldContain(q =>
            q.TaskId == created.Id && q.QueueReason == AgentTaskPipelineStatusService.QueueReasonSiblingLandInFlight);
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("auto")]
    [Arguments("target")]
    [Arguments("task")]
    public async Task T0442_V26(string name, CancellationToken ct)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var mode = name switch
        {
            "target" => AgentTaskWorktreeBaseMode.Target,
            "task" => AgentTaskWorktreeBaseMode.Task,
            _ => AgentTaskWorktreeBaseMode.Auto,
        };
        var created = await world.Services.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest("next", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
            {
                FreshWorktree = mode == AgentTaskWorktreeBaseMode.Target,
                WorktreeBaseTask = mode == AgentTaskWorktreeBaseMode.Task ? a.Id.ToString("D") : null,
            },
            world.Caller(), ct);
        await using var db = world.CreateDb();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id, ct);
        row.WorktreeBaseMode.ShouldBe(mode);
        if (mode == AgentTaskWorktreeBaseMode.Task)
            row.RequestedWorktreeBaseTaskId.ShouldBe(a.Id);

        row.Status = AgentTaskStatus.Blocked;
        row.FailureReason = "prelaunch";
        await db.SaveChangesAsync(ct);
        await using var scope = world.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(created.Id, ct);
        var retried = await world.ReloadAsync(created.Id);
        retried.Status.ShouldBe(AgentTaskStatus.Queued);
        retried.WorktreeBaseMode.ShouldBe(mode);
        retried.Attempt.ShouldBeGreaterThan(row.Attempt);
        if (mode == AgentTaskWorktreeBaseMode.Task)
            retried.RequestedWorktreeBaseTaskId.ShouldBe(a.Id);
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("all_contained")]
    [Arguments("still_divergent")]
    [Arguments("landed_merge")]
    [Arguments("landed_residue_merge")]
    public async Task T0442_V21(string name, CancellationToken ct)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        if (name.StartsWith("landed", StringComparison.Ordinal))
        {
            var residue = name.Contains("residue", StringComparison.Ordinal);
            var merge = await world.SeedMergeRangeAsync();
            var remaining = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
            var mergeShort = DelegationReportFormatter.Short(merge.Id);

            // The merge branch is a non-ancestor of master: its content reached master as two
            // cherry-picks, so only the completed-land event can release a hold on it.
            (await ScratchGitRepo.GitInAsync(
                world.Repo.Path, "merge-base", "--is-ancestor", merge.WorktreeBranch!, "master"))
                .Ok.ShouldBeFalse();

            await world.SetLandRequestedAsync(merge.Id);
            var held = await world.CreateNextAsync(ct: ct);
            held.WorktreeBase!.Decision.ShouldBe("WaitForLand");
            held.WorktreeBase.SourceTaskId.ShouldBe(merge.Id);
            await world.TickAsync(ct);
            await world.AssertNoLaunchAsync(held.Id);

            await world.SeedLandedAsync(
                merge.Id, residue ? AgentTaskEventType.LandedWithResidue : AgentTaskEventType.Landed);
            if (residue)
                await world.SetLandRequestedAsync(merge.Id); // a residue cleanup retry is still pending
            else
                await world.ClearLandRequestedAsync(merge.Id);
            var pending = (await world.ReloadAsync(merge.Id)).LandRequestedAt;
            if (residue)
                pending.ShouldNotBeNull();
            else
                pending.ShouldBeNull();

            // A fresh preview taken before anything is dispatched: the landed merge branch is
            // excluded outright, so it costs no candidate Git and raises no uncertainty, and the
            // one remaining tip is selected rather than a two-maxima ambiguity.
            var next = await world.CreateNextAsync(ct: ct);
            var preview = next.WorktreeBase!;
            preview.Decision.ShouldBe("Continue");
            preview.SourceTaskId.ShouldBe(remaining.Id);
            preview.SourceBranch.ShouldBe(remaining.WorktreeBranch);
            preview.Candidates.ShouldNotBeNull();
            preview.Candidates.ShouldNotContain(c => c.TaskId == merge.Id);
            preview.Candidates.ShouldNotContain(c => c.Branch == merge.WorktreeBranch);
            preview.Warnings.ShouldNotBeNull();
            preview.Warnings.ShouldNotContain(w => w.Contains(mergeShort, StringComparison.Ordinal));
            preview.Warnings.ShouldNotContain(w => w.Contains("unknown", StringComparison.OrdinalIgnoreCase));
            preview.Warnings.ShouldNotContain(w => w.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));

            await world.TickAsync(ct);
            var after = await world.ReloadAsync(held.Id);
            after.Status.ShouldBe(AgentTaskStatus.Dispatched);
            after.WorktreeBaseTaskId.ShouldBe(remaining.Id);

            // Negative control: a sibling whose only land evidence is LandRefused still holds.
            var refused = await world.SeedSucceededAsync("R", "r.txt", "R\n");
            await world.SeedLandedAsync(refused.Id, AgentTaskEventType.LandRefused);
            await world.SetLandRequestedAsync(refused.Id);
            var stillHeld = await world.CreateNextAsync(ct: ct);
            stillHeld.WorktreeBase!.Decision.ShouldBe("WaitForLand");
            stillHeld.WorktreeBase.SourceTaskId.ShouldBe(refused.Id);
            await world.TickAsync(ct);
            await world.AssertNoLaunchAsync(stillHeld.Id);
            return;
        }

        var (a, b, x, y) = await world.SeedFourTipsAsync();
        await world.SetLandRequestedAsync(a.Id);
        var queued = await world.CreateNextAsync(ct: ct);
        queued.WorktreeBase!.Decision.ShouldBe("WaitForLand");
        await world.TickAsync(ct);
        await world.AssertNoLaunchAsync(queued.Id);

        if (name == "all_contained")
        {
            await world.Repo.GitAsync("checkout", "master");
            await world.Repo.GitAsync("merge", "--no-ff", b.WorktreeBranch!, "-m", "integrate B");
            await world.Repo.GitAsync("merge", "--no-ff", y.WorktreeBranch!, "-m", "integrate Y");
            await world.SeedLandedAsync(a.Id);
            await using (var db = world.CreateDb())
            {
                var live = await db.AgentTasks.FindAsync([a.Id], ct);
                live!.LandRequestedAt = null;
                await db.SaveChangesAsync(ct);
            }

            await world.TickAsync(ct);
            var released = await world.ReloadAsync(queued.Id);
            released.Status.ShouldBe(AgentTaskStatus.Dispatched);
            released.WorktreeBaseTaskId.ShouldBeNull();
            return;
        }

        await world.Repo.GitAsync("checkout", "master");
        await world.Repo.GitAsync("merge", "--no-ff", a.WorktreeBranch!, "-m", "integrate A only");
        await world.SeedLandedAsync(a.Id);
        await using (var db = world.CreateDb())
        {
            var live = await db.AgentTasks.FindAsync([a.Id], ct);
            live!.LandRequestedAt = null;
            await db.SaveChangesAsync(ct);
        }

        await world.TickAsync(ct);
        var blocked = await world.ReloadAsync(queued.Id);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.FailureReason.ShouldContain("worktree_base_ambiguous");
        await world.AssertNoLaunchAsync(queued.Id);
        await world.Repo.GitAsync("merge", "--no-ff", b.WorktreeBranch!, "-m", "integrate B");
        await world.Repo.GitAsync("merge", "--no-ff", y.WorktreeBranch!, "-m", "integrate Y");
        await using var retryScope = world.Services.CreateAsyncScope();
        await retryScope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(queued.Id, ct);
        await world.TickAsync(ct);
        var done = await world.ReloadAsync(queued.Id);
        done.Status.ShouldBe(AgentTaskStatus.Dispatched);
        x.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("auto_diverges")]
    [Arguments("task_deleted")]
    [Arguments("task_dirty")]
    [Arguments("task_active")]
    public async Task T0442_V22(string name, CancellationToken ct)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var mode = name.StartsWith("task", StringComparison.Ordinal)
            ? AgentTaskWorktreeBaseMode.Task
            : AgentTaskWorktreeBaseMode.Auto;
        var created = await world.CreateNextAsync(mode, a.Id, ct: ct);
        created.Status.ShouldBe(AgentTaskStatus.Queued);

        if (name == "auto_diverges")
            await world.SeedSucceededAsync("X", "x.txt", "X\n");
        else if (name == "task_deleted")
            await world.Repo.GitAsync("branch", "-D", a.WorktreeBranch!);
        else if (name == "task_dirty")
        {
            await using var db = world.CreateDb();
            var live = await db.AgentTasks.FindAsync([a.Id], ct);
            live!.WorktreePath = world.Repo.Path;
            await db.SaveChangesAsync(ct);
            await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "README.md"), "dirty\n");
        }
        else
        {
            await using var db = world.CreateDb();
            var follow = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = follow, RootTaskId = follow, Title = "writer", Goal = "writer",
                Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = world.Repo.Path, RepoPath = world.Repo.Path,
                CardId = world.Card.Id, FollowUpOfTaskId = a.Id, Status = AgentTaskStatus.Working,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        await world.TickAsync(ct);
        var row = await world.ReloadAsync(created.Id);
        row.Status.ShouldBe(AgentTaskStatus.Blocked);
        if (name == "auto_diverges")
            row.FailureReason.ShouldContain("worktree_base_ambiguous");
        row.WorktreeBaseMode.ShouldBe(mode);
        await world.AssertNoLaunchAsync(created.Id);
    }

    [Test]
    [Timeout(60_000)]
    public async Task T0442_V28(CancellationToken ct)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var created = await world.CreateNextAsync(ct: ct);
        await world.SeedSucceededAsync("X", "x.txt", "X\n");
        await world.TickAsync(ct);
        var blocked = await world.ReloadAsync(created.Id);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await Should.ThrowAsync<ConflictException>(() =>
            replies.AnswerAsync(created.Id, $"use {a.WorktreeBranch}", ct));
        var after = await world.ReloadAsync(created.Id);
        after.WorktreeBaseMode.ShouldBe(AgentTaskWorktreeBaseMode.Auto);
        after.RequestedWorktreeBaseTaskId.ShouldBeNull();
        after.Status.ShouldBe(AgentTaskStatus.Blocked);
        await world.AssertNoLaunchAsync(created.Id);
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("deadline")]
    [Arguments("candidate_cap")]
    [Arguments("pending_land_budget")]
    public async Task T0442_V30(string name, CancellationToken ct)
    {
        var settings = new GitSettings
        {
            WorktreeBasePath = Path.GetTempPath(),
            WorktreeBaseMaxCandidates = name == "candidate_cap" ? 2 : 16,
            WorktreeBaseMaxGitCommands = name is "pending_land_budget" or "deadline" ? 1 : 128,
            WorktreeBaseInspectionTimeoutSeconds = 2,
        };
        await using var world = await WorktreeContinuityHarness.CreateAsync(settings);
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        if (name == "candidate_cap")
        {
            await world.SeedSucceededAsync("B", "b.txt", "B\n");
            await world.SeedSucceededAsync("C", "c.txt", "C\n");
        }

        if (name == "pending_land_budget")
            await world.SetLandRequestedAsync(a.Id);

        var created = await world.CreateNextAsync(ct: ct);
        if (name == "pending_land_budget")
            created.WorktreeBase!.Decision.ShouldBe("WaitForLand");
        else if (name == "candidate_cap")
            created.WorktreeBase!.Reason.ShouldBe("candidate_limit");

        await world.TickAsync(ct);
        var row = await world.ReloadAsync(created.Id);
        if (name == "pending_land_budget")
        {
            row.Status.ShouldBe(AgentTaskStatus.Queued);
            await world.AssertNoLaunchAsync(created.Id);
            await world.SeedLandedAsync(a.Id);
            await using var db = world.CreateDb();
            var live = await db.AgentTasks.FindAsync([a.Id], ct);
            live!.LandRequestedAt = null;
            await db.SaveChangesAsync(ct);
            await world.TickAsync(ct);
            var released = await world.ReloadAsync(created.Id);
            released.Status.ShouldBe(AgentTaskStatus.Dispatched);
        }
        else
        {
            row.WorktreeBaseTaskId.ShouldBeNull();
            if (row.Status == AgentTaskStatus.Dispatched)
                row.WorktreePath.ShouldNotBeNull();
            else
                await world.AssertNoLaunchAsync(created.Id);
        }
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
    public async Task a_stranded_request_row_with_a_null_column_continues(CancellationToken ct)
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
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(0);
        var tip = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", sibling.WorktreeBranch!))
            .StdOut.Trim();
        dispatched.WorktreeBaseSha.ShouldBe(tip);
    }

    private static async Task<AgentTask> SeedKeptSiblingAsync(
        AppDbContext db, ScratchGitRepo repo, Guid cardId, string commitMessage)
    {
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        await repo.GitAsync("checkout", "-b", branch);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "plan.md"), "the plan\n");
        await repo.GitAsync("add", "plan.md");
        await repo.GitAsync("commit", "-m", commitMessage);
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

    private static async Task<Card> SeedCardAsync(AppDbContext db, string identifier)
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
        return card;
    }

    private static ServiceProvider CreateProvider(string connectionString, string worktreeBase)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
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
            WorktreeBasePath = worktreeBase,
            WorktreeAddTimeoutSeconds = 180,
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
