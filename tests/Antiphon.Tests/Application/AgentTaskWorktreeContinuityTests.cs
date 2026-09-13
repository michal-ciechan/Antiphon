using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskWorktreeContinuityTests
{
    [Test]
    [Arguments("implicit_master")]
    [Arguments("explicit_master")]
    [Arguments("inherited_parent")]
    public async Task T0442_V01(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        AgentTask? parent = null;
        if (name == "inherited_parent")
        {
            await world.Repo.GitAsync("branch", "feat/parent");
            parent = await world.SeedSucceededAsync("parent", "parent.txt", "p\n", mergeTarget: "feat/parent");
            await using var dbp = world.CreateDb();
            var live = await dbp.AgentTasks.FindAsync(parent.Id);
            live!.WorktreeBranch = "feat/parent";
            live.Kind = AgentTaskKind.Orchestrator;
            await dbp.SaveChangesAsync();
            parent = live;
        }

        var first = await world.Services.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest("first", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                Card: "CARD-0442", MergeTargetRef: name == "explicit_master" ? "master" : null),
            world.Caller(parent), CancellationToken.None);
        await using (var scope = world.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        await using var db = world.CreateDb();
        var code1 = await db.AgentTasks.SingleAsync(t => t.Id == first.Id);
        code1.Status.ShouldBe(AgentTaskStatus.Dispatched);
        code1.WorktreePath.ShouldNotBeNull();
        await File.WriteAllTextAsync(Path.Combine(code1.WorktreePath!, "code-a.txt"), "A\n");
        (await ScratchGitRepo.GitInAsync(code1.WorktreePath!, "add", "code-a.txt")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(code1.WorktreePath!, "commit", "-m", "A")).Ok.ShouldBeTrue();
        var shaA = (await ScratchGitRepo.GitInAsync(code1.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();
        code1.Status = AgentTaskStatus.Succeeded;
        code1.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var review = await world.Services.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest("review", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442"),
            world.Caller(parent), CancellationToken.None);
        review.WorktreeBase.ShouldNotBeNull();
        review.WorktreeBase!.SourceSha.ShouldBe(shaA);
        await using (var scope = world.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        var reviewRow = await db.AgentTasks.SingleAsync(t => t.Id == review.Id);
        reviewRow.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await ScratchGitRepo.GitInAsync(reviewRow.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(shaA);
        File.ReadAllText(Path.Combine(reviewRow.WorktreePath!, "code-a.txt")).Replace("\r\n", "\n").ShouldBe("A\n");
        reviewRow.WorktreePath.ShouldNotBe(code1.WorktreePath);
        if (name == "inherited_parent")
            reviewRow.MergeTargetRef.ShouldBe("feat/parent");
    }

    [Test]
    [Arguments("no_target")]
    [Arguments("explicit_target")]
    public async Task T0442_V27(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var created = await world.Services.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest("next", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442",
                MergeTargetRef: name == "explicit_target" ? "master" : null),
            world.Caller(), CancellationToken.None);
        created.WorktreeBase!.SourceTaskId.ShouldBe(a.Id);
        created.WorktreeBase.SourceSha.ShouldBe(a.WorktreeBaseSha);
    }

    [Test]
    [Arguments("uncontained_sibling")]
    [Arguments("uncertain_sibling")]
    [Arguments("inspection_failure")]
    public async Task T0442_V31(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        if (name == "uncertain_sibling")
            await world.SeedMergeRangeAsync();
        else
            await world.SeedSucceededAsync("X", "x.txt", "X\n");
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var queued = world.NewQueued();
        if (name == "inspection_failure")
            queued.RepoPath = Path.Combine(world.Repo.Path, "missing");
        var resolution = await resolver.ResolveAsync(queued, CancellationToken.None);
        if (name == "uncontained_sibling")
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Ambiguous);
        else
            resolution.Preview.Candidates.ShouldNotBeNull();
    }
}
