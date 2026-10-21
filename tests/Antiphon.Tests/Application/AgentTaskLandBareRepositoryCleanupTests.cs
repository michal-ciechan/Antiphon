using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class AgentTaskLandBareRepositoryCleanupTests
{
    [Test]
    [Arguments("obj/private", "bare")]
    [Arguments("obj/deeper/cache/private", "bare")]
    [Arguments("obj/private.git", "bare")]
    [Arguments("obj/twin", "mixed-case")]
    [Arguments("obj/head-only", "head-only")]
    public async Task C1211_PublicationPreservesBareRepository(string relative, string shape)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        fixture.Boundary.ThrowConflict = false;
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        var nested = Path.Combine(h.Fixture.Source, relative);
        await SeedBareAsync(h.Fixture, nested);
        if (shape == "mixed-case")
        {
            RenameEntry(nested, "HEAD", "HeAd", false);
            RenameEntry(nested, "objects", "ObJeCtS", true);
            RenameEntry(nested, "refs", "ReFs", true);
        }
        else if (shape == "head-only")
        {
            ReadOnlyDirectory.Delete(Path.Combine(nested, "objects"));
            ReadOnlyDirectory.Delete(Path.Combine(nested, "refs"));
            foreach (var entry in Directory.EnumerateFileSystemEntries(nested).ToArray())
                if (Path.GetFileName(entry) != "HEAD")
                {
                    if (Directory.Exists(entry)) ReadOnlyDirectory.Delete(entry);
                    else File.Delete(entry);
                }
        }
        var sentinel = Path.Combine(nested, "private-sentinel.bin");
        byte[] sentinelBytes = [0, 13, 10, 255, 42];
        await File.WriteAllBytesAsync(sentinel, sentinelBytes);
        var bytes = await TreeBytesAsync(nested);
        var marker = Path.Combine(nested, shape == "mixed-case" ? "HeAd" : "HEAD");
        await IgnoreObjAsync(h.Fixture);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        row.SourceRefusalReason.ShouldBeNull("B.PublicationAccepted");
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("B.PublicationConfirmed");
        File.Exists(marker).ShouldBeTrue("B.BareHeadSurvivesPublication");
        (await TreeBytesAsync(nested)).ShouldBe(bytes, "B.AllPrivateBytesUnchanged");
        (await File.ReadAllBytesAsync(sentinel)).ShouldBe(sentinelBytes);
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused, "B.PreservationReported");
        op.LastReason.ShouldBe("ignored_content_preserved");
        op.DirectoryRemoved.ShouldBeFalse();
        op.RegistrationRemoved.ShouldBeFalse();
        op.BranchRemoved.ShouldBeFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1211_DisposableLookalikeIsCleaned(bool missingHead)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        fixture.Boundary.ThrowConflict = false;
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        var output = Path.Combine(h.Fixture.Source, "obj", "lookalike.git");
        Directory.CreateDirectory(output);
        if (missingHead)
        {
            Directory.CreateDirectory(Path.Combine(output, "objects"));
            Directory.CreateDirectory(Path.Combine(output, "refs"));
        }
        await File.WriteAllTextAsync(Path.Combine(output, "cache.bin"), "disposable bytes\n");
        await IgnoreObjAsync(h.Fixture);
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("B.PlainOutputPublishes");
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete, "B.LookalikeCleaned");
        op.DirectoryRemoved.ShouldBeTrue();
        Directory.Exists(output).ShouldBeFalse("B.DisposableBytesDeleted");
    }

    [Test]
    [Arguments("obj/private")]
    [Arguments("obj/deeper/cache/private.git")]
    public async Task C1211_BareRecoveryIsRefused(string relative)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        var nested = Path.Combine(h.Fixture.Source, relative);
        await SeedBareAsync(h.Fixture, nested);
        var bytes = await TreeBytesAsync(nested);
        await IgnoreObjAsync(h.Fixture);
        var proof = await h.Fixture.Git.InspectRecoveryCheckoutAsync(h.Fixture.Coordinates, local, reviewed, default);
        proof.Accepted.ShouldBeFalse("B.BareRecoveryRefused");
        proof.Reason.ShouldBe("recovery_checkout_unproven");
        await h.SweepAsync();
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty");
        (await h.OperationAsync()).ShouldBeNull("B.RefusalPreventsPublication");
        (await TreeBytesAsync(nested)).ShouldBe(bytes);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1211_NestedTrackedHeadKeepsExistingAdmission(bool gitFile)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        if (!gitFile)
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        else
            fixture.Boundary.ThrowConflict = false;
        var nested = Path.Combine(h.Fixture.Source, "obj", "nested-head");
        await SeedNestedAsync(h.Fixture, nested, gitFile, trackedHead: true);
        var bytes = await TreeBytesAsync(nested);
        var admin = gitFile ? Path.Combine(h.Fixture.Root, "private-admin") : Path.Combine(nested, ".git");
        var adminBytes = await TreeBytesAsync(admin);
        await IgnoreObjAsync(h.Fixture);
        if (gitFile)
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "--no-deref",
                h.Fixture.SourceRef, reviewed, local);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(reviewed,
            "F1.ProofUsesHalfResetCheckout");
        var proof = await h.Fixture.Git.InspectRecoveryCheckoutAsync(h.Fixture.Coordinates, local, reviewed, default);
        if (gitFile)
        {
            proof.Accepted.ShouldBeFalse("F1.GitFileRecoveryStillRefused");
            proof.Reason.ShouldBe("recovery_checkout_unproven");
            // Ordinary reviewed adoption supplies cleanup authority for the file-pointer case.
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "--no-deref",
                h.Fixture.SourceRef, local, reviewed);
        }
        else
        {
            proof.Accepted.ShouldBeTrue("F1.GitDirectoryWithTrackedHeadAccepted");
            await h.SweepAsync();
        }
        await h.RunQueuedAsync();
        await AssertPreservedPublicationAsync(h, nested, bytes);
        (await TreeBytesAsync(admin)).ShouldBe(adminBytes, "F1.PrivateAdministrationUnchanged");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1211_BareInsideAcceptedNestedRepositoryIsPreserved(bool trackedHead)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        var nested = Path.Combine(h.Fixture.Source, "obj", "enclosing");
        await SeedNestedAsync(h.Fixture, nested, gitFile: false, trackedHead);
        var bare = Path.Combine(nested, "private.git");
        await SeedBareAsync(h.Fixture, bare);
        await File.WriteAllTextAsync(Path.Combine(bare, "private-sentinel.txt"), "private bare bytes\n");
        var bytes = await TreeBytesAsync(nested);
        await IgnoreObjAsync(h.Fixture);
        var proof = await h.Fixture.Git.InspectRecoveryCheckoutAsync(h.Fixture.Coordinates, local, reviewed, default);
        proof.Accepted.ShouldBeTrue("F2.EnclosingRepositoryAdmissionDoesNotDescend");
        await h.SweepAsync();
        await h.RunQueuedAsync();
        await AssertPreservedPublicationAsync(h, nested, bytes);
        File.Exists(Path.Combine(bare, "HEAD")).ShouldBeTrue("F2.EnclosedBareHeadSurvivesCleanup");
    }

    [Test]
    public async Task C1211_HeadOnlyOutputIsPreservedWithoutBareProofRefusal()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        var output = Path.Combine(h.Fixture.Source, "obj", "head-only");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "HEAD"), "ordinary output, not bare administration\n");
        var bytes = await TreeBytesAsync(output);
        await IgnoreObjAsync(h.Fixture);
        var proof = await h.Fixture.Git.InspectRecoveryCheckoutAsync(h.Fixture.Coordinates, local, reviewed, default);
        proof.Accepted.ShouldBeTrue("F1.HeadOnlyIsNotStrictBareSignature");
        await h.SweepAsync();
        await h.RunQueuedAsync();
        await AssertPreservedPublicationAsync(h, output, bytes);
    }

    private static async Task SeedNestedAsync(LandingGitFixture fixture, string nested, bool gitFile, bool trackedHead)
    {
        Directory.CreateDirectory(nested);
        if (gitFile)
            await fixture.RequiredAsync(nested, "init", "--separate-git-dir", Path.Combine(fixture.Root, "private-admin"));
        else
            await fixture.RequiredAsync(nested, "init");
        var name = trackedHead ? "HEAD" : "committed.txt";
        await File.WriteAllTextAsync(Path.Combine(nested, name), "tracked private worktree bytes\n");
        await fixture.RequiredAsync(nested, "add", "--", name);
        await fixture.RequiredAsync(nested, "commit", "-m", "private tracked worktree file");
        await File.WriteAllTextAsync(Path.Combine(nested, "sentinel.txt"), "uncommitted private bytes\n");
    }

    private static async Task AssertPreservedPublicationAsync(LandingSafetyHarness h, string nested, string[] bytes)
    {
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("F.PublicationConfirmed");
        Directory.Exists(nested).ShouldBeTrue("F.NestedDirectorySurvivesCleanup");
        (await TreeBytesAsync(nested)).ShouldBe(bytes, "F.PrivateBytesUnchanged");
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe("ignored_content_preserved");
        op.DirectoryRemoved.ShouldBeFalse();
        op.RegistrationRemoved.ShouldBeFalse();
        op.BranchRemoved.ShouldBeFalse();
    }

    [Test]
    public async Task C1211_LateBareRepositorySurvivesSetAsideResume()
    {
        await using var h = await WorktreeGuardedCleanupTests.RemovalHarness.CreateAsync();
        h.CleanFirst = true;
        string? nested = null;
        string[]? bytes = null;
        h.OnRemove = async _ =>
        {
            nested = Path.Combine(h.SetAsideTrees().ShouldHaveSingleItem(), "obj", "private.git");
            // Clone from the stable repository: owner checkout has already moved aside.
            await SeedBareAsync(h.H.Fixture, nested);
            await File.WriteAllTextAsync(Path.Combine(nested, "sentinel.txt"), "late private bytes\n");
            bytes = await TreeBytesAsync(nested);
        };
        var first = await h.RemoveAsync(context: false);
        nested.ShouldNotBeNull("B.LateBareInjected");
        File.Exists(Path.Combine(nested, "HEAD")).ShouldBeTrue("B.LateBareHeadSurvivesDelete");
        (await TreeBytesAsync(nested)).ShouldBe(bytes);
        first.Residue.ShouldBe("ignored_content_preserved");
        first.Unregistered.ShouldBeTrue();
        first.BranchDeleted.ShouldBeFalse();
        h.OnRemove = null;
        var resumed = await h.RemoveAsync(context: false);
        resumed.Residue.ShouldBe("ignored_content_preserved", "B.ResumePreservationReported");
        File.Exists(Path.Combine(nested, "HEAD")).ShouldBeTrue("B.ResumeBareHeadSurvivesDelete");
        (await TreeBytesAsync(nested)).ShouldBe(bytes);
        h.SetAsideTrees().ShouldHaveSingleItem();
        resumed.BranchDeleted.ShouldBeFalse();
    }

    private static async Task SeedBareAsync(LandingGitFixture fixture, string nested)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        await fixture.RequiredAsync(fixture.Repository, "clone", "--bare", fixture.Repository, nested);
        (await fixture.RequiredAsync(nested, "rev-parse", "--is-bare-repository")).Trim().ShouldBe("true");
        File.Exists(Path.Combine(nested, ".git")).ShouldBeFalse();
        Directory.Exists(Path.Combine(nested, ".git")).ShouldBeFalse();
        Directory.EnumerateFiles(Path.Combine(nested, "objects"), "*", SearchOption.AllDirectories)
            .ShouldNotBeEmpty("B.RealCommittedObjects");
    }

    private static async Task IgnoreObjAsync(LandingGitFixture fixture)
    {
        var exclude = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, fixture.Source), "\n/obj/\n");
    }

    private static void RenameEntry(string root, string from, string to, bool directory)
    {
        // A temporary name exercises the actual case twin on case-insensitive filesystems too.
        var temporary = Path.Combine(root, "rename-temporary");
        if (directory)
        {
            Directory.Move(Path.Combine(root, from), temporary);
            Directory.Move(temporary, Path.Combine(root, to));
        }
        else
        {
            File.Move(Path.Combine(root, from), temporary);
            File.Move(temporary, Path.Combine(root, to));
        }
    }

    private static async Task<string[]> TreeBytesAsync(string root)
    {
        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            result.Add(Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(await File.ReadAllBytesAsync(path)));
        return result.ToArray();
    }
}
