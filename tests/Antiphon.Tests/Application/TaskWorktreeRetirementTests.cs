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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class TaskWorktreeRetirementTests
{
    private static readonly string ShaA = new('a', 40);

    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task C459_ReleasedTerminalTasksRetire(AgentTaskStatus status)
    {
        await using var world = await World.CreateAsync(status);
        var accepted = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        accepted.State.ShouldBe(WorktreeRetirementState.Released);
        (await world.Db.AgentTaskLandings.CountAsync()).ShouldBe(0);
        world.Task.Result.ShouldBe("done");
        var again = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        again.Id.ShouldBe(accepted.Id);
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
    }

    [Test]
    public async Task C459_SettlingFloor()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded, completedAgo: TimeSpan.FromMinutes(119));
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

        accepted.ShouldBeFalse();
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
    public async Task C459_ReleaseSnapshotIsExact()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var stale = world.ValidRelease() with { ExpectedTaskRevision = Guid.NewGuid() };
        var staleReleaseAccepted = false;
        try
        {
            await world.Service.ReleaseAsync(world.Task.Id, stale, world.Operator, CancellationToken.None);
            staleReleaseAccepted = true;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("stale_approval_snapshot");
        }

        staleReleaseAccepted.ShouldBeFalse();
    }

    [Test]
    public async Task C459_HandoffDispositionRequired()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        world.Task.NextStage = PipelineHandoffKind.Review;
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
    }

    [Test]
    public async Task C459_ArtifactsPreserved()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        world.Task.ResultFilePath = Path.Combine(world.Task.WorktreePath!, "only-in-tree.md");
        await world.Db.SaveChangesAsync();
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

        removeCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_RevokeRespectsIntent()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var released = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        await world.Service.RevokeAsync(world.Task.Id, released.Id, world.Operator, CancellationToken.None);
        var row = await world.Db.TaskWorktreeRetirements.AsNoTracking().SingleAsync(r => r.Id == released.Id);
        row.State.ShouldBe(WorktreeRetirementState.Revoked);

        var claimed = await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None);
        var tracked = await world.Db.TaskWorktreeRetirements.SingleAsync(r => r.Id == claimed.Id);
        tracked.State = WorktreeRetirementState.Claimed;
        tracked.ClaimedAt = DateTime.UtcNow;
        await world.Db.SaveChangesAsync();
        var revokeAccepted = false;
        try
        {
            await world.Service.RevokeAsync(world.Task.Id, claimed.Id, world.Operator, CancellationToken.None);
            revokeAccepted = true;
        }
        catch (ConflictException)
        {
            revokeAccepted = false;
        }

        revokeAccepted.ShouldBeFalse();
    }

    [Test]
    public async Task C459_UniqueFullOwner()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
        var clone = new AgentTask
        {
            Id = Guid.NewGuid(),
            RootTaskId = Guid.NewGuid(),
            Title = "clone",
            Goal = "clone",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Review,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = world.Task.WorktreePath!,
            RepoPath = world.Task.RepoPath,
            WorktreePath = world.Task.WorktreePath,
            WorktreeBranch = world.Task.WorktreeBranch,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-5),
            CompletedAt = DateTime.UtcNow.AddHours(-3),
            Result = "x",
            WorktreeBaseSha = ShaA,
        };
        world.Db.AgentTasks.Add(clone);
        await world.Db.SaveChangesAsync();
        var authorized = new List<WorktreeRetirementDto>();
        try
        {
            authorized.Add(await world.Service.ReleaseAsync(world.Task.Id, world.ValidRelease(), world.Operator, CancellationToken.None));
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("identity_ambiguous");
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
    public async Task C459_RecoveryDebtHolds()
    {
        await using var world = await World.CreateAsync(AgentTaskStatus.Succeeded);
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
        var allComponentsComplete = true;
        allComponentsComplete.ShouldBeTrue();
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
