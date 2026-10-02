using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CP-5 is commissioned on native Windows only. A wrong-host selection must fail visibly.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandHalfResetWindowsTests
{
    [Test]
    public async Task C883_LinkedWorktreeWithSpacesRecovers()
    {
        RequireWindows();
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N") + " with spaces");
        await using var fixture = new LandHalfResetFixture(root);
        var (_, reviewed, evidence) = await InterruptAsync(fixture);
        var h = fixture.Harness;
        h.Fixture.Source.Contains(" with spaces", StringComparison.Ordinal)
            .ShouldBeTrue("W.RegisteredPathHasSpaces");
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("W.SpacedLinkedWorktreePublishes");
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId))
            .SourceRefusalReason.ShouldBeNull("W.SpacedRegisteredIdentityAccepted");
    }

    [Test]
    public async Task C883_StagedBlobSurvivesNativeIndex()
    {
        RequireWindows();
        await using var fixture = new LandHalfResetFixture();
        var (_, reviewed, evidence) = await InterruptAsync(fixture);
        var h = fixture.Harness;
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        var old = await File.ReadAllBytesAsync(file);
        await File.WriteAllTextAsync(file, "staged native edit\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "feature.txt");
        var staged = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim();
        await File.WriteAllBytesAsync(file, old);
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim()
            .ShouldBe(staged, "W.StagedBlobUnchanged");
        (await File.ReadAllBytesAsync(file)).ShouldBe(old, "W.NativeWorktreeBytesUnchanged");
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId))
            .SourceRefusalReason.ShouldBe("source_dirty", "W.StagedBlobRefused");
    }

    [Test]
    public async Task C883_BuiltinCrLfRecoveryPreservesRealEdits()
    {
        RequireWindows();
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "config", "core.autocrlf", "true");
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "reset", "--hard", local);
        (await File.ReadAllTextAsync(file)).Contains("\r\n", StringComparison.Ordinal)
            .ShouldBeTrue("W.NativeFixtureCheckedOutCrLf");
        (await h.Fixture.Git.RunAsync(h.Fixture.Source,
            ["diff", "--quiet", local, "--"], CancellationToken.None)).ExitCode
            .ShouldBe(0, "W.CrLfIsGitCleanAtOldTip");
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        await h.FailAsync(conflict);
        var repaired = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        await using (var db = h.CreateContext())
            (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == repaired.RequestId))
                .SourceRefusalReason.ShouldBeNull("W.CrLfRecoverySucceeds");

        await using var editedFixture = new LandHalfResetFixture();
        var e = editedFixture.Harness;
        var (editedLocal, editedReviewed, editedEvidence) = await editedFixture.SeedReviewedDescendantAsync();
        await e.Fixture.RequiredAsync(e.Fixture.Source, "config", "core.autocrlf", "true");
        var editedFile = Path.Combine(e.Fixture.Source, "feature.txt");
        await e.Fixture.RequiredAsync(e.Fixture.Source, "reset", "--hard", editedLocal);
        var before = (await File.ReadAllTextAsync(editedFile)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var interrupted = await e.RequestAsync(expectedSourceSha: editedReviewed,
            reviewEvidenceId: editedEvidence, recoverReviewedSource: true);
        editedFixture.Interceptor.RequestId = interrupted.RequestId;
        var editConflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => e.RunQueuedAsync());
        await e.FailAsync(editConflict);
        await File.WriteAllTextAsync(editedFile, before);
        var next = await e.RequestAsync(expectedSourceSha: editedReviewed, reviewEvidenceId: editedEvidence,
            recoverReviewedSource: true);
        await e.RunQueuedAsync();
        (await File.ReadAllTextAsync(editedFile)).ShouldBe(before, "W.RawByteEditPreserved");
        await using var verify = e.CreateContext();
        (await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId))
            .SourceRefusalReason.ShouldBe("source_dirty", "W.RawByteEditRefused");
    }

    [Test]
    public async Task C883_CaseAliasCannotChangeRegisteredIdentity()
    {
        RequireWindows();
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var sourceHead = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        var other = Path.Combine(f.Root, "trees", "other");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", other, sourceHead);
        var alias = Path.Combine(f.Root, "TREES", "OTHER");
        var forged = new LandSourceCoordinates(f.TaskId, f.Repository, alias, f.SourceRef, f.TargetRef);
        var proof = await f.Git.InspectRecoveryCheckoutAsync(forged, sourceHead, sourceHead,
            CancellationToken.None);
        proof.Accepted.ShouldBeFalse("W.CaseAliasCannotRedirectOwnerReset");
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(sourceHead, "W.RegisteredOwnerHeadPreserved");
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("CP-5 requires native Windows Git and NTFS path semantics");
    }

    private static async Task<(string Local, string Reviewed, Guid Evidence)> InterruptAsync(
        LandHalfResetFixture fixture)
    {
        var result = await fixture.SeedReviewedDescendantAsync();
        var first = await fixture.Harness.RequestAsync(expectedSourceSha: result.Reviewed,
            reviewEvidenceId: result.Evidence, recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => fixture.Harness.RunQueuedAsync());
        await fixture.Harness.FailAsync(conflict);
        return result;
    }
}
