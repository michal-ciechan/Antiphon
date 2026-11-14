using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
        var context = await new RefOnlyDesktopProvisioner(f.Contexts).PlanAsync(task, lease!, default);

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
}
