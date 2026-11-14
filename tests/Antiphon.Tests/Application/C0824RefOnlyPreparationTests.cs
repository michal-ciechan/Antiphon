using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional real-Git crash/publication budget.
public sealed class C0824RefOnlyPreparationTests
{
    [Test]
    public async Task Initial_ref_creation_requires_expected_absence()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var task = await PlannedAsync(f);
        var producer = C0824RefOnlyProvisionTests.Producer(f);
        var registry = await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain");
        var result = await producer.PublishAsync(task, default);
        result.Pushed.ShouldBeTrue("initial ref is created and exactly published: " + result.Warning);
        result.Sha.ShouldBe(f.Git.Baseline);
        (await f.Git.RevParseAsync("refs/heads/" + task.WorktreeBranch)).ShouldBe(f.Git.Baseline);
        (await f.Db.TaskRefWorkspaceBindings.AsNoTracking().SingleAsync(b => b.Id == task.TaskRefWorkspaceBindingId))
            .PreparationState.ShouldBe(TaskRefWorkspacePreparationState.Published);
        (await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain")).ShouldBe(registry);
        Path.Exists(task.WorktreePath).ShouldBeFalse();

        var foreign = await PlannedAsync(f);
        await f.Git.RunAsync(f.Git.Desktop, "update-ref", "refs/heads/" + foreign.WorktreeBranch, f.Git.Baseline);
        var refused = await producer.PublishAsync(foreign, default);
        refused.Pushed.ShouldBeFalse("equal SHA does not establish local ownership");
        refused.Warning.ShouldBe("ref_only_local_ref_collision");
        (await f.Git.RevParseAsync("refs/heads/" + foreign.WorktreeBranch)).ShouldBe(f.Git.Baseline);
    }

    [Test]
    public async Task Initial_remote_collision_is_not_overwritten()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var task = await PlannedAsync(f);
        var foreign = await f.Git.PublishForeignBranchAsync(task.WorktreeBranch!, "foreign.txt");
        foreign.ShouldNotBe(f.Git.Baseline, "foreign branch has independent work");
        var result = await C0824RefOnlyProvisionTests.Producer(f).PublishAsync(task, default);
        result.Pushed.ShouldBeFalse();
        result.Warning.ShouldBe("ref_only_remote_ref_collision");
        (await f.Git.RunAsync(f.Git.Origin, "rev-parse", "refs/heads/" + task.WorktreeBranch)).ShouldBe(foreign);
        Path.Exists(task.WorktreePath).ShouldBeFalse();

