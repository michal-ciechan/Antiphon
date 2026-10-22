using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1485: a replacement land operation binds the approval its request was admitted with, as one
/// tuple (reviewed SHA, Review evidence including none, approval request). Only an identical ordinary
/// approval keeps the predecessor's original request. Every case runs persisted admission, source
/// resolution, the operation factory and the protocol through the real git fixture.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandReplacementApprovalTests
{
    [Test]
    public async Task ReplacementAfterSourceRewriteUsesNewReview()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        await h.Fixture.PushIndependentAsync("conflicting-target", "feature.txt", "different feature\n");
        var e1 = await AddReviewAsync(h, a);
        var first = await h.RequestAsync(expectedSourceSha: a, reviewEvidenceId: e1);
        await h.RunQueuedAsync();
        var old = (await h.OperationAsync()).ShouldNotBeNull();
        old.LastReason.ShouldBe("rebase_conflict");
        await SettleMergeAsync(h, first.RequestId);

        // The Merge helper's rewrite: B resolves the conflict on the moved target and is force-pushed.
        await h.Fixture.RequiredAsync(h.Fixture.Source, "fetch", "origin", h.Fixture.TargetRef);
        await h.Fixture.RequiredAsync(h.Fixture.Source, "reset", "--hard", "FETCH_HEAD");
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"), "valuable feature\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "feature.txt");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "merge resolution");
        var b = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "--force", "origin", "HEAD:" + h.Fixture.SourceRef);
        var e2 = await AddReviewAsync(h, b);

        var retry = await h.RequestAsync(expectedSourceSha: b, reviewEvidenceId: e2);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var replacement = await db.AgentTaskLandings.AsNoTracking()
            .SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Id != old.Id);
        replacement.ReviewEvidenceId.ShouldBe(e2);
        replacement.ApprovalLandRequestId.ShouldBe(retry.RequestId);
        replacement.OriginalSourceSha.ShouldBe(b);
        replacement.ReviewedSourceSha.ShouldBe(b);
        replacement.LastReason.ShouldNotBe("resume_approval_changed");
        new AgentTaskLandingState().HasPublication(replacement).ShouldBeTrue();
        await h.Fixture.RequiredAsync(h.Fixture.Remote, "merge-base", "--is-ancestor", b, h.Fixture.TargetRef);
        await AssertRetainedAsync(db, old);
    }

    [Test]
    public async Task ReplacementAtSameShaUsesNewReview()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (a, e1, first, old) = await RefusedPredecessorAsync(h);
        var e2 = await AddReviewAsync(h, a);

        var retry = await h.RequestAsync(expectedSourceSha: a, reviewEvidenceId: e2);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var replacement = await db.AgentTaskLandings.AsNoTracking()
            .SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Id != old.Id);
        replacement.ReviewEvidenceId.ShouldBe(e2);
        replacement.ApprovalLandRequestId.ShouldBe(retry.RequestId);
        replacement.OriginalSourceSha.ShouldBe(a);
        new AgentTaskLandingState().HasPublication(replacement).ShouldBeTrue();
        old.ReviewEvidenceId.ShouldBe(e1);
        old.ApprovalLandRequestId.ShouldBe(first);
        await AssertRetainedAsync(db, old);
    }

    [Test]
    public async Task ExplicitCallerReplacementDoesNotInheritReview()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (a, _, _, old) = await RefusedPredecessorAsync(h);

        // The ordinary SHA guard still applies to a caller-only approval.
        var stale = await h.RequestAsync(expectedSourceSha: h.Fixture.SeedSha);
        await h.RunQueuedAsync();
        await using (var observer = h.CreateContext())
        {
            (await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == stale.RequestId))
                .SourceRefusalReason.ShouldBe("reviewed_source_mismatch");
            (await observer.AgentTaskLandings.CountAsync(o => o.TaskId == h.Fixture.TaskId)).ShouldBe(1);
        }

        var caller = await h.RequestAsync(expectedSourceSha: a);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var replacement = await db.AgentTaskLandings.AsNoTracking()
            .SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Id != old.Id);
        replacement.ReviewEvidenceId.ShouldBeNull();
        replacement.ApprovalLandRequestId.ShouldBe(caller.RequestId);
        replacement.ApprovalKind.ShouldBe(LandApprovalKind.ExplicitCaller);
        replacement.OriginalSourceSha.ShouldBe(a);
        new AgentTaskLandingState().HasPublication(replacement).ShouldBeTrue();
        await AssertRetainedAsync(db, old);
    }

    [Test]
    public async Task IdenticalApprovalRetryRetainsProvenance()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (a, e1, first, old) = await RefusedPredecessorAsync(h);

        var retry = await h.RequestAsync(expectedSourceSha: a, reviewEvidenceId: e1);
        retry.RequestId.ShouldNotBe(first);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var replacement = await db.AgentTaskLandings.AsNoTracking()
            .SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Id != old.Id);
        replacement.ApprovalLandRequestId.ShouldBe(first);
        replacement.ReviewEvidenceId.ShouldBe(e1);
        replacement.OriginalSourceSha.ShouldBe(a);
        h.Verifier.Calls.ShouldBe(2);
        new AgentTaskLandingState().HasPublication(replacement).ShouldBeTrue();
        await AssertRetainedAsync(db, old);
    }

    [Test]
    public async Task OwnerReviewedRecoveryBindsCurrentApproval()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var (_, _, _, old) = await RefusedPredecessorAsync(h);
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "second.txt"), "reviewed again\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "second.txt");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "second");
        var b = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var e2 = await AddReviewAsync(h, b);

        var recovery = await h.RequestAsync(expectedSourceSha: b, reviewEvidenceId: e2, recoverReviewedSource: true);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var replacement = await db.AgentTaskLandings.AsNoTracking()
            .SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Id != old.Id);
        replacement.RecoveryMode.ShouldBe(LandRecoveryMode.OwnerReviewedSource);
        replacement.ReviewEvidenceId.ShouldBe(e2);
        replacement.ApprovalLandRequestId.ShouldBe(recovery.RequestId);
        replacement.OriginalSourceSha.ShouldBe(b);
        new AgentTaskLandingState().HasPublication(replacement).ShouldBeTrue();
        await AssertRetainedAsync(db, old);
    }

    /// <summary>A/E1 approved, pushed and refused by verification: an inactive-on-replacement Refused predecessor.</summary>
    private static async Task<(string Sha, Guid Evidence, Guid Request, AgentTaskLanding Operation)> RefusedPredecessorAsync(
        LandingSafetyHarness h)
    {
        var a = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var e1 = await AddReviewAsync(h, a);
        h.Verifier.Passed = false;
        var first = await h.RequestAsync(expectedSourceSha: a, reviewEvidenceId: e1);
        await h.RunQueuedAsync();
        h.Verifier.Passed = true;
        var old = (await h.OperationAsync()).ShouldNotBeNull();
        old.Phase.ShouldBe(LandPhase.Refused);
        old.ReviewEvidenceId.ShouldBe(e1);
        old.ApprovalLandRequestId.ShouldBe(first.RequestId);
        return (a, e1, first.RequestId, old);
    }

    /// <summary>The predecessor row is kept, inactive, with its approval, source and outcome unchanged.</summary>
    private static async Task AssertRetainedAsync(AppDbContext db, AgentTaskLanding old)
    {
        var stored = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == old.Id);
        stored.Active.ShouldBeFalse();
        stored.Phase.ShouldBe(old.Phase);
        stored.Publication.ShouldBe(old.Publication);
        stored.LastReason.ShouldBe(old.LastReason);
        stored.ReviewEvidenceId.ShouldBe(old.ReviewEvidenceId);
        stored.ApprovalLandRequestId.ShouldBe(old.ApprovalLandRequestId);
        stored.OriginalSourceSha.ShouldBe(old.OriginalSourceSha);
        stored.ReviewedSourceSha.ShouldBe(old.ReviewedSourceSha);
        stored.RecoveryMode.ShouldBe(old.RecoveryMode);
        stored.Mode.ShouldBe(old.Mode);
        stored.Cleanup.ShouldBe(old.Cleanup);
    }

    /// <summary>
    /// The persisted effect of a successful Merge settlement (AgentTaskReplyService.ResolveConflictedParentAsync):
    /// the helper is done, the owner is Succeeded with its land clocks cleared, and the NeedsResolution request is
    /// superseded. The old Review evidence and the operation rows are left as they are.
    /// </summary>
    private static async Task SettleMergeAsync(LandingSafetyHarness h, Guid requestId)
    {
        await using var db = h.CreateContext();
        var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        owner.Status.ShouldBe(AgentTaskStatus.Blocked);
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        request.State.ShouldBe(LandRequestState.NeedsResolution);
        request.IsPending.ShouldBeTrue();
        foreach (var helper in await db.AgentTasks
            .Where(t => t.ParentTaskId == owner.Id && t.Role == AgentTaskRole.Merge).ToListAsync())
        {
            helper.Status = AgentTaskStatus.Succeeded;
            helper.CompletedAt = DateTime.UtcNow;
        }
        owner.Status = AgentTaskStatus.Succeeded;
        owner.FailureReason = null;
        owner.LandRequestedAt = null;
        owner.LandVerifyFilter = null;
        owner.LandStartedAt = null;
        owner.ConcurrencyToken = Guid.NewGuid();
        request.State = LandRequestState.Superseded;
        request.IsPending = false;
        LandRequestWriteProvenance.Stamp(request, "merge-supersession", TimeProvider.System);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddReviewAsync(LandingSafetyHarness h, string sha)
    {
        await using var db = h.CreateContext();
        var subject = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId);
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = subject.Id, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = sha, ReviewedSourceClean = true,
            ReviewedSourceRef = "refs/heads/" + subject.WorktreeBranch,
            ReviewedRepositoryPath = subject.RepoPath, CommissionedRound = VerificationRound.Final,
            OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow,
        };
        db.StageOutcomes.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }
}
