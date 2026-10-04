using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CompletedCardWorktreeRemovalTests
{
    [Test]
    public async Task C1017_EvidenceNeedsDoneDisposition()
    {
        await using var f = await CompletedCardCleanupFixture.CreateAsync();
        await f.DiscoverAsync();
        await f.MoveAsync(f.ReviewColumnId);
        var before = await f.CleanupAsync();
        before.IsClean.ShouldBeFalse();
        var beforeDoneEvidenceBytes = await f.SentinelAsync();
        beforeDoneEvidenceBytes.ShouldBe(f.OriginalBytes);
        await f.MoveAsync(f.DoneColumnId);
        await f.DiscoverAsync();
        (await f.CleanupAsync()).IsClean.ShouldBeTrue();
        await f.AssertRemovedAsync();
    }

    [Test]
    public async Task C1017_DisposalFlagIsNotAuthority()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        await using (var db = h.Host.CreateContext())
            await db.CardWorktreeCleanupEndpoints.Where(e => e.Id == h.Card.EndpointId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.OperationId, (Guid?)null));
        await h.AssertHeldAsync(await h.RemoveAsync());
    }

    [Test]
    public async Task C1017_LeaseMustBeGenuine()
    {
        foreach (var shape in new[] { "null", "forged", "disposed", "foreign" })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            await using var good = await h.LeaseAsync();
            await using var foreign = await h.Host.Services.GetRequiredService<IRepositoryMutationLease>()
                .TryAcquireAsync(h.Git.Observer, CancellationToken.None);
            RepositoryLease lease = shape switch { "null" => null!, "forged" => new FakeLease(good.CommonDirectory), "foreign" => foreign!, _ => good };
            if (shape == "disposed") await good.DisposeAsync();
            var result = await h.Host.Services.GetRequiredService<GuardedWorktreeRemoval>()
                .RemoveAsync(h.Request(lease), CancellationToken.None);
            await h.AssertHeldAsync(result);
            result.Residue.ShouldBe("repository_lease_required");
        }
    }

    [Test]
    public async Task C1017_SourceShaMustMatch()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        await h.CommitAsync();
        await h.AssertHeldAsync(await h.RemoveAsync());
        (await File.ReadAllTextAsync(Path.Combine(h.Tree, "unique.txt"))).ShouldBe("unique committed source\n");
    }

    [Test]
    public async Task C1017_RepositoryMustMatch()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var file = Path.Combine(h.Git.Observer, "keep.txt");
        var originalBytes = await File.ReadAllBytesAsync(file);
        await h.AssertHeldAsync(await h.RemoveAsync(r => r with { Source = r.Source with { RepositoryPath = h.Git.Observer } }));
        var foreignRepositoryBytes = await File.ReadAllBytesAsync(file);
        foreignRepositoryBytes.ShouldBe(originalBytes);
    }

    [Test]
    public async Task C1017_PathMustMatch()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        await h.AssertHeldAsync(await h.RemoveAsync(r => r with { Source = r.Source with { WorktreePath = h.Git.Source } }));
        var otherTreeExists = Directory.Exists(h.Git.Source);
        otherTreeExists.ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(h.Git.Source, "keep.txt"))).ShouldBe("seed\n");
    }

    [Test]
    public async Task C1017_BranchMustMatch()
    {
        foreach (var shape in new[] { "symbolic", "detached" })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            await h.Git.RequiredAsync(h.Tree, "checkout", "-b", "other-branch");
            if (shape == "detached") await h.Git.RequiredAsync(h.Tree, "checkout", "--detach");
            await h.AssertHeldAsync(await h.RemoveAsync());
            var otherBranchStillExists = (await h.Git.Git.RunAsync(h.Git.Repository,
                ["show-ref", "--exists", "refs/heads/other-branch"], CancellationToken.None)).Succeeded;
            otherBranchStillExists.ShouldBeTrue();
        }
    }

    [Test]
    public async Task C1017_CommonDirectoryMustMatch()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var file = Path.Combine(h.Git.Observer, ".git", "HEAD");
        var originalBytes = await File.ReadAllBytesAsync(file);
        await h.AssertHeldAsync(await h.RemoveAsync(r => r with { CommonDirectory = Path.GetDirectoryName(file)! }));
        var foreignCommonBytes = await File.ReadAllBytesAsync(file);
        foreignCommonBytes.ShouldBe(originalBytes);
    }

    [Test]
    public async Task C1017_AdminDirectoryMustMatch()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var foreignAdmin = (await h.Git.RequiredAsync(h.Git.Source, "rev-parse", "--absolute-git-dir")).Trim();
        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(foreignAdmin, "HEAD"));
        await h.AssertHeldAsync(await h.RemoveAsync(r => r with { GitDirectory = foreignAdmin }));
        var foreignAdminExists = Directory.Exists(foreignAdmin);
        foreignAdminExists.ShouldBeTrue();
        (await File.ReadAllBytesAsync(Path.Combine(foreignAdmin, "HEAD"))).ShouldBe(originalBytes);
    }

    [Test]
    public async Task C1017_DirtyTrackedIsHeld()
    {
        foreach (var staged in new[] { false, true })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            await h.WriteAsync("keep.txt");
            if (staged) await h.Git.RequiredAsync(h.Tree, "add", "keep.txt");
            await h.AssertHeldAsync(await h.RemoveAsync());
            var fullInspections = h.FullInspections;
            fullInspections.ShouldBe(1);
            (await File.ReadAllTextAsync(Path.Combine(h.Tree, "keep.txt"))).ShouldBe("protected fixture bytes\n");
        }
    }

    [Test]
    public async Task C1017_UntrackedSourceIsHeld()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var path = await h.WriteAsync("untracked.cs");
        await h.AssertHeldAsync(await h.RemoveAsync());
        var fullInspections = h.FullInspections;
        fullInspections.ShouldBe(1);
        (await File.ReadAllTextAsync(path)).ShouldBe("protected fixture bytes\n");
    }

    [Test]
    public async Task C1017_UnpushedCommitIsHeld()
    {
        await using var f = await CompletedCardCleanupFixture.CreateAsync();
        var path = Path.Combine(f.Tree, "unique.txt");
        await File.WriteAllTextAsync(path, "unpublished bytes\n");
        await f.Host.Fixture.RequiredAsync(f.Tree, "add", "unique.txt");
        await f.Host.Fixture.RequiredAsync(f.Tree, "commit", "-m", "unique unpublished");
        var result = await f.CleanupAsync();
        result.IsClean.ShouldBeFalse();
        var treeExists = Directory.Exists(f.Tree);
        treeExists.ShouldBeTrue();
        (await File.ReadAllTextAsync(path)).ShouldBe("unpublished bytes\n");
        (await f.IntentsAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C1017_TaskBranchOnlyIsHeld()
    {
        await using var f = await CompletedCardCleanupFixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Tree, "unique.txt"), "pushed but unlanded\n");
        await f.Host.Fixture.RequiredAsync(f.Tree, "add", "unique.txt");
        await f.Host.Fixture.RequiredAsync(f.Tree, "commit", "-m", "task branch only");
        await f.Host.Fixture.RequiredAsync(f.Tree, "push", "origin", "HEAD:refs/heads/feat/card-task-" + f.TaskId.ToString("N")[..8]);
        var reason = (await f.CleanupAsync()).Residue;
        reason.ShouldBe("unlanded_work");
        (await File.ReadAllTextAsync(Path.Combine(f.Tree, "unique.txt"))).ShouldBe("pushed but unlanded\n");
        (await f.IntentsAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C1017_RebaseMappingMustBeBound()
    {
        foreach (var foreign in new[] { true, false })
        {
            await using var f = await CompletedCardCleanupFixture.CreateAsync();
            await File.WriteAllTextAsync(Path.Combine(f.Tree, "feature.txt"), "feature bytes\n");
            await f.Host.Fixture.RequiredAsync(f.Tree, "add", "feature.txt");
            await f.Host.Fixture.RequiredAsync(f.Tree, "commit", "-m", "feature");
            await f.Host.Fixture.RequiredAsync(f.Tree, "push", "origin", "HEAD:refs/heads/feat/card-task-" + f.TaskId.ToString("N")[..8]);
            await f.Host.Fixture.PushIndependentAsync("target-moved");
            await f.Host.RequestAsync(expectedSourceSha: (await f.Host.Fixture.RequiredAsync(f.Tree, "rev-parse", "HEAD")).Trim());
            await f.Host.RunAsync();
            var op = (await f.Host.OperationAsync()).ShouldNotBeNull();
            new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
            op.OriginalSourceSha.ShouldNotBe(op.VerifiedSourceSha);
            (await f.Host.Fixture.Git.RunAsync(f.Tree, ["merge-base", "--is-ancestor", op.OriginalSourceSha, op.VerifiedSourceSha!], CancellationToken.None)).ExitCode.ShouldBe(1);
            if (foreign)
            {
                await using var db = f.Host.CreateContext();
                await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.ActiveLandingId, (Guid?)null));
            }
            f.Host.Fixture.Git.Trace.Clear();
            var result = await f.CleanupAsync();
            if (foreign)
            {
                result.IsClean.ShouldBeFalse();
                var foreignMappingTreeExists = Directory.Exists(f.Tree);
                foreignMappingTreeExists.ShouldBeTrue();
                (await f.SentinelAsync()).ShouldBe(f.OriginalBytes);
            }
            else
            {
                if (!result.IsClean) await f.Host.RunQueuedAsync();
                Directory.Exists(f.Tree).ShouldBeFalse();
                var after = (await f.Host.OperationAsync()).ShouldNotBeNull();
                after.Id.ShouldBe(op.Id); after.Publication.ShouldBe(op.Publication);
                after.VerifiedSourceSha.ShouldBe(op.VerifiedSourceSha);
                f.Host.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase"));
            }
        }
    }

    [Test]
    public async Task C1017_RemoteProofMustBeFresh()
    {
        foreach (var unreadable in new[] { false, true })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            var reads = 0;
            h.Git.Git.BeforeCommand = async (repo, args) =>
            {
                if (args[0] != "ls-remote" || ++reads != 2) return null;
                if (unreadable) return new(128, "", "fixture unavailable");
                await h.Git.RequiredAsync(h.Git.Remote, "update-ref", "-d", "refs/heads/master");
                return null;
            };
            await h.AssertHeldAsync(await h.RemoveAsync());
            reads.ShouldBeGreaterThanOrEqualTo(2);
        }
    }

    [Test]
    public async Task C1017_ProtectedIgnoredIsHeld()
    {
        foreach (var path in new[] { ".claude/settings.json", "bin-test/appsettings.Development.json", "bin-test/data.user", "bin-test/.antiphon/unknown.txt", ".antiphon/unfamiliar-evidence/keep.txt" })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            var file = await h.WriteAsync(path);
            await h.AssertHeldAsync(await h.RemoveAsync());
            var fullInspections = h.FullInspections;
            fullInspections.ShouldBe(1);
            (await File.ReadAllTextAsync(file)).ShouldBe("protected fixture bytes\n");
        }
    }

    [Test]
    public async Task C1017_FinalContentIsRechecked()
    {
        foreach (var relative in new[] { "keep.txt", "late.cs", ".claude/late.json" })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            h.BeforeStatus(2, () => h.WriteAsync(relative));
            await h.AssertHeldAsync(await h.RemoveAsync());
            var lateBytes = await File.ReadAllTextAsync(Path.Combine(h.Tree, relative));
            lateBytes.ShouldBe("protected fixture bytes\n");
            h.FullInspections.ShouldBe(2);
        }
    }

    [Test]
    public async Task C1017_EvidenceChurnIsHeld()
    {
        foreach (var evidence in new[] { true, false })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            h.BeforeStatus(2, () => h.WriteAsync(evidence ? ".antiphon/checkpoints/late.trx" : "bin-test/late.dll"));
            var result = await h.RemoveAsync();
            if (evidence) { var reason = result.Residue; reason.ShouldBe("ignored_content_changed"); await h.AssertHeldAsync(result); }
            else { result.IsClean.ShouldBeTrue(result.Residue); await h.Card.AssertRemovedAsync(); }
        }
    }

    [Test]
    public async Task C1017_LinksNeverFollowed()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var outside = Path.Combine(h.Git.Root, "outside"); Directory.CreateDirectory(outside);
        var file = Path.Combine(outside, "sentinel.txt"); await File.WriteAllTextAsync(file, "outside bytes\n");
        await h.WriteAsync("bin-test/inside.txt");
        h.Git.Git.AfterUnregister = _ =>
        {
            var path = Path.Combine(WorktreeSetAside.SetAsidePath(h.Tree), "bin-test");
            WorktreeNoFollowDelete.Delete(path);
            Directory.CreateSymbolicLink(path, outside);
            return Task.CompletedTask;
        };
        var result = await h.RemoveAsync();
        result.IsClean.ShouldBeTrue(result.Residue);
        var outsideSentinel = await File.ReadAllTextAsync(file);
        outsideSentinel.ShouldBe("outside bytes\n");
    }

    [Test]
    public async Task C1017_RootConfinementIsFresh()
    {
        foreach (var shape in new[] { "outside", "sibling-prefix", "dot", "link" })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            var root = Path.Combine(h.Git.Root, shape == "sibling-prefix" ? "tree" : "outside");
            Directory.CreateDirectory(root);
            if (shape == "link") { root = Path.Combine(h.Git.Root, "root-link"); Directory.CreateSymbolicLink(root, Path.Combine(h.Git.Root, "trees")); }
            var result = await h.RemoveAsync(r => shape == "dot" ? r with { Source = r.Source with { WorktreePath = Path.Combine(h.Tree, "..", "..", "outside") } } : r with { ManagedRoot = root });
            await h.AssertHeldAsync(result);
            var outsideTreeExists = Directory.Exists(h.Tree);
            outsideTreeExists.ShouldBeTrue();
        }
    }

    [Test]
    public async Task C1017_NestedRegistrationIsHeld()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var nested = Path.Combine(h.Tree, "nested");
        await h.Git.RequiredAsync(h.Git.Repository, "worktree", "add", "--detach", nested, "HEAD");
        await h.AssertHeldAsync(await h.RemoveAsync());
        var nestedTreeExists = Directory.Exists(nested);
        nestedTreeExists.ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(nested, "keep.txt"))).ShouldBe("seed\n");
    }

    [Test]
    public async Task C1017_RegistrationMustExist()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        File.Delete(Path.Combine(h.Tree, ".git"));
        await h.Git.RequiredAsync(h.Git.Repository, "worktree", "prune");
        await h.AssertHeldAsync(await h.RemoveAsync());
        h.FullInspections.ShouldBe(0);
    }

    [Test]
    public async Task C1017_LockedRegistrationIsHeld()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        await h.Git.RequiredAsync(h.Git.Repository, "worktree", "lock", h.Tree);
        await h.AssertHeldAsync(await h.RemoveAsync());
        h.FullInspections.ShouldBe(0);
    }

    [Test]
    public async Task C1017_PrunableRegistrationIsHeld()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        // Git marks this registration prunable while its source bytes remain present.
        await File.WriteAllTextAsync(Path.Combine(h.Retirement.GitDirectory, "gitdir"), Path.Combine(h.Tree, "absent-backlink") + "\n");
        await h.AssertHeldAsync(await h.RemoveAsync());
        h.FullInspections.ShouldBe(0);
    }

    [Test]
    public async Task C1017_ReportMustBeExternal()
    {
        foreach (var rootKind in new[] { "original", "set-aside", "external-sibling" })
        {
            await using var f = await CompletedCardCleanupFixture.CreateAsync();
            var root = rootKind == "original" ? f.Tree : rootKind == "set-aside" ? WorktreeSetAside.SetAsidePath(f.Tree) : f.Tree + "-reports";
            Directory.CreateDirectory(Path.Combine(root, ".antiphon", "checkpoints"));
            var path = Path.Combine(root, ".antiphon", "checkpoints", "report.md");
            await File.WriteAllTextAsync(path, "complete canonical report\n");
            await f.ChangeTaskAsync(t => t.ResultFilePath = path);
            var result = await f.CleanupAsync();
            if (rootKind == "external-sibling") { result.IsClean.ShouldBeTrue(result.Residue); (await File.ReadAllTextAsync(path)).ShouldBe("complete canonical report\n"); }
            else { var reason = result.Residue; reason.ShouldBe("artifact_unpreserved"); (await f.SentinelAsync()).ShouldBe(f.OriginalBytes); }
        }
    }

    [Test]
    public async Task C1017_ReportDigestMustMatch()
    {
        foreach (var bad in new[] { "truncated", "different content of same length" })
        {
            await using var f = await CompletedCardCleanupFixture.CreateAsync();
            var path = await StoreReportAsync(f);
            await File.WriteAllTextAsync(path, bad);
            var reason = (await f.CleanupAsync()).Residue;
            reason.ShouldBe("artifact_unpreserved");
            (await File.ReadAllTextAsync(path)).ShouldBe(bad, "admission must not silently repair the evidence it validates");
            (await f.SentinelAsync()).ShouldBe(f.OriginalBytes);
            await File.WriteAllTextAsync(path, "complete canonical report\n");
            (await f.CleanupAsync()).IsClean.ShouldBeTrue();
        }
    }

    [Test]
    public async Task C1017_TaskReportDetailMustSurvive()
    {
        await using var f = await CompletedCardCleanupFixture.CreateAsync();
        var path = Path.Combine(f.Tree, ".antiphon", "task-" + f.TaskId.ToString("N")[..8] + ".md");
        const string originalDetailBytes = "complete canonical report\nadditional author detail\n";
        await File.WriteAllTextAsync(path, originalDetailBytes);
        (await f.CleanupAsync()).Residue.ShouldBe("artifact_unpreserved");
        var detailBytesAfterAttempt = await File.ReadAllTextAsync(path);
        detailBytesAfterAttempt.ShouldBe(originalDetailBytes);
        var preserved = Path.Combine(f.Host.Fixture.Root, "preserved-detail.md");
        await File.WriteAllTextAsync(preserved, originalDetailBytes);
        await f.ChangeTaskAsync(t => t.DeliverablePath = preserved);
        (await f.CleanupAsync()).IsClean.ShouldBeTrue();
        await f.AssertRemovedAsync();
        (await File.ReadAllTextAsync(preserved)).ShouldBe(originalDetailBytes);
    }

    [Test]
    public async Task C1017_DeliverablesMustBeExternal()
    {
        foreach (var kind in new[] { "local", "set-aside", "mirror", "external" })
        {
            await using var f = await CompletedCardCleanupFixture.CreateAsync();
            await f.DiscoverAsync();
            var root = kind == "local" ? f.Tree : kind == "set-aside" ? WorktreeSetAside.SetAsidePath(f.Tree) : Path.Combine(f.Host.Fixture.Root, kind);
            var path = Path.Combine(root, ".antiphon", "checkpoints", "deliverable.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "deliverable bytes\n");
            if (kind == "mirror")
            {
                await using var db = f.Host.CreateContext();
                db.CardWorktreeCleanupEndpoints.Add(new CardWorktreeCleanupEndpoint { Id = Guid.NewGuid(), TargetId = f.TargetId,
                    EndpointIdentity = "fixture-mirror", RunnerId = "fixture", RunnerStoreId = "fixture-store", WorktreePath = root,
                    RepositoryPath = f.Host.Fixture.Repository, SourceFullRef = "refs/heads/fixture", UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            await f.ChangeTaskAsync(t => t.DeliverablePath = path);
            var result = await f.CleanupAsync();
            if (kind == "external") { result.IsClean.ShouldBeTrue(result.Residue); await f.AssertRemovedAsync(); }
            else { var reason = result.Residue; reason.ShouldBe("artifact_unpreserved"); (await f.SentinelAsync()).ShouldBe(f.OriginalBytes); }
            (await File.ReadAllTextAsync(path)).ShouldBe("deliverable bytes\n");
        }
    }

    [Test]
    public async Task C1017_DeliverablesMustExist()
    {
        foreach (var kind in new[] { "missing", "locked", "link" })
        {
            await using var f = await CompletedCardCleanupFixture.CreateAsync();
            var path = Path.Combine(f.Host.Fixture.Root, "deliverable.md");
            if (kind == "link") File.CreateSymbolicLink(path, f.Sentinel);
            else if (kind == "locked") await File.WriteAllTextAsync(path, "external unreadable\n");
            using var held = kind == "locked" ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
            await f.ChangeTaskAsync(t => t.DeliverablePath = path);
            var reason = (await f.CleanupAsync()).Residue;
            reason.ShouldBe("artifact_unpreserved");
            (await f.SentinelAsync()).ShouldBe(f.OriginalBytes);
        }
    }

    [Test]
    public async Task C1017_FinalAuthorityIsFresh()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var reads = 0;
        h.Git.Git.AfterCommand = async (repo, args, _) =>
        {
            if (repo != h.Tree || args[0] != "ls-files" || ++reads != 2) return;
            await using var db = h.Host.CreateContext();
            await db.CardWorktreeCleanupEndpoints.Where(e => e.Id == h.Card.EndpointId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.State, CardWorktreeCleanupEndpointState.Revoked));
        };
        await h.AssertHeldAsync(await h.RemoveAsync());
        reads.ShouldBe(2);
    }

    [Test]
    public async Task C1017_BranchCasPreservesConcurrentRef()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var concurrentSha = "";
        h.Git.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] != "update-ref" || !args.Contains("-d")) return null;
            await h.Git.RequiredAsync(h.Git.Repository, "commit", "--allow-empty", "-m", "concurrent ref");
            concurrentSha = (await h.Git.RequiredAsync(h.Git.Repository, "rev-parse", "HEAD")).Trim();
            await h.Git.RequiredAsync(h.Git.Repository, "update-ref", h.Ref, concurrentSha);
            return null;
        };
        var result = await h.RemoveAsync();
        result.BranchDeleted.ShouldBeFalse(); concurrentSha.ShouldNotBeNullOrEmpty();
        var observedRef = (await h.Git.RequiredAsync(h.Git.Repository, "rev-parse", h.Ref)).Trim();
        observedRef.ShouldBe(concurrentSha);
    }

    [Test]
    public async Task C1017_OriginRefsSurvive()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        await h.Git.RequiredAsync(h.Tree, "push", "origin", h.Ref);
        var originRefsBefore = await h.Git.RequiredAsync(h.Git.Remote, "show-ref");
        (await h.RemoveAsync()).IsClean.ShouldBeTrue();
        await h.Card.AssertRemovedAsync();
        var originRefsAfter = await h.Git.RequiredAsync(h.Git.Remote, "show-ref");
        originRefsAfter.ShouldBe(originRefsBefore);
    }

    [Test]
    public async Task C1017_NoImplicitResetOrForce()
    {
        foreach (var dirty in new[] { false, true })
        {
            await using var h = await CompletedCardRemovalFixture.CreateAsync();
            if (dirty) await h.WriteAsync("keep.txt");
            h.Git.Git.Trace.Clear();
            var result = await h.RemoveAsync();
            if (dirty) await h.AssertHeldAsync(result); else result.IsClean.ShouldBeTrue();
            var sourceMutationCommands = h.Git.Git.Trace.Where(a => a[0] is "reset" or "stash" || a.Contains("--force") || a.Contains("-f")).ToArray();
            sourceMutationCommands.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task C1017_MissingReportNeedsDisposition()
    {
        await using var f = await CompletedCardCleanupFixture.CreateAsync();
        await f.ChangeTaskAsync(t => t.Result = null);
        var reason = (await f.CleanupAsync()).Residue;
        reason.ShouldBe("artifact_unpreserved");
        (await f.SentinelAsync()).ShouldBe(f.OriginalBytes);
        await using (var scope = f.Host.Services.CreateAsyncScope())
        {
            await using var db = f.Host.CreateContext();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId);
            var request = new ReleaseWorktreeRetirementRequest(task.ConcurrencyToken, f.Host.Fixture.SeedSha, null, true,
                "fixture reviewed missing report", [new(null, WorktreeHandoffDispositionKind.MissingReviewed, null, null, "reviewed")], true);
            await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()
                .ReleaseAsync(f.TaskId, request, new(null, null, ""), CancellationToken.None);
        }
        (await f.CleanupAsync()).IsClean.ShouldBeTrue();
        Directory.Exists(f.Tree).ShouldBeFalse();
    }

    [Test]
    public async Task C1017_FirstIgnoredLinkIsHeld()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var outside = Path.Combine(h.Git.Root, "outside.trx"); await File.WriteAllTextAsync(outside, "outside\n");
        File.CreateSymbolicLink(Path.Combine(h.Tree, ".antiphon", "checkpoints", "link.trx"), outside);
        await h.AssertHeldAsync(await h.RemoveAsync());
        var fullInspections = h.FullInspections;
        fullInspections.ShouldBe(1);
        (await File.ReadAllTextAsync(outside)).ShouldBe("outside\n");
    }

    [Test]
    public async Task C1017_FinalIgnoredLinkIsHeld()
    {
        await using var h = await CompletedCardRemovalFixture.CreateAsync();
        var outside = Path.Combine(h.Git.Root, "outside.trx"); await File.WriteAllTextAsync(outside, "outside\n");
        h.BeforeStatus(2, () => { File.CreateSymbolicLink(Path.Combine(h.Tree, ".antiphon", "checkpoints", "link.trx"), outside); return Task.CompletedTask; });
        var result = await h.RemoveAsync();
        var reason = result.Residue;
        reason.ShouldBe("ignored_reparse_point");
        await h.AssertHeldAsync(result);
        (await File.ReadAllTextAsync(outside)).ShouldBe("outside\n");
    }

    private static async Task<string> StoreReportAsync(CompletedCardCleanupFixture f)
    {
        await using var db = f.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
        var result = await f.Host.Services.GetRequiredService<IAgentReportStore>().StoreAsync(task, CancellationToken.None);
        result.Succeeded.ShouldBeTrue(); task.ResultFilePath = result.Path;
        await db.SaveChangesAsync(); return result.Path!;
    }

    private sealed class FakeLease(string common) : RepositoryLease
    {
        public override string CommonDirectory => common;
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
