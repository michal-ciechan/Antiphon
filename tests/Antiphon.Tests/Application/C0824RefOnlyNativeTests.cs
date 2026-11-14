using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(120_000)] // CARD-0824 provisional native baseline; Windows timings remain required.
public sealed class C0824RefOnlyNativeTests
{
    [Test]
    public async Task Windows_dispatch_settle_late_land_has_zero_desktop_checkout()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("CARD-0824 requires actual Windows path and Git registration semantics.");
        await new C0824RefOnlyProducerLifecycleTests().Dispatch_settle_and_late_land_preserve_other_task();
    }

    [Test]
    public async Task Windows_restart_and_gate_rollback_keep_ref_only_custody()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("CARD-0824 requires actual Windows restart and identity semantics.");
        await new C0824RefOnlyProducerLifecycleTests().Blocked_reply_and_restart_keep_mode_when_gate_closes();
    }

    [Test]
    public async Task Windows_two_tasks_and_case_normalization_never_alias()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("CARD-0824 requires Windows case-insensitive canonical identities.");
        await using var f = await RefOnlyDispatchFixture.CreateAsync(capacity: 2);
        var first = await f.LaunchAsync(await f.SeedAsync());
        var second = await f.LaunchAsync(await f.SeedAsync());
        Path.IsPathFullyQualified(first.WorktreePath!).ShouldBeTrue();
        Path.GetPathRoot(first.WorktreePath!).ShouldContain(":");
        WorkspaceReservationKey.PathsEqual(first.WorktreePath!, first.WorktreePath!.ToUpperInvariant()).ShouldBeTrue();
        WorkspaceReservationKey.PathsEqual(first.WorktreePath!, second.WorktreePath!).ShouldBeFalse();
        first.TaskRefWorkspaceBindingId.ShouldNotBe(second.TaskRefWorkspaceBindingId);
        first.WorktreeBranch.ShouldNotBe(second.WorktreeBranch);
        await using var db = f.Rig.NewDb();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == first.Id);
        task.WorktreePath = task.WorktreePath!.ToUpperInvariant();
        task.RepoPath = task.RepoPath!.ToUpperInvariant();
        var context = await new TaskWorkspaceContextResolver(db).ResolveAsync(task, default);
        context.Kind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        context.BindingId.ShouldBe(first.TaskRefWorkspaceBindingId);
        context.IdentityPath.ShouldBe(first.WorktreePath);
        first.RemoteWorktreePath.ShouldStartWith("/runner/");
        Path.Exists(first.WorktreePath).ShouldBeFalse(); Path.Exists(second.WorktreePath).ShouldBeFalse();
        (await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain"))
            .ShouldNotContain("branch refs/heads/" + first.WorktreeBranch);
        f.Git.SnapshotWorktree().ShouldNotBeNullOrWhiteSpace();
    }
}
