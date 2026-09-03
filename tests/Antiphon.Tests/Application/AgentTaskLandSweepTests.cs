using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0331 S2: the sweep re-enqueues pending lands this process does not hold.
/// Isolated schema, fake clock, real queue — no git.
/// </summary>
[Category("Integration")]
public class AgentTaskLandSweepTests
{
    [Test]
    public async Task a_pending_row_that_is_not_active_is_enqueued_with_no_event()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue);
        var task = await SeedPendingAsync(db);

        await land.SweepAsync(CancellationToken.None);

        queue.IsActive(task.Id).ShouldBeTrue();
        queue.PendingCount.ShouldBe(1);
        queue.TryDequeue(out var request).ShouldBeTrue();
        request.TaskId.ShouldBe(task.Id);
        request.VerifyFilter.ShouldBe("/*/Antiphon.Tests.Application/*/*");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id)).ShouldBe(0);
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldNotBeNull();
        stored.LandStartedAt.ShouldBeNull();
    }

    [Test]
    public async Task an_interrupted_attempt_warns_once_nulls_started_at_and_enqueues_once_across_two_passes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var started = new DateTime(2026, 9, 3, 11, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var land = CreateLand(db, queue, clock);
        var task = await SeedPendingAsync(db, startedAt: started, attempt: 1);

        await land.SweepAsync(CancellationToken.None);
        await land.SweepAsync(CancellationToken.None);

        queue.PendingCount.ShouldBe(1);
        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("Land attempt 1");
        warning.Detail.ShouldContain(started.ToString("u"));
        warning.Detail.ShouldContain("did not finish (server restarted); re-running.");
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandStartedAt.ShouldBeNull();
        stored.LandRequestedAt.ShouldNotBeNull();
        stored.LandAttempt.ShouldBe(1);
    }

    [Test]
    public async Task a_pending_row_that_is_already_active_is_skipped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue);
        var task = await SeedPendingAsync(db);
        queue.TryEnqueue(task.Id, "already").ShouldBeTrue();

        await land.SweepAsync(CancellationToken.None);

        queue.PendingCount.ShouldBe(1);
        queue.TryDequeue(out var request).ShouldBeTrue();
        request.VerifyFilter.ShouldBe("already");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id)).ShouldBe(0);
    }

    [Test]
    public async Task a_blocked_pending_row_is_skipped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue);
        var task = await SeedPendingAsync(db, status: AgentTaskStatus.Blocked);

        await land.SweepAsync(CancellationToken.None);

        queue.PendingCount.ShouldBe(0);
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldNotBeNull();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id)).ShouldBe(0);
    }

    [Test]
    public async Task three_interrupted_attempts_refuse_and_do_not_enqueue()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var started = new DateTime(2026, 9, 3, 11, 0, 0, DateTimeKind.Utc);
        var land = CreateLand(db, queue, settings: new DelegationSettings { LandMaxAttempts = 3 });
        var task = await SeedPendingAsync(db, startedAt: started, attempt: 3);

        await land.SweepAsync(CancellationToken.None);

        queue.PendingCount.ShouldBe(0);
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldBeNull();
        stored.LandStartedAt.ShouldBeNull();
        stored.LandAttempt.ShouldBe(3);
        var refused = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRefused);
        refused.Detail.ShouldContain("3 times");
        refused.Detail.ShouldContain(started.ToString("u"));
    }

    [Test]
    public async Task a_canceled_row_with_the_column_set_is_cleared_and_not_enqueued()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue);
        var task = await SeedPendingAsync(db, status: AgentTaskStatus.Canceled);

        await land.SweepAsync(CancellationToken.None);

        queue.PendingCount.ShouldBe(0);
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldBeNull();
        stored.LandVerifyFilter.ShouldBeNull();
        stored.LandStartedAt.ShouldBeNull();
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(0);
    }

    [Test]
    public async Task drain_releases_the_id_when_the_scoped_service_throws()
    {
        var queue = new AgentTaskLandQueue();
        var id = Guid.NewGuid();
        queue.TryEnqueue(id, null).ShouldBeTrue();
        var hosted = new AgentTaskLandHostedService(
            queue, new ThrowingScopeFactory(), NullLogger<AgentTaskLandHostedService>.Instance);
        using var cts = new CancellationTokenSource();
        await hosted.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (queue.IsActive(id) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        queue.IsActive(id).ShouldBeFalse();
        await hosted.StopAsync(CancellationToken.None);
        cts.Cancel();
    }

    private static async Task<AgentTask> SeedPendingAsync(
        AppDbContext db,
        AgentTaskStatus status = AgentTaskStatus.Succeeded,
        DateTime? startedAt = null,
        int attempt = 0)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "land sweep task",
            Goal = "Land me.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "C:/tmp/land-sweep",
            RepoPath = "C:/tmp/land-sweep",
            WorktreePath = "C:/tmp/land-sweep-tree",
            WorktreeBranch = $"feat/card-task-{DelegationReportFormatter.Short(id)}",
            Status = status,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            LandRequestedAt = DateTime.UtcNow.AddMinutes(-2),
            LandVerifyFilter = "/*/Antiphon.Tests.Application/*/*",
            LandStartedAt = startedAt,
            LandAttempt = attempt,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskLandService CreateLand(
        AppDbContext db,
        AgentTaskLandQueue queue,
        TimeProvider? clock = null,
        DelegationSettings? settings = null)
    {
        clock ??= TimeProvider.System;
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
            Options.Create(settings ?? new DelegationSettings()),
            NullLogger<AgentTaskLandService>.Instance);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new ThrowingScope();

        private sealed class ThrowingScope : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) =>
                throw new InvalidOperationException("scope down");
            public void Dispose() { }
        }
    }
}
