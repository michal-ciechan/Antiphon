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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandCleanupSafetyTests
{
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
        (await h.OperationAsync()).ShouldBeNull();
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(1);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent))).ShouldBe(0);
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
