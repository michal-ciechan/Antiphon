using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-7, R-5. Final promotion through actual Git: the landing safety harness's real
/// canonical repository, source worktree and bare remote, observed by an independent clone. The
/// CARD-0544 graph attaches to the same database and repository, so the baseline, Interim admission,
/// latch, Final Review settlement and land all act on one owner.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class InterimVerificationLandGitTests
{
    private const string Filter = "/*/*/C544PinnedClass/*";

    [Test]
    public async Task C544_FinalPromotionPublishesOnlyReviewedCandidate()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var candidate = await h.AddSourceAsync();
        // Move the target first so the ordinary rebase produces a different landed SHA than the reviewed C.
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Repository, "target-moved.txt"), "target\n");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "add", "target-moved.txt");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "target moved");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "refs/heads/master");
        var targetMoved = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim();

        await using var world = await C544World.AttachAsync(h.Schema, h.Fixture.Repository,
            Path.Combine(h.Fixture.Root, "c544-trees"), h.Fixture.TaskId, candidate);
        var baseline = await world.SettleReviewAsync(found: true, next: "code");
        baseline.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, "found-full-baseline");

        var interimReview = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await world.CreateTaskAsync(world.InterimCode(baseline.Id));
        (await world.TaskAsync(h.Fixture.TaskId)).RequiresFinalVerificationReview.ShouldBeTrue("interim-latched");
        var interimOutcome = await world.SettleExistingReviewAsync(interimReview.Id, scope: "Full", next: "land");
        interimOutcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Interim, "interim-review-capped");

        var remoteBefore = await ObservedRemoteMasterAsync(h);
        remoteBefore.ShouldBe(targetMoved, "observer baseline");
        var noEvidence = await Should.ThrowAsync<ConflictException>(() => h.RequestAsync(expectedSourceSha: candidate), "refused-no-evidence");
        noEvidence.Code.ShouldBe(LandApproval.FinalReviewRequiredCode, "refused-no-evidence");
        var interimApproval = await Should.ThrowAsync<ConflictException>(
            () => h.RequestAsync(expectedSourceSha: candidate, reviewEvidenceId: interimOutcome.Id), "refused-interim");
        interimApproval.Code.ShouldBe(LandApproval.ScopeIneligibleCode, "refused-interim");
        await using (var db = h.CreateContext())
            (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Fixture.TaskId)).ShouldBe(0, "refusals create no request");
        (await ObservedRemoteMasterAsync(h)).ShouldBe(remoteBefore, "refusals publish nothing");

        // Fresh Final Full Clean Review of the UNCHANGED candidate; no no-change Code task.
        int codeTasksBefore;
        await using (var db = world.CreateContext())
            codeTasksBefore = await db.AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Code);
        var final = await world.SettleReviewAsync();
        final.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, "final-full");
        final.ReviewedSourceSha.ShouldBe(candidate, "final reviewed the unchanged candidate");
        await using (var db = world.CreateContext())
            (await db.AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Code)).ShouldBe(codeTasksBefore, "no no-change Code task");

        var queued = await h.RequestAsync(filter: Filter, expectedSourceSha: candidate, reviewEvidenceId: final.Id);
        queued.Status.ShouldBe("queued", "final-promotion");
        (await h.RunQueuedAsync(Filter)).ShouldBe(LandRunResult.Complete, "final-promotion");

        var op = (await h.OperationAsync()).ShouldNotBeNull("published");
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("published");
        op.OriginalSourceSha.ShouldBe(candidate, "approved C");
        op.ReviewedSourceSha.ShouldBe(candidate, "approved C");
        op.ReviewEvidenceId.ShouldBe(final.Id, "approval identity");
        op.VerifiedSourceSha.ShouldNotBeNull().ShouldNotBe(candidate, "rebase changed the verified L");
        op.VerificationFilter.ShouldBe(Filter, "LandVerifyFilter unchanged");
        h.Verifier.Invocations.ShouldHaveSingleItem("ordinary verifier invocation").Filter.ShouldBe(Filter);
        h.Fixture.Git.Trace.ShouldContain(a => a.Contains("rebase"), "ordinary rebase ran");
        await using (var db = h.CreateContext())
            (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId)).VerifyFilter.ShouldBe(Filter);

        // Independent observer: the remote target is exactly the verified L, on top of the moved target, carrying C's change.
        var observed = await ObservedRemoteMasterAsync(h);
        observed.ShouldBe(op.VerifiedSourceSha, "remote carries L");
        (await ObserverGitAsync(h, "rev-parse", observed + "^")).Trim().ShouldBe(targetMoved, "L rebased onto the moved target");
        (await ObserverGitAsync(h, "show", observed + ":feature.txt")).ShouldContain("valuable feature", Case.Sensitive, "L carries the reviewed change");
    }

    [Test]
    public async Task C544_EditAfterFinalRefuses()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var candidate = await h.AddSourceAsync();
        await using var world = await C544World.AttachAsync(h.Schema, h.Fixture.Repository,
            Path.Combine(h.Fixture.Root, "c544-trees"), h.Fixture.TaskId, candidate);
        var baseline = await world.SettleReviewAsync(found: true, next: "code");
        await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        var final = await world.SettleReviewAsync();
        final.ReviewedSourceSha.ShouldBe(candidate);

        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "after-review.txt"), "unreviewed edit\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "after-review.txt");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "edit after final review");
        var edited = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var remoteBefore = await ObservedRemoteMasterAsync(h);

        var mismatch = await Should.ThrowAsync<ConflictException>(
            () => h.RequestAsync(expectedSourceSha: edited, reviewEvidenceId: final.Id), "new-sha-with-stale-review");
        mismatch.Code.ShouldBe("review_evidence_sha_mismatch", "new-sha-with-stale-review");

        (await h.RequestAsync(expectedSourceSha: candidate, reviewEvidenceId: final.Id)).Status.ShouldBe("queued", "stale-review-admitted-by-identity");
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        if (op is not null) new AgentTaskLandingState().HasPublication(op).ShouldBeFalse("stale review never publishes");
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push", "no push of the edited source");
        (await ObservedRemoteMasterAsync(h)).ShouldBe(remoteBefore, "remote unchanged");
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandRefused))
            .ShouldBeTrue("stale review refused before publication");
    }

    [Test]
    public async Task C544_AncestryUsesActualGit()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var reviewed = await h.AddSourceAsync();
        await using var world = await C544World.AttachAsync(h.Schema, h.Fixture.Repository,
            Path.Combine(h.Fixture.Root, "c544-trees"), h.Fixture.TaskId, reviewed);
        var baseline = await world.SettleReviewAsync();

        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "repair.txt"), "repair\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "repair.txt");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "repair after baseline");
        var head = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var tree = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SeedSha + "^{tree}")).Trim();
        var orphanResult = await ScratchGitRepo.GitInAsync(h.Fixture.Repository, new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = "C544", ["GIT_AUTHOR_EMAIL"] = "c544@antiphon.local",
            ["GIT_COMMITTER_NAME"] = "C544", ["GIT_COMMITTER_EMAIL"] = "c544@antiphon.local",
        }, "commit-tree", tree, "-m", "unrelated root");
        orphanResult.Ok.ShouldBeTrue(orphanResult.StdErr);
        var unrelated = orphanResult.StdOut.Trim();

        var progress = world.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.ITaskProgressGit>();
        (await progress.IsAncestorAsync(h.Fixture.Repository, reviewed, head, CancellationToken.None)).ShouldBe(true, "actual-git ancestor");
        (await progress.IsAncestorAsync(h.Fixture.Repository, unrelated, head, CancellationToken.None)).ShouldBe(false, "actual-git unrelated");

        var ancestorTask = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await TickAsync(world);
        var launched = await world.TaskAsync(ancestorTask.Id);
        launched.Status.ShouldNotBe(AgentTaskStatus.Blocked, $"ancestor row: {launched.FailureReason}");
        launched.Status.ShouldNotBe(AgentTaskStatus.Queued, "ancestor row launched");

        await InterimVerificationPolicyTests.UpdateTaskAsync(world, ancestorTask.Id, t => { t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = DateTime.UtcNow; });
        var unrelatedTask = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await using (var db = world.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == unrelatedTask.Id);
            task.VerificationAdmissionJson = (VerificationAdmission.TryRead(task.VerificationAdmissionJson)! with { BaselineReviewedSha = unrelated }).Serialize();
            await db.SaveChangesAsync();
        }
        await TickAsync(world);
        var held = await world.TaskAsync(unrelatedTask.Id);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, "unrelated row");
        held.FailureReason.ShouldNotBeNull().ShouldStartWith(InterimVerificationPolicy.BaselineInvalidCode, Case.Sensitive, "unrelated row");
        held.AgentSessionId.ShouldBeNull("unrelated row: no launch");
    }

    private static async Task TickAsync(C544World world)
    {
        await using var scope = world.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
    }

    private static async Task<string> ObservedRemoteMasterAsync(LandingSafetyHarness h)
    {
        await ObserverGitAsync(h, "fetch", "--no-tags", h.Fixture.Remote, "+refs/heads/master:refs/c544-observed/master");
        return (await ObserverGitAsync(h, "rev-parse", "refs/c544-observed/master")).Trim();
    }

    private static async Task<string> ObserverGitAsync(LandingSafetyHarness h, params string[] args)
    {
        var reader = new LandingGitFixture.FixtureGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId);
        var result = await reader.RunAsync(h.Fixture.Observer, args, CancellationToken.None);
        result.Succeeded.ShouldBeTrue($"observer git {string.Join(' ', args)}: {result.Diagnostic}");
        return result.Output;
    }
}
