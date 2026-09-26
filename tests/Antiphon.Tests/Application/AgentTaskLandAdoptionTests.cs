using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandAdoptionTests
{
    [Test]
    public async Task C753_InterruptedLocalAdvanceResumesOnlyPinnedOldCheckout()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var oldLocal = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var replacement = Path.Combine(h.Fixture.Root, "trees", "resume-reviewed");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", replacement,
            h.Fixture.SeedSha);
        await File.WriteAllTextAsync(Path.Combine(replacement, "resume.txt"), "replacement\n");
        await h.Fixture.RequiredAsync(replacement, "add", ".");
        await h.Fixture.RequiredAsync(replacement, "commit", "-m", "reviewed replacement");
        var reviewed = (await h.Fixture.RequiredAsync(replacement, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(replacement, "push", "--force-with-lease", "origin",
            $"HEAD:{h.Fixture.SourceRef}");
        Guid evidence;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.Status = AgentTaskStatus.Failed;
            evidence = await AddReviewAsync(db, owner, reviewed);
        }
        var accepted = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == accepted.RequestId);
            request.RecoveryLocalBeforeSha = oldLocal;
            request.RecoveryOwnerRemoteBeforeSha = reviewed;
            request.SourceResolutionState = LandSourceResolutionState.AdvanceStarted;
            request.SourceAdvanceChildOperation = "source-adopt-reset";
            await db.SaveChangesAsync();
        }
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "--no-deref",
            h.Fixture.SourceRef, reviewed, oldLocal);

        await h.RunQueuedAsync();

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(reviewed);
    }

    [Test]
    public async Task C753_DirtyOwnerCheckoutRefusesBeforeReviewedSourceMutation()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var reviewed = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        Guid evidence;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.Status = AgentTaskStatus.Failed;
            evidence = await AddReviewAsync(db, owner, reviewed);
        }
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "untracked-recovery.txt"), "unreviewed\n");

        await h.RunQueuedAsync();

        await using var check = h.CreateContext();
        var row = await check.AgentTaskLandRequests.SingleAsync(r => r.Id == request.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty");
        (await check.AgentTaskLandings.CountAsync()).ShouldBe(0);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(reviewed);
    }

    [Test]
    public async Task C753_MovedReviewedRemoteTipRefusesWithoutTargetPublication()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var reviewed = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        Guid evidence;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.Status = AgentTaskStatus.Failed;
            evidence = await AddReviewAsync(db, owner, reviewed);
        }
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        var other = Path.Combine(h.Fixture.Root, "trees", "moved-source");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", other, reviewed);
        await File.WriteAllTextAsync(Path.Combine(other, "later.txt"), "different source\n");
        await h.Fixture.RequiredAsync(other, "add", ".");
        await h.Fixture.RequiredAsync(other, "commit", "-m", "move reviewed tip");
        var moved = (await h.Fixture.RequiredAsync(other, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(other, "push", "origin", $"HEAD:{h.Fixture.SourceRef}");

        await h.RunQueuedAsync();

        await using var check = h.CreateContext();
        var row = await check.AgentTaskLandRequests.SingleAsync(r => r.Id == request.RequestId);
        row.SourceRefusalReason.ShouldBe("recovery_source_not_remote_tip");
        (await check.AgentTaskLandings.CountAsync()).ShouldBe(0);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(moved);
    }

    [Test]
    public async Task C753_ReviewedSelfRecoveryAlignsRewrittenOwnerSourceAndPublishes()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var repairPath = Path.Combine(h.Fixture.Root, "trees", "reviewed-self");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", repairPath,
            h.Fixture.SeedSha);
        await File.WriteAllTextAsync(Path.Combine(repairPath, "recovery.txt"), "reviewed rewrite\n");
        await h.Fixture.RequiredAsync(repairPath, "add", ".");
        await h.Fixture.RequiredAsync(repairPath, "commit", "-m", "reviewed recovery");
        var reviewed = (await h.Fixture.RequiredAsync(repairPath, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(repairPath, "push", "--force-with-lease", "origin",
            $"HEAD:{h.Fixture.SourceRef}");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(local);
        Guid evidenceId;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.Status = AgentTaskStatus.Failed;
            owner.WorktreeBaseSha = h.Fixture.SeedSha;
            evidenceId = await AddReviewAsync(db, owner, reviewed);
        }

        var queued = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidenceId,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.RecoveryMode.ShouldBe(LandRecoveryMode.OwnerReviewedSource);
        op.RecoveryLocalBeforeSha.ShouldBe(local);
        op.RecoveryOwnerRemoteBeforeSha.ShouldBe(reviewed);
        op.OriginalSourceSha.ShouldBe(reviewed);
        await using var observer = h.CreateContext();
        (await observer.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).Status.ShouldBe(AgentTaskStatus.Failed);
        (await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
            .RecoveryOwnerRemoteAfterSha.ShouldBe(reviewed);
    }

    [Test]
    [Arguments("success")]
    [Arguments("local-cas")]
    [Arguments("remote-lease")]
    [Arguments("push-rejected")]
    public async Task C753_ReviewedRepairAdoptionObeysLocalCasAndRemoteLease(string scenario)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var ownerBefore = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var sourceId = Guid.NewGuid();
        var sourceRef = $"refs/heads/feat/card-task-{sourceId:N}";
        var sourcePath = Path.Combine(h.Fixture.Root, "trees", "reviewed-repair");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "-b", sourceRef[11..],
            sourcePath, ownerBefore);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "repair.txt"), "reviewed repair\n");
        await h.Fixture.RequiredAsync(sourcePath, "add", ".");
        await h.Fixture.RequiredAsync(sourcePath, "commit", "-m", "repair owner source");
        var reviewed = (await h.Fixture.RequiredAsync(sourcePath, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(sourcePath, "push", "origin", sourceRef);
        Guid evidenceId;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.Status = AgentTaskStatus.Failed;
            var now = DateTime.UtcNow;
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var columnId = Guid.NewGuid();
            var cardId = Guid.NewGuid();
            db.Projects.Add(new Project
            {
                Id = projectId, Name = "reviewed recovery fixture",
                LocalRepositoryPath = h.Fixture.Repository, CreatedAt = now, UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId, ProjectId = projectId, Name = "reviewed recovery", CreatedAt = now, UpdatedAt = now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = columnId, BoardId = boardId, Name = "Ready", StateKey = "ready",
                CreatedAt = now, UpdatedAt = now,
            });
            db.Cards.Add(new Card
            {
                Id = cardId, BoardId = boardId, BoardColumnId = columnId,
                Identifier = "CARD-0753", Title = "reviewed recovery", CreatedAt = now, UpdatedAt = now,
            });
            owner.ProjectId = projectId;
            owner.CardId = cardId;
            db.AgentTasks.Add(new AgentTask
            {
                Id = sourceId, RootTaskId = sourceId, Title = "reviewed repair", Goal = "repair",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = h.Fixture.Repository, RepoPath = h.Fixture.Repository,
                WorktreePath = sourcePath, WorktreeBranch = sourceRef[11..],
                WorktreeBaseSha = ownerBefore, Status = AgentTaskStatus.Failed,
                CardId = owner.CardId, ProjectId = owner.ProjectId,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var source = await db.AgentTasks.SingleAsync(t => t.Id == sourceId);
            evidenceId = await AddReviewAsync(db, source, reviewed);
        }

        await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidenceId,
            adoptFromTaskId: sourceId);
        var raced = false;
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (raced) return null;
            if (scenario == "local-cas" && args.Count > 4 && args[0] == "update-ref"
                && args.Contains(h.Fixture.SourceRef))
            {
                raced = true;
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", h.Fixture.SourceRef,
                    h.Fixture.SeedSha, ownerBefore);
            }
            if ((scenario is "remote-lease" or "push-rejected") && args.Count > 2 && args[0] == "push"
                && args.Any(a => a.StartsWith("--force-with-lease=" + h.Fixture.SourceRef + ":", StringComparison.Ordinal)))
            {
                raced = true;
                if (scenario == "remote-lease")
                    await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.SourceRef,
                        h.Fixture.SeedSha, ownerBefore);
                else
                    return new Antiphon.Server.Application.Dtos.LandingGitResult(1, "", "injected push refusal");
            }
            return null;
        };
        await h.RunQueuedAsync();

        if (scenario != "success")
        {
            raced.ShouldBeTrue();
            await using var refused = h.CreateContext();
            var row = await refused.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            row.SourceRefusalReason.ShouldBe(scenario == "local-cas"
                ? "adopt_local_cas_rejected" : "adopt_source_push_rejected");
            (await refused.AgentTaskLandings.CountAsync()).ShouldBe(0);
            Directory.Exists(sourcePath).ShouldBeTrue();
            return;
        }

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.RecoveryMode.ShouldBe(LandRecoveryMode.AdoptReviewedSource);
        op.RecoverySourceTaskId.ShouldBe(sourceId);
        op.RecoveryOwnerRemoteBeforeSha.ShouldBe(ownerBefore);
        op.RecoveryOwnerRemoteAfterSha.ShouldBe(reviewed);
        op.RecoveryPatchesContained.ShouldBe(true);
        op.RecoveryUncontainedPatches.ShouldBe("");
        Directory.Exists(sourcePath).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "show-ref", "--verify", "--hash", sourceRef))
            .Trim().ShouldBe(reviewed);
        h.Fixture.Git.Trace.ShouldContain(a => a.Contains($"--force-with-lease={h.Fixture.SourceRef}:{ownerBefore}"));
    }

    private static async Task<Guid> AddReviewAsync(
        Antiphon.Server.Infrastructure.Data.AppDbContext db, AgentTask subject, string sha)
    {
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = subject.Id, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = sha, ReviewedSourceRef = "refs/heads/" + subject.WorktreeBranch,
            ReviewedRepositoryPath = subject.RepoPath, CommissionedRound = VerificationRound.Final,
            OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow,
        };
        db.StageOutcomes.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }
}
