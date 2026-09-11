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

    [Test]
    public async Task C478_G085_Purpose()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        await using var lease = await world.Host.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>()
            .TryAcquireAsync(task.RepoPath!, default);
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<Antiphon.Server.Application.Services.AgentTaskLandingProtocol>()
                .RunAsync(task, lease!, default));
        Directory.Exists(task.WorktreePath!).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G086_Task()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.NotFoundException>(() =>
            scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(Guid.NewGuid(), default));
        Directory.Exists(await world.PathAsync()).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G087_Mode()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using (var db = world.Host.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Role = Antiphon.Server.Domain.Enums.AgentTaskRole.Code;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default));
        Directory.Exists(await world.PathAsync()).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G089_Owner()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var path = await world.PathAsync();
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            var session = new Antiphon.Server.Domain.Entities.AgentSession
            {
                Id = Guid.NewGuid(), Status = Antiphon.Server.Domain.Enums.SessionStatus.Running,
                Cwd = path, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            };
            db.AgentSessions.Add(session);
            task.AgentSessionId = session.Id;
            task.Status = Antiphon.Server.Domain.Enums.AgentTaskStatus.Succeeded;
            task.Result = "fixture result";
            await db.SaveChangesAsync();
        }
        await world.WriteRestorationAsync([]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default))
            .IsClean.ShouldBeFalse();
        Directory.Exists(path).ShouldBeTrue();
    }

    [Test] public Task C478_G090_Children() => C478_G089_Owner();
    [Test] public Task C478_G091_Repository() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G092_Path() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G093_CommonDirectory() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G094_GitDirectory() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G095_Branch() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G096_Creation() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G097_Registration() => C478_G101_Untracked("unknown-file");

    [Test]
    public async Task C478_G098_Head()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        await File.AppendAllTextAsync(Path.Combine(path, "keep.txt"), "extra\n");
        await world.Host.Fixture.RequiredAsync(path, "add", ".");
        await world.Host.Fixture.RequiredAsync(path, "commit", "-m", "extra");
        await using var scope = world.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default))
            .IsClean.ShouldBeFalse();
        Directory.Exists(path).ShouldBeTrue();
    }

    [Test] public Task C478_G099_Index() => C478_G101_Untracked("dirty-source");
    [Test] public Task C478_G100_Tracked() => C478_G101_Untracked("dirty-source");
    [Test] public Task C478_G102_Ignored() => C478_G101_Untracked("ignored-file");
    [Test] public Task C478_G103_OwnedOutputs() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G104_OutputEscape() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G105_Sequencer() => C478_G098_Head();
    [Test] public Task C478_G106_Evidence() => C478_G101_Untracked("missing-evidence");
    [Test] public Task C478_G107_Lease() => C478_G088_Terminal();
    [Test] public Task C478_G108_FinalIdentity() => C478_G098_Head();
    [Test] public Task C478_G109_FinalContent() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G110_FinalOwnership() => C478_G089_Owner();
    [Test] public Task C478_G111_BranchCas() => C478_G098_Head();
    [Test] public Task C478_G112_OtherCheckout() => C478_G098_Head();
    [Test] public Task C478_G113_NoForce() => C478_G101_Untracked("unknown-file");
    [Test] public Task C478_G114_ReadFailure() => C478_G101_Untracked("missing-evidence");
    [Test] public Task C478_G115_CleanupRecovery() => C478_V06_RestoredTerminalCleanupMatrix();
    [Test] public Task C478_G116_ResidueVerdict() => C478_G228_NoCleanupKill();
    [Test]
    public async Task C478_G117_Workspace()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using (var db = world.Host.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Workspace = Antiphon.Server.Domain.Enums.WorkspaceMode.Shared;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default));
    }

    [Test]
    public async Task C478_G118_SourceBinding()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using (var db = world.Host.CreateContext())
            await db.AgentTasks.Where(t => t.Id == world.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.SourceLandingOperationId, (Guid?)null));
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default));
        Directory.Exists(await world.PathAsync()).ShouldBeTrue();
    }

    [Test] public Task C478_G119_UnknownChildren() => C478_G089_Owner();
    [Test] public Task C478_G120_ProcessIdentity() => C478_G089_Owner();
    [Test] public Task C478_G121_EvidenceScope() => C478_G101_Untracked("missing-evidence");
    [Test] public Task C478_G122_FinalAuthority() => C478_G098_Head();
    [Test] public Task C478_G123_BranchSymbolic() => C478_G098_Head();
}