        // A foreign ancestor would be overwritten by an ordinary fast-forward push.
        await f.Git.RunAsync(f.Git.Desktop, "commit", "--allow-empty", "-m", "new selected base");
        var newer = C0824RefOnlyProvisionTests.NewTask(f);
        newer.WorktreeBaseRequestedRef = await f.Git.RevParseAsync("HEAD");
        f.Db.AgentTasks.Add(newer); await f.Db.SaveChangesAsync();
        await using (var lease = await f.Git.Leases.TryAcquireAsync(f.Git.Desktop, default))
            await C0824RefOnlyProvisionTests.Producer(f).PlanAsync(newer, lease!, default);
        await f.Git.RunAsync(f.Git.Desktop, "push", "origin", f.Git.Baseline + ":refs/heads/" + newer.WorktreeBranch);
        result = await C0824RefOnlyProvisionTests.Producer(f).PublishAsync(newer, default);
        result.Pushed.ShouldBeFalse("foreign ancestor is never adopted by fast-forward");
        result.Warning.ShouldBe("ref_only_remote_ref_collision");
        (await f.Git.RunAsync(f.Git.Origin, "rev-parse", "refs/heads/" + newer.WorktreeBranch)).ShouldBe(f.Git.Baseline);
    }

    [Test]
    public async Task Crash_boundaries_resume_only_owned_intent()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        foreach (var cut in new[] { "before-local-ref", "after-local-ref", "ref-created", "before-push", "after-push", "published" })
        {
            var task = await PlannedAsync(f);
            var producer = C0824RefOnlyProvisionTests.Producer(f);
            var reached = false;
            producer.PreparationBoundaryAsync = (site, _) =>
            {
                if (site != cut) return Task.CompletedTask;
                reached = true;
                throw new InjectedCrash();
            };
            await Should.ThrowAsync<InjectedCrash>(() => producer.PublishAsync(task, default));
            reached.ShouldBeTrue(cut);
            await using var fresh = new Antiphon.Server.Infrastructure.Data.AppDbContext(
                TestDbFixture.CreateDbContextOptions(f.Schema.ConnectionString));
            var durable = await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            durable.TaskRefWorkspaceBindingId.ShouldBe(task.TaskRefWorkspaceBindingId);
            // New service scope: retry has no in-memory success flag or ref ownership cache.
            var settings = new Antiphon.Server.Application.Settings.GitSettings { WorktreeBasePath = f.Git.Root };
            var graph = DelegationTestServices.CreateGitGraph(settings, fresh);
            var resumed = new RefOnlyDesktopProvisioner(fresh, new(fresh), graph.Worktrees,
                f.Git.Git, f.Git.Git, f.Git.Leases, Microsoft.Extensions.Options.Options.Create(settings));
            var result = await resumed.PublishAsync(durable, default);
            result.Pushed.ShouldBeTrue(cut + ": " + result.Warning);
            result.Sha.ShouldBe(f.Git.Baseline);
            Path.Exists(durable.WorktreePath).ShouldBeFalse();
        }
    }

    [Test]
    public async Task Publication_uses_frozen_ref_and_never_rewinds()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var task = await PlannedAsync(f);
        await f.Git.RunAsync(f.Git.Desktop, "commit", "--allow-empty", "-m", "move canonical HEAD");
        (await f.Git.RevParseAsync("HEAD")).ShouldNotBe(f.Git.Baseline);
        var producer = C0824RefOnlyProvisionTests.Producer(f);
        var result = await producer.PublishAsync(task, default);
        result.Pushed.ShouldBeTrue(result.Warning);
        result.Sha.ShouldBe(f.Git.Baseline, "frozen B, never canonical HEAD");
        await f.Git.RunAsync(f.Git.Runner, "fetch", "origin", "refs/heads/" + task.WorktreeBranch);
        await f.Git.RunAsync(f.Git.Runner, "checkout", "-B", task.WorktreeBranch!, "FETCH_HEAD");
        await f.Git.RunAsync(f.Git.Runner, "commit", "--allow-empty", "-m", "runner progress");
        var advanced = await f.Git.RunAsync(f.Git.Runner, "rev-parse", "HEAD");
        await f.Git.RunAsync(f.Git.Runner, "push", "origin", "refs/heads/" + task.WorktreeBranch);
        f.Git.Git.Clear();
        await producer.PublishAsync(task, default);
        f.Git.Git.Commands.ShouldNotContain(c => c.StartsWith("push ", StringComparison.Ordinal), "no creation push on replay");
        (await f.Git.RunAsync(f.Git.Origin, "rev-parse", "refs/heads/" + task.WorktreeBranch))
            .ShouldBe(advanced, "replay must never rewind the runner");
        Path.Exists(task.WorktreePath).ShouldBeFalse();
    }

    internal static async Task<AgentTask> PlannedAsync(RefOnlySyncFixture f)
    {
        var task = C0824RefOnlyProvisionTests.NewTask(f);
        f.Db.AgentTasks.Add(task); await f.Db.SaveChangesAsync();
        await using var lease = await f.Git.Leases.TryAcquireAsync(f.Git.Desktop, default);
        lease.ShouldNotBeNull();
        await C0824RefOnlyProvisionTests.Producer(f).PlanAsync(task, lease!, default);
        return task;
    }

    [Test]
    public async Task Baseline_and_launch_bind_published_ref_and_runner_cwd()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync();
        var id = await f.SeedAsync(t => t.Goal = new string('x', 100_000));
        var task = await f.LaunchAsync(id);
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson).ShouldNotBeNull();
        baseline.SchemaVersion.ShouldBe(2);
        baseline.Primary.RegisteredCheckout.ShouldBeNull();
        baseline.Primary.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        baseline.Primary.TaskRefWorkspaceBindingId.ShouldBe(task.TaskRefWorkspaceBindingId);
        baseline.Primary.LocalSha.ShouldBe(f.Git.Baseline);
        baseline.Primary.FullRef.ShouldBe("refs/heads/" + task.WorktreeBranch);
        baseline.Primary.Remote.State.ShouldBe(ProgressRemoteState.Present);
        baseline.Primary.Remote.Sha.ShouldBe(f.Git.Baseline);
        await using var db = f.Rig.NewDb();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.RunnerCwd.ShouldBe(task.RemoteWorktreePath);
        f.Rig.Sink.Specs.ShouldHaveSingleItem().Cwd.ShouldBe(task.RemoteWorktreePath);
        Path.Exists(task.WorktreePath).ShouldBeFalse();
        Path.Exists(Path.Combine(f.Git.Desktop, ".antiphon", "task-" + DelegationReportFormatter.Short(id) + "-brief.md")).ShouldBeFalse();
        // The fake launch sink does not perform Grok's rules-ready handshake. Exercise the
        // same production queue entry point that readiness invokes, against the real binding.
        using (var scope = f.Rig.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .EnsureLaunchBriefAsync(task, session.Id, session.StartedAt, default);
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == session.Id);
        message.RemoteSpillBody.ShouldNotBeNull().ShouldContain(new string('x', 1000));
        message.RemoteSpillRelativePath.ShouldNotBeNull().ShouldStartWith(".antiphon/");
        message.RemoteSpillBody.ShouldNotContain("desktop checkout " + task.WorktreePath);
        // Missing remote coordinates must refuse before even an inline brief can use local fallback.
        Should.Throw<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            AgentTaskDispatcher.FitBriefForTyping(task, new(), runnerCwd: null));
    }

    [Test]
    public async Task Lost_mirror_reply_reuses_preparation_and_slot()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync(slots: true);
        var id = await f.SeedAsync();
        await f.Rig.TickAsync();
        var first = await f.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var initial = first.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        initial.Binding.ShouldNotBeNull();
        initial.Slot.ShouldBe(true);
        var selected = await f.Rig.ReadTaskAsync(id);
        await f.Rig.RestartAsync();
        f.Gate.CurrentValue.Enabled = false; f.Gate.CurrentValue.Roles = "none";
        await f.Rig.TickAsync();
        var second = await f.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 2);
        var replay = second.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        replay.Binding.ShouldNotBeNull().PreparationId.ShouldBe(initial.Binding!.PreparationId);
        replay.Binding.RunnerStoreId.ShouldBe(initial.Binding.RunnerStoreId);
        replay.Branch.ShouldBe(initial.Branch);
        replay.Sha.ShouldBe(initial.Sha);
        replay.Slot.ShouldBe(true);
        await f.Rig.Peer.EmitAsync(new(PhoneHomeFrameKind.Result, second.Epoch, second.RequestId, second.Operation,
            JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceMirrorResponse("/runner/replayed/" + id.ToString("N"),
                SlotId: "ws-00", Generation: 1), PhoneHomeFraming.Json)));
        await f.Rig.Preparer.WhenIdleAsync();
        var resumed = await f.Rig.ReadTaskAsync(id);
        resumed.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        resumed.TaskRefWorkspaceBindingId.ShouldBe(selected.TaskRefWorkspaceBindingId);
        resumed.RemoteWorkspaceSlotId.ShouldBe("ws-00");
        resumed.RemoteWorkspaceSlotGeneration.ShouldBe(1);
        await using var db = f.Rig.NewDb();
        (await db.TaskWorktreeMirrorBindings.CountAsync(b => b.TaskId == id)).ShouldBe(1);
        Path.Exists(resumed.WorktreePath).ShouldBeFalse();
    }

    private sealed class InjectedCrash : Exception;
}
