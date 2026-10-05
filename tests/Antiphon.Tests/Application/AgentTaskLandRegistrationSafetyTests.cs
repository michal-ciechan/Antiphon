using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRegistrationSafetyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C975_CorruptedBacklinkBeforeMoveRefuses(bool adoption) =>
        AssertBoundaryCorruptionAsync(adoption, afterMove: false);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C975_CorruptedBacklinkAfterMoveRefuses(bool adoption) =>
        AssertBoundaryCorruptionAsync(adoption, afterMove: true);

    // Shared with the native spaced-path wrappers in the subsequent Windows slice.
    internal static async Task AssertBoundaryCorruptionAsync(bool adoption, bool afterMove, string? root = null)
    {
        await using var fixture = new LandHalfResetFixture(root);
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync(adoption: adoption);
        local.ShouldNotBe(reviewed, "the guarded reset would change tracked bytes");
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: !adoption, adoptFromTaskId: fixture.AdoptionSourceId);
        fixture.Interceptor.RequestId = request.RequestId;
        fixture.Boundary.ThrowConflict = false;
        await using var corruption = await LandHalfResetFixture.BacklinkCorruption.CreateAsync(h.Fixture);
        if (afterMove) fixture.Boundary.AtCut = corruption.RedirectAsync;
        else fixture.Boundary.BeforeRefMove = corruption.RedirectAsync;
        h.Fixture.Git.Commands.Clear();
        h.Fixture.Git.Trace.Clear();

        await h.RunQueuedAsync();

        fixture.Boundary.BeforeRefReached.ShouldBe(afterMove ? 0 : 1);
        fixture.Boundary.Reached.ShouldBe(afterMove ? 1 : 0);
        var ownerWrites = OwnerRefWrites(h);
        ownerWrites.Length.ShouldBe(afterMove ? 1 : 0, "no late refusal or compensating CAS may conceal mutation");
        if (afterMove)
            ownerWrites[0].ShouldBe(new[] { "update-ref", "--no-deref", h.Fixture.SourceRef, reviewed, local });
        AssertNoResetPushOrCleanup(h);
        (await h.OperationAsync()).ShouldBeNull("refusal occurs before publication or cleanup");
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        row.SourceRefusalReason.ShouldBe(afterMove ? "adopt_local_changed" : "registration_mismatch");
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId)).Status
            .ShouldBe(AgentTaskStatus.Failed, "the original owner status stays historical");
        await AssertIntentAndPinsAsync(h, row, local, reviewed);
        await corruption.AssertPreservedAsync(afterMove ? reviewed : local);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C975_CorruptedBacklinkDuringResumeRefuses(bool freshRequest)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Boundary.Reached.ShouldBe(1, "the historical interruption leaves HEAD=S and bytes=L");
        if (freshRequest) await h.FailAsync(conflict);
        else await h.SweepAsync();
        var requestId = freshRequest
            ? (await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
                recoverReviewedSource: true)).RequestId : first.RequestId;
        if (freshRequest) requestId.ShouldNotBe(first.RequestId);
        await using var corruption = await LandHalfResetFixture.BacklinkCorruption.CreateAsync(h.Fixture);
        var injections = 0;
        var proofEntryLists = 0;
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (!LandingGit.PathsEqual(directory, h.Fixture.Source) || args[0] != "ls-tree"
                || args[^1] != reviewed || injections != 0) return;
            result.Succeeded.ShouldBeTrue();
            proofEntryLists = WorktreeLists(h);
            proofEntryLists.ShouldBeGreaterThan(0, "strict proof must reach its first identity sample");
            injections++;
            await corruption.RedirectAsync();
        };
        h.Fixture.Git.Commands.Clear();
        h.Fixture.Git.Trace.Clear();

        await h.RunQueuedAsync();

        injections.ShouldBe(1);
        WorktreeLists(h).ShouldBeGreaterThan(proofEntryLists, "proof exit must read registration after corruption");
        OwnerRefWrites(h).ShouldBeEmpty("a resume must not replay CAS or compensate it");
        AssertNoResetPushOrCleanup(h);
        (await h.OperationAsync()).ShouldBeNull();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
        row.SourceRefusalReason.ShouldBe("adopt_local_changed");
        await AssertIntentAndPinsAsync(h, row, local, reviewed);
        await corruption.AssertPreservedAsync(reviewed);
    }

    [Test]
    public async Task C975_CleanupBacklinkChangePreservesPublicationAndBytes()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        await h.RequestAsync();
        LandHalfResetFixture.BacklinkCorruption? corruption = null;
        AgentTaskLanding? published = null;
        var cleanupStarted = 0;
        var cleanupStatuses = 0;
        var injections = 0;
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (phase != LandPhase.CleanupStarted || cleanupStarted != 0) return;
            cleanupStarted++;
            published = (await h.OperationAsync()).ShouldNotBeNull();
            published.Phase.ShouldBe(LandPhase.CleanupStarted);
            new AgentTaskLandingState().HasPublication(published).ShouldBeTrue();
            corruption = await LandHalfResetFixture.BacklinkCorruption.CreateAsync(h.Fixture);
            // Only cleanup commands count: earlier detached-worktree preparation is legitimate.
            h.Fixture.Git.Commands.Clear();
            h.Fixture.Git.Trace.Clear();
        };
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (cleanupStarted == 0 || !LandingGit.PathsEqual(directory, h.Fixture.Source) || args[0] != "status") return;
            cleanupStatuses++;
            if (cleanupStatuses != 2) return; // Status in the final Full removal inspection.
            result.Succeeded.ShouldBeTrue();
            injections++;
            await corruption!.RedirectAsync();
        };
        try
        {
            await h.RunQueuedAsync();

            cleanupStarted.ShouldBe(1);
            cleanupStatuses.ShouldBe(2);
            injections.ShouldBe(1);
            var op = (await h.OperationAsync()).ShouldNotBeNull();
            published.ShouldNotBeNull();
            op.Id.ShouldBe(published.Id);
            op.SchemaVersion.ShouldBe(3);
            op.Publication.ShouldBe(published.Publication);
            new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
            op.OriginalSourceSha.ShouldBe(published.OriginalSourceSha);
            op.VerifiedSourceSha.ShouldBe(published.VerifiedSourceSha);
            op.ObservedRemoteTargetSha.ShouldBe(published.ObservedRemoteTargetSha);
            op.RemoteConfirmedAt.ShouldBe(published.RemoteConfirmedAt);
            op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
            op.LastReason.ShouldBe("source_changed");
            op.DirectoryRemoved.ShouldBeFalse();
            op.RegistrationRemoved.ShouldBeFalse();
            op.BranchRemoved.ShouldBeFalse();
            WorktreeSetAside.Read(op.CommonDirectory, h.Fixture.Source).ShouldBeNull("no move-aside intent was written");
            Directory.Exists(WorktreeSetAside.SetAsidePath(h.Fixture.Source)).ShouldBeFalse();
            h.Fixture.Git.RegistrationDrops.ShouldBeEmpty();
            OwnerRefWrites(h).ShouldBeEmpty();
            AssertNoResetPushOrCleanup(h);
            await corruption!.AssertPreservedAsync(local);
        }
        finally
        {
            if (corruption is not null) await corruption.DisposeAsync();
        }
    }

    private static int WorktreeLists(LandingSafetyHarness h) => h.Fixture.Git.Trace.Count(args =>
        args.Length >= 2 && args[0] == "worktree" && args[1] == "list");

    private static string[][] OwnerRefWrites(LandingSafetyHarness h) => h.Fixture.Git.Commands
        .Where(command => command.Arguments[0] == "update-ref" && command.Arguments.Contains(h.Fixture.SourceRef))
        .Select(command => command.Arguments).ToArray();

    private static void AssertNoResetPushOrCleanup(LandingSafetyHarness h)
    {
        h.Fixture.Git.Commands.Where(command => command.Arguments[0] is "reset" or "push" or "clean"
                || command.Arguments[0] == "worktree" && command.Arguments.Contains("remove"))
            .Select(command => command.Arguments).ToArray().ShouldBeEmpty("refusal must precede reset, push and cleanup");
        h.Fixture.Git.RegistrationDrops.ShouldBeEmpty();
    }

    private static async Task AssertIntentAndPinsAsync(LandingSafetyHarness h, AgentTaskLandRequest row,
        string local, string reviewed)
    {
        row.SourceAdvanceChildOperation.ShouldBe("source-adopt-reset");
        row.RecoveryLocalBeforeSha.ShouldBe(local);
        row.ExpectedSourceSha.ShouldBe(reviewed);
        row.RecoveryAdoptedAt.ShouldBeNull();
        var prefix = $"refs/antiphon/land/{h.Fixture.TaskId:N}/{row.Id:N}/adopt";
        foreach (var (name, sha) in new[] { ("local-before", local), ("source", reviewed) })
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "show-ref", "--verify", "--hash", prefix + "/" + name))
                .Trim().ShouldBe(sha, "durable L/S pins remain available for explicit recovery");
    }
}
