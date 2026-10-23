using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Category("Slow")]
public sealed class AgentTaskLandCleanupSafetyTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task C1536_PlainReplacementRecordsCleanupRefusal(bool populated, bool cleanupOnly)
    {
        await using var h = new LandingSafetyHarness();
        await PreparePublishedAsync(h);
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        await ReplaceSourceAsync(h, populated);
        new AgentTaskLandingState().HasPublication(receipt).ShouldBeTrue();
        File.GetAttributes(h.Fixture.Source).ShouldBe(FileAttributes.Directory);
        Directory.GetFileSystemEntries(h.Fixture.Source).Length.ShouldBe(populated ? 1 : 0);
        (await h.Fixture.Git.LiveRegistrationsAsync(h.Fixture.Repository, CancellationToken.None))
            .ShouldNotContain(r => GitIndexLock.PathsEqual(r.Path, h.Fixture.Source));
        if (cleanupOnly) await h.RequestCleanupRetryAsync(receipt.Id);
        else await h.RepostAsync();
        var requestId = await CurrentRequestAsync(h);
        var calls = h.Verifier.Calls;
        await using var before = h.CreateContext();
        var publications = await before.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent || e.Type == AgentTaskEventType.LandedWithResidue));
        var cleanups = await before.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandingCleanup);
        h.Fixture.Git.Trace.Clear();
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
        await AssertCompletedRefusalAsync(h, requestId, receipt, "unregistered_directory");
        h.Verifier.Calls.ShouldBe(calls);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("rebase") || a[0] == "push");
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        if (populated) (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "new.txt"))).ShouldBe("new work\n");
        else Directory.GetFileSystemEntries(h.Fixture.Source).ShouldBeEmpty();
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(receipt.OriginalSourceSha);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(receipt.ObservedRemoteTargetSha);
        await using var after = h.CreateContext();
        (await after.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandingCleanup)).ShouldBe(cleanups + 1);
        (await after.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent || e.Type == AgentTaskEventType.LandedWithResidue))).ShouldBe(publications);
        (await after.AgentTaskEvents.SingleAsync(e => e.LandRequestId == requestId && e.Type == AgentTaskEventType.LandingCleanup)).LandingOperationId.ShouldBe(receipt.Id);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("no-operation")]
    [Arguments("unconfirmed")]
    [Arguments("null-sha")]
    [Arguments("empty-sha")]
    [Arguments("crossed-path")]
    [Arguments("registered-broken")]
    [Arguments("empty-gitfile")]
    [Arguments("query-error")]
    [Arguments("source-alias")]
    public async Task C1536_UncertainSourceRetainsAdmissionHold(string uncertainty)
    {
        await using var h = new LandingSafetyHarness();
        await PreparePublishedAsync(h);
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        await h.RepostAsync(); // Queue while evidence is valid; admission must judge the altered input.
        var requestId = await CurrentRequestAsync(h);
        if (uncertainty == "registered-broken")
        {
            File.Delete(Path.Combine(h.Fixture.Source, ".git"));
            (await h.Fixture.Git.LiveRegistrationsAsync(h.Fixture.Repository, CancellationToken.None))
                .ShouldContain(r => GitIndexLock.PathsEqual(r.Path, h.Fixture.Source));
        }
        else await ReplaceSourceAsync(h, true);
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == receipt.Id);
            switch (uncertainty)
            {
                case "no-operation": task.ActiveLandingId = null; break;
                case "unconfirmed": op.RemoteConfirmedAt = null; break;
                case "null-sha": op.VerifiedSourceSha = null; break;
                case "empty-sha": op.VerifiedSourceSha = ""; break;
                case "crossed-path": op.WorktreePath = h.Fixture.Observer; break;
                case "empty-gitfile":
                    await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, ".git"), "");
                    new FileInfo(Path.Combine(h.Fixture.Source, ".git")).Length.ShouldBe(0);
                    break;
                case "source-alias":
                    var foreign = Path.Combine(h.Fixture.Root, "foreign");
                    Directory.Move(h.Fixture.Source, foreign);
                    Directory.CreateSymbolicLink(h.Fixture.Source, foreign);
                    File.GetAttributes(h.Fixture.Source).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
                    (await h.Fixture.Git.CanonicalDirectoryAsync(h.Fixture.Source, CancellationToken.None)).ShouldBe(foreign);
                    break;
            }
            await db.SaveChangesAsync();
        }
        await using (var input = h.CreateContext())
        {
            var op = await input.AgentTaskLandings.SingleAsync(o => o.Id == receipt.Id);
            if (uncertainty == "null-sha") op.VerifiedSourceSha.ShouldBeNull();
            if (uncertainty == "empty-sha") op.VerifiedSourceSha.ShouldBe("");
            if (uncertainty == "unconfirmed") op.RemoteConfirmedAt.ShouldBeNull();
            if (uncertainty == "crossed-path") op.WorktreePath.ShouldBe(h.Fixture.Observer);
            if (uncertainty == "no-operation") (await input.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).ActiveLandingId.ShouldBeNull();
        }
        var queryFailed = false;
        if (uncertainty == "query-error") h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] != "worktree" || !args.Contains("list")) return Task.FromResult<LandingGitResult?>(null);
            queryFailed = true;
            return Task.FromResult<LandingGitResult?>(new(128, "", "fixture enumeration unavailable"));
        };
        h.Fixture.Git.Trace.Clear();
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Held);
        if (uncertainty == "query-error") queryFailed.ShouldBeTrue();
        await using var after = h.CreateContext();
        var request = await after.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        request.State.ShouldBe(LandRequestState.Held);
        request.HoldReasonCode.ShouldBe(GitIndexLock.HeldCode);
        request.HoldDetail.ShouldContain(GitIndexLock.PathErrorReason);
        request.Attempt.ShouldBe(0);
        (await after.AgentTaskEvents.AnyAsync(e => e.LandRequestId == requestId && e.IsLandTerminal)).ShouldBeFalse();
        (await after.AgentTaskLandings.SingleAsync(o => o.Id == receipt.Id)).Mode.ShouldBe(receipt.Mode);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("rebase") || a[0] == "push");
        (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, uncertainty == "registered-broken" ? "feature.txt" : "new.txt")))
            .ShouldBe(uncertainty == "registered-broken" ? "valuable feature\n" : "new work\n");
        h.Fixture.Git.BeforeCommand = null;
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("remote-rewrite", "remote_no_longer_contains_source")]
    [Arguments("pin-missing", "recovery_pin_changed")]
    public async Task C1536_PlainReplacementRechecksCleanupAuthority(string change, string reason)
    {
        await using var h = new LandingSafetyHarness();
        await PreparePublishedAsync(h);
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        await ReplaceSourceAsync(h, true);
        await h.RepostAsync();
        var requestId = await CurrentRequestAsync(h);
        if (change == "remote-rewrite") await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.TargetRef, h.Fixture.SeedSha);
        else await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "-d", receipt.RecoveryRefPrefix + "/source");
        var calls = h.Verifier.Calls;
        h.Fixture.Git.Trace.Clear();
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
        await AssertCompletedRefusalAsync(h, requestId, receipt, reason);
        (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "new.txt"))).ShouldBe("new work\n");
        h.Verifier.Calls.ShouldBe(calls);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("rebase") || a[0] == "push");
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(receipt.OriginalSourceSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private static async Task PreparePublishedAsync(LandingSafetyHarness h)
    {
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var report = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await File.WriteAllTextAsync(report, "precious report\n");
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        File.Delete(report);
    }

    private static async Task ReplaceSourceAsync(LandingSafetyHarness h, bool populated)
    {
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", h.Fixture.Source);
        Directory.CreateDirectory(h.Fixture.Source);
        Should.Throw<FileNotFoundException>(() => File.GetAttributes(Path.Combine(h.Fixture.Source, ".git")));
        if (populated) await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "new.txt"), "new work\n");
    }

    private static async Task<Guid> CurrentRequestAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        return (await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).CurrentLandRequestId!.Value;
    }

    private static async Task AssertCompletedRefusalAsync(LandingSafetyHarness h, Guid requestId,
        Antiphon.Server.Domain.Entities.AgentTaskLanding receipt, string reason)
    {
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        request.State.ShouldBe(LandRequestState.Completed);
        request.IsPending.ShouldBeFalse();
        request.HoldReasonCode.ShouldBeNull();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Id.ShouldBe(receipt.Id);
        op.RemoteConfirmedAt.ShouldBe(receipt.RemoteConfirmedAt);
        op.ObservedRemoteTargetSha.ShouldBe(receipt.ObservedRemoteTargetSha);
        op.VerifiedSourceSha.ShouldBe(receipt.VerifiedSourceSha);
        op.Publication.ShouldBe(receipt.Publication);
        op.Mode.ShouldBe(LandOperationMode.CleanupRetry);
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe(reason);
        op.DirectoryRemoved.ShouldBeFalse();
        op.BranchRemoved.ShouldBeFalse();
    }

    [Test]
    [Arguments(1, "rewrite")]
    [Arguments(1, "delete")]
    [Arguments(1, "unavailable")]
    [Arguments(1, "containing-descendant")]
    [Arguments(2, "rewrite")]
    [Arguments(2, "delete")]
    [Arguments(2, "unavailable")]
    [Arguments(2, "containing-descendant")]
    [Arguments(3, "rewrite")]
    [Arguments(3, "delete")]
    [Arguments(3, "unavailable")]
    [Arguments(3, "containing-descendant")]
    public async Task C448_V27_EveryDeletionRefreshesRemoteProof(int observation, string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.CleanupStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        var seen = 0;
        var fired = false;
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] != "ls-remote" || ++seen != observation) return null;
            fired = true;
            switch (change)
            {
                case "rewrite": await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.TargetRef, h.Fixture.SeedSha); break;
                case "delete": await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", "-d", h.Fixture.TargetRef); break;
                case "unavailable": return new LandingGitResult(128, "", "fixture unavailable");
                default:
                    var descendant = (await h.Fixture.RequiredAsync(h.Fixture.Remote, "commit-tree", source + "^{tree}", "-p", source, "-m", "remote descendant")).Trim();
                    await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.TargetRef, descendant, source);
                    break;
            }
            return null;
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue("the named low-level cleanup observation must be reached");
        h.Fixture.Git.BeforeCommand = null;
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(receipt.Id);
        after.Publication.ShouldBe(receipt.Publication);
        after.RemoteConfirmedAt.ShouldBe(receipt.RemoteConfirmedAt);
        after.ObservedRemoteTargetSha.ShouldBe(receipt.ObservedRemoteTargetSha);
        after.Mode.ShouldBe(LandOperationMode.CleanupRetry);
        if (change == "containing-descendant")
        {
            after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        }
        else
        {
            after.Cleanup.ShouldBe(LandCleanupStatus.Refused);
            h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "update-ref" && a.Contains("-d") && a.Contains(h.Fixture.SourceRef));
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
            if (observation < 3)
            {
                h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
                (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"))).ShouldBe("valuable feature\n");
            }
            else Directory.Exists(h.Fixture.Source).ShouldBeFalse("only the completed first component may be absent");
        }
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase"));
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("advance")]
    [Arguments("detach")]
    [Arguments("switch")]
    [Arguments("tracked")]
    [Arguments("untracked")]
    [Arguments("ignored")]
    [Arguments("unregistered")]
    [Arguments("other-checkout")]
    [Arguments("remote-rewrite")]
    [Arguments("remote-delete")]
    [Arguments("remote-error")]
    [Arguments("pin-missing")]
    public async Task C448_V18_CleanupRetryPreservesChangedWork(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "precious report\n");
        await h.RunAsync();
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        receipt.Publication.ShouldBe(LandPublicationOutcome.Landed);
        receipt.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        // This file is fixture-owned; removal lets the next distinct guard become load-bearing.
        File.Delete(sentinel);
        var tracked = Path.Combine(h.Fixture.Source, "feature.txt");
        switch (change)
        {
            case "advance": await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "new work"); break;
            case "detach": await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "--detach"); break;
            case "switch": await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "-b", "new-work"); break;
            case "tracked": await File.WriteAllTextAsync(tracked, "new work\n"); break;
            case "untracked": tracked = Path.Combine(h.Fixture.Source, "new.txt"); await File.WriteAllTextAsync(tracked, "new work\n"); break;
            case "ignored": tracked = sentinel; await File.WriteAllTextAsync(tracked, "new work\n"); break;
            case "unregistered":
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", h.Fixture.Source);
                Directory.CreateDirectory(h.Fixture.Source);
                tracked = Path.Combine(h.Fixture.Source, "new.txt");
                await File.WriteAllTextAsync(tracked, "new work\n");
                break;
            case "other-checkout":
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", h.Fixture.Source);
                var other = Path.Combine(h.Fixture.Root, "other");
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", other, h.Fixture.SourceRef[11..]);
                tracked = Path.Combine(other, "feature.txt");
                break;
            case "remote-rewrite":
                await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.TargetRef, h.Fixture.SeedSha);
                break;
            case "remote-delete":
                await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", "--no-deref", "-d", h.Fixture.TargetRef, receipt.VerifiedSourceSha!);
                break;
            case "remote-error":
                h.Fixture.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(args[0] == "ls-remote" ? new(128, "", "fixture error") : null);
                break;
            case "pin-missing":
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "-d", receipt.RecoveryRefPrefix + "/source");
                break;
        }
        var bytes = await File.ReadAllBytesAsync(tracked);
        h.Fixture.Git.Trace.Clear();
        await h.RepostAsync();
        await h.RunAsync();
        if (change == "unregistered")
        {
            await using var observed = h.CreateContext();
            var id = (await observed.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).CurrentLandRequestId;
            var request = await observed.AgentTaskLandRequests.SingleAsync(r => r.Id == id);
            request.State.ShouldBe(LandRequestState.Completed);
            request.HoldReasonCode.ShouldBeNull();
            (await h.OperationAsync()).ShouldNotBeNull().LastReason.ShouldBe("unregistered_directory");
        }
        File.Exists(tracked).ShouldBeTrue("cleanup retry must retain changed work");
        (await File.ReadAllBytesAsync(tracked)).ShouldBe(bytes);
        var retry = (await h.OperationAsync()).ShouldNotBeNull();
        retry.Id.ShouldBe(receipt.Id);
        retry.RemoteConfirmedAt.ShouldBe(receipt.RemoteConfirmedAt);
        retry.ObservedRemoteTargetSha.ShouldBe(receipt.ObservedRemoteTargetSha);
        retry.Publication.ShouldBe(receipt.Publication);
        retry.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        retry.Mode.ShouldBe(LandOperationMode.CleanupRetry);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a[0] == "push" || a.Contains("rebase"));
        h.Fixture.Git.BeforeCommand = null;
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V19_AbsentComponentsRequireReceipt(bool deleteBranch)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", h.Fixture.Source);
        if (deleteBranch) await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "-d", h.Fixture.SourceRef);
        await h.RunAsync();
        await using var db = h.CreateContext();
        if (deleteBranch)
        {
            // Without the branch there is no source to land: refused before any operation or receipt.
            (await h.OperationAsync()).ShouldBeNull();
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(1);
            (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId)).SourceRefusalReason.ShouldBe("source_ref_missing");
        }
        else
        {
            // CARD-0688 D-2: the source is the branch ref, so a task whose worktree is already gone lands from it;
            // the publication receipt then authorizes cleanup of the absent components and the branch delete.
            var op = (await h.OperationAsync()).ShouldNotBeNull();
            op.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
            op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.AlreadyPresent)).ShouldBe(1);
            (await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["show-ref", "--exists", h.Fixture.SourceRef], CancellationToken.None)).ExitCode.ShouldBe(2);
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V36_UnknownPurposeCannotBorrowPublicationReceipt()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        File.Delete(sentinel); // Remove our blocker to make the purpose guard load-bearing.
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        var request = new WorktreeRemovalRequest((WorktreeRemovalPurpose)999,
            new(op.TaskId, op.RepositoryPath, op.WorktreePath, op.SourceFullRef, op.TargetFullRef),
            op.CommonDirectory, op.GitDirectory, op.VerifiedSourceSha!, op.TargetBeforeSha, op.Id, lease!);
        h.Fixture.Git.Trace.Clear();
        var removed = await h.Services.GetRequiredService<IWorktreeManager>().TryRemoveAsync(request, CancellationToken.None);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue("unknown purpose must not inherit publication deletion authority");
        removed.Residue.ShouldBe("removal_purpose_unsupported");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
