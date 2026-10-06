using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepairSourceRecoveryLandingTests
{
    private const string VerifyFilter = "/*/*/RepairSourceRecoveryLandingTests/C603_FailedOwnerLandsReviewedRepairedTip*";
    private const string RepairContents = "reviewed repair\n";

    [Test]
    [Timeout(180_000)]
    public async Task C603_FailedOwnerLandsReviewedRepairedTip()
    {
        var verifier = new RecordingVerifier();
        await using var world = await RepairSourceWorld.CreateAsync(landingVerifier: verifier);
        var (ownerBefore, repair, sha, targetBefore) = await PrepareAsync(world);
        var reviewId = await AddReviewAsync(world, world.Owner.Id, sha);
        var land = world.Services.GetRequiredService<AgentTaskLandService>();
        LandRequestResult accepted = null!;
        await Should.NotThrowAsync(async () => accepted = await land.RequestAsync(world.Owner.Id,
            new LandAgentTaskRequest(Verify: VerifyFilter, ExpectedSourceSha: sha,
                ReviewEvidenceId: reviewId, RecoverReviewedSource: true), default));
        accepted.Status.ShouldBe("queued", "reviewed Failed owner must be admitted");
        var queue = world.Services.GetRequiredService<AgentTaskLandQueue>();
        queue.TryDequeue(out var claim).ShouldBeTrue("the accepted request must enter the real queue");
        claim.TaskId.ShouldBe(world.Owner.Id);
        claim.RequestId.ShouldBe(accepted.RequestId);
        claim.VerifyFilter.ShouldBe(VerifyFilter);
        LandRunResult result;
        try
        {
            await using var scope = world.Services.CreateAsyncScope();
            result = await scope.ServiceProvider.GetRequiredService<AgentTaskLandService>()
                .RunRequestAsync(claim.TaskId, claim.RequestId, claim.VerifyFilter, default);
        }
        finally { queue.Release(claim); }
        result.ShouldBe(LandRunResult.Complete, "the queued recovery must finish publication");
        await using var db = world.CreateContext();
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
        request.State.ShouldBe(LandRequestState.Completed);
        request.IsPending.ShouldBeFalse();
        request.RecoveryMode.ShouldBe(LandRecoveryMode.OwnerReviewedSource);
        request.RecoveryOwnerStatus.ShouldBe(AgentTaskStatus.Failed);
        request.RecoverySourceTaskId.ShouldBe(world.Owner.Id);
        request.RecoverySourceFullRef.ShouldBe(world.OwnerRef);
        request.ReviewEvidenceId.ShouldBe(reviewId);
        request.ExpectedSourceSha.ShouldBe(sha);
        request.ResolvedSourceSha.ShouldBe(sha);
        request.ApprovalKind.ShouldBe(LandApprovalKind.ReviewEvidence);
        request.RecoveryOwnerRemoteBeforeSha.ShouldBe(sha);
        request.RecoveryOwnerRemoteAfterSha.ShouldBe(sha);
        var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == request.LandingOperationId);
        op.TaskId.ShouldBe(world.Owner.Id);
        op.RecoveryMode.ShouldBe(LandRecoveryMode.OwnerReviewedSource);
        op.RecoveryOwnerStatus.ShouldBe(AgentTaskStatus.Failed);
        op.RecoverySourceTaskId.ShouldBe(world.Owner.Id);
        op.RecoverySourceFullRef.ShouldBe(world.OwnerRef);
        op.ReviewEvidenceId.ShouldBe(reviewId);
        op.OriginalSourceSha.ShouldBe(sha);
        op.ReviewedSourceSha.ShouldBe(sha);
        op.VerifiedSourceSha.ShouldBe(sha);
        op.ApprovalKind.ShouldBe(LandApprovalKind.ReviewEvidence);
        op.ApprovalLandRequestId.ShouldBe(request.Id);
        op.RecoveryOwnerRemoteBeforeSha.ShouldBe(sha);
        op.RecoveryOwnerRemoteAfterSha.ShouldBe(sha);
        op.VerificationPassed.ShouldBeTrue();
        verifier.Invocations.Count.ShouldBe(1, "the selected filter must reach the land verifier");
        verifier.Invocations[0].Filter.ShouldBe(VerifyFilter);
        verifier.Invocations[0].Worktree.ShouldBe(op.LandWorktreePath);
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.RemoteConfirmedAt.ShouldNotBeNull();
        op.ConfirmationMethod.ShouldBe("push-endpoint-read-fetch-ancestry");
        var remoteTarget = (await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master")).StdOut.Trim();
        remoteTarget.ShouldBe(sha, "bare origin must contain the exact repaired commit");
        remoteTarget.ShouldNotBe(targetBefore);
        op.ObservedRemoteTargetSha.ShouldBe(sha);
        (await ScratchGitRepo.GitInAsync(world.Remote, "show", "refs/heads/master:repair.md"))
            .StdOut.ShouldBe(RepairContents);
        await AssertOwnerUnchangedAsync(world, ownerBefore);
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == repair.Id)).ShouldBe(0);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == repair.Id)).ShouldBe(0);
        (await db.AgentTasks.CountAsync()).ShouldBe(2, "recovery must not manufacture another Code owner");
        Directory.Exists(repair.WorktreePath!).ShouldBeTrue();
    }

    [Test]
    [Arguments("plain")]
    [Arguments("missing-review")]
    [Arguments("repair-subject")]
    [Timeout(180_000)]
    public async Task C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand(string scenario)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (ownerBefore, repair, sha, targetBefore) = await PrepareAsync(world);
        Guid? evidence = scenario == "repair-subject"
            ? await AddReviewAsync(world, repair.Id, sha) : null;
        await using var before = world.CreateContext();
        var eventCount = await before.AgentTaskEvents.CountAsync(e => e.AgentTaskId == world.Owner.Id || e.AgentTaskId == repair.Id);
        var notificationCount = await before.AgentTaskLandNotifications.CountAsync();
        var queue = world.Services.GetRequiredService<AgentTaskLandQueue>();
        var error = await Should.ThrowAsync<ConflictException>(() =>
            world.Services.GetRequiredService<AgentTaskLandService>().RequestAsync(world.Owner.Id,
                new LandAgentTaskRequest(ExpectedSourceSha: sha, ReviewEvidenceId: evidence,
                    RecoverReviewedSource: scenario != "plain"), default));
        error.Code.ShouldBe(scenario switch
        {
            "plain" => "conflict",
            "missing-review" => "recovery_review_required",
            "repair-subject" => "review_evidence_subject_mismatch",
            _ => throw new InvalidOperationException("unexpected recovery scenario: " + scenario),
        }, $"{scenario}: admission boundary");
        if (scenario == "repair-subject")
        {
            // Clean is checked before subject. This review is clean and names the repair, so admission
            // must reach subject identity and refuse the owner with that repair recorded as the subject.
            error.Message.ShouldContain("subject differs", Case.Sensitive, "clean repair review reaches subject identity");
            error.Message.ShouldContain(repair.Id.ToString("N")[..8], Case.Sensitive, "recorded subject is the repair");
            error.Message.ShouldContain(world.Owner.Id.ToString("N")[..8], Case.Sensitive, "required subject is the recovery owner");
        }
        error.StatusCode.ShouldBe(409);
        await using var db = world.CreateContext();
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
        (await db.AgentTaskLandings.CountAsync()).ShouldBe(0);
        (await db.AgentTaskLandNotifications.CountAsync()).ShouldBe(notificationCount);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == world.Owner.Id || e.AgentTaskId == repair.Id))
            .ShouldBe(eventCount);
        queue.PendingCount.ShouldBe(0);
        queue.IsActive(world.Owner.Id).ShouldBeFalse();
        queue.IsActive(repair.Id).ShouldBeFalse();
        (await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master"))
            .StdOut.Trim().ShouldBe(targetBefore);
        await AssertOwnerUnchangedAsync(world, ownerBefore);
    }

    private static async Task<(AgentTask OwnerBefore, AgentTask Repair, string Sha, string TargetBefore)> PrepareAsync(RepairSourceWorld world)
    {
        await using (var db = world.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            owner.Status = AgentTaskStatus.Failed;
            owner.FailureCode = AgentTaskFailureCode.CompletedWithoutProgress;
            owner.FailureReason = "unclaimed_or_unmatched_commit: original owner failed";
            await db.SaveChangesAsync();
        }
        await using var initial = world.CreateContext();
        var ownerBefore = await initial.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.Owner.Id);
        ownerBefore.CompletedAt.ShouldNotBeNull();
        ownerBefore.RequiresFinalVerificationReview.ShouldBeFalse();
        (await initial.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
        (await initial.AgentTaskLandings.CountAsync()).ShouldBe(0);
        var (repair, session) = await world.DispatchAsync();
        session.ShouldNotBe(Guid.Empty);
        repair.Status.ShouldBe(AgentTaskStatus.Dispatched);
        repair.RepairSourceTaskId.ShouldBe(world.Owner.Id);
        repair.WorktreeBranch.ShouldNotBe(world.Owner.WorktreeBranch);
        repair.WorktreePath.ShouldNotBe(world.Owner.WorktreePath);
        var baseline = TaskProgressJson.TryReadBaseline(repair.ProgressBaselineJson).ShouldNotBeNull();
        baseline!.RepairSource.ShouldNotBeNull();
        baseline.RepairSource!.OwnerTaskId.ShouldBe(world.Owner.Id);
        baseline.RepairSource.FullRef.ShouldBe(world.OwnerRef);
        baseline.RepairSource.LocalSha.ShouldBe(world.OwnerSha);
        var sha = await world.CommitInOwnerTreeAsync(RepairContents.TrimEnd(), push: true);
        (await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", world.OwnerRef))
            .StdOut.Trim().ShouldBe(sha);
        var targetBefore = (await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master")).StdOut.Trim();
        (await ScratchGitRepo.GitInAsync(world.Remote, "merge-base", "--is-ancestor", sha,
            "refs/heads/master")).Ok.ShouldBeFalse("the repaired tip must not already be published");
        await world.SettleAsync(world.DoneReport("Repaired the original owner branch.", sha));
        await using var settledDb = world.CreateContext();
        var settled = await settledDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.FailureCode.ShouldBeNull();
        var progress = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson).ShouldNotBeNull();
        progress!.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var source = progress.Sources!.First(s => s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote);
        source.OwnerTaskId.ShouldBe(world.Owner.Id);
        source.ClaimedSha.ShouldBe(sha);
        source.VerifiedSha.ShouldBe(sha);
        source.RegisteredPath.ShouldBe(world.Owner.WorktreePath);
        settled.MergeTargetRef.ShouldBeNull();
        (await ScratchGitRepo.GitInAsync(repair.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim().ShouldBe(world.OwnerSha);
        (await settledDb.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
        (await settledDb.AgentTaskLandings.CountAsync()).ShouldBe(0);
        await AssertOwnerUnchangedAsync(world, ownerBefore);
        return (ownerBefore, repair, sha, targetBefore);
    }

    private static async Task<Guid> AddReviewAsync(RepairSourceWorld world, Guid subjectId, string sha)
    {
        await using var db = world.CreateContext();
        var id = Guid.NewGuid();
        db.StageOutcomes.Add(new StageOutcome
        {
            Id = id, Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, StageTaskId = Guid.NewGuid(), SubjectTaskId = subjectId,
            ReviewedSourceSha = sha, ReviewedSourceClean = true, ReviewedSourceRef = world.OwnerRef,
            ReviewedRepositoryPath = world.Owner.RepoPath, CommissionedRound = VerificationRound.Final,
            OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task AssertOwnerUnchangedAsync(RepairSourceWorld world, AgentTask before)
    {
        await using var db = world.CreateContext();
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == before.Id);
        after.Status.ShouldBe(AgentTaskStatus.Failed);
        after.FailureCode.ShouldBe(before.FailureCode);
        after.FailureReason.ShouldBe(before.FailureReason);
        after.CompletedAt.ShouldBe(before.CompletedAt);
    }

    private sealed class RecordingVerifier : ILandingVerifier
    {
        public List<(string Worktree, string? Filter)> Invocations { get; } = [];
        public Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        {
            Invocations.Add((worktree, filter));
            return Task.FromResult(new LandingVerification(true, "fixture verification"));
        }
    }
}
