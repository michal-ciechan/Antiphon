using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandMutationCleanupTests
{
    [Test]
    public async Task C478_V06_RestoredTerminalCleanupMatrix()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        var path = await world.PathAsync();
        await world.WriteRestorationAsync([]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<VerificationCleanupService>();
        (await service.CleanupAsync(world.TaskId, default)).Residue.ShouldBeNull();
        Directory.Exists(path).ShouldBeFalse();
        File.Exists(await world.EvidencePathAsync()).ShouldBeTrue();
        (await service.CleanupAsync(world.TaskId, default)).IsClean.ShouldBeTrue();
    }

    [Test]
    [Arguments("unknown-file")]
    [Arguments("ignored-file")]
    [Arguments("dirty-source")]
    [Arguments("missing-evidence")]
    public async Task C478_G101_Untracked(string variant)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        var path = await world.PathAsync();
        await world.WriteRestorationAsync([]);
        switch (variant)
        {
            case "unknown-file": await File.WriteAllTextAsync(Path.Combine(path, "unknown.txt"), "keep"); break;
            case "ignored-file":
                Directory.CreateDirectory(Path.Combine(path, "bin-private"));
                await File.WriteAllTextAsync(Path.Combine(path, "bin-private", "secret.txt"), "keep");
                break;
            case "dirty-source": await File.AppendAllTextAsync(Path.Combine(path, "keep.txt"), "mutant"); break;
            case "missing-evidence": File.Delete(await world.EvidencePathAsync()); break;
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default))
            .IsClean.ShouldBeFalse();
        Directory.Exists(path).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G088_Terminal()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default));
    }

    [Test]
    public async Task C478_G228_NoCleanupKill()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await File.WriteAllTextAsync(Path.Combine(await world.PathAsync(), "unknown.txt"), "keep");
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Status.ShouldBe(Antiphon.Server.Domain.Enums.AgentTaskStatus.Succeeded);
    }
}
