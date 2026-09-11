using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandMutationWorktreeTests
{
    [Test]
    public async Task C478_V02_RebasedSnapshotAfterSourceRemoval()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        var c = world.Host.Fixture.SeedSha;
        task.SourceLandingSha.ShouldBe(c);
        task.WorktreeBaseSha.ShouldBe(c);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(c);
        await File.WriteAllTextAsync(Path.Combine(world.Host.Fixture.Repository, "later-r.txt"), "later R\n");
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "add", ".");
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "commit", "-m", "advance target to R");
        var r = (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
        r.ShouldNotBe(c);
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(c);
    }

    [Test]
    public async Task C478_V03_CreateRestartAndMissingCommit()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.WorktreePath.ShouldContain(DelegationReportFormatter.Short(world.TaskId));
        task.MergeTargetRef.ShouldBeNull();
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default);
        task.SourceLandingSha = new string('0', 40);
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default));
    }

    [Test]
    public async Task C478_V05_SettlementNeverPublishesSnapshot()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        await File.AppendAllTextAsync(Path.Combine(task.WorktreePath!, "keep.txt"), "mutant\n");
        var before = await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "diff");
        var outcome = await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().TryMergeBackAsync(task, default);
        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.LeftForHuman);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "diff")).ShouldBe(before);
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>().RunAsync(task, lease!, default));
    }

    [Test]
    public async Task C478_G056_ExactL()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.WorktreeBaseSha.ShouldBe(task.SourceLandingSha);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(task.SourceLandingSha);
    }

    [Test]
    public async Task C478_G074_NoLandRequest() => await C478_V05_SettlementNeverPublishesSnapshot();
}
