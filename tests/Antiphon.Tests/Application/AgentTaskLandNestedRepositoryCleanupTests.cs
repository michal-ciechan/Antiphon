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
public sealed class AgentTaskLandNestedRepositoryCleanupTests
{
    [Test]
    [Arguments("obj/nested", false)]
    [Arguments("ignored/nested", false)]
    [Arguments("obj/deeper/cache/nested", false)]
    [Arguments("obj/nested", true)]
    public async Task C971_PublicationPreservesNestedRepository(string relative, bool gitFile)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        if (!gitFile)
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        else
            fixture.Boundary.ThrowConflict = false; // File pointers remain refused by strict half-reset proof.

        var nested = Path.Combine(h.Fixture.Source, relative);
        Directory.CreateDirectory(nested);
        if (gitFile)
            await h.Fixture.RequiredAsync(nested, "init", "--separate-git-dir", Path.Combine(h.Fixture.Root, "private-admin"));
        else
            await h.Fixture.RequiredAsync(nested, "init");
        await File.WriteAllTextAsync(Path.Combine(nested, "committed.txt"), "nested committed bytes\n");
        await h.Fixture.RequiredAsync(nested, "add", "committed.txt");
        await h.Fixture.RequiredAsync(nested, "commit", "-m", "private repository");
        var sentinel = Path.Combine(nested, "sentinel.txt");
        byte[] sentinelBytes = [0, 1, 13, 10, 255, 42];
        await File.WriteAllBytesAsync(sentinel, sentinelBytes); // Deliberately uncommitted private work.
        var marker = Path.Combine(nested, ".git");
        var adminBytes = await AdministrativeBytesAsync(marker);
        var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\n/obj/\n/ignored/\n");
        if (!gitFile) await h.SweepAsync();
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        request.SourceRefusalReason.ShouldBeNull("N.Cleanup.RecoveryAccepted");
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("N.Cleanup.PublicationConfirmed");
        (File.Exists(marker) || Directory.Exists(marker)).ShouldBeTrue("N.Cleanup.NestedGitSurvivesPublication");
        File.Exists(sentinel).ShouldBeTrue("N.Cleanup.UncommittedSentinelSurvivesPublication");
        (await File.ReadAllBytesAsync(sentinel)).ShouldBe(sentinelBytes, "N.Cleanup.UncommittedBytesUnchanged");
        (await AdministrativeBytesAsync(marker)).ShouldBe(adminBytes, "N.Cleanup.AdministrativeBytesUnchanged");
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused, "N.Cleanup.PreservationReported");
        op.LastReason.ShouldBe("ignored_content_preserved", "N.Cleanup.PreservedReason");
        op.DirectoryRemoved.ShouldBeFalse("N.Cleanup.OwnerWorktreeRetained");
        op.RegistrationRemoved.ShouldBeFalse("N.Cleanup.OwnerRegistrationRetained");
        op.BranchRemoved.ShouldBeFalse("N.Cleanup.OwnerBranchRetained");
    }

    [Test]
    public async Task C971_PlainDisposableOutputIsCleaned()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        var output = Path.Combine(h.Fixture.Source, "obj", "disposable.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, "ordinary build output\n");
        var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\n/obj/\n");
        await h.SweepAsync();
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("N.Cleanup.PlainOutputPublishes");
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete, "N.Cleanup.PlainOutputCleaned");
        op.DirectoryRemoved.ShouldBeTrue();
        File.Exists(output).ShouldBeFalse("N.Cleanup.DisposableBytesDeleted");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C971_UnsupportedNestedRecoveryIsRefused(bool gitFile)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        var nested = Path.Combine(h.Fixture.Source, "nested");
        Directory.CreateDirectory(nested);
        if (gitFile)
        {
            await h.Fixture.RequiredAsync(nested, "init", "--separate-git-dir", Path.Combine(h.Fixture.Root, "private-admin"));
            var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
            await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\n/nested/\n");
        }
        else await h.Fixture.RequiredAsync(nested, "init");
        await File.WriteAllTextAsync(Path.Combine(nested, "committed.txt"), "private\n");
        await h.Fixture.RequiredAsync(nested, "add", "committed.txt");
        await h.Fixture.RequiredAsync(nested, "commit", "-m", "private repository");
        var sentinel = Path.Combine(nested, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "uncommitted private bytes\n");
        var adminBytes = await AdministrativeBytesAsync(Path.Combine(nested, ".git"));
        var proof = await h.Fixture.Git.InspectRecoveryCheckoutAsync(h.Fixture.Coordinates, local, reviewed, default);
        proof.Accepted.ShouldBeFalse("N.Cleanup.UnsafeStrictProofRefused");
        proof.Reason.ShouldBe(gitFile ? "recovery_checkout_unproven" : "source_dirty");
        await h.SweepAsync();
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty", "N.Cleanup.UnsafeProofRefused");
        (await h.OperationAsync()).ShouldBeNull("N.Cleanup.RefusalPreventsPublication");
        (await File.ReadAllTextAsync(sentinel)).ShouldBe("uncommitted private bytes\n");
        (await AdministrativeBytesAsync(Path.Combine(nested, ".git"))).ShouldBe(adminBytes);
    }

    [Test]
    public async Task C971_LateNestedRepositorySurvivesSetAsideResume()
    {
        await using var h = await WorktreeGuardedCleanupTests.RemovalHarness.CreateAsync();
        h.CleanFirst = true;
        string? nested = null;
        string[]? adminBytes = null;
        h.OnRemove = async _ =>
        {
            nested = Path.Combine(h.SetAsideTrees().ShouldHaveSingleItem(), "obj", "nested");
            Directory.CreateDirectory(nested);
            await h.H.Fixture.RequiredAsync(nested, "init");
            await File.WriteAllTextAsync(Path.Combine(nested, "committed.txt"), "private\n");
            await h.H.Fixture.RequiredAsync(nested, "add", "committed.txt");
            await h.H.Fixture.RequiredAsync(nested, "commit", "-m", "private repository");
            await File.WriteAllTextAsync(Path.Combine(nested, "sentinel.txt"), "late uncommitted bytes\n");
            adminBytes = await AdministrativeBytesAsync(Path.Combine(nested, ".git"));
        };
        var first = await h.RemoveAsync(context: false);
        nested.ShouldNotBeNull("N.Cleanup.LateRepositoryInjected");
        Directory.Exists(Path.Combine(nested, ".git")).ShouldBeTrue("N.Cleanup.LateGitSurvivesDelete");
        (await AdministrativeBytesAsync(Path.Combine(nested, ".git"))).ShouldBe(adminBytes);
        (await File.ReadAllTextAsync(Path.Combine(nested, "sentinel.txt"))).ShouldBe("late uncommitted bytes\n");
        first.Residue.ShouldBe("ignored_content_preserved", "N.Cleanup.LatePreservationReported");
        first.Unregistered.ShouldBeTrue();
        first.BranchDeleted.ShouldBeFalse();
        h.OnRemove = null;
        var resumed = await h.RemoveAsync(context: false);
        resumed.Residue.ShouldBe("ignored_content_preserved", "N.Cleanup.ResumePreservationReported");
        (await AdministrativeBytesAsync(Path.Combine(nested, ".git"))).ShouldBe(adminBytes);
        (await File.ReadAllTextAsync(Path.Combine(nested, "sentinel.txt"))).ShouldBe("late uncommitted bytes\n");
        h.SetAsideTrees().ShouldHaveSingleItem("N.Cleanup.PreservedSetAsideRetained");
        resumed.BranchDeleted.ShouldBeFalse();
    }

    private static async Task<string[]> AdministrativeBytesAsync(string marker)
    {
        if (File.Exists(marker)) return [Convert.ToHexString(await File.ReadAllBytesAsync(marker))];
        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(marker, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            result.Add(Path.GetRelativePath(marker, path) + ":" + Convert.ToHexString(await File.ReadAllBytesAsync(path)));
        return result.ToArray();
    }
}
