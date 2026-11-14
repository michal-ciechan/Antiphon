using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

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
        foreign.ShouldNotBe(f.Git.Baseline, "a foreign descendant would pass ordinary FF publication");
        var result = await C0824RefOnlyProvisionTests.Producer(f).PublishAsync(task, default);
        result.Pushed.ShouldBeFalse();
        result.Warning.ShouldBe("ref_only_remote_ref_collision");
        (await f.Git.RunAsync(f.Git.Origin, "rev-parse", "refs/heads/" + task.WorktreeBranch)).ShouldBe(foreign);
        Path.Exists(task.WorktreePath).ShouldBeFalse();
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
        await producer.PublishAsync(task, default);
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

    private sealed class InjectedCrash : Exception;
}
