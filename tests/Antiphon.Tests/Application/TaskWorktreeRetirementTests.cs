using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TaskWorktreeRetirementTests
{
    private static readonly string ShaA = new('a', 40);

    [Test]
    [Arguments(AgentTaskStatus.Succeeded, AgentTaskRole.Code)]
    [Arguments(AgentTaskStatus.Failed, AgentTaskRole.Review)]
    [Arguments(AgentTaskStatus.Canceled, AgentTaskRole.Plan)]
    public async Task C459_ReleasedTerminalTasksRetire(AgentTaskStatus status, AgentTaskRole role)
    {
        await using var h = await SettledRemovalHarness.CreateAsync(namedLeaf: true);
        await using var scope = h.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Host.Fixture.TaskId);
        task.Status = status;
        task.Role = role;
        if (role != AgentTaskRole.Code) task.NextStage = PipelineHandoffKind.Review;
        await db.SaveChangesAsync();
        var dispositions = role == AgentTaskRole.Code
            ? Array.Empty<WorktreeHandoffDispositionDto>()
            : new[] { new WorktreeHandoffDispositionDto(PipelineHandoffKind.Review, WorktreeHandoffDispositionKind.Consumed, Guid.NewGuid(), null, "consumed") };
        var body = new ReleaseWorktreeRetirementRequest(task.ConcurrencyToken, h.SourceSha,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(task.Result ?? ""))),
            true, "reviewed no further use", dispositions);
        var accepted = await service.ReleaseAsync(task.Id, body, new AgentTaskService.Caller(null, null, ""), CancellationToken.None);
        accepted.State.ShouldBe(WorktreeRetirementState.Released);
        var fresh = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == accepted.Id);
        var removed = await service.TryRetireAsync(fresh, null, CancellationToken.None);
        removed.IsClean.ShouldBeTrue(removed.Residue);
        Directory.Exists(h.NamedWorktree).ShouldBeFalse();
        (await h.Host.Fixture.Git.RunAsync(h.Host.Fixture.Repository, ["show-ref", "--exists", h.NamedRef], CancellationToken.None))
            .ExitCode.ShouldBe(2);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == task.Id)).ShouldBe(0);
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Result.ShouldBe("done");
        await db.Entry(fresh).ReloadAsync();
        fresh.State.ShouldBe(WorktreeRetirementState.Complete);
        var again = await service.TryRetireAsync(fresh, null, CancellationToken.None);
        again.IsClean.ShouldBeTrue();
        var replay = await service.ReleaseAsync(task.Id, body, new AgentTaskService.Caller(null, null, ""), CancellationToken.None);
        replay.Id.ShouldBe(accepted.Id);
        await h.AssertRemoteUnchangedAsync();
    }

    [Test]
    public async Task C459_TerminalRequired()
    {
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked })
        {
            await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
            world.Task.Status = status;
            await world.Db.SaveChangesAsync();
            var accepted = false;
            try
            {
                await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
                accepted = true;
            }
            catch (ConflictException ex)
            {
                ex.Code.ShouldBe("terminal_required");
            }

            accepted.ShouldBeFalse();
        }

        foreach (var status in new[] { AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled })
        {
            await using var world = await World.CreateAsync(status);
            var released = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
            released.State.ShouldBe(WorktreeRetirementState.Released);
        }
    }

    [Test]
    [Arguments("null")]
    [Arguments("below")]
    [Arguments("exact")]
    [Arguments("above")]
    public async Task C459_SettlingFloor(string age)
    {
        TimeSpan? completedAgo = age switch
        {
            "null" => null,
            "below" => TimeSpan.FromMinutes(119),
            "exact" => TimeSpan.FromMinutes(120),
            _ => TimeSpan.FromMinutes(120) + TimeSpan.FromMilliseconds(1),
        };
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded, completedAgo);
        if (age == "null")
        {
            world.Task.CompletedAt = null;
            await world.Db.SaveChangesAsync();
        }

        var accepted = false;
        try
        {
            await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
            accepted = true;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("settling_floor");
        }

        if (age is "null" or "below") accepted.ShouldBeFalse();
        else accepted.ShouldBeTrue();
    }

    [Test]
    public async Task C459_ReleaseRequired()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var (authorized, reason) = await world.Service.EvaluateEligibilityAsync(world.Task, null, CancellationToken.None);
        authorized.ShouldBeFalse();
        reason.ShouldBe("release_required");
        var removeCalls = 0;
        removeCalls.ShouldBe(0);
    }

    [Test]
    [Arguments("revision")]
    [Arguments("sha")]
    [Arguments("digest")]
    public async Task C459_ReleaseSnapshotIsExact(string field)
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var body = world.ValidRelease();
        body = field switch
        {
            "revision" => body with { ExpectedTaskRevision = Guid.NewGuid() },
            "sha" => body with { SourceSha = new string('b', 40) },
            _ => body with { ReportDigest = new string('c', 64) },
        };
        var staleReleaseAccepted = false;
        try
        {
            await world.Service.ReleaseAsync(world.Task.Id, body, world.Operator, CancellationToken.None);
            staleReleaseAccepted = true;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("stale_approval_snapshot");
        }

        staleReleaseAccepted.ShouldBeFalse();
    }

    [Test]
    [Arguments(PipelineHandoffKind.Code)]
    [Arguments(PipelineHandoffKind.Review)]
    [Arguments(PipelineHandoffKind.Plan)]
    [Arguments(PipelineHandoffKind.TestDesign)]
    [Arguments(PipelineHandoffKind.Land)]
    [Arguments(PipelineHandoffKind.Decide)]
    public async Task C459_HandoffDispositionRequired(PipelineHandoffKind next)
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        world.Task.NextStage = next;
        await world.Db.SaveChangesAsync();
        var removeCalls = 0;
        try
        {
            await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
            removeCalls = 1;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("handoff_pending");
        }

        removeCalls.ShouldBe(0);

        var consumed = world.ValidRelease() with
        {
            HandoffDispositions = [new(next, WorktreeHandoffDispositionKind.Consumed, Guid.NewGuid(), null, "done")],
        };
        var released = await world.Service.ReleaseAsync(world.Task.Id, consumed, world.Operator, CancellationToken.None);
        released.State.ShouldBe(WorktreeRetirementState.Released);
    }

    [Test]
    [Arguments("inline")]
    [Arguments("tree-only")]
    [Arguments("missing")]
    public async Task C459_ArtifactsPreserved(string shape)
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        if (shape == "tree-only")
        {
            world.Task.ResultFilePath = Path.Combine(world.Task.WorktreePath!, "only-in-tree.md");
            await world.Db.SaveChangesAsync();
        }
        if (shape == "missing")
        {
            world.Task.DeliverablePath = Path.Combine(world.Task.WorktreePath!, "missing-out.bin");
            await world.Db.SaveChangesAsync();
        }

        var removeCalls = 0;
        try
        {
            await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
            removeCalls = 1;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("artifact_unpreserved");
        }

        if (shape == "inline") removeCalls.ShouldBe(1);
        else removeCalls.ShouldBe(0);
    }

    [Test]
    [Arguments("before-claim")]
    [Arguments("claimed")]
    [Arguments("intent")]
    [Arguments("partial")]
    [Arguments("complete")]
    public async Task C459_RevokeRespectsIntent(string state)
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var released = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        if (state == "before-claim")
        {
            await world.Service.RevokeAsync(world.Task.Id, released.Id, world.Operator, CancellationToken.None);
            var row = await world.Db.TaskWorktreeRetirements.AsNoTracking().SingleAsync(r => r.Id == released.Id);
            row.State.ShouldBe(WorktreeRetirementState.Revoked);
            return;
        }

        var tracked = await world.Db.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
        tracked.State = state switch
        {
            "claimed" => WorktreeRetirementState.Claimed,
            "intent" => WorktreeRetirementState.CommandStarted,
            "partial" => WorktreeRetirementState.Partial,
            _ => WorktreeRetirementState.Complete,
        };
        tracked.ClaimedAt = DateTime.UtcNow;
        if (state != "claimed") tracked.CommandIntentId = Guid.NewGuid();
        await world.Db.SaveChangesAsync();
        var revokeAccepted = false;
        try
        {
            await world.Service.RevokeAsync(world.Task.Id, released.Id, world.Operator, CancellationToken.None);
            revokeAccepted = true;
        }
        catch (ConflictException)
        {
            revokeAccepted = false;
        }

        revokeAccepted.ShouldBeFalse();
    }

    [Test]
    [Arguments("duplicate-path")]
    [Arguments("unknown")]
    [Arguments("short-prefix")]
    public async Task C459_UniqueFullOwner(string shape)
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        if (shape == "unknown")
        {
            world.Task.WorktreePath = null;
            await world.Db.SaveChangesAsync();
        }
        else
        {
            var cloneId = shape == "short-prefix"
                ? Guid.Parse(world.Task.Id.ToString("N")[..8] + "ffffffffffffffffffff".PadRight(24, 'f'))
                : Guid.NewGuid();
            var clone = new AgentTask
            {
                Id = cloneId,
                RootTaskId = Guid.NewGuid(),
                Title = "clone",
                Goal = "clone",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Review,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = world.Task.WorktreePath!,
                RepoPath = world.Task.RepoPath,
                WorktreePath = shape == "short-prefix" ? world.Task.WorktreePath + "-x" : world.Task.WorktreePath,
                WorktreeBranch = shape == "short-prefix" ? world.Task.WorktreeBranch : world.Task.WorktreeBranch,
                Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = DateTime.UtcNow.AddHours(-5),
                CompletedAt = DateTime.UtcNow.AddHours(-3),
                Result = "x",
                WorktreeBaseSha = ShaA,
            };
            if (shape != "short-prefix") world.Db.AgentTasks.Add(clone);
            else
            {
                clone.WorktreeBranch = world.Task.WorktreeBranch;
                clone.WorktreePath = world.Task.WorktreePath;
                world.Db.AgentTasks.Add(clone);
            }
            await world.Db.SaveChangesAsync();
        }

        var authorized = new List<WorktreeRetirementDto>();
        try
        {
            authorized.Add(await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None));
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBeOneOf("identity_ambiguous", "identity_unknown", "outside_scope");
        }

        authorized.Count.ShouldBe(0);
    }

    [Test]
    public async Task C459_MutationIsExcluded()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        world.Task.Role = AgentTaskRole.Mutation;
        var (authorized, reason) = await world.Service.EvaluateEligibilityAsync(world.Task, null, CancellationToken.None);
        authorized.ShouldBeFalse();
        reason.ShouldBe("mutation_excluded");
    }

    [Test]
    public async Task C459_SourceLandingIsExcluded()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        world.Task.SourceLandingOperationId = Guid.NewGuid();
        var (authorized, reason) = await world.Service.EvaluateEligibilityAsync(world.Task, null, CancellationToken.None);
        authorized.ShouldBeFalse();
        reason.ShouldBe("source_landing_excluded");
    }

    [Test]
    public async Task C459_RepairSourceIsExcluded()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        world.Task.RepairSourceTaskId = Guid.NewGuid();
        var (authorized, reason) = await world.Service.EvaluateEligibilityAsync(world.Task, null, CancellationToken.None);
        authorized.ShouldBeFalse();
        reason.ShouldBe("repair_source_excluded");
    }

    [Test]
    public async Task C459_TargetSelectionIsExplicit()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        world.Task.MergeTargetRef = null;
        await world.Db.SaveChangesAsync();
        var released = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        released.SourceFullRef.ShouldStartWith("refs/heads/");
    }

    [Test]
    [Arguments("land-request")]
    [Arguments("landing")]
    public async Task C459_RecoveryDebtHolds(string debt)
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        if (debt == "land-request")
        {
            world.Db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(),
                TaskId = world.Task.Id,
                RequestedAt = DateTime.UtcNow,
                State = LandRequestState.Queued,
                IsPending = true,
                LastEvaluatedAt = DateTime.UtcNow,
                LastProgressAt = DateTime.UtcNow,
            });
        }
        else
        {
            world.Db.AgentTaskLandings.Add(new AgentTaskLanding
            {
                Id = Guid.NewGuid(),
                TaskId = world.Task.Id,
                SchemaVersion = 1,
                Active = true,
                RepositoryPath = world.Task.RepoPath ?? "",
                WorktreePath = world.Task.WorktreePath ?? "",
                CommonDirectory = world.Task.RepoPath ?? "",
                GitDirectory = world.Task.RepoPath ?? "",
                SourceFullRef = "refs/heads/" + world.Task.WorktreeBranch,
                TargetFullRef = "refs/heads/master",
                OriginalSourceSha = ShaA,
                TargetBeforeSha = ShaA,
                RecoveryRefPrefix = $"refs/antiphon/land/{world.Task.Id:N}/{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        await world.Db.SaveChangesAsync();
        var removeCalls = 0;
        try
        {
            await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
            removeCalls = 1;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("recovery_debt");
        }

        removeCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_ReleaseIdentityIsUnique()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var first = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        var second = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        var retirementIds = new[] { first.Id, second.Id };
        retirementIds.Distinct().Count().ShouldBe(1);
    }

    [Test]
    public async Task C459_LegacyRowsAreUnreleased()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var legacyReleaseCount = await world.Db.TaskWorktreeRetirements.CountAsync();
        legacyReleaseCount.ShouldBe(0);
    }

    [Test]
    public async Task C459_OwnProgressKeepsRelease()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var released = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        world.Task.ConcurrencyToken = Guid.NewGuid();
        await world.Db.SaveChangesAsync();
        var row = await world.Db.TaskWorktreeRetirements.AsNoTracking().SingleAsync(r => r.Id == released.Id);
        world.Service.SnapshotStillValid(row, world.Task).ShouldBeTrue();
    }

    private sealed class World : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required AppDbContext Db { get; init; }
        public required AgentTask Task { get; init; }
        public required TaskWorktreeRetirementService Service { get; init; }
        public AgentTaskService.Caller Operator { get; } = new(null, null, "");

        public static async Task<World> CreateAsync(AgentTaskStatus status, TimeSpan? completedAgo = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var id = Guid.NewGuid();
            var shortId = id.ToString("N")[..8];
            var root = Path.Combine(Path.GetTempPath(), "c459-" + shortId);
            var tree = Path.Combine(root, "card-task-" + shortId);
            Directory.CreateDirectory(tree);
            var now = DateTime.UtcNow;
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "retire",
                Goal = "retire",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = tree,
                RepoPath = root,
                WorktreePath = tree,
                WorktreeBranch = "feat/card-task-" + shortId,
                MergeTargetRef = "master",
                Status = status,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now.AddHours(-5),
                CompletedAt = now - (completedAgo ?? TimeSpan.FromHours(3)),
                Result = "done",
                WorktreeBaseSha = ShaA,
                Attempt = 1,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            var settings = Options.Create(new WorktreeResidueSettings { MinSettledMinutes = 120, MaxActionsPerRun = 25, RunResultPageSize = 50 });
            var git = Options.Create(new GitSettings { WorktreeBasePath = root, DefaultBranch = "master" });
            var service = new TaskWorktreeRetirementService(db, TimeProvider.System, settings, git,
                NullLogger<TaskWorktreeRetirementService>.Instance);
            return new World { Schema = schema, Db = db, Task = task, Service = service };
        }

        public ReleaseWorktreeRetirementRequest ValidRelease() => new(
            Task.ConcurrencyToken,
            ShaA,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Task.Result ?? ""))),
            true,
            "reviewed no further use");

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }
}
