using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandBoundaryControlledTests
{
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
        await using var h = new LandingProtocolHarness();
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
        // CARD-0688 D-4: schema 3 has no target-advance phases before publication, so the two legacy boundaries
        // collapse onto Verified, the last acknowledged boundary before the push.
        var acknowledged = boundary is "TargetAdvanceStarted" or "LocalTargetAdvanced" ? "Verified" : boundary;
        h.Fault.AfterAcknowledged = async phase => { if (phase.ToString() == acknowledged) await MutateAsync(); };
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] == "fetch" && result.Succeeded && args.Any(a => a.StartsWith(h.Fixture.TargetRef, StringComparison.Ordinal)))
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
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(retainedSha);
        if (change is "dirty" or "staged" or "untracked")
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt"))).ShouldBe("new writer bytes\n");
        if (change == "staged") (await h.Fixture.RequiredAsync(h.Fixture.Source, "diff", "--cached")).ShouldContain("new writer bytes");
        if (change is "dirty" or "staged" or "untracked" or "switch")
        {
            // CARD-0688 D-2 / I-4 / I-5: the task worktree's tree and checked-out branch are not landing
            // preconditions any more (the land publishes from the branch ref); guarded cleanup keeps them as residue.
            op.RemoteConfirmedAt.ShouldNotBeNull();
            op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
            op.LastReason.ShouldBe(change == "switch" ? "source_branch_mismatch" : "source_dirty");
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
            await h.Fixture.AssertRemoteSourceAsync();
            return;
        }
        op.RemoteConfirmedAt.ShouldBeNull("changed source cannot receive an AlreadyPresent/publication receipt");
        // CARD-0688 D-4: the first target observation now happens while the operation is created, so a task-coordinate
        // change there is caught by the protocol's entry identity check rather than the per-checkpoint recheck.
        op.LastReason.ShouldBe(change == "advance" ? "source_changed"
            : boundary != "remote" ? "task_coordinates_changed"
            : change == "metadata-repository" ? "repository_lease_required" : "pending_operation_coordinates_changed");
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        if (boundary is "remote" or "BeforeRebaseIntent" or "RebaseStarted")
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"), "source fence must precede the rebase request");
        if (boundary == "remote") op.SourcePinned.ShouldBeFalse("changed source cannot be pinned after the creation observation");
        if (boundary == "BeforeRebaseIntent") op.RebaseStartedAt.ShouldBeNull("the source fence must precede rebase intent");
        if (boundary == "Prepared") h.Verifier.Calls.ShouldBe(0, "source changes must refuse before running verification");
        if (boundary is "Verified" or "TargetAdvanceStarted" or "LocalTargetAdvanced")
            op.Phase.ShouldBe(LandPhase.Verified, "source changes must refuse before push intent");
        if (boundary is "Verified" or "LocalTargetAdvanced")
            h.Fixture.Git.Trace.Skip(afterBoundary).ShouldNotContain(a => a[0] == "fetch" && a.Any(x => x.StartsWith(h.Fixture.TargetRef, StringComparison.Ordinal)), "source changes must refuse before a new publication observation");
        if (boundary == "BeforePushIntent") op.PushStartedAt.ShouldBeNull("source changes must refuse before push intent");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "update-ref" && a.Contains(h.Fixture.TargetRef),
            "source changes must refuse before advancing the target");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("source-ref")]
    [Arguments("source-path")]
    [Arguments("target")]
    [Arguments("repository")]
    public async Task C448_V10_VerificationCannotFreezeOldTaskCoordinates(string change)
    {
        await using var h = new LandingProtocolHarness();
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
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        (await h.OperationAsync())!.LastReason.ShouldBe("task_coordinates_changed");
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }
}

