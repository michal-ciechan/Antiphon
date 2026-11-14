using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional lifecycle budget; measure the first red/green pair.
public sealed class C0824RefOnlyProvisionTests
{
    [Test]
    public async Task Intent_precedes_git_and_never_creates_identity()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        // A fresh ordinary task has neither a recorded workspace nor a branch.
        // The fixture's other task/ref is a decoy that the producer must preserve.
        var id = Guid.NewGuid();
        var task = new Antiphon.Server.Domain.Entities.AgentTask
        {
            Id = id, RootTaskId = id, Workspace = WorkspaceMode.Worktree,
            RunnerId = "fixture-runner", Role = AgentTaskRole.Plan,
            RepoPath = f.Git.Desktop, WorkingDirectory = f.Git.Desktop,
            WorktreeBaseRequestedRef = f.Git.Baseline,
        };
        f.Db.AgentTasks.Add(task);
        await f.Db.SaveChangesAsync();
        var registrations = await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain");
        var inventory = await f.InventoryAsync();
        await using var lease = await f.Git.Leases.TryAcquireAsync(f.Git.Desktop, default);
        lease.ShouldNotBeNull();
        var context = await Producer(f).PlanAsync(task, lease!, default);

        context.Kind.ShouldBe(DesktopWorkspaceKind.RefOnly, "new intent is persisted as RefOnly before ref creation");
        await using var reader = new Antiphon.Server.Infrastructure.Data.AppDbContext(
            TestDbFixture.CreateDbContextOptions(f.Schema.ConnectionString));
        var persisted = await reader.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        persisted.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        persisted.TaskRefWorkspaceBindingId.ShouldBe(context.BindingId);
        var binding = await reader.TaskRefWorkspaceBindings.SingleAsync(b => b.Id == context.BindingId);
        binding.PreparationState.ShouldBe(TaskRefWorkspacePreparationState.Planned);
        binding.SelectedBaseSha.ShouldBe(f.Git.Baseline);
        Path.Exists(context.IdentityPath).ShouldBeFalse("identity is never a desktop directory");
        (await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain")).ShouldBe(registrations);
        (await f.InventoryAsync()).ShouldBe(inventory, "planning mutates no ref");
    }

    [Test]
    public async Task Bases_and_short_name_collisions_preserve_ownership()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        await using var lease = await f.Git.Leases.TryAcquireAsync(f.Git.Desktop, default);
        lease.ShouldNotBeNull();
        var producer = Producer(f);
        foreach (var source in new[] { "explicit", "repair", "card", "target", "default" })
        {
            var task = NewTask(f);
            task.WorktreeBaseRequestedRef = source is "explicit" or "repair" ? f.Git.Baseline : null;
            task.MergeTargetRef = source == "target" ? "master" : null;
            task.RepairSourceTaskId = source == "repair" ? f.Task.Id : null;
            f.Db.AgentTasks.Add(task); await f.Db.SaveChangesAsync();
            var result = await producer.PlanAsync(task, lease!, source == "repair" ? f.Git.Baseline : null,
                source == "card" ? new(f.Git.Baseline, WorktreeBaseSource.CardCurrent, null) : null,
                f.Task.Id, f.Task.WorktreeBranch, default);
            result.BaseSha.ShouldBe(f.Git.Baseline);
            task.WorktreeBaseSource.ShouldBe(source switch
            {
                "explicit" => WorktreeBaseSource.Explicit, "repair" => WorktreeBaseSource.Repair,
                "card" => WorktreeBaseSource.CardCurrent, "target" => WorktreeBaseSource.MergeTarget,
                _ => WorktreeBaseSource.DefaultBranch,
            });
        }
        var collision = NewTask(f);
        collision.Id = Guid.ParseExact(f.Task.Id.ToString("N")[..8] + "111111111111111111111111", "N");
        collision.RootTaskId = collision.Id;
        f.Db.AgentTasks.Add(collision); await f.Db.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => producer.PlanAsync(collision, lease!, default)))
            .Code.ShouldBe("ref_only_legacy_ref_owner_conflict");
        (await f.TipAsync()).ShouldBe(f.Git.Baseline, "foreign equal-tip owner retained");
        Path.Exists(Path.Combine(f.Git.Root, ".ref-only", collision.Id.ToString("N"))).ShouldBeFalse();
    }

    [Test]
    public async Task Existing_mode_survives_gate_and_attempt_change()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var initial = await f.Contexts.ResolveAsync(f.Task, default);
        f.Task.Attempt++; await f.Db.SaveChangesAsync();
        var disabled = new RefOnlyDesktopSettings();
        disabled.Enabled.ShouldBeFalse(); disabled.RunnerIds.ShouldBeEmpty(); disabled.Roles.ShouldBe("none");
        await using var lease = await f.Git.Leases.TryAcquireAsync(f.Git.Desktop, default);
        var result = await Producer(f).PlanAsync(f.Task, lease!, default);
        result.ShouldBe(initial, "recorded mode bypasses new admission");
        (await f.Db.TaskRefWorkspaceBindings.CountAsync(b => b.TaskId == f.Task.Id)).ShouldBe(1);
        Path.Exists(result.IdentityPath).ShouldBeFalse();
    }

    [Test]
    public async Task Selected_failure_never_falls_back_to_checkout()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var task = NewTask(f); f.Db.AgentTasks.Add(task); await f.Db.SaveChangesAsync();
        var inventory = await f.InventoryAsync();
        var registry = await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain");
        await using var lease = await f.Git.Leases.TryAcquireAsync(f.Git.Desktop, default);
        var producer = Producer(f);
        var selected = await producer.PlanAsync(task, lease!, default);
        await f.Db.TaskRefWorkspaceBindings.Where(b => b.Id == selected.BindingId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.SelectedBaseSha, new string('f', 40)));
        (await Should.ThrowAsync<ConflictException>(() => producer.PlanAsync(task, lease!, default)))
            .Code.ShouldBe("ref_only_binding_base_mismatch");
        (await f.InventoryAsync()).ShouldBe(inventory);
        (await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain")).ShouldBe(registry);
        Path.Exists(selected.IdentityPath).ShouldBeFalse();
        (await f.Db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
    }

    internal static AgentTask NewTask(RefOnlySyncFixture f)
    {
        var id = Guid.NewGuid();
        return new() { Id = id, RootTaskId = id, Workspace = WorkspaceMode.Worktree,
            RunnerId = "fixture-runner", Role = AgentTaskRole.Plan, RepoPath = f.Git.Desktop,
            WorkingDirectory = f.Git.Desktop, WorktreeBaseRequestedRef = f.Git.Baseline };
    }

    internal static RefOnlyDesktopProvisioner Producer(RefOnlySyncFixture f)
    {
        var settings = new GitSettings { WorktreeBasePath = f.Git.Root };
        var graph = DelegationTestServices.CreateGitGraph(settings, f.Db);
        return new(f.Db, f.Contexts, graph.Worktrees, f.Git.Git, f.Git.Git, f.Git.Leases, Options.Create(settings));
    }
}
