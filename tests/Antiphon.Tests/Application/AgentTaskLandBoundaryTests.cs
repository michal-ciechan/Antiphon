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
        op.Phase.ShouldBe(LandPhase.TargetAdvanceStarted);
        op.LocalTargetAfterSha.ShouldBeNull("the post-FF fence must reject the new target before acknowledging local advance");
        op.RemoteConfirmedAt.ShouldBeNull();
        h.Fixture.Git.Trace.Skip(afterBoundary).ShouldNotContain(a => a[0] is "fetch" or "push" || a.Contains("remove"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(retainedHead);
        if (change is "dirty" or "staged")
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Repository, "keep.txt"))).ShouldBe("new target bytes\n");
        if (change == "staged") (await h.Fixture.RequiredAsync(h.Fixture.Repository, "diff", "--cached")).ShouldContain("new target bytes");
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
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
            fired.ShouldBeTrue();
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(rival);
            (await h.OperationAsync())!.RemoteConfirmedAt.ShouldBeNull();
            h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
            Directory.Exists(h.Fixture.Source).ShouldBeTrue();
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
        var provider = new Antiphon.Server.Infrastructure.Git.RepositoryMutationLease(fixture.Git);
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
        var created = await manager.CreateAsync(fixture.Repository, "other", "master", lease, CancellationToken.None);
        File.Exists(Path.Combine(created.Path, "keep.txt")).ShouldBeTrue("nested creation with the original lease must make progress");
        provider.Owns(lease, lease.CommonDirectory).ShouldBeTrue();
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("remote", "advance", false)]
    [Arguments("remote", "dirty", false)]
    [Arguments("remote", "staged", false)]
    [Arguments("remote", "untracked", false)]
    [Arguments("remote", "switch", false)]
    [Arguments("remote", "metadata", false)]
    [Arguments("remote", "metadata-path", false)]
    [Arguments("remote", "metadata-target", false)]
    [Arguments("remote", "metadata-repository", false)]
    [Arguments("remote", "advance", true)]
    [Arguments("remote", "dirty", true)]
    [Arguments("remote", "staged", true)]
    [Arguments("remote", "untracked", true)]
    [Arguments("remote", "switch", true)]
    [Arguments("remote", "metadata", true)]
    [Arguments("remote", "metadata-path", true)]
    [Arguments("remote", "metadata-target", true)]
    [Arguments("remote", "metadata-repository", true)]
    [Arguments("BeforeRebaseIntent", "advance", false)]
    [Arguments("BeforeRebaseIntent", "dirty", false)]
    [Arguments("BeforeRebaseIntent", "staged", false)]
    [Arguments("BeforeRebaseIntent", "untracked", false)]
    [Arguments("BeforeRebaseIntent", "switch", false)]
    [Arguments("BeforeRebaseIntent", "metadata", false)]
    [Arguments("BeforeRebaseIntent", "metadata-path", false)]
    [Arguments("BeforeRebaseIntent", "metadata-target", false)]
    [Arguments("BeforeRebaseIntent", "metadata-repository", false)]
    [Arguments("RebaseStarted", "advance", false)]
    [Arguments("RebaseStarted", "dirty", false)]
    [Arguments("RebaseStarted", "staged", false)]
    [Arguments("RebaseStarted", "untracked", false)]
    [Arguments("RebaseStarted", "switch", false)]
    [Arguments("RebaseStarted", "metadata", false)]
    [Arguments("RebaseStarted", "metadata-path", false)]
    [Arguments("RebaseStarted", "metadata-target", false)]
    [Arguments("RebaseStarted", "metadata-repository", false)]
    [Arguments("Prepared", "advance", false)]
    [Arguments("Prepared", "dirty", false)]
    [Arguments("Prepared", "staged", false)]
    [Arguments("Prepared", "untracked", false)]
    [Arguments("Prepared", "switch", false)]
    [Arguments("Prepared", "metadata", false)]
    [Arguments("Prepared", "metadata-path", false)]
    [Arguments("Prepared", "metadata-target", false)]
    [Arguments("Prepared", "metadata-repository", false)]
    [Arguments("Verified", "advance", false)]
    [Arguments("Verified", "dirty", false)]
    [Arguments("Verified", "staged", false)]
    [Arguments("Verified", "untracked", false)]
    [Arguments("Verified", "switch", false)]
    [Arguments("Verified", "metadata", false)]
    [Arguments("Verified", "metadata-path", false)]
    [Arguments("Verified", "metadata-target", false)]
    [Arguments("Verified", "metadata-repository", false)]
    [Arguments("TargetAdvanceStarted", "advance", false)]
    [Arguments("TargetAdvanceStarted", "dirty", false)]
    [Arguments("TargetAdvanceStarted", "staged", false)]
    [Arguments("TargetAdvanceStarted", "untracked", false)]
    [Arguments("TargetAdvanceStarted", "switch", false)]
    [Arguments("TargetAdvanceStarted", "metadata", false)]
    [Arguments("TargetAdvanceStarted", "metadata-path", false)]
    [Arguments("TargetAdvanceStarted", "metadata-target", false)]
    [Arguments("TargetAdvanceStarted", "metadata-repository", false)]
    [Arguments("LocalTargetAdvanced", "advance", false)]
    [Arguments("LocalTargetAdvanced", "dirty", false)]
    [Arguments("LocalTargetAdvanced", "staged", false)]
    [Arguments("LocalTargetAdvanced", "untracked", false)]
    [Arguments("LocalTargetAdvanced", "switch", false)]
    [Arguments("LocalTargetAdvanced", "metadata", false)]
    [Arguments("LocalTargetAdvanced", "metadata-path", false)]
    [Arguments("LocalTargetAdvanced", "metadata-target", false)]
    [Arguments("LocalTargetAdvanced", "metadata-repository", false)]
    [Arguments("BeforePushIntent", "advance", false)]
    [Arguments("BeforePushIntent", "dirty", false)]
    [Arguments("BeforePushIntent", "staged", false)]
    [Arguments("BeforePushIntent", "untracked", false)]
    [Arguments("BeforePushIntent", "switch", false)]
    [Arguments("BeforePushIntent", "metadata", false)]
    [Arguments("BeforePushIntent", "metadata-path", false)]
    [Arguments("BeforePushIntent", "metadata-target", false)]
    [Arguments("BeforePushIntent", "metadata-repository", false)]
    [Arguments("PushStarted", "advance", false)]
    [Arguments("PushStarted", "dirty", false)]
    [Arguments("PushStarted", "staged", false)]
    [Arguments("PushStarted", "untracked", false)]
    [Arguments("PushStarted", "switch", false)]
    [Arguments("PushStarted", "metadata", false)]
    [Arguments("PushStarted", "metadata-path", false)]
    [Arguments("PushStarted", "metadata-target", false)]
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
        h.Fault.AfterAcknowledged = async phase => { if (phase.ToString() == boundary) await MutateAsync(); };
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] == "fetch" && result.Succeeded)
            {
                observedFetches++;
                if (boundary == "remote" || boundary == "BeforePushIntent" && observedFetches == 2) await MutateAsync();
            }
            if (boundary == "BeforeRebaseIntent" && args[0] == "merge-base" && args.Count == 4
                && args[2] == h.Fixture.SeedSha && args[3] == h.Fixture.SeedSha && result.Succeeded)
                await MutateAsync();
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue("the named boundary must be reached before claiming refusal coverage");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldBeNull("changed source cannot receive an AlreadyPresent/publication receipt");
        op.LastReason.ShouldBe(change switch { "advance" => "source_changed", "dirty" or "staged" or "untracked" => "source_dirty", "switch" => "source_branch_mismatch", _ => "task_coordinates_changed" });
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(retainedSha);
        if (change is "dirty" or "staged" or "untracked")
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt"))).ShouldBe("new writer bytes\n");
        if (change == "staged") (await h.Fixture.RequiredAsync(h.Fixture.Source, "diff", "--cached")).ShouldContain("new writer bytes");
        if (boundary is "remote" or "BeforeRebaseIntent" or "RebaseStarted")
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"), "source fence must precede the rebase request");
        if (boundary == "remote") op.RemoteBeforeSha.ShouldBeNull("changed source cannot acknowledge the initial remote observation");
        if (boundary == "BeforeRebaseIntent") op.RebaseStartedAt.ShouldBeNull("the source fence must precede rebase intent");
        if (boundary == "Prepared") h.Verifier.Calls.ShouldBe(0, "source changes must refuse before running verification");
        if (boundary == "Verified") op.Phase.ShouldBe(LandPhase.Verified, "source changes must refuse before target-advance intent");
        if (boundary == "LocalTargetAdvanced")
            h.Fixture.Git.Trace.Skip(afterBoundary).ShouldNotContain(a => a[0] == "fetch", "source changes must refuse before a new publication observation");
        if (boundary == "BeforePushIntent") op.PushStartedAt.ShouldBeNull("source changes must refuse before push intent");
        if (boundary == "TargetAdvanceStarted")
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
        (await h.OperationAsync())!.RemoteConfirmedAt.ShouldBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("merge") || a[0] == "push" || a.Contains("remove"));
        Path.Exists(marker).ShouldBeTrue();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("source-ref")]
    [Arguments("source-path")]
    [Arguments("target")]
    [Arguments("repository")]
    public async Task C448_V10_VerificationCannotFreezeOldTaskCoordinates(string change)
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
            await using var db = h.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            switch (change)
            {
                case "source-ref": task.WorktreeBranch = "other"; break;
                case "source-path": task.WorktreePath = h.Fixture.Repository; break;
                case "target": task.MergeTargetRef = "other"; break;
                case "repository": task.RepoPath = h.Fixture.Remote; break;
            }
            await db.SaveChangesAsync();
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        h.Verifier.Calls.ShouldBe(1);
        (await h.OperationAsync())!.LastReason.ShouldBe("task_coordinates_changed");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
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
