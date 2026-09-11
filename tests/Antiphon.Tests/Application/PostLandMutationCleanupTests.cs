using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
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
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G088_Terminal()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default));
        Directory.Exists(await world.PathAsync()).ShouldBeTrue();
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
        (await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Status.ShouldBe(AgentTaskStatus.Succeeded);
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
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(task.RepoPath!, default);
        world.Host.Fixture.Git.Trace.Clear();
        await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>()
                .RunAsync(task, lease!, default));
        Directory.Exists(task.WorktreePath!).ShouldBeTrue();
        world.Host.Fixture.Git.Trace.Any(PostLandMutationWorld.IsDestructive).ShouldBeFalse();
    }

    [Test]
    public async Task C478_G086_Task()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        world.Host.Fixture.Git.Trace.Clear();
        await Should.ThrowAsync<NotFoundException>(() =>
            scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(Guid.NewGuid(), default));
        Directory.Exists(path).ShouldBeTrue();
        world.Host.Fixture.Git.Trace.Any(PostLandMutationWorld.IsDestructive).ShouldBeFalse();
    }

    [Test]
    public async Task C478_G087_Mode()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using (var db = world.Host.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Role = AgentTaskRole.Code;
            await db.SaveChangesAsync();
        }
        await RefuseCleanupAsync(world, await world.PathAsync(), expectConflict: true);
    }

    [Test]
    public async Task C478_G089_Owner()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var path = await world.PathAsync();
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            var session = new AgentSession
            {
                Id = Guid.NewGuid(), Status = SessionStatus.Running,
                Cwd = path, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            };
            db.AgentSessions.Add(session);
            task.AgentSessionId = session.Id;
            task.Status = AgentTaskStatus.Succeeded;
            task.Result = "fixture result";
            await db.SaveChangesAsync();
        }
        await world.WriteRestorationAsync([]);
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G090_Children()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        if (world.Runner is FakeSessionRunnerClient fake)
            fake.VerificationCustody = (b, _, _) => Task.FromResult(
                new VerificationCustodyStatus(b, VerificationCustodyState.Draining, "live descendants"));
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        var result = await RefuseCleanupAsync(world, await world.PathAsync());
        result.Residue.ShouldNotBeNull();
    }

    [Test]
    public async Task C478_G091_Repository()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        await using (var db = world.Host.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).RepoPath = world.Host.Fixture.Observer;
            await db.SaveChangesAsync();
        }
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G092_Path()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var sibling = Path.Combine(world.Host.Fixture.Root, "trees", "sibling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sibling);
        await using (var db = world.Host.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).WorktreePath = sibling;
            await db.SaveChangesAsync();
        }
        await RefuseCleanupAsync(world, path);
        Directory.Exists(sibling).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G093_CommonDirectory()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        await using (var db = world.Host.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == world.Operation);
            op.CommonDirectory = world.Host.Fixture.Observer;
            await db.SaveChangesAsync();
        }
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G094_GitDirectory()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var creation = await CreationAsync(world);
        var copy = Path.Combine(world.Host.Fixture.Root, "gitdir-copy-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(creation.WorktreeGitDirectory, copy);
        var gitFile = Path.Combine(path, ".git");
        if (File.Exists(gitFile))
        {
            File.SetAttributes(gitFile, FileAttributes.Normal);
            File.Delete(gitFile);
        }
        else if (Directory.Exists(gitFile))
        {
            foreach (var file in Directory.EnumerateFiles(gitFile, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(gitFile, recursive: true);
        }
        await File.WriteAllTextAsync(gitFile, "gitdir: " + copy + Environment.NewLine);
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G095_Branch()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        await world.Host.Fixture.RequiredAsync(path, "switch", "-c", "c478-other-branch");
        await RefuseCleanupAsync(world, path);
        (await world.Host.Fixture.RequiredAsync(path, "symbolic-ref", "-q", "HEAD")).Trim()
            .ShouldBe("refs/heads/c478-other-branch");
    }

    [Test]
    public async Task C478_G096_Creation()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        (await world.Host.Fixture.RequiredAsync(path, "status", "--porcelain", "--untracked-files=all"))
            .Trim().ShouldBe("");
        var creation = await CreationAsync(world);
        var replacementId = Guid.NewGuid();
        var metadataDir = Path.Combine(world.Host.Fixture.Root, "trees", ".antiphon", "worktrees");
        var rewritten = 0;
        foreach (var file in Directory.GetFiles(metadataDir, "*.json"))
        {
            var text = await File.ReadAllTextAsync(file);
            if (!text.Contains(creation.CreationId.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            await File.WriteAllTextAsync(file, text.Replace(creation.CreationId.ToString(), replacementId.ToString(),
                StringComparison.OrdinalIgnoreCase));
            rewritten++;
        }
        rewritten.ShouldBeGreaterThan(0);
        await RefuseCleanupAsync(world, path);
        Directory.Exists(path).ShouldBeTrue();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var metadata = await scope.ServiceProvider.GetRequiredService<IWorktreeManager>()
            .ReadVerificationCreationAsync(path, default);
        metadata.ShouldNotBeNull();
        metadata!.CreationId.ShouldBe(replacementId);
        metadata.CreationId.ShouldNotBe(creation.CreationId);
    }

    [Test]
    public async Task C478_G097_Registration()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var parked = path + ".parked";
        Directory.Move(path, parked);
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "worktree", "prune");
        Directory.Move(parked, path);
        await RefuseCleanupAsync(world, path);
    }

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
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G099_Index()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var file = Path.Combine(path, "keep.txt");
        await File.AppendAllTextAsync(file, "staged-only\n");
        await world.Host.Fixture.RequiredAsync(path, "add", "keep.txt");
        await File.WriteAllTextAsync(file, await world.Host.Fixture.RequiredAsync(path, "show", "HEAD:keep.txt"));
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G100_Tracked()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        await File.AppendAllTextAsync(Path.Combine(path, "keep.txt"), "unstaged\n");
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G102_Ignored()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        Directory.CreateDirectory(Path.Combine(path, "bin-private"));
        await File.WriteAllTextAsync(Path.Combine(path, "bin-private", "secret.txt"), "keep");
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G103_OwnedOutputs()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        var path = await world.PathAsync();
        Directory.CreateDirectory(Path.Combine(path, "bin-verification"));
        var listed = Encoding.UTF8.GetBytes("owned result\n");
        await File.WriteAllBytesAsync(Path.Combine(path, "bin-verification", "result.txt"), listed);
        await File.WriteAllTextAsync(Path.Combine(path, "bin-verification", "sibling.txt"), "unlisted");
        await world.WriteRestorationAsync([new("bin-verification/result.txt", Convert.ToHexString(SHA256.HashData(listed)))]);
        await RefuseCleanupAsync(world, path);
        File.Exists(Path.Combine(path, "bin-verification", "sibling.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G104_OutputEscape()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        var outside = Path.Combine(world.Host.Fixture.Root, "outside.txt");
        await File.WriteAllTextAsync(outside, "keep-outside");
        await world.WriteRestorationAsync([new("../outside.txt", Convert.ToHexString(SHA256.HashData("keep-outside"u8.ToArray())))]);
        await RefuseCleanupAsync(world, await world.PathAsync());
        File.Exists(outside).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G105_Sequencer()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var gitDir = (await CreationAsync(world)).WorktreeGitDirectory;
        Directory.CreateDirectory(Path.Combine(gitDir, "rebase-merge"));
        await File.WriteAllTextAsync(Path.Combine(gitDir, "rebase-merge", "head-name"), "refs/heads/keep");
        await RefuseCleanupAsync(world, path);
        (await world.Host.Fixture.RequiredAsync(path, "rev-parse", "HEAD")).Trim().ShouldBe(world.Host.Fixture.SeedSha);
    }

    [Test]
    public async Task C478_G106_Evidence()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        File.Delete(await world.EvidencePathAsync());
        await RefuseCleanupAsync(world, await world.PathAsync());
    }

    [Test]
    public async Task C478_G107_Lease()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        await using var held = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(world.Host.Fixture.Repository, default);
        held.ShouldNotBeNull();
        var result = await RefuseCleanupAsync(world, path);
        result.Residue.ShouldBe("repository_busy_or_child_recovery_required");
    }

    [Test]
    public async Task C478_G108_FinalIdentity()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var inspections = 0;
        world.Host.Fixture.Git.BeforeObservedCommand = async arguments =>
        {
            if (!arguments.Contains("status")) return;
            inspections++;
            if (inspections < 2) return;
            await File.AppendAllTextAsync(Path.Combine(path, "keep.txt"), "late-head\n");
            await world.Host.Fixture.RequiredAsync(path, "add", ".");
            await world.Host.Fixture.RequiredAsync(path, "commit", "-m", "late identity");
        };
        await RefuseCleanupAsync(world, path);
        inspections.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task C478_G109_FinalContent()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var inspections = 0;
        world.Host.Fixture.Git.BeforeObservedCommand = async arguments =>
        {
            if (!arguments.Contains("status")) return;
            inspections++;
            if (inspections < 2) return;
            await File.WriteAllTextAsync(Path.Combine(path, "late-unknown.txt"), "keep");
        };
        await RefuseCleanupAsync(world, path);
        File.Exists(Path.Combine(path, "late-unknown.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G110_FinalOwnership()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var inspections = 0;
        world.Host.Fixture.Git.BeforeObservedCommand = async arguments =>
        {
            if (!arguments.Contains("status")) return;
            inspections++;
            if (inspections < 2) return;
            await using var db = world.Host.CreateContext();
            var session = new AgentSession
            {
                Id = Guid.NewGuid(), Status = SessionStatus.Running, Cwd = path,
                StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            };
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
        };
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G111_BranchCas()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var branch = "refs/heads/feat/card-task-" + DelegationReportFormatter.Short(world.TaskId);
        world.Host.Fixture.Git.AfterCommand = async (_, arguments, result) =>
        {
            if (!result.Succeeded || !arguments.Contains("worktree") || !arguments.Contains("remove")) return;
            await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "commit", "--allow-empty", "-m", "advance");
            var advanced = (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
            await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "update-ref", branch, advanced);
        };
        world.Host.Fixture.Git.Trace.Clear();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldNotBeNull();
        (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "show-ref", "--exists", branch)).ShouldNotBeNull();
    }

    [Test]
    public async Task C478_G112_OtherCheckout()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var branch = "feat/card-task-" + DelegationReportFormatter.Short(world.TaskId);
        var sibling = Path.Combine(world.Host.Fixture.Root, "trees", "other-checkout-" + Guid.NewGuid().ToString("N"));
        world.Host.Fixture.Git.AfterCommand = async (_, arguments, result) =>
        {
            if (!result.Succeeded || !arguments.Contains("worktree") || !arguments.Contains("remove")) return;
            await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "worktree", "add", sibling, branch);
        };
        world.Host.Fixture.Git.Trace.Clear();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(sibling).ShouldBeTrue();
        (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "show-ref", "--exists",
            "refs/heads/" + branch)).ShouldNotBeNull();
    }

    [Test]
    public async Task C478_G113_NoForce()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        world.Host.Fixture.Git.BeforeCommand = (_, arguments) =>
            arguments.Contains("remove")
                ? Task.FromResult<LandingGitResult?>(new LandingGitResult(128, "", "injected remove failure"))
                : Task.FromResult<LandingGitResult?>(null);
        world.Host.Fixture.Git.Trace.Clear();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldNotBeNull();
        Directory.Exists(path).ShouldBeTrue();
        world.Host.Fixture.Git.Trace.Any(a => a.Contains("remove")).ShouldBeTrue();
        world.Host.Fixture.Git.Trace.Any(a => a.Contains("--force")).ShouldBeFalse();
        File.Exists(await world.EvidencePathAsync()).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G114_ReadFailure()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        world.Host.Fixture.Git.BeforeCommand = (_, arguments) =>
            arguments.Contains("status")
                ? Task.FromResult<LandingGitResult?>(new LandingGitResult(128, "", "injected status failure"))
                : Task.FromResult<LandingGitResult?>(null);
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G115_CleanupRecovery()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var sentinel = Path.Combine(path, "replacement-after-remove.txt");
        world.Host.Fixture.Git.AfterCommand = async (_, arguments, result) =>
        {
            if (!result.Succeeded || !arguments.Contains("worktree") || !arguments.Contains("remove")) return;
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(sentinel, "replacement");
        };
        world.Host.Fixture.Git.Trace.Clear();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<VerificationCleanupService>();
        (await service.CleanupAsync(world.TaskId, default)).IsClean.ShouldBeFalse();
        File.Exists(sentinel).ShouldBeTrue();
        (await service.CleanupAsync(world.TaskId, default)).IsClean.ShouldBeFalse();
        File.Exists(sentinel).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G116_ResidueVerdict()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await File.WriteAllTextAsync(Path.Combine(await world.PathAsync(), "unknown.txt"), "keep");
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldNotBeNull();
        await using var observer = world.Host.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Succeeded);
        task.Result.ShouldBe("fixture result");
        var op = await observer.AgentTaskLandings.SingleAsync(o => o.Id == world.Operation);
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
    }

    [Test]
    public async Task C478_G117_Workspace()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using (var db = world.Host.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Workspace = WorkspaceMode.Shared;
            await db.SaveChangesAsync();
        }
        await RefuseCleanupAsync(world, await world.PathAsync(), expectConflict: true);
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
        await RefuseCleanupAsync(world, await world.PathAsync(), expectConflict: true);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("unknown")]
    [Arguments("unsupported")]
    public async Task C478_G119_UnknownChildren(string variant)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        if (variant == "missing")
        {
            await using (var db = world.Host.CreateContext())
            {
                db.VerificationExecutions.Remove(await db.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId));
                await db.SaveChangesAsync();
            }
        }
        else if (world.Runner is FakeSessionRunnerClient fake)
        {
            fake.VerificationCustody = (b, _, _) => Task.FromResult(variant == "unknown"
                ? new VerificationCustodyStatus(b, VerificationCustodyState.Unknown, "observation_failed")
                : new VerificationCustodyStatus(b, VerificationCustodyState.UnsupportedBackend,
                    "verification_custody_unsupported_backend"));
        }
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        var result = await RefuseCleanupAsync(world, await world.PathAsync());
        result.Residue.ShouldNotBeNull();
    }

    [Test]
    public async Task C478_G120_ProcessIdentity()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        var result = await RefuseCleanupAsync(world, await world.PathAsync());
        result.Residue.ShouldNotBeNull();
        Process.GetProcessById(Environment.ProcessId).HasExited.ShouldBeFalse();
    }

    [Test]
    public async Task C478_G121_EvidenceScope()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.EvidencePathAsync();
        var restoration = JsonSerializer.Deserialize<VerificationRestoration>(await File.ReadAllBytesAsync(path),
            PostLandMutationWorld.WebJson)!;
        restoration = restoration with { Source = restoration.Source with { SourceOperationId = Guid.NewGuid() } };
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(restoration, PostLandMutationWorld.WebJson));
        await RefuseCleanupAsync(world, await world.PathAsync());
    }

    [Test]
    public async Task C478_G122_FinalAuthority()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var inspections = 0;
        world.Host.Fixture.Git.BeforeObservedCommand = async arguments =>
        {
            if (!arguments.Contains("status")) return;
            inspections++;
            if (inspections < 2) return;
            await using var db = world.Host.CreateContext();
            (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Status = AgentTaskStatus.Working;
            await db.SaveChangesAsync();
        };
        await RefuseCleanupAsync(world, path);
    }

    [Test]
    public async Task C478_G123_BranchSymbolic()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var path = await world.PathAsync();
        var branch = "refs/heads/feat/card-task-" + DelegationReportFormatter.Short(world.TaskId);
        var master = (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "rev-parse", "refs/heads/master")).Trim();
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "symbolic-ref", branch, "refs/heads/master");
        await RefuseCleanupAsync(world, path);
        (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "rev-parse", "refs/heads/master")).Trim()
            .ShouldBe(master);
    }

    private static async Task<WorktreeRemoval> RefuseCleanupAsync(PostLandMutationWorld world, string path,
        bool expectConflict = false)
    {
        world.Host.Fixture.Git.Trace.Clear();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<VerificationCleanupService>();
        if (expectConflict)
        {
            await Should.ThrowAsync<ConflictException>(() => service.CleanupAsync(world.TaskId, default));
            Directory.Exists(path).ShouldBeTrue();
            world.Host.Fixture.Git.Trace.Any(PostLandMutationWorld.IsDestructive).ShouldBeFalse();
            return new(false, false, false, "conflict");
        }
        var result = await service.CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(path).ShouldBeTrue();
        world.Host.Fixture.Git.Trace.Any(PostLandMutationWorld.IsDestructive).ShouldBeFalse();
        return result;
    }

    private static async Task<VerificationCreationCoordinates> CreationAsync(PostLandMutationWorld world)
    {
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        return JsonSerializer.Deserialize<VerificationCreationCoordinates>(task.VerificationCreationJson!)!;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination), overwrite: true);
    }
}
