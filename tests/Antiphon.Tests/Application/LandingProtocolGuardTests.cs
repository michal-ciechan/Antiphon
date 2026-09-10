using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
        h.Git.BeforeCommand = (repo, args) => Task.FromResult(
            args[0] == "symbolic-ref" && args[^1] == "HEAD"
            && string.Equals(Path.GetFullPath(repo), Path.GetFullPath(h.Git.Repository), StringComparison.OrdinalIgnoreCase)
                ? new Antiphon.Server.Application.Dtos.LandingGitResult(128, h.Git.TargetRef + "\n", "git_exit_128")
                : null);
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("target_checkout_changed");
        h.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push");
    }

    [Test]
    [Arguments("ambiguous")]
    [Arguments("locked")]
    [Arguments("prunable")]
    [Arguments("unrecorded")]
    public async Task C475_TargetRegistrationAuthority(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        if (change == "ambiguous") h.Git.SetAmbiguousTargetRegistrations();
        if (change == "locked") h.Git.SetTargetLocked();
        if (change == "prunable") h.Git.SetTargetPrunable();
        if (change == "unrecorded")
        {
            await using var db = h.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.LandVerifyFilter = "/*/*/Fixture/*";
            await db.SaveChangesAsync();
            h.Verifier.Barrier = async () =>
            {
                await using var inner = h.CreateContext();
                var op = await inner.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Git.TaskId && o.Active);
                op.TargetCheckoutRecorded = false;
                await inner.SaveChangesAsync();
            };
        }
        await h.RunAsync();
        var result = (await h.OperationAsync()).ShouldNotBeNull();
        result.RemoteConfirmedAt.ShouldBeNull();
        h.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push");
        result.LastReason.ShouldBe(change switch
        {
            "ambiguous" => "ambiguous_target_checkout",
            "locked" or "prunable" => "target_registration_unavailable",
            _ => "target_checkout_changed",
        });
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
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldBeNull();
        Directory.Exists(h.Git.Source).ShouldBeTrue();
    }

    [Test]
    public async Task C475_PushExitDoesNotConfirmPublication()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var fetches = 0;
        h.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] == "fetch" && result.Succeeded)
            {
                fetches++;
                if (fetches >= 2) h.Git.RewriteRemoteAwayFromSource();
            }
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldBeNull();
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
                case "active_operation": task.ActiveLandingId = Guid.NewGuid(); break;
                case "task_status": task.Status = AgentTaskStatus.Working; break;
                case "verification_filter": task.LandVerifyFilter = "/*/*/Other/*"; break;
            }
            await db.SaveChangesAsync();
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        (op.LastReason is "task_coordinates_changed" or "verification_filter_changed").ShouldBeTrue(op.LastReason);
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
        op.RemoteConfirmedAt.ShouldBeNull();
        h.Git.Trace.ShouldNotContain(a => a[0] == "push");
        if (change == "rejected") op.LastReason.ShouldBe("source_rejected");
        else op.LastReason.ShouldBe("source_changed");
    }
}
