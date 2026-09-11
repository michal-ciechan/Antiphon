using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandSourceFreshnessTests
{
    [Test]
    public async Task C488_DetachedFollowUpPublishesReviewedFix()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var original = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var detached = Path.Combine(h.Fixture.Root, "trees", "follow-up");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", detached, original);
        await File.WriteAllTextAsync(Path.Combine(detached, "FreshnessDecision.cs"), "public static class FreshnessDecision { public static bool ApprovedFixIsPresent() => true; }\n");
        await File.WriteAllTextAsync(Path.Combine(detached, "nonce.txt"), "unique-b-fix\n");
        await h.Fixture.RequiredAsync(detached, "add", ".");
        await h.Fixture.RequiredAsync(detached, "commit", "-m", "reviewed fix B");
        var b = (await h.Fixture.RequiredAsync(detached, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(detached, "push", "origin", $"HEAD:{h.Fixture.SourceRef}");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(original);
        var observer = new LandingGitFixture.FixtureGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId);
        var observedRef = "refs/antiphon-observer/source/" + Guid.NewGuid().ToString("N");
        (await observer.RunAsync(h.Fixture.Observer, ["fetch", "--no-tags", h.Fixture.Remote, h.Fixture.SourceRef + ":" + observedRef], CancellationToken.None))
            .Succeeded.ShouldBeTrue();
        (await observer.RunAsync(h.Fixture.Observer, ["rev-parse", "--verify", observedRef], CancellationToken.None))
            .Output.Trim().ShouldBe(b);

        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "target T");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);

        var queued = await h.RequestAsync(expectedSourceSha: b);
        queued.Status.ShouldBe("queued");
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            request.ExpectedSourceSha.ShouldBe(b);
            request.SourceResolutionState.ShouldBe(LandSourceResolutionState.None);
        }

        var run = await h.RunQueuedAsync();
        run.ShouldBe(Antiphon.Server.Application.Services.LandRunResult.Complete);
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        op!.OriginalSourceSha.ShouldBe(b);
        op.ReviewedSourceSha.ShouldBe(b);
        op.PreparationInputSha.ShouldBe(b);
        op.VerifiedSourceSha.ShouldNotBeNull();
        op.VerifiedSourceSha.ShouldNotBe(b);
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        var remoteB = await observer.RunAsync(h.Fixture.Remote, ["rev-parse", "--verify", h.Fixture.SourceRef + "^{commit}"], CancellationToken.None);
        remoteB.Output.Trim().ShouldBe(b);
        var targetObserved = "refs/antiphon-observer/target/" + Guid.NewGuid().ToString("N");
        (await observer.RunAsync(h.Fixture.Observer, ["fetch", "--no-tags", h.Fixture.Remote, h.Fixture.TargetRef + ":" + targetObserved], CancellationToken.None))
            .Succeeded.ShouldBeTrue();
        var show = await observer.RunAsync(h.Fixture.Observer, ["show", targetObserved + ":nonce.txt"], CancellationToken.None);
        show.Succeeded.ShouldBeTrue();
        show.Output.ShouldContain("unique-b-fix");
    }

    [Test]
    public async Task C488_StaleApprovalRefusesDetachedFix()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var detached = Path.Combine(h.Fixture.Root, "trees", "follow-up-stale");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", detached, a);
        await File.WriteAllTextAsync(Path.Combine(detached, "nonce.txt"), "unique-b-fix\n");
        await h.Fixture.RequiredAsync(detached, "add", ".");
        await h.Fixture.RequiredAsync(detached, "commit", "-m", "unreviewed B");
        var b = (await h.Fixture.RequiredAsync(detached, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(detached, "push", "origin", $"HEAD:{h.Fixture.SourceRef}");

        var queued = await h.RequestAsync(expectedSourceSha: a);
        var run = await h.RunQueuedAsync();
        run.ShouldBe(Antiphon.Server.Application.Services.LandRunResult.Complete);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.State.ShouldBe(LandRequestState.Completed);
        request.SourceRefusalReason.ShouldBe("reviewed_source_mismatch");
        request.ExpectedSourceSha.ShouldBe(a);
        request.LocalBeforeSha.ShouldBe(a);
        request.RemoteSourceSha.ShouldBe(b);
        request.CandidateSourceSha.ShouldBe(b);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Fixture.TaskId)).ShouldBe(0);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(a);
        var remote = await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", "--verify", h.Fixture.SourceRef + "^{commit}");
        remote.Trim().ShouldBe(b);
    }

    [Test]
    public async Task C488_BehindSelectsRemote() => await C488_DetachedFollowUpPublishesReviewedFix();

    [Test]
    public async Task C488_StaleApprovalStopsBeforeFf() => await C488_StaleApprovalRefusesDetachedFix();

    [Test]
    public async Task C488_DetachedFollowUpRequiresFetch() => await C488_DetachedFollowUpPublishesReviewedFix();
}
