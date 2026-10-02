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
public sealed class AgentTaskLandHalfResetTests
{
    [Test]
    public async Task C883_IgnoredFileDoesNotHalfResetOrdinaryAdoption()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var ignored = Path.Combine(h.Fixture.Source, "obj", "build.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(ignored)!);
        await File.WriteAllTextAsync(ignored, "owned cache\n");
        string? alignedTree = null;
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (result.Succeeded && directory == h.Fixture.Source && args.Count > 0 && args[0] == "reset")
                alignedTree = (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim();
        };
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        row.SourceRefusalReason.ShouldBeNull("H.IgnoredOrdinaryAdoptionLands");
        alignedTree.ShouldNotBeNull("H.IgnoredOrdinaryResetReached");
        alignedTree.ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", reviewed + "^{tree}")).Trim(),
                "H.IgnoredOrdinaryIndexAligned");
    }

    [Test]
    public async Task C883_PreRefDirtyRefusalLeavesRefAndCheckoutAtOldTip()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var feature = Path.Combine(h.Fixture.Source, "feature.txt");
        var oldTree = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim();
        var injected = false;
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (!injected && result.Succeeded && args.Count > 1 && args[0] == "update-ref"
                && args[1].EndsWith("/adopt/source", StringComparison.Ordinal))
            {
                injected = true;
                await File.WriteAllTextAsync(feature, "real edit before branch move\n");
            }
        };
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        injected.ShouldBeTrue("H.PreRefEditInjected");
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId))
            .SourceRefusalReason.ShouldBe("source_dirty", "H.PreRefEditRefused");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(local, "H.PreRefRefusalLeavesRefAtOldTip");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe(oldTree, "H.PreRefRefusalLeavesIndexAtOldTip");
        (await File.ReadAllTextAsync(feature)).ShouldBe("real edit before branch move\n",
            "H.PreRefRefusalPreservesOwnerBytes");
    }


    [Test]
    public async Task C883_DirtyFreshRequestAtOldHeadKeepsSourceDirty()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        await File.WriteAllTextAsync(file, "real edit\n");
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty", "H.OldHeadEditRetainsSourceDirty");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(local, "H.OldHeadEditPreservesRef");
        (await File.ReadAllTextAsync(file)).ShouldBe("real edit\n", "H.OldHeadEditPreservesBytes");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C883_FilemodeFalseExecutableAdoptsAndRepairs(bool interrupted)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync(executable: true);
        await h.Fixture.RequiredAsync(h.Fixture.Source, "config", "core.filemode", "false");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(h.Fixture.Source, "run.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        if (interrupted)
        {
            fixture.Interceptor.RequestId = first.RequestId;
            var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
            await h.FailAsync(conflict);
            first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
                recoverReviewedSource: true);
        }
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        row.SourceRefusalReason.ShouldBeNull(interrupted ? "H.FilemodeFalseRepairLands" : "H.FilemodeFalseAdoptionLands");
    }

    [Test]
    public async Task C883_FreshRequestRepairsPinnedAncestor()
    {
        await using var fixture = new LandHalfResetFixture();
        var (local, reviewed, evidence, oldRequest) = await InterruptedAsync(fixture);
        var h = fixture.Harness;
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        next.RequestId.ShouldNotBe(oldRequest, "H.FreshRequestHasNewIdentity");
        string? alignedTree = null;
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (result.Succeeded && directory == h.Fixture.Source && args.Count > 0 && args[0] == "reset")
                alignedTree = (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim();
        };
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        row.RecoveryWitnessRequestId.ShouldBe(oldRequest, "H.WitnessIsDurable");
        row.RecoveryLocalBeforeSha.ShouldBe(local, "H.OldTipIsPinned");
        alignedTree.ShouldNotBeNull("H.ResetAlignedBeforeCleanup");
        alignedTree.ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", reviewed + "^{tree}")).Trim(),
                "H.IndexAlignedToReviewedSource");
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("H.FreshPublicationConfirmed");
    }

    [Test]
    public async Task C883_StagedEditSurvivesFreshAndSameRequest()
    {
        await using var fixture = new LandHalfResetFixture();
        var (_, reviewed, evidence, _) = await InterruptedAsync(fixture);
        var h = fixture.Harness;
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        var oldBytes = await File.ReadAllBytesAsync(file);
        await File.WriteAllTextAsync(file, "real staged edit\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "feature.txt");
        await File.WriteAllBytesAsync(file, oldBytes);
        var before = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim();
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        await h.RunQueuedAsync();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim()
            .ShouldBe(before, "H.StagedBlobPreserved");
        (await File.ReadAllBytesAsync(file)).ShouldBe(oldBytes, "H.WorktreeBytesPreserved");
        await using var db = h.CreateContext();
        var newest = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        newest.SourceRefusalReason.ShouldBe("source_dirty", "H.StagedEditRefused");
    }

    [Test]
    public async Task C883_UnstagedEditSurvivesFreshAndSameRequest()
    {
        await using var fixture = new LandHalfResetFixture();
        var (_, reviewed, evidence, _) = await InterruptedAsync(fixture);
        var h = fixture.Harness;
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        var edit = "real unstaged edit\n";
        await File.WriteAllTextAsync(file, edit);
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        await h.RunQueuedAsync();
        (await File.ReadAllTextAsync(file)).ShouldBe(edit, "H.UnstagedBytesPreserved");
        await using var db = h.CreateContext();
        var newest = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        newest.SourceRefusalReason.ShouldBe("source_dirty", "H.UnstagedEditRefused");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C883_UntrackedAndIgnoredArePreserved(bool ignored)
    {
        await using var fixture = new LandHalfResetFixture();
        var (_, reviewed, evidence, _) = await InterruptedAsync(fixture, bulk: true);
        var h = fixture.Harness;
        var relative = "added-00.txt";
        var file = Path.Combine(h.Fixture.Source, relative);
        if (ignored)
        {
            var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source,
                "rev-parse", "--git-path", "info/exclude")).Trim();
            await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\nadded-00.txt\n");
        }
        await File.WriteAllTextAsync(file, "real sentinel\n");
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        (await File.ReadAllTextAsync(file)).ShouldBe("real sentinel\n",
            ignored ? "H.IgnoredObstructionPreserved" : "H.UntrackedObstructionPreserved");
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty",
            ignored ? "H.IgnoredObstructionRefused" : "H.UntrackedObstructionRefused");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset", "H.ObstructedCheckoutNotReset");
    }

    [Test]
    [Arguments("absent")]
    [Arguments("not-started")]
    [Arguments("wrong-operation")]
    [Arguments("already-adopted")]
    public async Task C883_FreshRequestRequiresInterruptedWitness(string variant)
    {
        await using var fixture = new LandHalfResetFixture();
        var (local, reviewed, evidence, oldId) = await InterruptedAsync(fixture);
        var h = fixture.Harness;
        await using (var db = h.CreateContext())
        {
            var old = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == oldId);
            switch (variant)
            {
                case "absent": old.ExpectedSourceSha = new string('b', 40); break;
                case "not-started": old.SourceResolutionState = LandSourceResolutionState.None; break;
                case "wrong-operation": old.SourceAdvanceChildOperation = "source-adopt-push"; break;
                case "already-adopted": old.RecoveryAdoptedAt = DateTime.UtcNow; break;
            }
            await db.SaveChangesAsync();
        }
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        await using var verify = h.CreateContext();
        var row = await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty", $"H.{variant}.MissingWitnessRefused");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, $"H.{variant}.HeadPreserved");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim(),
                $"H.{variant}.IndexPreserved");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset", $"H.{variant}.NoReset");
    }

    [Test]
    [Arguments("index")]
    [Arguments("tracked")]
    [Arguments("untracked")]
    [Arguments("local-pin")]
    [Arguments("source-pin")]
    public async Task C883_ContentChangesBeforeResetRefuse(string variant)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Boundary.ThrowConflict = false;
        var feature = Path.Combine(h.Fixture.Source, "feature.txt");
        var oldBytes = await File.ReadAllBytesAsync(feature);
        var sentinel = Path.Combine(h.Fixture.Source, "sentinel.txt");
        string? staged = null;
        fixture.Boundary.BeforeRefMove = async () =>
        {
            switch (variant)
            {
                case "index":
                    await File.WriteAllTextAsync(feature, "real staged edit\n");
                    await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "feature.txt");
                    staged = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim();
                    await File.WriteAllBytesAsync(feature, oldBytes);
                    break;
                case "tracked": await File.WriteAllTextAsync(feature, "real tracked edit\n"); break;
                case "untracked": await File.WriteAllTextAsync(sentinel, "real sentinel\n"); break;
                case "ignored":
                    var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source,
                        "rev-parse", "--git-path", "info/exclude")).Trim();
                    await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\nsentinel.txt\n");
                    await File.WriteAllTextAsync(sentinel, "real sentinel\n");
                    break;
                case "local-pin":
                case "source-pin":
                    var pin = $"refs/antiphon/land/{h.Fixture.TaskId:N}/{first.RequestId:N}/adopt/"
                        + (variant == "local-pin" ? "local-before" : "source");
                    await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", pin,
                        variant == "local-pin" ? reviewed : local);
                    break;
            }
        };
        await h.RunQueuedAsync();
        fixture.Boundary.BeforeRefReached.ShouldBe(1, $"H.{variant}.PreRefBoundaryReached");
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        row.SourceRefusalReason.ShouldBe(variant.EndsWith("pin", StringComparison.Ordinal)
            ? "adopt_source_pin_changed" : "source_dirty", $"H.{variant}.RefusedBeforeReset");
        if (variant == "index")
            (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim()
                .ShouldBe(staged, "H.index.StagedBlobPreserved");
        if (variant == "tracked")
            (await File.ReadAllTextAsync(feature)).ShouldBe("real tracked edit\n", "H.tracked.BytesPreserved");
        if (variant is "untracked" or "ignored")
            (await File.ReadAllTextAsync(sentinel)).ShouldBe("real sentinel\n", $"H.{variant}.BytesPreserved");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset", $"H.{variant}.NoReset");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(local, $"H.{variant}.RefUnmovedOnRefusal");
    }

    [Test]
    [Arguments("review-sha")]
    [Arguments("review-clean-false")]
    [Arguments("review-clean-null")]
    [Arguments("review-superseded")]
    [Arguments("owner")]
    public async Task C883_AuthorityChangesBeforeResetRefuse(string variant)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Boundary.ThrowConflict = false;
        fixture.Boundary.BeforeRefMove = async () =>
        {
            await using var db = h.CreateContext();
            if (variant == "review-sha")
                await db.StageOutcomes.Where(o => o.Id == evidence)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceSha, local));
            else if (variant == "review-clean-false")
                await db.StageOutcomes.Where(o => o.Id == evidence)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceClean, false));
            else if (variant == "review-clean-null")
                await db.StageOutcomes.Where(o => o.Id == evidence)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceClean, (bool?)null));
            else if (variant == "review-superseded")
            {
                var prior = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == evidence);
                db.StageOutcomes.Add(new StageOutcome
                {
                    Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
                    Source = StageOutcomeSource.Delegate, SubjectTaskId = h.Fixture.TaskId,
                    StageTaskId = Guid.NewGuid(), SupersedesId = evidence,
                    ReviewedSourceSha = reviewed, ReviewedSourceClean = true,
                    ReviewedSourceRef = h.Fixture.SourceRef, ReviewedRepositoryPath = prior.ReviewedRepositoryPath,
                    CommissionedRound = VerificationRound.Final,
                    OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            else
            {
                var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                owner.Status = AgentTaskStatus.Working;
                await db.SaveChangesAsync();
            }
        };
        await h.RunQueuedAsync();
        fixture.Boundary.BeforeRefReached.ShouldBe(1, $"H.{variant}.AuthorityCutReached");
        await using var verify = h.CreateContext();
        var row = await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        var expectedCode = variant switch
        {
            "review-sha" => "review_evidence_sha_mismatch",
            "review-clean-false" or "review-clean-null" => "review_evidence_source_not_clean",
            "review-superseded" => "review_evidence_superseded",
            _ => "adopt_authority_changed",
        };
        if (variant == "owner")
        {
            // The task-locked checkpoint rejects a changed owner as stale; it must not write
            // a new refusal under the now-ineligible owner status.
            row.SourceRefusalReason.ShouldBeNull("H.owner.StaleWriterDidNotOverwriteRequest");
            row.IsPending.ShouldBeTrue("H.owner.PendingRequestLeftForSweep");
            (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId))
                .Status.ShouldBe(AgentTaskStatus.Working, "H.owner.WorkingStatusPreserved");
        }
        else
            row.SourceRefusalReason.ShouldBe(expectedCode, $"H.{variant}.AuthorityRefused");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset", $"H.{variant}.NoReset");
    }

    private static async Task<(string Local, string Reviewed, Guid Evidence, Guid RequestId)> InterruptedAsync(
        LandHalfResetFixture fixture, bool bulk = false)
    {
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync(bulk);
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Boundary.Reached.ShouldBe(1, "H.HistoricalCutReached");
        await h.FailAsync(conflict);
        return (local, reviewed, evidence, first.RequestId);
    }
}
