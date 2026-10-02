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
    [Arguments(false)]
    [Arguments(true)]
    public async Task C954_IgnoredResidueAllowsHalfResetPublication(bool freshRequest)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        var brief = Path.Combine(h.Fixture.Source, ".antiphon", "inbox", "brief.md");
        var obj = Path.Combine(h.Fixture.Source, "obj", "x");
        Directory.CreateDirectory(Path.GetDirectoryName(brief)!);
        Directory.CreateDirectory(Path.GetDirectoryName(obj)!);
        await File.WriteAllTextAsync(brief, "private brief\n");
        await File.WriteAllTextAsync(obj, "private build output\n");
        var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\n.antiphon/\nobj/\n");
        string? alignedHead = null;
        string? preservedBrief = null;
        string? preservedObj = null;
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (!result.Succeeded || directory != h.Fixture.Source || args.Count == 0 || args[0] != "reset") return;
            alignedHead = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
            preservedBrief = await File.ReadAllTextAsync(brief);
            preservedObj = await File.ReadAllTextAsync(obj);
        };
        if (freshRequest) await h.FailAsync(new IOException("interrupted adoption"));
        else await h.SweepAsync();
        var requestId = freshRequest
            ? (await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
                recoverReviewedSource: true)).RequestId
            : first.RequestId;
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
        row.SourceRefusalReason.ShouldBeNull(freshRequest ? "H.FreshIgnoredResidueAccepted" : "H.SameRequestIgnoredResidueAccepted");
        alignedHead.ShouldBe(reviewed, "H.IgnoredResidueHeadAligned");
        preservedBrief.ShouldBe("private brief\n", "H.IgnoredBriefPreserved");
        preservedObj.ShouldBe("private build output\n", "H.IgnoredObjPreserved");
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue(freshRequest ? "H.FreshIgnoredResiduePublishes" : "H.SameRequestIgnoredResiduePublishes");
    }

    [Test]
    public async Task C883_IgnoredFileDoesNotHalfResetOrdinaryAdoption()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var ignored = Path.Combine(h.Fixture.Source, "obj", "build.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(ignored)!);
        await File.WriteAllTextAsync(ignored, "owned cache\n");
        var strictProbes = 0;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Count == 0 || args[0] != "check-attr")
                return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
            strictProbes++;
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(
                new Antiphon.Server.Application.Dtos.LandingGitResult(128, "", "strict_inspector_unexpected"));
        };
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
        strictProbes.ShouldBe(0, "H.OrdinaryAdoptionSkipsStrictInspector");
        alignedTree.ShouldNotBeNull("H.IgnoredOrdinaryResetReached");
        alignedTree.ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", reviewed + "^{tree}")).Trim(),
                "H.IgnoredOrdinaryIndexAligned");
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("H.IgnoredOrdinaryPublicationConfirmed");
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
    public async Task C883_StaleIndexSizeCrLfRewriteRefusesBeforeAdoption()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "config", "core.autocrlf", "true");
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        var lf = await File.ReadAllTextAsync(file);
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(file, crlf);
        new FileInfo(file).Length.ShouldBeGreaterThan(System.Text.Encoding.UTF8.GetByteCount(lf),
            "H.StaleIndexSizeFixtureHasLongerWorktreeBytes");
        (await h.Fixture.Git.RunAsync(h.Fixture.Source,
            ["diff", "--quiet", local, "--"], CancellationToken.None)).ExitCode
            .ShouldBe(0, "H.StaleIndexSizeNormalizedDiffIsClean");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "status", "--porcelain=v1", "--untracked-files=all"))
            .ShouldContain(" M feature.txt", customMessage: "H.StaleIndexSizeStatusIsDirty");

        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId))
            .SourceRefusalReason.ShouldBe("source_dirty", "H.StaleIndexSizeRefusedBeforeAdoption");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(local, "H.StaleIndexSizeLeavesOldHead");
        (await File.ReadAllTextAsync(file)).ShouldBe(crlf, "H.StaleIndexSizePreservesBytes");
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
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue(interrupted ? "H.FilemodeFalseRepairPublishes" : "H.FilemodeFalseAdoptionPublishes");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C883_FreshRequestRepairsPinnedAncestor(bool adoption)
    {
        await using var fixture = new LandHalfResetFixture();
        var (local, reviewed, evidence, oldRequest) = await InterruptedAsync(fixture, adoption: adoption);
        var h = fixture.Harness;
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: !adoption, adoptFromTaskId: fixture.AdoptionSourceId);
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
        if (adoption)
        {
            (await h.Fixture.RequiredAsync(fixture.AdoptionSourcePath!, "rev-parse", "HEAD")).Trim().ShouldBe(reviewed, "H.SeparateSourceHeadPreserved");
            (await File.ReadAllTextAsync(Path.Combine(fixture.AdoptionSourcePath!, "feature.txt"))).ShouldBe("reviewed feature\n", "H.SeparateSourceBytesPreserved");
        }
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("H.FreshPublicationConfirmed");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C883_StagedEditSurvivesFreshAndSameRequest(bool sameRequest)
    {
        await using var fixture = new LandHalfResetFixture();
        var (local, reviewed, evidence, oldId) = await InterruptedAsync(fixture, sameRequest: sameRequest);
        var h = fixture.Harness;
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        var oldBytes = await File.ReadAllBytesAsync(file);
        await File.WriteAllTextAsync(file, "real staged edit\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "feature.txt");
        await File.WriteAllBytesAsync(file, oldBytes);
        var before = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim();
        var nextId = sameRequest ? oldId : (await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true)).RequestId;
        var indexBefore = (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim();
        await h.RunQueuedAsync();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim().ShouldBe(indexBefore, "H.EditPreservesIndex");
        AssertNoResetOrPublication(h);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim()
            .ShouldBe(before, "H.StagedBlobPreserved");
        (await File.ReadAllBytesAsync(file)).ShouldBe(oldBytes, "H.WorktreeBytesPreserved");
        await using var db = h.CreateContext();
        var newest = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == nextId);
        newest.SourceRefusalReason.ShouldBe("source_dirty", "H.StagedEditRefused");
        await AssertOrdinaryStagedEditAsync(sameRequest);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C883_UnstagedEditSurvivesFreshAndSameRequest(bool sameRequest)
    {
        await using var fixture = new LandHalfResetFixture();
        var (local, reviewed, evidence, oldId) = await InterruptedAsync(fixture, sameRequest: sameRequest);
        var h = fixture.Harness;
        var file = Path.Combine(h.Fixture.Source, "feature.txt");
        var edit = "real unstaged edit\n";
        await File.WriteAllTextAsync(file, edit);
        var nextId = sameRequest ? oldId : (await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true)).RequestId;
        var indexBefore = (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim();
        await h.RunQueuedAsync();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim().ShouldBe(indexBefore, "H.EditPreservesIndex");
        AssertNoResetOrPublication(h);
        (await File.ReadAllTextAsync(file)).ShouldBe(edit, "H.UnstagedBytesPreserved");
        await using var db = h.CreateContext();
        var newest = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == nextId);
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
        if (variant == "remote") (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(local);
        if (variant == "fingerprint") (await h.Fixture.RequiredAsync(h.Fixture.Repository, "remote", "get-url", "origin")).Trim().ShouldBe(Path.Combine(h.Fixture.Root, "alternate.git"));
    }

    [Test]
    [Arguments("index")]
    [Arguments("tracked")]
    [Arguments("untracked")]
    [Arguments("ignored")]
    [Arguments("local-pin")]
    [Arguments("source-pin")]
    public async Task C883_ContentChangesBeforeResetRefuse(string variant)
    {
        if (variant == "ignored") { await AssertIgnoredContentChangeAsync(); return; }
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
    [Arguments("remote")]
    [Arguments("fingerprint")]
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
            else if (variant == "remote")
                await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.SourceRef, local, reviewed);
            else if (variant == "fingerprint")
            {
                var alternate = Path.Combine(h.Fixture.Root, "alternate.git");
                await h.Fixture.RequiredAsync(h.Fixture.Root, "clone", "--bare", h.Fixture.Remote, alternate);
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "remote", "set-url", "origin", alternate);
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
            "remote" or "fingerprint" => "adopt_source_remote_changed",
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

    [Test]
    [Arguments("owner")]
    [Arguments("ref")]
    [Arguments("worktree")]
    [Arguments("sha")]
    [Arguments("fingerprint")]
    [Arguments("common-directory")]
    [Arguments("source-identity")]
    [Arguments("local-pin")]
    [Arguments("source-pin")]
    [Arguments("ambiguous")]
    public async Task C883_FreshRequestRejectsWitnessIdentity(string variant)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence, oldId) = await InterruptedAsync(fixture, equivalentOldTip: variant == "ambiguous");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(h.Fixture.Source, "feature.txt"));
        var prefix = $"refs/antiphon/land/{h.Fixture.TaskId:N}/{oldId:N}/adopt";
        await using (var db = h.CreateContext())
        {
            var old = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == oldId);
            switch (variant)
            {
                case "owner":
                    var owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId);
                    var otherId = Guid.NewGuid();
                    db.AgentTasks.Add(new AgentTask { Id = otherId, RootTaskId = otherId, Title = "other owner", Goal = "fixture",
                        Kind = owner.Kind, Role = owner.Role, Workspace = owner.Workspace, WorkingDirectory = owner.WorkingDirectory,
                        RepoPath = owner.RepoPath, Status = AgentTaskStatus.Failed, ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow });
                    old.TaskId = otherId;
                    break;
                case "ref": old.SourceFullRefSnapshot = "refs/heads/other"; break;
                case "worktree": old.WorktreePathSnapshot = h.Fixture.Observer; break;
                case "sha": old.ExpectedSourceSha = local; break;
                case "fingerprint": old.RecoverySourceFingerprint = new string('0', 64); break;
                case "common-directory": old.RepositoryPathSnapshot = h.Fixture.Observer; break;
                case "source-identity": old.RecoverySourceTaskId = Guid.NewGuid(); break;
                case "local-pin": await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", prefix + "/local-before", reviewed); break;
                case "source-pin": await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", prefix + "/source", local); break;
                case "ambiguous":
                    var duplicate = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == oldId);
                    duplicate.Id = Guid.NewGuid();
                    duplicate.RecoveryLocalBeforeSha = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^")).Trim();
                    (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", duplicate.RecoveryLocalBeforeSha + "^{tree}")).Trim()
                        .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim(), "H.AmbiguousEqualOldTrees");
                    (await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["merge-base", "--is-ancestor", duplicate.RecoveryLocalBeforeSha, reviewed], CancellationToken.None))
                        .Succeeded.ShouldBeTrue("H.BothWitnessesAreAncestors");
                    db.AgentTaskLandRequests.Add(duplicate);
                    var duplicatePrefix = $"refs/antiphon/land/{h.Fixture.TaskId:N}/{duplicate.Id:N}/adopt";
                    await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", duplicatePrefix + "/local-before", duplicate.RecoveryLocalBeforeSha);
                    await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", duplicatePrefix + "/source", reviewed);
                    break;
            }
            await db.SaveChangesAsync();
        }
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        await h.RunQueuedAsync();
        await using var verify = h.CreateContext();
        var row = await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        row.SourceRefusalReason.ShouldBe(variant == "ambiguous" ? "adopt_recovery_ambiguous" : "source_dirty", $"H.{variant}.WitnessBindingRefused");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim(), "H.WitnessRefusalPreservesIndex");
        (await File.ReadAllBytesAsync(Path.Combine(h.Fixture.Source, "feature.txt"))).ShouldBe(bytes, "H.WitnessRefusalPreservesBytes");
        AssertNoResetOrPublication(h);
        if (variant == "ambiguous")
        {
            await using var repair = h.CreateContext();
            var equivalent = await repair.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId && r.Id != oldId && r.Id != next.RequestId);
            equivalent.RecoveryLocalBeforeSha = local;
            await repair.SaveChangesAsync();
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", $"refs/antiphon/land/{h.Fixture.TaskId:N}/{equivalent.Id:N}/adopt/local-before", local);
            await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
            await h.RunQueuedAsync();
            new AgentTaskLandingState().HasPublication((await h.OperationAsync()).ShouldNotBeNull()).ShouldBeTrue("H.EquivalentDuplicateWitnessAccepted");
        }
    }

    [Test]
    public async Task C883_FreshRequestRequiresAncestor()
    {
        await using var fixture = new LandHalfResetFixture();
        var (local, reviewed, evidence, oldId) = await InterruptedAsync(fixture);
        var h = fixture.Harness;
        var tree = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim();
        var unrelated = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree", tree, "-m", "unrelated equal tree")).Trim();
        (await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["merge-base", "--is-ancestor", unrelated, reviewed], CancellationToken.None)).ExitCode.ShouldBe(1);
        await using (var db = h.CreateContext())
        {
            var old = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == oldId);
            old.RecoveryLocalBeforeSha = unrelated;
            await db.SaveChangesAsync();
        }
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", $"refs/antiphon/land/{h.Fixture.TaskId:N}/{oldId:N}/adopt/local-before", unrelated);
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        await h.RunQueuedAsync();
        await using var verify = h.CreateContext();
        var row = await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty", "H.NonAncestorRefused");
        (await verify.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == row.TerminalEventId)).Detail.ShouldContain("recovery_checkout_unproven");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim().ShouldBe(tree);
        AssertNoResetOrPublication(h);
    }

    [Test]
    [Arguments("head")]
    [Arguments("branch")]
    [Arguments("registration")]
    public async Task C883_IdentityChangesBeforeResetRefuse(string variant)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Boundary.ThrowConflict = false;
        var marker = "";
        fixture.Boundary.BeforeRefMove = async () =>
        {
            if (variant == "head") await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", h.Fixture.SourceRef, h.Fixture.SeedSha, local);
            else if (variant == "branch")
            {
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "refs/heads/other", local);
                await h.Fixture.RequiredAsync(h.Fixture.Source, "symbolic-ref", "HEAD", "refs/heads/other");
            }
            else
            {
                marker = Path.Combine((await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--absolute-git-dir")).Trim(), "gitdir");
                await File.WriteAllTextAsync(marker, Path.Combine(h.Fixture.Observer, ".git") + "\n");
            }
        };
        await h.RunQueuedAsync();
        fixture.Boundary.BeforeRefReached.ShouldBe(1, "H.IdentityFinalBoundaryReached");
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        row.SourceRefusalReason.ShouldNotBeNull("H.ChangedIdentityRefused");
        if (variant == "head") (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(h.Fixture.SeedSha);
        if (variant == "branch") (await h.Fixture.RequiredAsync(h.Fixture.Source, "symbolic-ref", "HEAD")).Trim().ShouldBe("refs/heads/other");
        if (variant == "registration") (await File.ReadAllTextAsync(marker)).ShouldBe(Path.Combine(h.Fixture.Observer, ".git") + "\n");
        AssertNoResetOrPublication(h);
    }

    [Test]
    [Arguments("dirty")]
    [Arguments("wrong-head")]
    public async Task C883_PostResetMismatchCannotPublish(string variant)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Boundary.ThrowConflict = false;
        var hit = 0;
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (directory != h.Fixture.Source || args[0] != "reset" || !result.Succeeded) return;
            hit++;
            if (variant == "dirty") await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"), "post reset edit\n");
            else await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", h.Fixture.SourceRef, local, reviewed);
        };
        await h.RunQueuedAsync();
        hit.ShouldBe(1, "H.PostResetCutReached");
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        row.SourceRefusalReason.ShouldBe("adopt_local_changed", "H.PostResetMismatchRefused");
        row.RecoveryAdoptedAt.ShouldBeNull("H.MismatchNotAcknowledged");
        row.SourceAdvanceChildOperation.ShouldBe("source-adopt-reset", "H.MismatchRetainsIntent");
        if (variant == "dirty") (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"))).ShouldBe("post reset edit\n");
        else (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(local);
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Arguments[0] == "push" && x.Arguments.Any(a => a.Contains(":refs/heads/master", StringComparison.Ordinal)), "H.MismatchNoTargetPush");
    }

    [Test]
    public async Task C883_CurrentPendingRequestCannotBeStolen()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        await using var before = h.CreateContext();
        var original = await before.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() => h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true));
        await using var after = h.CreateContext();
        var preserved = await after.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
        preserved.ConcurrencyToken.ShouldBe(original.ConcurrencyToken, "H.PendingIdentityNotStolen");
        preserved.RecoveryLocalBeforeSha.ShouldBe(original.RecoveryLocalBeforeSha);
        preserved.SourceAdvanceChildOperation.ShouldBe(original.SourceAdvanceChildOperation);
        preserved.IsPending.ShouldBeTrue();
        (await after.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId)).CurrentLandRequestId.ShouldBe(first.RequestId);
        AssertNoResetOrPublication(h);
    }

    [Test]
    public async Task C883_CompletedResetIsIdempotent()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Boundary.ThrowConflict = false;
        var resets = 0;
        h.Fixture.Git.AfterCommand = (directory, args, result) =>
        {
            if (directory == h.Fixture.Source && args[0] == "reset" && result.Succeeded) { resets++; fixture.Interceptor.Armed = true; }
            return Task.CompletedTask;
        };
        var error = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        await h.FailAsync(error);
        resets.ShouldBe(1);
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        await h.RunQueuedAsync();
        resets.ShouldBe(1, "H.CompletedResetNotReplayed");
        new AgentTaskLandingState().HasPublication((await h.OperationAsync()).ShouldNotBeNull()).ShouldBeTrue();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId)).TerminalFailureCode.ShouldBe("landing_concurrency_conflict");
        next.RequestId.ShouldNotBe(first.RequestId);

        // Ordinary land publishes its branch and leaves dirty checkout residue without recovery probes.
        await using var ordinary = new LandingSafetyHarness();
        await ordinary.InitializeAsync();
        await ordinary.AddSourceAsync();
        await File.WriteAllTextAsync(Path.Combine(ordinary.Fixture.Source, "feature.txt"), "ordinary real edit\n");
        var probes = 0;
        ordinary.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "check-attr") probes++;
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        await ordinary.RequestAsync();
        await ordinary.RunQueuedAsync();
        probes.ShouldBe(0, "H.OrdinaryLandSkipsRecoveryProof");
        var op = (await ordinary.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("H.OrdinaryDirtyCheckoutPublishes");
        op.Cleanup.ShouldNotBe(LandCleanupStatus.Complete, "H.OrdinaryDirtyCheckoutRetained");
        await ordinary.RequestCleanupRetryAsync(op.Id);
        await ordinary.RunQueuedAsync();
        probes.ShouldBe(0, "H.CleanupOnlySkipsRecoveryProof");
        (await File.ReadAllTextAsync(Path.Combine(ordinary.Fixture.Source, "feature.txt"))).ShouldBe("ordinary real edit\n");
    }

    private static async Task AssertIgnoredContentChangeAsync()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence, _) = await InterruptedAsync(fixture, bulk: true);
        var sentinel = Path.Combine(h.Fixture.Source, "added-00.txt");
        var exclude = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, h.Fixture.Source), "\nadded-00.txt\n");
        var cuts = 0;
        var attributeProbes = 0;
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] == "check-attr") attributeProbes++;
            if (args[0] != "cat-file" || !result.Succeeded || cuts != 0) return;
            cuts++;
            await File.WriteAllTextAsync(sentinel, "late ignored obstruction\n");
        };
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        await h.RunQueuedAsync();
        cuts.ShouldBe(1, "H.IgnoredContentChangedAfterFirstListing");
        attributeProbes.ShouldBe(2, "H.FinalProofResampledIgnoredContent");
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId)).SourceRefusalReason.ShouldBe("source_dirty", "H.LateIgnoredObstructionRefused");
        (await File.ReadAllTextAsync(sentinel)).ShouldBe("late ignored obstruction\n");
        AssertNoResetOrPublication(h);
    }

    private static async Task AssertOrdinaryStagedEditAsync(bool sameRequest)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence, oldId) = await InterruptedAsync(fixture, sameRequest: sameRequest);
        var feature = Path.Combine(h.Fixture.Source, "feature.txt");
        await File.WriteAllTextAsync(feature, "ordinary staged edit\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "feature.txt");
        var blob = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim();
        var id = sameRequest ? oldId : (await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true)).RequestId;
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == id)).SourceRefusalReason.ShouldBe("source_dirty");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim().ShouldBe(blob);
        (await File.ReadAllTextAsync(feature)).ShouldBe("ordinary staged edit\n");
        AssertNoResetOrPublication(h);
    }

    [Test]
    [Arguments("live-request")]
    [Arguments("unknown-request")]
    [Arguments("live-journal")]
    [Arguments("unknown-journal")]
    public async Task C883_UncertainPriorChildRefuses(string variant)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence, oldId) = await InterruptedAsync(fixture);
        var journalCase = variant.EndsWith("journal", StringComparison.Ordinal);
        if (!journalCase) await using (var db = h.CreateContext())
        {
            var old = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == oldId);
            old.SourceAdvanceChildProcessId = 883939;
            old.SourceAdvanceChildStartTicks = 883939;
            await db.SaveChangesAsync();
        }
        var custody = new LandHalfResetFixture.CustodyGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId)
        { ChildAlive = variant.StartsWith("live", StringComparison.Ordinal) ? true : null };
        h.GitOverride = custody;
        h.ConfigureServices = sc => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>(sc, new Antiphon.Server.Infrastructure.Git.RepositoryMutationLease(custody));
        await h.RestartServicesAsync();
        Antiphon.Server.Infrastructure.Git.RepositoryChildJournal? journal = null;
        if (journalCase)
        {
            journal = await Antiphon.Server.Infrastructure.Git.RepositoryChildJournal.BeginAsync(h.Fixture.Repository, CancellationToken.None);
            await journal.StartedAsync(883939, 883939, CancellationToken.None); // Synthetic identity; the injected interface supplies liveness, no process starts.
        }
        try
        {
        var next = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        await h.RunQueuedAsync();
        custody.LivenessReads.ShouldBeGreaterThan(0, "H.PriorChildLivenessActuallyRead");
        await using var verify = h.CreateContext();
        var refused = await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId);
        if (journalCase)
        {
            refused.State.ShouldBe(LandRequestState.Held, "H.JournalFencesAdmission");
            refused.HoldReasonCode.ShouldBe("repository_mutation_lease_busy");
            var common = await custody.CommonDirectoryAsync(h.Fixture.Repository, CancellationToken.None);
            (await Antiphon.Server.Infrastructure.Git.RepositoryChildJournal.HasUnfinishedAsync(common, custody, CancellationToken.None)).ShouldBeTrue("H.JournalPreserved");
        }
        else
        {
            refused.SourceRefusalReason.ShouldBe("interrupted_process_requires_inspection", "H.UncertainPriorChildRefused");
            (await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == oldId)).SourceAdvanceChildProcessId.ShouldBe(883939);
        }
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim());
        custody.Commands.ShouldNotContain(x => (x.Arguments[0] == "reset" || x.Arguments[0] == "clean" || x.Arguments[0] == "push"), "H.UncertainChildNoMutationOrKill");
        (await h.OperationAsync()).ShouldBeNull();
        }
        finally { journal?.NotStarted(); } // Only synthetic fixture custody; no child was created.
    }

    private static void AssertNoResetOrPublication(LandingSafetyHarness h)
    {
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Directory == h.Fixture.Source && x.Arguments[0] == "reset", "H.NoResetOnRefusal");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Arguments[0] == "push" && x.Arguments.Any(a => a.Contains(":refs/heads/master", StringComparison.Ordinal)), "H.NoTargetPublicationOnRefusal");
    }

    private static async Task<(string Local, string Reviewed, Guid Evidence, Guid RequestId)> InterruptedAsync(
        LandHalfResetFixture fixture, bool bulk = false, bool sameRequest = false, bool equivalentOldTip = false, bool adoption = false)
    {
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync(bulk, equivalentOldTip: equivalentOldTip, adoption: adoption);
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: !adoption, adoptFromTaskId: fixture.AdoptionSourceId);
        fixture.Interceptor.RequestId = first.RequestId;
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Boundary.Reached.ShouldBe(1, "H.HistoricalCutReached");
        if (sameRequest) await h.SweepAsync();
        else await h.FailAsync(conflict);
        return (local, reviewed, evidence, first.RequestId);
    }
}
