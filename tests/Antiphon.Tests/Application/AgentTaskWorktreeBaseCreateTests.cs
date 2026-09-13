using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskWorktreeBaseCreateTests
{
    [Test]
    [Arguments("both_flags")]
    [Arguments("shared")]
    [Arguments("onagent_task")]
    [Arguments("task_without_card")]
    public async Task T0442_V11(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var service = world.Services.GetRequiredService<AgentTaskService>();
        var request = name switch
        {
            "both_flags" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                { WorktreeBaseTask = owner.Id.ToString("D"), FreshWorktree = true },
            "shared" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared)
                { FreshWorktree = true },
            "onagent_task" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                FollowUpOnTask: owner.Id.ToString("N")[..8]) { WorktreeBaseTask = owner.Id.ToString("D") },
            _ => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                { WorktreeBaseTask = owner.Id.ToString("D"), Card = null },
        };
        if (name == "task_without_card")
            request = request with { Card = null, Title = "no card" };
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(request, world.Caller(), CancellationToken.None));
        ex.Code.ShouldNotBeNull();
    }

    [Test]
    [Arguments("cross_card")]
    [Arguments("destination_mismatch")]
    public async Task T0442_V12(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        if (name == "destination_mismatch")
        {
            await using var db = world.CreateDb();
            var live = await db.AgentTasks.FindAsync(owner.Id);
            live!.MergeTargetRef = "release";
            await db.SaveChangesAsync();
        }

        if (name == "cross_card")
        {
            await using var db = world.CreateDb();
            var live = await db.AgentTasks.FindAsync(owner.Id);
            live!.CardId = null;
            await db.SaveChangesAsync();
        }

        var service = world.Services.GetRequiredService<AgentTaskService>();
        var ex = await Should.ThrowAsync<HttpException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
                { WorktreeBaseTask = owner.Id.ToString("D") },
            world.Caller(), CancellationToken.None));
        ex.StatusCode.ShouldBeOneOf(409, 422);
    }

    [Test]
    [Arguments("full_guid")]
    [Arguments("unique_short")]
    [Arguments("missing")]
    public async Task T0442_V13(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var service = world.Services.GetRequiredService<AgentTaskService>();
        var id = name switch
        {
            "full_guid" => owner.Id.ToString("D"),
            "unique_short" => owner.Id.ToString("N")[..8],
            _ => Guid.NewGuid().ToString("D"),
        };
        var request = new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442")
            { WorktreeBaseTask = id };
        if (name == "missing")
        {
            await Should.ThrowAsync<NotFoundException>(() =>
                service.CreateAsync(request, world.Caller(), CancellationToken.None));
            return;
        }

        var created = await service.CreateAsync(request, world.Caller(), CancellationToken.None);
        created.WorktreeBase.ShouldNotBeNull();
        created.WorktreeBase!.SourceTaskId.ShouldBe(owner.Id);
    }

    [Test]
    [Arguments("two_tips")]
    public async Task T0442_V14(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        await world.Repo.GitAsync("checkout", "master");
        await world.SeedSucceededAsync("X", "x.txt", "X\n");
        var service = world.Services.GetRequiredService<AgentTaskService>();
        var ex = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: "CARD-0442"),
            world.Caller(), CancellationToken.None));
        ex.Code.ShouldBe(AgentTaskWorktreeBaseResolver.AmbiguousCode);
        await using var db = world.CreateDb();
        (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.AgentTasks, t => t.Status == AgentTaskStatus.Queued))
            .ShouldBe(0);
    }

    [Test]
    [Arguments("directory_denied")]
    public async Task T0442_V17(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var owner = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var service = world.Services.GetRequiredService<AgentTaskService>();
        var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                WorkingDirectory: "C:\\not-allowed-c442", Card: "CARD-0442")
                { WorktreeBaseTask = owner.Id.ToString("D") },
            world.Caller(), CancellationToken.None));
        ex.StatusCode.ShouldBe(422);
    }
}
