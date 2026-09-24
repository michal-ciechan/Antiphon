using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0644 V-3. A retired Worktree follow-up continues at the predecessor's frozen local tip
/// on a new branch. A missing tip refuses with nothing inserted. A retired Shared predecessor
/// takes the fresh Worktree default. Live follow-up, standing <c>-Agent</c> and a routing pin
/// keep that agent's checkout; explicit Worktree plus an existing agent is refused, including
/// when a conflicting row reaches dispatch.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeDefaultContinuationTests
{
    // The 120s budget is the continuation, not the first shared-database migration.
    // A cold container on a busy host otherwise cancels FrozenTip before its assertion.
    [Before(Class)]
    public static Task WarmSharedStoreAsync() => TestDbFixture.Lifecycle.EnsureReadyAsync();

    [Test]
    [Timeout(120_000)]
    public async Task RetiredWorktreeCutsAtPriorTip()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var frozen = await HeadShaAsync(world.Owner.WorktreePath!);
        var mainBranch = await HeadRefAsync(world.Repo.Path);
        var mainSha = await HeadShaAsync(world.Repo.Path);
        var priorBranch = world.Owner.WorktreeBranch!;

        var created = await world.CreateTaskAsync(Follow(world, "continue the committed work"));

        created.Workspace.ShouldBe(WorkspaceMode.Worktree);
        created.AgentId.ShouldBeNull("a retired predecessor starts a fresh process");
        created.FollowUpOfTaskId.ShouldBe(world.Owner.Id);
        created.WorktreeBaseRequestedRef.ShouldBe(frozen, "the local tip is frozen before queueing");
        SamePath(created.WorkingDirectory, world.Repo.Path).ShouldBeTrue("the new task is cut from the repository, not the old checkout");
        (await CreatedDetailAsync(world, created.Id)).ShouldContain(frozen);
        (await CreatedDetailAsync(world, created.Id)).ShouldContain("Worktree");
        (await CreatedDetailAsync(world, created.Id)).ShouldContain(DelegationReportFormatter.Short(world.Owner.Id));

        var (task, sessionId) = await world.DispatchAsync();
        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        task.WorktreeBranch.ShouldBe("feat/card-task-" + DelegationReportFormatter.Short(task.Id));
        task.WorktreeBranch.ShouldNotBe(priorBranch);
        var worktree = task.WorktreePath.ShouldNotBeNull();
        SamePath(worktree, world.Repo.Path).ShouldBeFalse();
        SamePath(worktree, world.Owner.WorktreePath!).ShouldBeFalse();
        (await HeadShaAsync(worktree)).ShouldBe(frozen);
        (await HeadRefAsync(worktree)).ShouldBe("refs/heads/" + task.WorktreeBranch);

        await using (var db = world.CreateContext())
        {
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            SamePath(session.Cwd, worktree).ShouldBeTrue();
        }

        (await HeadRefAsync(world.Repo.Path)).ShouldBe(mainBranch);
        (await HeadShaAsync(world.Repo.Path)).ShouldBe(mainSha);
        (await HeadShaAsync(world.Owner.WorktreePath!)).ShouldBe(frozen, "the predecessor checkout is not reset");
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "rev-parse", priorBranch)).StdOut.Trim().ShouldBe(frozen);
    }

    [Test]
    [Timeout(120_000)]
    public async Task FrozenTipSurvivesPriorRefMovement()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var frozen = await HeadShaAsync(world.Owner.WorktreePath!);
        var mainSha = await HeadShaAsync(world.Repo.Path);
        var priorBranch = world.Owner.WorktreeBranch!;

        var created = await world.CreateTaskAsync(Follow(world, "freeze then move"));
        created.WorktreeBaseRequestedRef.ShouldBe(frozen);

        var moved = await world.CommitInOwnerTreeAsync("move the predecessor after the freeze", push: false);
        moved.ShouldNotBe(frozen);

        var (task, _) = await world.DispatchAsync();
        var worktree = task.WorktreePath.ShouldNotBeNull();
        (await HeadShaAsync(worktree)).ShouldBe(frozen, "dispatch uses the frozen sha, not the branch's new tip");
        (await HeadShaAsync(world.Owner.WorktreePath!)).ShouldBe(moved);
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "rev-parse", priorBranch)).StdOut.Trim().ShouldBe(moved);
        (await HeadShaAsync(world.Repo.Path)).ShouldBe(mainSha);
        task.WorktreeBranch.ShouldNotBe(priorBranch);
        SamePath(worktree, world.Owner.WorktreePath!).ShouldBeFalse();
    }

    [Test]
    [Timeout(120_000)]
    public async Task UnavailablePriorTipRefuses()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var beforeTasks = await world.TaskCountAsync();
        var beforeSessions = await SessionCountAsync(world);
        var beforeTrees = await WorktreeListAsync(world);
        var shortId = DelegationReportFormatter.Short(world.Owner.Id);

        await using (var db = world.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            owner.WorktreeBranch = null;
            await db.SaveChangesAsync();
        }

        var neverDispatched = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(Follow(world, "no tip yet")));
        neverDispatched.Code.ShouldBe("follow_up_source_unavailable");
        string.Join(" ", neverDispatched.Errors[nameof(CreateAgentTaskRequest.FollowUpOnTask)]).ShouldContain(shortId);

        const string deleted = "feat/deleted-c644";
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "branch", deleted, world.OwnerSha)).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "branch", "-D", deleted)).Ok.ShouldBeTrue();
        await using (var db = world.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            owner.WorktreeBranch = deleted;
            owner.WorktreeBaseSha = world.OwnerSha;
            await db.SaveChangesAsync();
        }

        var missing = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(Follow(world, "branch is gone")));
        missing.Code.ShouldBe("follow_up_source_unavailable");
        var message = string.Join(" ", missing.Errors[nameof(CreateAgentTaskRequest.FollowUpOnTask)]);
        message.ShouldContain(shortId);
        message.ShouldContain(deleted);
        message.ShouldNotContain("master");

        (await world.TaskCountAsync()).ShouldBe(beforeTasks);
        (await SessionCountAsync(world)).ShouldBe(beforeSessions);
        (await WorktreeListAsync(world)).ShouldBe(beforeTrees);

        var competing = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(
            Follow(world, "caller start ref stays a conflict") with { WorktreeBaseRequestedRef = world.OwnerSha }));
        competing.Code.ShouldBe("worktree_start_ref_mode");
        (await world.TaskCountAsync()).ShouldBe(beforeTasks);
    }

    [Test]
    [Timeout(120_000)]
    public async Task RetiredSharedGetsFreshWorktree()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        await using (var db = world.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            owner.Workspace = WorkspaceMode.Shared;
            await db.SaveChangesAsync();
        }

        var mainSha = await HeadShaAsync(world.Repo.Path);
        var created = await world.CreateTaskAsync(Follow(world, "shared predecessor is a fresh worktree"));
        created.Workspace.ShouldBe(WorkspaceMode.Worktree);
        created.WorktreeBaseRequestedRef.ShouldBeNull("a Shared predecessor has no branch to freeze");
        created.AgentId.ShouldBeNull();

        var (task, sessionId) = await world.DispatchAsync();
        var worktree = task.WorktreePath.ShouldNotBeNull();
        (await HeadShaAsync(worktree)).ShouldBe(mainSha);
        File.Exists(Path.Combine(worktree, "owner.md")).ShouldBeFalse("the predecessor-only commit is not the fresh base");
        task.WorktreeBranch.ShouldBe("feat/card-task-" + DelegationReportFormatter.Short(task.Id));
        task.WorktreeBranch.ShouldNotBe(world.Owner.WorktreeBranch);
        (await HeadShaAsync(world.Repo.Path)).ShouldBe(mainSha);
        await using var dbAfter = world.CreateContext();
        var session = await dbAfter.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        SamePath(session.Cwd, worktree).ShouldBeTrue();
    }

    [Test]
    [Timeout(120_000)]
    public async Task LiveFollowUpKeepsCwd()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var (agentId, sessionId) = await SeedAgentAsync(world, world.Repo.Path, pool: true);
        await using (var db = world.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            owner.AgentId = agentId;
            owner.AgentKind = AgentKind.ClaudeCode;
            await db.SaveChangesAsync();
        }

        var trees = await WorktreeListAsync(world);
        var created = await world.CreateTaskAsync(Follow(world, "keep the live checkout"));
        created.Workspace.ShouldBe(WorkspaceMode.Shared);
        created.AgentId.ShouldBe(agentId);
        SamePath(created.WorkingDirectory, world.Repo.Path).ShouldBeTrue();
        created.WorktreePath.ShouldBeNull();
        created.WorktreeBaseRequestedRef.ShouldBeNull();

        var (task, launched) = await world.DispatchAsync();
        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        task.WorktreePath.ShouldBeNull("a live process is not given a second checkout");
        task.AgentSessionId.ShouldBe(sessionId);
        launched.ShouldBe(sessionId);
        (await WorktreeListAsync(world)).ShouldBe(trees);
        await using var dbAfter = world.CreateContext();
        var session = await dbAfter.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        SamePath(session.Cwd, world.Repo.Path).ShouldBeTrue();
    }

    [Test]
    [Timeout(120_000)]
    public async Task StandingAndRoutingPinsKeepCwd()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var standingDir = Path.Combine(world.Repo.Path, "standing-seat");
        var pinnedDir = Path.Combine(world.Repo.Path, "pinned-seat");
        Directory.CreateDirectory(standingDir);
        Directory.CreateDirectory(pinnedDir);
        var (standingId, standingSession) = await SeedAgentAsync(world, standingDir, pool: false);
        var (pinnedId, pinnedSession) = await SeedAgentAsync(world, pinnedDir, pool: false);
        var trees = await WorktreeListAsync(world);

        var standing = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "reuse the standing seat", Role: AgentTaskRole.Code) { AgentId = standingId });
        standing.Workspace.ShouldBe(WorkspaceMode.Shared);
        standing.AgentId.ShouldBe(standingId);
        standing.Ephemeral.ShouldBeFalse();
        SamePath(standing.WorkingDirectory, standingDir).ShouldBeTrue();
        (await WarningDetailAsync(world, standing.Id)).ShouldContain("Reusing existing agent");

        await using (var db = world.CreateContext())
        {
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                Role = AgentTaskRole.Code,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentId = pinnedId,
                Reason = "CARD-0644 keep the pinned checkout",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var routed = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "routing pin chooses the seat", Role: AgentTaskRole.Code));
        routed.Workspace.ShouldBe(WorkspaceMode.Shared);
        routed.AgentId.ShouldBe(pinnedId);
        SamePath(routed.WorkingDirectory, pinnedDir).ShouldBeTrue();
        (await WarningDetailAsync(world, routed.Id)).ShouldContain("Reusing existing agent");

        await TickAsync(world);
        await using (var afterStanding = world.CreateContext())
        {
            var standingRow = await afterStanding.AgentTasks.SingleAsync(t => t.Id == standing.Id);
            standingRow.Status.ShouldBe(AgentTaskStatus.Dispatched);
            standingRow.AgentSessionId.ShouldBe(standingSession);
            standingRow.WorktreePath.ShouldBeNull();
            // Two Shared writers in one repository take the lease one at a time. Free the first
            // seat's task so the routing-pin task can reach its own launch boundary.
            standingRow.Status = AgentTaskStatus.Succeeded;
            standingRow.CompletedAt = DateTime.UtcNow;
            await afterStanding.SaveChangesAsync();
        }

        await TickAsync(world);
        await using var after = world.CreateContext();
        var routedRow = await after.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == routed.Id);
        routedRow.Status.ShouldBe(AgentTaskStatus.Dispatched);
        routedRow.AgentSessionId.ShouldBe(pinnedSession);
        routedRow.WorktreePath.ShouldBeNull();
        (await WorktreeListAsync(world)).ShouldBe(trees);
    }

    [Test]
    [Timeout(120_000)]
    public async Task ExplicitWorktreePinRefuses()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var seat = Path.Combine(world.Repo.Path, "explicit-seat");
        Directory.CreateDirectory(seat);
        var (agentId, _) = await SeedAgentAsync(world, seat, pool: false);
        var beforeTasks = await world.TaskCountAsync();
        var beforeSessions = await SessionCountAsync(world);
        var trees = await WorktreeListAsync(world);

        var refused = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(
            new CreateAgentTaskRequest("do not cut a checkout for a pinned process", Role: AgentTaskRole.Code,
                Workspace: WorkspaceMode.Worktree) { AgentId = agentId }));
        refused.Code.ShouldBe("workspace_existing_agent_conflict");
        (await world.TaskCountAsync()).ShouldBe(beforeTasks);
        (await SessionCountAsync(world)).ShouldBe(beforeSessions);
        (await WorktreeListAsync(world)).ShouldBe(trees);

        var conflictId = Guid.NewGuid();
        await using (var db = world.CreateContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = conflictId,
                RootTaskId = conflictId,
                Title = "queued conflict",
                Goal = "queued conflict",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = world.Repo.Path,
                RepoPath = world.Repo.Path,
                AgentId = agentId,
                Ephemeral = false,
                Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = DateTime.UtcNow,
                ConcurrencyToken = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        world.Repair = (await world.CreateContext().AgentTasks.AsNoTracking().SingleAsync(t => t.Id == conflictId));
        var (task, sessionId) = await world.DispatchAsync();
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldNotBeNull().ShouldContain("workspace_existing_agent_conflict");
        task.WorktreePath.ShouldBeNull();
        task.WorktreeBranch.ShouldBeNull();
        task.AgentSessionId.ShouldBeNull();
        sessionId.ShouldBe(Guid.Empty);
        (await SessionCountAsync(world)).ShouldBe(beforeSessions);
        (await WorktreeListAsync(world)).ShouldBe(trees);
    }

    [Test]
    [Timeout(120_000)]
    public async Task ContinuationRetainsCardContextAndPolicy()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var now = DateTime.UtcNow;
        var cardId = Guid.NewGuid();
        await using (var db = world.CreateContext())
        {
            var project = new Project { Id = Guid.NewGuid(), Name = "CARD-0644", CreatedAt = now, UpdatedAt = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "CARD-0644", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(), BoardId = board.Id, Name = "Work", StateKey = "work",
                CreatedAt = now, UpdatedAt = now,
            };
            db.Projects.Add(project);
            db.Boards.Add(board);
            db.BoardColumns.Add(column);
            db.Cards.Add(new Card
            {
                Id = cardId, BoardId = board.Id, BoardColumnId = column.Id,
                Identifier = "CARD-0644", Title = "continuation", CreatedAt = now, UpdatedAt = now,
            });
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            owner.CardId = cardId;
            owner.CommitOnSettle = CommitOnSettlePolicy.Always;
            owner.Goal = "Keep the predecessor goal.";
            owner.Result = "Committed on the task branch.";
            await db.SaveChangesAsync();
        }

        var created = await world.CreateTaskAsync(Follow(world, "carry the card forward"));
        created.CardId.ShouldBe(cardId);
        created.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Always);
        created.FollowUpOfTaskId.ShouldBe(world.Owner.Id);
        created.Goal.ShouldContain("Keep the predecessor goal.");
        created.Goal.ShouldContain("Committed on the task branch.");
        created.Goal.ShouldContain("CARD-0644");
        created.Goal.ShouldEndWith("carry the card forward");
        created.Workspace.ShouldBe(WorkspaceMode.Worktree);
        created.WorktreeBaseRequestedRef.ShouldNotBeNull();

        var shared = await world.CreateTaskAsync(Follow(world, "explicit shared does not continue the branch") with
        {
            Workspace = WorkspaceMode.Shared,
        });
        shared.Workspace.ShouldBe(WorkspaceMode.Shared);
        shared.WorktreeBaseRequestedRef.ShouldBeNull();
        shared.CardId.ShouldBe(cardId);
        shared.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Always);
        shared.FollowUpOfTaskId.ShouldBe(world.Owner.Id);
        var warning = await WarningDetailAsync(world, shared.Id);
        warning.ShouldContain("does not continue");
        warning.ShouldContain(world.Owner.WorktreeBranch!);
    }

    private static CreateAgentTaskRequest Follow(RepairSourceWorld world, string goal) =>
        new(goal, Role: AgentTaskRole.Code)
        {
            FollowUpOnTask = world.Owner.Id.ToString("D"),
        };

    private static async Task TickAsync(RepairSourceWorld world)
    {
        await using var scope = world.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedAgentAsync(
        RepairSourceWorld world, string directory, bool pool)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var name = (pool ? "pool-" : "seat-") + agentId.ToString("N")[..8];
        await using var db = world.CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now.AddMinutes(-30),
            StartedAt = now.AddMinutes(-30),
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = directory,
            Details = "CARD-0644 existing checkout",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = pool,
            PoolIdleSince = pool ? now.AddMinutes(-10) : null,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddMinutes(-30),
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<string> CreatedDetailAsync(RepairSourceWorld world, Guid taskId)
    {
        await using var db = world.CreateContext();
        var detail = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Created)
            .Select(e => e.Detail)
            .SingleAsync();
        return detail ?? "";
    }

    private static async Task<string> WarningDetailAsync(RepairSourceWorld world, Guid taskId)
    {
        await using var db = world.CreateContext();
        var details = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Warning)
            .Select(e => e.Detail)
            .ToListAsync();
        return string.Join(" ", details);
    }

    private static async Task<int> SessionCountAsync(RepairSourceWorld world)
    {
        await using var db = world.CreateContext();
        return await db.AgentSessions.CountAsync();
    }

    private static async Task<string> WorktreeListAsync(RepairSourceWorld world) =>
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "worktree", "list", "--porcelain")).StdOut;

    private static async Task<string> HeadRefAsync(string dir) =>
        (await ScratchGitRepo.GitInAsync(dir, "symbolic-ref", "HEAD")).StdOut.Trim();

    private static async Task<string> HeadShaAsync(string dir) =>
        (await ScratchGitRepo.GitInAsync(dir, "rev-parse", "HEAD")).StdOut.Trim();

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).Replace('\\', '/').TrimEnd('/'),
            Path.GetFullPath(b).Replace('\\', '/').TrimEnd('/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
