using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
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
    public async Task C883_FreshRequestRepairsPinnedAncestor()
    {
        await using var fixture = new LandHalfResetFixture();
        var (local, reviewed, evidence, oldRequest) = await InterruptedAsync(fixture);
        var h = fixture.Harness;
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        next.RequestId.ShouldNotBe(oldRequest, "H.FreshRequestHasNewIdentity");
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        row.RecoveryWitnessRequestId.ShouldBe(oldRequest, "H.WitnessIsDurable");
        row.RecoveryLocalBeforeSha.ShouldBe(local, "H.OldTipIsPinned");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", reviewed + "^{tree}")).Trim(),
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

    private static async Task<(string Local, string Reviewed, Guid Evidence, Guid RequestId)> InterruptedAsync(
        LandHalfResetFixture fixture)
    {
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Boundary.Reached.ShouldBe(1, "H.HistoricalCutReached");
        await h.FailAsync(conflict);
        return (local, reviewed, evidence, first.RequestId);
    }
}
