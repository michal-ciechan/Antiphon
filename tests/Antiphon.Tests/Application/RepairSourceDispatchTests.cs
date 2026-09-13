using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RepairSourceDispatchTests
{
    [Test]
    public async Task C499_V02_FreshCodeWorktreeRepairIsAccepted()
    {
        using var repo = new ScratchGitRepo("c499-v02");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var db = CreateContext();
        var owner = await SeedOwnerAsync(db, repo);
        var parent = await SeedParentAsync(db, repo);
        var service = CreateService(db, repo);
        var created = await service.CreateAsync(
            new CreateAgentTaskRequest("repair the owner", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
            { RepairSourceTaskId = owner.Id },
            new AgentTaskService.Caller(parent, null, repo.Path),
            CancellationToken.None);
        var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        row.RepairSourceTaskId.ShouldBe(owner.Id);
        row.Workspace.ShouldBe(WorkspaceMode.Worktree);
        row.MergeTargetRef.ShouldBeNull();
    }

    [Test]
    public async Task C499_V02b_ExplicitOwnerBranchTargetIsAccepted()
    {
        using var repo = new ScratchGitRepo("c499-v02b");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var db = CreateContext();
        var owner = await SeedOwnerAsync(db, repo);
        var service = CreateService(db, repo);
        var created = await service.CreateAsync(
            new CreateAgentTaskRequest("repair", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                MergeTargetRef: owner.WorktreeBranch)
            { RepairSourceTaskId = owner.Id },
            new AgentTaskService.Caller(null, null, repo.Path),
            CancellationToken.None);
        (await db.AgentTasks.SingleAsync(t => t.Id == created.Id)).MergeTargetRef.ShouldBe(owner.WorktreeBranch);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C499_V03_OccupiedSourceRoutesToAnIsolatedBranchAtTheOwnerSha()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var before = (await ScratchGitRepo.GitInAsync(world.Repo.Path, "worktree", "list", "--porcelain", "-z")).StdOut
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Count(s => s.StartsWith("worktree ", StringComparison.Ordinal));
        var (repair, _) = await world.DispatchAsync();
        repair.Status.ShouldBe(AgentTaskStatus.Dispatched);
        repair.WorktreeBranch.ShouldBe("feat/card-task-" + DelegationReportFormatter.Short(repair.Id));
        repair.WorktreePath.ShouldNotBe(world.Owner.WorktreePath);
        (await ScratchGitRepo.GitInAsync(repair.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(world.OwnerSha);
        (await ScratchGitRepo.GitInAsync(world.Owner.WorktreePath!, "symbolic-ref", "HEAD")).StdOut.Trim()
            .ShouldBe(world.OwnerRef);
        var after = (await ScratchGitRepo.GitInAsync(world.Repo.Path, "worktree", "list", "--porcelain", "-z")).StdOut
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Count(s => s.StartsWith("worktree ", StringComparison.Ordinal));
        after.ShouldBe(before + 1);
        var baseline = TaskProgressJson.TryReadBaseline(repair.ProgressBaselineJson).ShouldNotBeNull();
        baseline!.Primary.FullRef.ShouldBe("refs/heads/" + repair.WorktreeBranch);
        baseline.Primary.LocalSha.ShouldBe(world.OwnerSha);
        baseline.RepairSource.ShouldNotBeNull();
        baseline.RepairSource!.FullRef.ShouldBe(world.OwnerRef);
        baseline.RepairSource.LocalSha.ShouldBe(world.OwnerSha);
        baseline.RepairSource.Remote.State.ShouldBe(ProgressRemoteState.Present);
        baseline.RepairSource.Remote.Sha.ShouldBe(world.OwnerSha);
        baseline.RepairSource.Remote.EndpointFingerprint.ShouldNotBeNull();
        baseline.RepairSource.Remote.EndpointFingerprint!.Length.ShouldBe(64);
        baseline.FileProbeCutoff.ShouldBe(repair.DispatchedAt!.Value, TimeSpan.FromSeconds(5));
        repair.WorktreeBaseSha.ShouldBe(world.OwnerSha);
        var warnings = await world.Warnings();
        warnings.ShouldContain(w => w.Detail.Contains("occupied", StringComparison.OrdinalIgnoreCase)
            && w.Detail.Contains(world.Owner.WorktreePath!));
        await using var db = world.CreateContext();
        var queued = await db.SessionQueuedMessages.SingleAsync(m =>
            m.AgentSessionId == repair.AgentSessionId && m.Origin == QueuedMessageOrigin.Delegation);
        var brief = await world.BriefTextAsync(repair, queued);
        brief.ShouldContain(DelegationReportFormatter.Short(world.Owner.Id));
        brief.ShouldContain(world.OwnerRef);
        brief.ShouldContain(world.OwnerSha);
        brief.ShouldContain(repair.WorktreePath!);
        brief.ShouldContain(repair.WorktreeBranch!);
        brief.ShouldContain("integration: not requested");
        brief.ShouldContain($"[antiphon-progress:{repair.Id:D} commit=");
    }

    [Test]
    [Timeout(60_000)]
    public async Task C499_V04_DirtyOwnerFilesAreNotSnapshotted()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(world.Owner.WorktreePath!, "scratch.txt"), "untracked\n");
        await File.AppendAllTextAsync(Path.Combine(world.Owner.WorktreePath!, "owner.md"), "dirty\n");
        var (repair, _) = await world.DispatchAsync();
        File.Exists(Path.Combine(repair.WorktreePath!, "scratch.txt")).ShouldBeFalse();
        (await File.ReadAllTextAsync(Path.Combine(repair.WorktreePath!, "owner.md"))).ShouldNotContain("dirty");
        (await world.Warnings()).ShouldContain(w => w.Detail.Contains("uncommitted", StringComparison.OrdinalIgnoreCase));
        File.Exists(Path.Combine(world.Owner.WorktreePath!, "scratch.txt")).ShouldBeTrue();
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("ambiguous")]
    [Arguments("mismatched")]
    [Arguments("replaced")]
    public async Task C499_V05_AmbiguousOrMismatchedRegistrationRefusesBeforeLaunch(string kind)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        if (kind == "ambiguous")
        {
            var extra = Path.Combine(world.Repo.WorktreeRoot, "forced-" + Guid.NewGuid().ToString("N")[..8]);
            (await ScratchGitRepo.GitInAsync(world.Repo.Path, "worktree", "add", "--force", extra, world.Owner.WorktreeBranch!))
                .Ok.ShouldBeTrue();
        }
        else if (kind == "mismatched")
        {
            (await ScratchGitRepo.GitInAsync(world.Owner.WorktreePath!, "checkout", "--detach")).Ok.ShouldBeTrue();
        }
        else
        {
            (await ScratchGitRepo.GitInAsync(world.Repo.Path, "worktree", "remove", "--force", world.Owner.WorktreePath!)).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(world.Repo.Path, "branch", "-D", world.Owner.WorktreeBranch!)).Ok.ShouldBeTrue();
        }

        var sessionsBefore = await CountSessions(world);
        var (repair, _) = await world.DispatchAsync();
        repair.Status.ShouldBe(AgentTaskStatus.Failed);
        repair.FailureReason.ShouldContain("repair_source_identity_unavailable");
        repair.FailureReason.ShouldContain(DelegationReportFormatter.Short(world.Owner.Id));
        repair.WorktreePath.ShouldBeNull();
        (await CountSessions(world)).ShouldBe(sessionsBefore);
        Directory.GetDirectories(world.Repo.WorktreeRoot, "card-task-" + DelegationReportFormatter.Short(repair.Id) + "*")
            .ShouldBeEmpty();
    }

    [Test]
    [Timeout(60_000)]
    public async Task C499_V07_ABaselineIsPersistedOnlyWithTheClaimAndRecapturedOnRetry()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        world.Fault.Armed = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => world.DispatchAsync());
        await using (var db = world.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == world.Repair.Id);
            row.Status.ShouldBe(AgentTaskStatus.Queued);
            row.ProgressBaselineJson.ShouldBeNull();
            row.DispatchedAt.ShouldBeNull();
        }
        world.Fault.Armed = false;
        var leftover = Directory.GetDirectories(world.Repo.WorktreeRoot)
            .FirstOrDefault(d => d.Contains(DelegationReportFormatter.Short(world.Repair.Id), StringComparison.OrdinalIgnoreCase));
        leftover.ShouldNotBeNull();
        var (repair, _) = await world.DispatchAsync();
        repair.Status.ShouldBe(AgentTaskStatus.Dispatched);
        repair.WorktreePath.ShouldBe(leftover);
        TaskProgressJson.TryReadBaseline(repair.ProgressBaselineJson)!.Primary.LocalSha.ShouldBe(world.OwnerSha);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C499_V07b_RemoteFailureStillDispatchesWithAnUnavailableBaseline()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        world.Git.BeforeCommand = (_, args) => args.Count > 0 && args[0] == "ls-remote"
            ? Task.FromResult<LandingGitResult?>(new LandingGitResult(128, "", "git_exit_128"))
            : Task.FromResult<LandingGitResult?>(null);
        var (repair, _) = await world.DispatchAsync();
        repair.Status.ShouldBe(AgentTaskStatus.Dispatched);
        var baseline = TaskProgressJson.TryReadBaseline(repair.ProgressBaselineJson);
        baseline!.RepairSource!.Remote.State.ShouldBe(ProgressRemoteState.Unavailable);
        (await world.Warnings()).ShouldContain(w => w.Detail.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    [Arguments("source-landing")]
    [Arguments("pinned-agent")]
    [Arguments("follow-up")]
    [Arguments("shared")]
    [Arguments("read-only")]
    [Arguments("plan-role")]
    [Arguments("orchestrator-kind")]
    public async Task C499_R25_ForbiddenCombinationsAreRefused(string kind)
    {
        using var repo = new ScratchGitRepo("c499-r25");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var db = CreateContext();
        var owner = await SeedOwnerAsync(db, repo);
        var service = CreateService(db, repo);
        var request = kind switch
        {
            "source-landing" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                { RepairSourceTaskId = owner.Id, SourceLandingOperationId = Guid.NewGuid() },
            "pinned-agent" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, AgentId: Guid.NewGuid())
                { RepairSourceTaskId = owner.Id },
            "follow-up" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, FollowUpOnTask: owner.Id.ToString("N")[..8])
                { RepairSourceTaskId = owner.Id },
            "shared" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared)
                { RepairSourceTaskId = owner.Id },
            "read-only" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.ReadOnly)
                { RepairSourceTaskId = owner.Id },
            "plan-role" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Worktree)
                { RepairSourceTaskId = owner.Id },
            _ => new CreateAgentTaskRequest("x", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                { RepairSourceTaskId = owner.Id },
        };
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(request, new AgentTaskService.Caller(null, null, repo.Path), CancellationToken.None));
        ex.Code.ShouldBe("repair_source_mode");
        ex.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.RepairSourceTaskId));
        (await db.AgentTasks.CountAsync(t => t.RepairSourceTaskId == owner.Id)).ShouldBe(0);
    }

    [Test]
    [Arguments("unknown-guid")]
    [Arguments("no-branch")]
    [Arguments("plan-owner")]
    [Arguments("other-repo")]
    [Arguments("published")]
    public async Task C499_R26_AForeignOwnerIsRefused(string kind)
    {
        using var repo = new ScratchGitRepo("c499-r26");
        await repo.CommitFileAsync("README.md", "base\n");
        using var other = new ScratchGitRepo("c499-r26-other");
        await other.CommitFileAsync("README.md", "other\n");
        await using var db = CreateContext();
        var ownerRepo = kind == "other-repo" ? other : repo;
        var owner = await SeedOwnerAsync(db, ownerRepo);
        if (kind == "no-branch") owner.WorktreeBranch = null;
        if (kind == "plan-owner") owner.Role = AgentTaskRole.Plan;
        if (kind == "published")
        {
            var op = PublishedLanding(owner.Id);
            db.AgentTaskLandings.Add(op);
            owner.ActiveLandingId = op.Id;
        }
        await db.SaveChangesAsync();
        var extra = kind == "other-repo" ? new[] { other.Path } : null;
        var service = CreateService(db, repo, extra);
        var id = kind == "unknown-guid" ? Guid.NewGuid() : owner.Id;
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                    { RepairSourceTaskId = id },
                new AgentTaskService.Caller(null, null, repo.Path),
                CancellationToken.None));
        ex.Code.ShouldBe(kind switch
        {
            "unknown-guid" => "repair_source_not_found",
            "published" => "repair_source_published",
            _ => "repair_source_owner_invalid",
        });
    }

    [Test]
    public async Task C499_R27_ADifferentExplicitMergeTargetIsRefused()
    {
        using var repo = new ScratchGitRepo("c499-r27");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var db = CreateContext();
        var owner = await SeedOwnerAsync(db, repo);
        var service = CreateService(db, repo);
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, MergeTargetRef: "master")
                    { RepairSourceTaskId = owner.Id },
                new AgentTaskService.Caller(null, null, repo.Path),
                CancellationToken.None));
        ex.Code.ShouldBe("repair_source_merge_target_mismatch");
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_R08_RelaunchAndRetryKeepTheOriginalBaseline()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, sessionId) = await world.DispatchAsync();
        var original = repair.ProgressBaselineJson;
        original.ShouldNotBeNull();
        await world.CommitInOwnerTreeAsync("owner continued", push: true);
        await using (var scope = world.Services.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await dispatcher.RelaunchWedgedAsync(repair.Id, sessionId, CancellationToken.None);
        }
        await using (var db = world.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == repair.Id)).ProgressBaselineJson.ShouldBe(original);
        }

        await using (var scope = world.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
            await tasks.RetryAsync(repair.Id, CancellationToken.None);
        }
        await world.DispatchAsync();
        await using (var db = world.CreateContext())
        {
            var retried = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
            retried.ProgressBaselineJson.ShouldBe(original);
            retried.Status = AgentTaskStatus.Succeeded;
            await db.SaveChangesAsync();
        }

        await using var followScope = world.Services.CreateAsyncScope();
        var followUp = await followScope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .CreateAsync(
                new CreateAgentTaskRequest("follow-up repair", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                    { RepairSourceTaskId = world.Owner.Id },
                new AgentTaskService.Caller(null, null, world.Repo.Path),
                CancellationToken.None);
        await using (var db = world.CreateContext())
        {
            world.Repair = await db.AgentTasks.SingleAsync(t => t.Id == followUp.Id);
        }
        var (fresh, _) = await world.DispatchAsync();
        var freshBaseline = TaskProgressJson.TryReadBaseline(fresh.ProgressBaselineJson)!;
        var originalBaseline = TaskProgressJson.TryReadBaseline(original)!;
        freshBaseline.CapturedAt.ShouldNotBe(originalBaseline.CapturedAt);
        freshBaseline.RepairSource!.LocalSha.ShouldNotBe(originalBaseline.RepairSource!.LocalSha);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C499_R37_LocalIdentityFailureRefusesDispatch()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        world.Git.BeforeCommand = (_, args) =>
            args.Count >= 2 && args[0] == "rev-parse" && args.Any(a => a.Contains(world.Owner.WorktreeBranch!, StringComparison.Ordinal))
                ? Task.FromResult<LandingGitResult?>(new LandingGitResult(128, "", "git_exit_128"))
                : Task.FromResult<LandingGitResult?>(null);
        var (repair, _) = await world.DispatchAsync();
        repair.Status.ShouldBe(AgentTaskStatus.Failed);
        repair.FailureReason.ShouldContain("repair_source_identity_unavailable");
        repair.ProgressBaselineJson.ShouldBeNull();
        repair.AgentSessionId.ShouldBeNull();
        repair.WorktreePath.ShouldBeNull();
    }

    private static async Task<int> CountSessions(RepairSourceWorld world)
    {
        await using var db = world.CreateContext();
        return await db.AgentSessions.CountAsync();
    }

    private static async Task<AgentTask> SeedOwnerAsync(AppDbContext db, ScratchGitRepo repo)
    {
        var id = Guid.NewGuid();
        var branch = "feat/card-task-" + DelegationReportFormatter.Short(id);
        await repo.GitAsync("branch", branch);
        var owner = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "owner", Goal = "owner",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = repo.Path, RepoPath = repo.Path,
            WorktreePath = repo.Path, WorktreeBranch = branch,
            Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        db.AgentTasks.Add(owner);
        await db.SaveChangesAsync();
        return owner;
    }

    private static async Task<AgentTask> SeedParentAsync(AppDbContext db, ScratchGitRepo repo)
    {
        var id = Guid.NewGuid();
        var parent = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "parent", Goal = "parent",
            Kind = AgentTaskKind.Orchestrator, Role = AgentTaskRole.Plan,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = repo.Path, RepoPath = repo.Path,
            WorktreeBranch = "feat/parent", Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(parent);
        await db.SaveChangesAsync();
        return parent;
    }

    private static AgentTaskLanding PublishedLanding(Guid ownerId)
    {
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = ownerId, SchemaVersion = 1,
            SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            RepositoryPath = "repo", CommonDirectory = "common", GitDirectory = "git", WorktreePath = "tree",
            OriginalSourceSha = new string('a', 40), RebasedSourceSha = new string('a', 40), VerifiedSourceSha = new string('a', 40),
            TargetBeforeSha = new string('b', 40), ObservedRemoteTargetSha = new string('a', 40),
            RemoteFingerprint = new string('c', 64), RemoteConfirmedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow,
            Publication = LandPublicationOutcome.Landed, VerificationSkipReason = "base_unchanged",
            SourcePinned = true, TargetPinned = true, PreparedPinned = true,
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry", Phase = LandPhase.Prepared,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}";
        return op;
    }

    private static AgentTaskService CreateService(AppDbContext db, ScratchGitRepo repo, IReadOnlyList<string>? extraRoots = null)
    {
        var roots = extraRoots is null ? new List<string> { repo.Path } : extraRoots.Prepend(repo.Path).ToList();
        var settings = new DelegationSettings { MaxDepth = 5, MaxTasksPerRoot = 40, AllowedRoots = roots };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            landingGit: new LandingGit());
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
