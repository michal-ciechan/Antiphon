using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0331 S1: a land request is a row. Isolated schema, no git — the columns, the
/// in-process active set, and the honest 409 are the thing under test.
/// </summary>
[Category("Integration")]
public class AgentTaskLandRequestTests
{
    [Test]
    public async Task request_sets_columns_writes_the_event_and_enqueues()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var clock = Frozen(new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc));
        var land = CreateLand(db, queue, clock);
        var task = await SeedSucceededWorktreeAsync(db);

        var result = await land.RequestAsync(task.Id, " /*/Antiphon.Tests.Application/*/* ", CancellationToken.None);

        result.Status.ShouldBe("queued");
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
        stored.LandVerifyFilter.ShouldBe("/*/Antiphon.Tests.Application/*/*");
        stored.LandStartedAt.ShouldBeNull();
        stored.LandAttempt.ShouldBe(0);
        queue.IsActive(task.Id).ShouldBeTrue();
        queue.PendingCount.ShouldBe(1);
        queue.TryDequeue(out var request).ShouldBeTrue();
        request.TaskId.ShouldBe(task.Id);
        request.VerifyFilter.ShouldBe(stored.LandVerifyFilter);
        var requested = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRequested);
        requested.Detail.ShouldContain("/*/Antiphon.Tests.Application/*/*");
    }

    [Test]
    public async Task a_second_request_while_active_is_409_naming_running_and_the_requested_time()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var clock = Frozen(new DateTime(2026, 9, 3, 12, 30, 0, DateTimeKind.Utc));
        var land = CreateLand(db, queue, clock);
        var task = await SeedSucceededWorktreeAsync(db);

        await land.RequestAsync(task.Id, null, CancellationToken.None);

        var error = await Should.ThrowAsync<ConflictException>(
            () => land.RequestAsync(task.Id, null, CancellationToken.None));
        error.Message.ShouldContain("running");
        error.Message.ShouldContain(clock.GetUtcNow().UtcDateTime.ToString("u"));
        error.Message.ShouldContain("queued");
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRequested)).ShouldBe(1);
    }

    [Test]
    public async Task a_pending_row_that_is_not_active_is_requeued_with_a_warning()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var clock = Frozen(new DateTime(2026, 9, 3, 13, 0, 0, DateTimeKind.Utc));
        var land = CreateLand(db, queue, clock);
        var previous = new DateTime(2026, 9, 3, 1, 3, 21, DateTimeKind.Utc);
        var task = await SeedSucceededWorktreeAsync(db, requestedAt: previous, attempt: 2);

        var result = await land.RequestAsync(task.Id, null, CancellationToken.None);

        result.Status.ShouldBe("requeued");
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
        stored.LandAttempt.ShouldBe(0);
        stored.LandStartedAt.ShouldBeNull();
        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain(previous.ToString("u"));
        warning.Detail.ShouldContain("was not running");
        queue.IsActive(task.Id).ShouldBeTrue();
    }

    [Test]
    public async Task a_request_after_landed_queues_again_and_resets_the_attempt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue, Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db, attempt: 1);
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Landed,
            Detail = "landed",
            At = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();

        var result = await land.RequestAsync(task.Id, null, CancellationToken.None);

        result.Status.ShouldBe("queued");
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldNotBeNull();
        stored.LandAttempt.ShouldBe(0);
        queue.IsActive(task.Id).ShouldBeTrue();
    }

    [Test]
    public void try_enqueue_twice_returns_false_the_second_time()
    {
        var queue = new AgentTaskLandQueue();
        var id = Guid.NewGuid();
        queue.TryEnqueue(id, null).ShouldBeTrue();
        queue.TryEnqueue(id, "later").ShouldBeFalse();
        queue.PendingCount.ShouldBe(1);
        queue.IsActive(id).ShouldBeTrue();
        queue.Release(id);
        queue.IsActive(id).ShouldBeFalse();
        queue.TryEnqueue(id, null).ShouldBeTrue();
    }

    [Test]
    public async Task run_async_with_a_null_column_is_a_noop()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);

        var result = await land.RunAsync(task.Id, null, CancellationToken.None);

        result.ShouldBe(LandRunResult.Complete);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id)).ShouldBe(0);
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldBeNull();
        stored.LandAttempt.ShouldBe(0);
        stored.Status.ShouldBe(AgentTaskStatus.Succeeded);
    }

    [Test]
    public async Task fail_async_writes_land_failed_and_clears_the_pending_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db, requestedAt: DateTime.UtcNow, attempt: 1);

        await land.FailAsync(task.Id, new InvalidOperationException("git launch failed"), CancellationToken.None);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldBeNull();
        stored.LandStartedAt.ShouldBeNull();
        stored.LandAttempt.ShouldBe(1);
        var refused = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRefused);
        refused.Detail.ShouldStartWith("land failed:");
        refused.Detail.ShouldContain("git launch failed");
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning)).ShouldBe(1);
    }

    private static async Task<AgentTask> SeedSucceededWorktreeAsync(
        AppDbContext db, DateTime? requestedAt = null, int attempt = 0)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "land request task",
            Goal = "Land me.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "C:/tmp/land-request",
            RepoPath = "C:/tmp/land-request",
            WorktreePath = "C:/tmp/land-request-tree",
            WorktreeBranch = $"feat/card-task-{DelegationReportFormatter.Short(id)}",
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            LandRequestedAt = requestedAt,
            LandAttempt = attempt,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskLandService CreateLand(AppDbContext db, AgentTaskLandQueue queue, TimeProvider clock)
    {
        var manager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = Path.GetTempPath(),
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }),
            clock,
            NullLogger<WorktreeManager>.Instance);
        var worktrees = new DelegationWorktreeService(
            manager,
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        var tasks = new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            clock,
            NullLogger<AgentTaskService>.Instance);
        return new AgentTaskLandService(
            db,
            worktrees,
            tasks,
            queue,
            null!,
            new MockEventBus(),
            clock,
            Options.Create(new DelegationSettings()),
            NullLogger<AgentTaskLandService>.Instance);
    }

    private static FakeTimeProvider Frozen(DateTime utc) =>
        new(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
