using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class LandingProtocolGuardTests
{
    [Test]
    public Task C475_Task_repository() => TaskCoordinate("repository");
    [Test]
    public Task C475_Task_path() => TaskCoordinate("path");
    [Test]
    public Task C475_Task_source_ref() => TaskCoordinate("source_ref");
    [Test]
    public Task C475_Task_target_ref() => TaskCoordinate("target_ref");
    [Test]
    public Task C475_Task_active_operation() => TaskCoordinate("active_operation");
    [Test]
    public Task C475_Task_task_status() => TaskCoordinate("task_status");
    [Test]
    public Task C475_Task_verification_filter() => TaskCoordinate("verification_filter");

    [Test]
    public Task C475_Source_head() => SourceComponent("head");
    [Test]
    public Task C475_Source_common() => SourceComponent("common");
    [Test]
    public Task C475_Source_admin() => SourceComponent("admin");
    [Test]
    public Task C475_Source_registered_path() => SourceComponent("registered_path");
    [Test]
    public Task C475_Source_rejected() => SourceComponent("rejected");

    [Test]
    public async Task C475_TargetSymbolicQueryFailureRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        // CARD-0688 D-5: the target decision reads <common>/HEAD after publication, not symbolic-ref before it.
        // An unreadable main HEAD is never taken for "checked out nowhere": the canonical step records a
        // residue and nothing moves the target (before CARD-0688 this refused target_checkout_changed).
        h.Git.AfterCommand = (_, args, result) =>
        {
            if (args[0] == "push" && result.Succeeded) File.WriteAllText(Path.Combine(h.Git.CommonDir, "HEAD"), "\0garbage\n");
            return Task.CompletedTask;
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldNotBeNull();
        op.CanonicalAdvanceReason.ShouldBe("canonical_checkout_unknown");
        h.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "update-ref" && a.Contains(h.Git.TargetRef));
        h.Git.TargetHead.ShouldBe(h.Git.SeedSha);
    }

    [Test]
    [Arguments("ambiguous")]
    [Arguments("locked")]
    [Arguments("prunable")]
    [Arguments("unrecorded")]
    [Arguments("valid")]
    public async Task C475_TargetRegistrationAuthority(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var op = new Antiphon.Server.Domain.Entities.AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = h.Git.TaskId, Active = true,
            RepositoryPath = h.Git.Repository, WorktreePath = h.Git.Source,
            CommonDirectory = h.Git.CommonDir, GitDirectory = h.Git.GitDirectory,
            SourceFullRef = h.Git.SourceRef, TargetFullRef = h.Git.TargetRef,
            TargetBeforeSha = h.Git.SeedSha, OriginalSourceSha = h.Git.SeedSha,
            TargetCheckoutRecorded = change != "unrecorded",
            TargetCheckoutPath = change == "unrecorded" ? null : h.Git.Repository,
        };
        if (change == "ambiguous") h.Git.SetAmbiguousTargetRegistrations();
        if (change == "locked") h.Git.SetTargetLocked();
        if (change == "prunable") h.Git.SetTargetPrunable();
        await using var db = h.CreateContext();
        var protocol = new AgentTaskLandingProtocol(db, h.Git,
            h.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>(),
            h.Worktrees, h.Verifier, TimeProvider.System);
        // CARD-0688 D-5: the target decision now runs after publication, from HEAD files. A second checkout of
        // the target is a named residue (was ambiguous_target_checkout); registration lock/prune state and the
        // old "recorded" flag are no longer consulted (were target_registration_unavailable/target_checkout_changed).
        var method = typeof(AgentTaskLandingProtocol).GetMethod("CanonicalDecisionAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var decision = (Task)method.Invoke(protocol, [op, CancellationToken.None])!;
        await decision;
        var (reason, checkout) = ((string?, string?))decision.GetType().GetProperty("Result")!.GetValue(decision)!;
        if (change == "ambiguous")
        {
            reason.ShouldBe("target_checked_out_elsewhere");
            checkout.ShouldBe(Path.Combine(h.Git.Root, "canonical-alias"));
        }
        else
        {
            reason.ShouldBeNull();
            checkout.ShouldBe(h.Git.Repository);
        }
        h.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push");
    }

    [Test]
    public async Task C475_RemoteErrorDoesNotPublish()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Git.BeforeCommand = (_, args) => Task.FromResult(
            args[0] == "fetch" ? new Antiphon.Server.Application.Dtos.LandingGitResult(128, "", "remote_read_failed") : null);
        await h.RunAsync();
        // CARD-0688 D-4: the remote target is observed when the operation is created, so a fetch fault may
        // refuse before any operation exists; either way nothing is pushed or confirmed.
        (await h.OperationAsync())?.RemoteConfirmedAt.ShouldBeNull();
        h.Git.Trace.ShouldNotContain(a => a[0] == "push");
        Directory.Exists(h.Git.Source).ShouldBeTrue();
    }

    [Test]
    public async Task C475_PushExitDoesNotConfirmPublication()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var observedAfterPush = false;
        h.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] == "push" && result.Succeeded) h.Git.RewriteRemoteAwayFromSource();
            if (args[0] == "fetch" && h.Git.Trace.Any(a => a[0] == "push")) observedAfterPush = true;
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldBeNull();
        h.Git.Trace.Count(a => a[0] == "push").ShouldBe(1);
        observedAfterPush.ShouldBeTrue();
        Directory.Exists(h.Git.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("source")]
    [Arguments("target-before")]
    [Arguments("prepared")]
    public async Task C475_CleanupPinsAreFresh(string pin)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Git.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "preserve");
        await h.RunAsync();
        await h.RepostAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        h.Git.DeleteRef(op.RecoveryRefPrefix + "/" + pin);
        await h.RunAsync();
        (await h.OperationAsync())!.LastReason.ShouldBe("recovery_pin_changed");
        File.Exists(sentinel).ShouldBeTrue();
    }

    private static async Task TaskCoordinate(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.LandVerifyFilter = "/*/*/Fixture/*";
            await db.SaveChangesAsync();
        }
        h.Verifier.Barrier = async () =>
        {
            await using var db = h.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            switch (change)
            {
                case "repository": task.RepoPath = h.Git.Remote; break;
                case "path": task.WorktreePath = h.Git.Repository; break;
                case "source_ref": task.WorktreeBranch = "other"; break;
                case "target_ref": task.MergeTargetRef = "other"; break;
                case "active_operation": task.ActiveLandingId = null; break;
                case "task_status": task.Status = AgentTaskStatus.Working; break;
                case "verification_filter": task.LandVerifyFilter = "/*/*/Other/*"; break;
            }
            await db.SaveChangesAsync();
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        h.Verifier.Calls.ShouldBe(1);
        op.LastReason.ShouldBe(change == "verification_filter" ? "verification_filter_changed" : "task_coordinates_changed");
        h.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push");
    }

    private static async Task SourceComponent(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.LandVerifyFilter = "/*/*/Fixture/*";
            await db.SaveChangesAsync();
        }
        h.Verifier.Barrier = async () =>
        {
            if (change == "head")
                await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "moved");
            else if (change == "common")
                h.Git.OverrideCommonDirectory = Path.Combine(h.Git.Root, "other-common");
            else if (change == "admin")
                h.Git.OverrideGitDirectory = Path.Combine(h.Git.Root, "other-admin");
            else if (change == "registered_path")
                h.Git.OverrideRegisteredPath = h.Git.Repository;
            else if (change == "rejected")
                h.Git.RejectInspection = true;
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        h.Verifier.Calls.ShouldBe(1);
        if (change == "head")
        {
            // I-2: the branch is re-read by show-ref at every source checkpoint; a moved branch never publishes.
            op.RemoteConfirmedAt.ShouldBeNull();
            h.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("--ff-only") || a.Contains("remove"));
            op.LastReason.ShouldBe("source_changed");
            return;
        }
        // CARD-0688 D-2 / I-3: the task worktree's identity is a cleanup concern, not a landing precondition.
        // The land publishes from the branch ref; guarded cleanup refuses the changed worktree and keeps it.
        op.RemoteConfirmedAt.ShouldNotBeNull();
        if (change == "registered_path")
        {
            // Cleanup identifies the worktree by the operation's recorded path and its registration row;
            // a snapshot's registered-path field is not part of that identity (real git reports the same path).
            op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            return;
        }
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        h.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        Directory.Exists(h.Git.Source).ShouldBeTrue();
        if (change == "rejected") op.LastReason.ShouldBe("source_rejected");
        else op.LastReason.ShouldBe("source_changed");
    }
}
