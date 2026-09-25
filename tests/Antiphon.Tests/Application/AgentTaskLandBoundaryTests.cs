using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public sealed class AgentTaskLandBoundaryTests
{
    [Test]
    [Arguments("advance")]
    [Arguments("switch")]
    [Arguments("dirty")]
    [Arguments("staged")]
    public async Task C448_V11_TargetMutationAfterFastForwardCannotBeAcknowledged(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var fired = false;
        string? retainedHead = null;
        var afterBoundary = 0;
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (fired || !result.Succeeded || !args.Contains("--ff-only")) return;
            fired = true;
            if (change == "advance")
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new target writer");
            else if (change == "switch")
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "checkout", "-b", "new-target-owner");
            else
            {
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Repository, "keep.txt"), "new target bytes\n");
                if (change == "staged") await h.Fixture.RequiredAsync(h.Fixture.Repository, "add", "keep.txt");
            }
            retainedHead = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim();
            afterBoundary = h.Fixture.Git.Trace.Count;
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue("the mutation must follow the real checked-out target fast-forward");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        // CARD-0688 D-4: the checked-out target is fast-forwarded after publication, so the post-FF fence decides
        // only whether the canonical advance is acknowledged. A target that moved past the landed commit is not
        // (canonical_advance_failed); a switch or edits after a completed fast-forward are the operator's own.
        op.RemoteConfirmedAt.ShouldNotBeNull();
        if (change == "advance")
        {
            op.CanonicalAdvanceReason.ShouldBe("canonical_advance_failed");
            op.LocalTargetAfterSha.ShouldBeNull("the post-FF fence must reject the new target before acknowledging local advance");
        }
        else
        {
            op.CanonicalAdvanceReason.ShouldBeNull();
            op.LocalTargetAfterSha.ShouldBe(op.VerifiedSourceSha);
        }
        h.Fixture.Git.Trace.Skip(afterBoundary).ShouldNotContain(a => a[0] == "push" || a.Contains("--ff-only"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(retainedHead);
        if (change is "dirty" or "staged")
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Repository, "keep.txt"))).ShouldBe("new target bytes\n");
        if (change == "staged") (await h.Fixture.RequiredAsync(h.Fixture.Repository, "diff", "--cached")).ShouldContain("new target bytes");
        Directory.Exists(h.Fixture.Source).ShouldBeFalse("publication came first, so guarded cleanup completed");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("source", false)]
    [Arguments("source", true)]
    [Arguments("target-before", false)]
    [Arguments("target-before", true)]
    [Arguments("prepared", false)]
    [Arguments("prepared", true)]
    public async Task C448_V32_EachRecoveryPinFailureStopsDependentMutation(string name, bool collision)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var wrongSha = name == "target-before" ? source : h.Fixture.SeedSha;
        var fired = false;
        string? pin = null;
        h.Fixture.Git.BeforeCommand = async (repo, args) =>
        {
            if (fired || args[0] != "update-ref" || args.Count != 4 || !args[1].EndsWith("/" + name, StringComparison.Ordinal)) return null;
            fired = true;
            pin = args[1];
            if (!collision) return new Antiphon.Server.Application.Dtos.LandingGitResult(128, "", "fixture pin failed");
            await h.Fixture.RequiredAsync(repo, "update-ref", pin, wrongSha); // Race after the absence read; expected-old-zero must fail.
            return null;
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("recovery_pin_failed");
        op.RemoteConfirmedAt.ShouldBeNull();
        h.Verifier.Calls.ShouldBe(0);
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove") || a.Contains("--ff-only"));
        if (name != "prepared") h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
        h.Fixture.Git.BeforeCommand = null;
        if (collision) (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", pin!)).Trim().ShouldBe(wrongSha);
        if (name != "source") (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", op.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V11_TargetAdvanceUsesTheCapturedCheckoutAndOldSha(bool checkedOut)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        if (!checkedOut) await h.Fixture.RequiredAsync(h.Fixture.Repository, "checkout", "-b", "other-main-branch");
        string? rival = null;
        var fired = false;
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (!checkedOut && !fired && args[0] == "update-ref" && args.Contains(h.Fixture.TargetRef))
            {
                fired = true;
                rival = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree", h.Fixture.SeedSha + "^{tree}", "-p", h.Fixture.SeedSha, "-m", "rival target")).Trim();
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", h.Fixture.TargetRef, rival, h.Fixture.SeedSha);
            }
            return null;
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        h.Fixture.Git.BeforeCommand = null;
        if (checkedOut)
        {
            (await h.OperationAsync())!.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(source);
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "status", "--porcelain")).ShouldBe("");
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Repository, "feature.txt"))).ShouldBe("valuable feature\n");
        }
        else
        {
            // CARD-0688 D-4: update-ref after publication still CASes the old local SHA, so a rival written in
            // between is never overwritten; the land stays published with a canonical residue.
            fired.ShouldBeTrue();
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(rival);
            var op = (await h.OperationAsync())!;
            op.RemoteConfirmedAt.ShouldNotBeNull();
            op.CanonicalAdvanceReason.ShouldBe("canonical_advance_failed");
            h.Fixture.Git.Trace.Count(a => a[0] == "update-ref" && a.Contains(h.Fixture.TargetRef)).ShouldBe(1);
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "branch", "other-owner", source);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "different base");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "config", "rebase.autoStash", "true");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "config", "rebase.updateRefs", "true");
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        var rebase = h.Fixture.Git.Trace.Single(a => a.Contains("rebase") && !a.Contains("--abort"));
        rebase.ShouldContain("rebase.autoStash=false");
        rebase.ShouldContain("rebase.updateRefs=false");
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "refs/heads/other-owner")).Trim().ShouldBe(source);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "stash", "list")).ShouldBe("");
        var op = (await h.OperationAsync())!;
        op.VerifiedSourceSha.ShouldNotBe(source);
        op.VerificationPassed.ShouldBeTrue();
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V36_CreationUsesTheRepositoryLeaseAndThreadsNestedOwnership()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var provider = new CountingCreationLease(new Antiphon.Server.Infrastructure.Git.RepositoryMutationLease(fixture.Git));
        var manager = new Antiphon.Server.Infrastructure.Git.WorktreeManager(
            Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.GitSettings { WorktreeBasePath = Path.Combine(fixture.Root, "trees") }),
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Antiphon.Server.Infrastructure.Git.WorktreeManager>.Instance,
            leases: provider, landingGit: fixture.Git);
        await using var lease = await provider.TryAcquireAsync(fixture.Source, CancellationToken.None);
        lease.ShouldNotBeNull();
        var error = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            () => manager.CreateAsync(fixture.Repository, "other", "master", CancellationToken.None));
        error.Message.ShouldBe("repository_busy_or_child_recovery_required");
        Directory.Exists(Path.Combine(fixture.Root, "trees", "card-other")).ShouldBeFalse();
        var beforeNested = provider.Acquisitions;
        Antiphon.Server.Application.Dtos.WorktreeInfo? created = null;
        Exception? nestedFailure = null;
        try { created = await manager.CreateAsync(fixture.Repository, "other", "master", lease, CancellationToken.None); }
        catch (Exception ex) { nestedFailure = ex; }
        provider.Acquisitions.ShouldBe(beforeNested, "nested creation must thread the existing lease without a second acquisition request");
        nestedFailure.ShouldBeNull();
        File.Exists(Path.Combine(created.ShouldNotBeNull().Path, "keep.txt")).ShouldBeTrue("nested creation with the original lease must make progress");
        provider.Owns(lease, lease.CommonDirectory).ShouldBeTrue();
        await fixture.AssertRemoteSourceAsync();
    }

    private sealed class CountingCreationLease(Antiphon.Server.Application.Interfaces.IRepositoryMutationLease inner)
        : Antiphon.Server.Application.Interfaces.IRepositoryMutationLease
    {
        public int Acquisitions { get; private set; }
        public Task<Antiphon.Server.Application.Interfaces.RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        { Acquisitions++; return inner.TryAcquireAsync(repository, ct); }
        public bool Owns(Antiphon.Server.Application.Interfaces.RepositoryLease lease, string commonDirectory)
            => inner.Owns(lease, commonDirectory);
    }

    [Test]
    [Arguments("remote", "advance", false)]
    [Arguments("remote", "advance", true)]
    [Arguments("remote", "switch", true)]
    [Arguments("BeforeRebaseIntent", "dirty", false)]
    [Arguments("RebaseStarted", "staged", false)]
    [Arguments("Prepared", "untracked", false)]
    [Arguments("Prepared", "metadata", false)]
    [Arguments("Verified", "switch", false)]
    [Arguments("Verified", "metadata-path", false)]
    [Arguments("TargetAdvanceStarted", "advance", false)]
    [Arguments("LocalTargetAdvanced", "dirty", false)]
    [Arguments("BeforePushIntent", "staged", false)]
    [Arguments("BeforePushIntent", "metadata-target", false)]
    [Arguments("PushStarted", "untracked", false)]
    [Arguments("PushStarted", "metadata-repository", false)]
    public async Task C448_V10_EachAcknowledgedBoundaryRechecksSource(string boundary, string change, bool contained)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (!contained) await h.AddSourceAsync();
        if (boundary == "Prepared")
        {
            await using var db = h.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandVerifyFilter = "/*/*/Required/*";
            await db.SaveChangesAsync();
        }
        var observedFetches = 0;
        var afterBoundary = 0;
        var fired = false;
        string? retainedSha = null;
        async Task MutateAsync()
        {
            if (fired) return;
            fired = true;
            if (change == "advance")
                await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "new writer");
            else if (change == "switch")
                await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "-b", "another-source-writer");
            else if (change is "dirty" or "staged" or "untracked")
            {
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt"), "new writer bytes\n");
                if (change == "staged") await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "keep.txt");
            }
            else
            {
                await using var db = h.CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                if (change == "metadata-path") task.WorktreePath = Path.Combine(h.Fixture.Root, "different-recorded-source");
                else if (change == "metadata-target") task.MergeTargetRef = "different-recorded-target";
                else if (change == "metadata-repository") task.RepoPath = h.Fixture.Remote;
                else task.WorktreeBranch = "different-recorded-source";
                await db.SaveChangesAsync();
            }
            retainedSha = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
            afterBoundary = h.Fixture.Git.Trace.Count;
        }
        // CARD-0688 D-4: schema 3 has no target-advance phases before publication; the legacy boundaries collapse
        // onto Verified, the last acknowledged boundary before the push.
        var acknowledged = boundary is "TargetAdvanceStarted" or "LocalTargetAdvanced" ? "Verified" : boundary;
        h.Fault.AfterAcknowledged = async phase => { if (phase.ToString() == acknowledged) await MutateAsync(); };
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] == "fetch" && result.Succeeded)
            {
                observedFetches++;
                if (boundary == "remote" || boundary == "BeforePushIntent" && observedFetches == 2) await MutateAsync();
            }
            // Before rebase intent: the containment check against the observed target, after the recovery pins.
            if (boundary == "BeforeRebaseIntent" && args is ["merge-base", "--is-ancestor", _, var target]
                && target == h.Fixture.SeedSha
                && h.Fixture.Git.Trace.Any(a => a[0] == "update-ref" && a[1].EndsWith("/target-before", StringComparison.Ordinal)))
                await MutateAsync();
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue("the named boundary must be reached before claiming refusal coverage");
        var op = await h.OperationAsync();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(retainedSha);
        if (change is "dirty" or "staged" or "untracked")
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt"))).ShouldBe("new writer bytes\n");
        if (change == "staged") (await h.Fixture.RequiredAsync(h.Fixture.Source, "diff", "--cached")).ShouldContain("new writer bytes");
        if (change is "dirty" or "staged" or "untracked" or "switch")
        {
            // CARD-0688 D-2 / I-4 / I-5: the task worktree's tree and checked-out branch are not landing
            // preconditions any more (the land publishes from the branch ref); guarded cleanup keeps them as residue.
            op.ShouldNotBeNull().RemoteConfirmedAt.ShouldNotBeNull();
            op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
            op.LastReason.ShouldBe(change == "switch" ? "source_branch_mismatch" : "source_dirty");
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
            await h.Fixture.AssertRemoteSourceAsync();
            return;
        }
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        if (boundary == "remote")
        {
            // CARD-0688 D-2: the first fetch is now the resolver's source observation; the branch show-ref when the
            // operation is created catches the new commit before any operation, pin or rebase exists.
            op.ShouldBeNull();
            await using var db = h.CreateContext();
            (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId)).SourceRefusalReason.ShouldBe("source_changed");
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
            await h.Fixture.AssertRemoteSourceAsync();
            return;
        }
        op.ShouldNotBeNull().RemoteConfirmedAt.ShouldBeNull("changed source cannot receive an AlreadyPresent/publication receipt");
        // The second fetch is the target observation made while the operation is created, so a coordinate change
        // there is caught by the protocol's entry identity check rather than the per-checkpoint recheck.
        op.LastReason.ShouldBe(change == "advance" ? "source_changed"
            : boundary == "BeforePushIntent" ? "pending_operation_coordinates_changed" : "task_coordinates_changed");
        if (boundary is "BeforeRebaseIntent" or "RebaseStarted")
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"), "source fence must precede the rebase request");
        if (boundary == "BeforeRebaseIntent") op.RebaseStartedAt.ShouldBeNull("the source fence must precede rebase intent");
        if (boundary == "Prepared") h.Verifier.Calls.ShouldBe(0, "source changes must refuse before running verification");
        if (boundary is "Verified" or "TargetAdvanceStarted" or "LocalTargetAdvanced")
            op.Phase.ShouldBe(LandPhase.Verified, "source changes must refuse before push intent");
        if (boundary is "Verified" or "LocalTargetAdvanced")
            h.Fixture.Git.Trace.Skip(afterBoundary).ShouldNotContain(a => a[0] == "fetch", "source changes must refuse before a new publication observation");
        if (boundary == "BeforePushIntent") op.PushStartedAt.ShouldBeNull("source changes must refuse before push intent");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "update-ref" && a.Contains(h.Fixture.TargetRef),
            "source changes must refuse before advancing the target");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("MERGE_HEAD")]
    [Arguments("CHERRY_PICK_HEAD")]
    [Arguments("REVERT_HEAD")]
    [Arguments("rebase-merge")]
    [Arguments("rebase-apply")]
    [Arguments("sequencer")]
    public async Task C448_V11_TargetSequencerBlocksPreparation(string state)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var marker = Path.Combine(h.Fixture.Repository, ".git", state);
        if (state.Contains("HEAD", StringComparison.Ordinal)) await File.WriteAllTextAsync(marker, h.Fixture.SeedSha + "\n");
        else Directory.CreateDirectory(marker);
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        // CARD-0688 D-4 / I-11: a sequencer in the main checkout no longer blocks preparation (the rebase runs in the
        // land worktree); after publication it is a canonical residue and nothing touches that checkout.
        var op = (await h.OperationAsync())!;
        op.RemoteConfirmedAt.ShouldNotBeNull();
        op.CanonicalAdvanceReason.ShouldBe("canonical_active_sequencer");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("merge") && a.Contains("--ff-only")
            || a[0] == "update-ref" && a.Contains(h.Fixture.TargetRef));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        Path.Exists(marker).ShouldBeTrue();
        Directory.Exists(h.Fixture.Source).ShouldBeFalse("publication came first, so guarded cleanup completed");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("source")]
    [Arguments("target-before")]
    [Arguments("prepared")]
    public async Task C448_V26_RecoveryPinsRemainPrerequisitesAfterVerification(string pin)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandVerifyFilter = "/*/*/Fixture/*";
            await db.SaveChangesAsync();
        }
        h.Verifier.Barrier = async () =>
        {
            var op = (await h.OperationAsync())!;
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "-d", op.RecoveryRefPrefix + "/" + pin);
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        (await h.OperationAsync())!.LastReason.ShouldBe("recovery_pin_changed");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
