using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.Tests.Application;

// Native Windows rows must fail visibly when selected on the wrong host.
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
        var brief = Path.Combine(h.Fixture.Source, ".antiphon", "inbox", "brief.md");
        var obj = Path.Combine(h.Fixture.Source, "obj", "x");
        Directory.CreateDirectory(Path.GetDirectoryName(brief)!);
        Directory.CreateDirectory(Path.GetDirectoryName(obj)!);
        await File.WriteAllTextAsync(brief, "private brief\n");
        await File.WriteAllTextAsync(obj, "private build output\n");
        var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\n.antiphon/\nobj/\n");
        string? preservedBrief = null;
        string? preservedObj = null;
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (!result.Succeeded || directory != h.Fixture.Source || args.Count == 0 || args[0] != "reset") return;
            preservedBrief = await File.ReadAllTextAsync(brief);
            preservedObj = await File.ReadAllTextAsync(obj);
        };
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("W.SpacedLinkedWorktreePublishes");
        preservedBrief.ShouldBe("private brief\n", "W.IgnoredBriefPreserved");
        preservedObj.ShouldBe("private build output\n", "W.IgnoredObjPreserved");
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
        await CheckoutTrackedFixtureFilesWithCrLfAsync(h.Fixture);
        (await File.ReadAllTextAsync(file)).Contains("\r\n", StringComparison.Ordinal)
            .ShouldBeTrue("W.NativeFixtureCheckedOutCrLf");
        (await h.Fixture.Git.RunAsync(h.Fixture.Source,
            ["diff", "--quiet", local, "--"], CancellationToken.None)).ExitCode
            .ShouldBe(0, "W.CrLfIsGitCleanAtOldTip");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "status", "--porcelain=v1", "--untracked-files=all"))
            .ShouldBe(string.Empty, "W.CrLfCheckoutHasFreshIndexStat");
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
        await CheckoutTrackedFixtureFilesWithCrLfAsync(e.Fixture);
        var before = (await File.ReadAllTextAsync(editedFile)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var interrupted = await e.RequestAsync(expectedSourceSha: editedReviewed,
            reviewEvidenceId: editedEvidence, recoverReviewedSource: true);
        editedFixture.Interceptor.RequestId = interrupted.RequestId;
        var editConflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => e.RunQueuedAsync());
        await e.FailAsync(editConflict);
        await File.WriteAllTextAsync(editedFile, before);
        (await e.Fixture.Git.RunAsync(e.Fixture.Source,
            ["diff", "--quiet", editedLocal, "--"], CancellationToken.None)).ExitCode
            .ShouldBe(0, "W.RawEditNormalizesToOldBlob");
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
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N") + " with spaces");
        await using var f = new LandingGitFixture(root);
        await f.InitializeAsync();
        var sourceHead = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(f.Source, "keep.txt"));
        var admin = (await f.RequiredAsync(f.Source, "rev-parse", "--absolute-git-dir")).Trim();
        var index = Path.Combine(admin, "index");
        var indexBytes = await File.ReadAllBytesAsync(index);
        using var sourceLink = DirectoryLink.TryCreate(Path.Combine(f.Root, "source junction"), f.Source)
            .ShouldNotBeNull("native qualification requires an actual owned junction");
        File.GetAttributes(sourceLink.Path).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
        foreach (var same in new[] { f.Source.ToUpperInvariant(), f.Source.Replace('\\', '/'), sourceLink.Path })
        {
            var accepted = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates with { WorktreePath = same },
                sourceHead, sourceHead, CancellationToken.None);
            accepted.Accepted.ShouldBeTrue("W.SameRegisteredIdentityAccepted: " + same);
        }
        var other = Path.Combine(f.Root, "trees", "other");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", other, sourceHead);
        using var otherLink = DirectoryLink.TryCreate(Path.Combine(f.Root, "other junction"), other)
            .ShouldNotBeNull("native qualification requires an actual owned junction");
        foreach (var alias in new[] { other.ToUpperInvariant(), other.Replace('\\', '/'), otherLink.Path })
        {
            var forged = new LandSourceCoordinates(f.TaskId, f.Repository, alias, f.SourceRef, f.TargetRef);
            var proof = await f.Git.InspectRecoveryCheckoutAsync(forged, sourceHead, sourceHead, CancellationToken.None);
            proof.Accepted.ShouldBeFalse("W.CaseAliasCannotRedirectOwnerReset: " + alias);
        }
        using (var held = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var locked = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, sourceHead, sourceHead,
                CancellationToken.None);
            locked.Accepted.ShouldBeFalse("W.LockedIndexCannotAuthorizeReset");
        }
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(sourceHead, "W.RegisteredOwnerHeadPreserved");
        (await File.ReadAllBytesAsync(index)).ShouldBe(indexBytes, "W.NativeIndexPreserved");
        (await File.ReadAllBytesAsync(Path.Combine(f.Source, "keep.txt"))).ShouldBe(sourceBytes, "W.NativeBytesPreserved");
        var shortName = new StringBuilder(32768);
        var shortLength = GetShortPathNameW(f.Source, shortName, (uint)shortName.Capacity);
        shortLength.ShouldBeGreaterThan(0u, "W.NativeShortNameAvailable");
        shortLength.ShouldBeLessThan((uint)shortName.Capacity);
        shortName.ToString().ShouldNotBe(f.Source, "W.NativeShortNameMustExerciseDifferentSpelling");
        var shortProof = await f.Git.InspectRecoveryCheckoutAsync(
            f.Coordinates with { WorktreePath = shortName.ToString() }, sourceHead, sourceHead, CancellationToken.None);
        shortProof.Accepted.ShouldBeTrue("W.ShortNameOfSameRegisteredIdentityAccepted");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetShortPathNameW(string path, StringBuilder shortPath, uint capacity);

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("CP-5 requires native Windows Git and NTFS path semantics");
    }

    private static async Task CheckoutTrackedFixtureFilesWithCrLfAsync(LandingGitFixture fixture)
    {
        foreach (var relative in (await fixture.RequiredAsync(fixture.Source, "ls-files", "-z"))
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(fixture.Source, relative.Replace('/', Path.DirectorySeparatorChar));
            File.Delete(full);
        }
        await fixture.RequiredAsync(fixture.Source, "checkout", "--", ".");
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
