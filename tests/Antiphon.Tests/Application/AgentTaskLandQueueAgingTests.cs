using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandQueueAgingTests
{
    private const string SameRepository = @"C:\antiphon-land\repo";
    private const string OtherRepository = @"C:\antiphon-land\other";

    [Test]
    [Timeout(120_000)]
    public async Task C641_Queued_aged_note_names_running_holder_and_first_position()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = new DateTime(2026, 9, 10, 0, 6, 0, DateTimeKind.Utc);
        var requested = now.AddMinutes(-6);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var holder = await AddRequestAsync(db, requested, now, LandRequestState.Running, SameRepository, AgentTaskStatus.Working);
        var waiting = await AddRequestAsync(db, requested, requested, LandRequestState.Queued, SameRepository, AgentTaskStatus.Succeeded);
        await db.SaveChangesAsync();

        var queue = new AgentTaskLandQueue();
        queue.TryEnqueue(holder.TaskId, null, holder.RequestId).ShouldBeTrue();
        queue.TryDequeue(out _).ShouldBeTrue();
        queue.TryEnqueue(waiting.TaskId, null, waiting.RequestId).ShouldBeTrue();

        var body = await SweepAgedBodyAsync(schema.ConnectionString, queue, now, waiting.RequestId);
        body.ShouldContain($"holder={holder.TaskId:N} request={holder.RequestId:N} status=Working state=Running");
        body.ShouldContain("queue position=1;");
        body.ShouldContain("waiting=1;");
        body.ShouldContain($"request={waiting.RequestId:N}");
        body.ShouldContain($"requested {requested:O}");
        body.ShouldContain("reason=none");
        body.ShouldNotContain("holder= ()");
        body.ShouldNotContain("queue blocker");
        body.ShouldNotContain("lease");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C641_Queued_position_counts_predecessors_and_advances_after_release()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var start = new DateTime(2026, 9, 10, 1, 0, 0, DateTimeKind.Utc);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var running = await AddRequestAsync(db, start, start, LandRequestState.Running, SameRepository, AgentTaskStatus.Working);
        var first = await AddRequestAsync(db, start.AddMinutes(-6), start.AddMinutes(-6), LandRequestState.Queued, SameRepository, AgentTaskStatus.Succeeded);
        var second = await AddRequestAsync(db, start, start, LandRequestState.Queued, SameRepository, AgentTaskStatus.Succeeded);
        await db.SaveChangesAsync();

        var queue = new AgentTaskLandQueue();
        queue.TryEnqueue(running.TaskId, null, running.RequestId).ShouldBeTrue();
        queue.TryEnqueue(first.TaskId, null, first.RequestId).ShouldBeTrue();
        queue.TryEnqueue(second.TaskId, null, second.RequestId).ShouldBeTrue();
        queue.TryDequeue(out var executing).ShouldBeTrue();
        executing.RequestId.ShouldBe(running.RequestId);
        queue.Capture(start).WaitingPosition(first.TaskId, first.RequestId).ShouldBe(1);
        queue.Capture(start).WaitingPosition(second.TaskId, second.RequestId).ShouldBe(2);

        var firstBody = await SweepAgedBodyAsync(schema.ConnectionString, queue, start, first.RequestId);
        firstBody.ShouldContain("queue position=1;");
        firstBody.ShouldContain($"holder={running.TaskId:N} request={running.RequestId:N}");
        (await AgedCountAsync(schema.ConnectionString, second.RequestId)).ShouldBe(0);

        queue.Release(running.TaskId);
        queue.TryDequeue(out var advanced).ShouldBeTrue();
        advanced.RequestId.ShouldBe(first.RequestId);
        var later = start.AddMinutes(6);
        queue.Capture(later).WaitingPosition(second.TaskId, second.RequestId).ShouldBe(1);
        queue.Capture(later).IsExecuting(first.TaskId, first.RequestId).ShouldBeTrue();

        var secondBody = await SweepAgedBodyAsync(schema.ConnectionString, queue, later, second.RequestId);
        secondBody.ShouldContain("queue position=1;");
        secondBody.ShouldNotContain("queue position=2;");
        secondBody.ShouldContain($"holder={first.TaskId:N} request={first.RequestId:N} status=Succeeded state=Queued");
        var preserved = await AgedBodyAsync(schema.ConnectionString, first.RequestId);
        preserved.ShouldContain("queue position=1;");
        preserved.ShouldContain($"holder={running.TaskId:N}");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C641_Different_repository_predecessor_is_queue_blocker_not_lease_owner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = new DateTime(2026, 9, 10, 2, 6, 0, DateTimeKind.Utc);
        var requested = now.AddMinutes(-6);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var predecessor = await AddRequestAsync(db, requested, now, LandRequestState.Running, OtherRepository, AgentTaskStatus.Working);
        var waiting = await AddRequestAsync(db, requested, requested, LandRequestState.Queued, SameRepository, AgentTaskStatus.Succeeded);
        await db.SaveChangesAsync();

        var queue = new AgentTaskLandQueue();
        queue.TryEnqueue(predecessor.TaskId, null, predecessor.RequestId).ShouldBeTrue();
        queue.TryDequeue(out _).ShouldBeTrue();
        queue.TryEnqueue(waiting.TaskId, null, waiting.RequestId).ShouldBeTrue();

        var body = await SweepAgedBodyAsync(schema.ConnectionString, queue, now, waiting.RequestId);
        body.ShouldContain($"queue blocker={predecessor.TaskId:N} request={predecessor.RequestId:N} status=Working state=Running");
        body.ShouldContain("queue position=1;");
        body.ShouldNotContain($"holder={predecessor.TaskId:N}");
        body.ShouldNotContain("holder=");
        body.ShouldNotContain("lease");
        body.ShouldNotContain("repository owner");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C641_Unreplayed_request_reports_unknown_position_without_blank_holder()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = new DateTime(2026, 9, 10, 3, 6, 0, DateTimeKind.Utc);
        var requested = now.AddMinutes(-6);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stale = await AddRequestAsync(db, requested, now, LandRequestState.Running, SameRepository, AgentTaskStatus.Working);
        var waiting = await AddRequestAsync(db, requested, requested, LandRequestState.Queued, SameRepository, AgentTaskStatus.Succeeded);
        await db.SaveChangesAsync();

        var restarted = new AgentTaskLandQueue();
        var body = await SweepAgedBodyAsync(schema.ConnectionString, restarted, now, waiting.RequestId);
        body.ShouldContain("queue position=unknown (awaiting replay)");
        body.ShouldContain("holder=unknown");
        body.ShouldNotContain("holder= ()");
        body.ShouldNotContain("holder=()");
        body.ShouldNotContain(stale.TaskId.ToString("N"));
        body.ShouldNotContain(stale.RequestId.ToString("N"));
        body.ShouldNotContain("queue blocker");
        (await AgedCountAsync(schema.ConnectionString, stale.RequestId)).ShouldBe(0);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C641_Queue_observation_does_not_reset_age_or_attempts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var progress = new DateTime(2026, 9, 10, 4, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(progress);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var holder = await AddRequestAsync(db, progress, progress, LandRequestState.Running, SameRepository, AgentTaskStatus.Working);
        var waiting = await AddRequestAsync(db, progress, progress, LandRequestState.Queued, SameRepository, AgentTaskStatus.Succeeded, attempt: 4);
        await db.SaveChangesAsync();

        var queue = new AgentTaskLandQueue();
        queue.TryEnqueue(holder.TaskId, null, holder.RequestId).ShouldBeTrue();
        queue.TryDequeue(out _).ShouldBeTrue();
        queue.TryEnqueue(waiting.TaskId, null, waiting.RequestId).ShouldBeTrue();

        var monitor = new AgentTaskLandMonitorService(db, clock, Options.Create(new DelegationSettings()), new MockEventBus(), queue);
        clock.Advance(TimeSpan.FromSeconds(299.999));
        await monitor.SweepAsync(CancellationToken.None);
        (await AgedCountAsync(schema.ConnectionString, waiting.RequestId)).ShouldBe(0);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await monitor.SweepAsync(CancellationToken.None);
        (await AgedCountAsync(schema.ConnectionString, waiting.RequestId)).ShouldBe(1);
        await monitor.SweepAsync(CancellationToken.None);
        (await AgedCountAsync(schema.ConnectionString, waiting.RequestId)).ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(599.999));
        await monitor.SweepAsync(CancellationToken.None);
        (await AgedCountAsync(schema.ConnectionString, waiting.RequestId)).ShouldBe(1);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await monitor.SweepAsync(CancellationToken.None);
        (await AgedCountAsync(schema.ConnectionString, waiting.RequestId)).ShouldBe(2);
        await monitor.SweepAsync(CancellationToken.None);
        (await AgedCountAsync(schema.ConnectionString, waiting.RequestId)).ShouldBe(2);

        await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var saved = await fresh.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == waiting.RequestId);
        saved.Attempt.ShouldBe(4);
        saved.LastProgressAt.ShouldBe(progress);
        saved.HoldingTaskId.ShouldBeNull();
        saved.HoldReasonCode.ShouldBeNull();
        var notes = await fresh.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.RequestId == waiting.RequestId && n.Kind == LandNotificationKind.Aged)
            .OrderBy(n => n.CreatedAt).ToListAsync();
        notes.Count.ShouldBe(2);
        notes[0].Body.ShouldContain("Warning:");
        notes[1].Body.ShouldContain("Error:");
        foreach (var note in notes)
        {
            note.Body.ShouldContain("queue position=1;");
            note.Body.ShouldContain($"holder={holder.TaskId:N}");
            note.Body.ShouldContain("attempt=4;");
        }
    }

    [Test]
    [Timeout(180_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C641_Queue_aged_note_reaches_busy_and_idle_caller(bool busy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var caller = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
        });
        var now = new DateTime(2026, 9, 10, 5, 6, 0, DateTimeKind.Utc);
        var requested = now.AddMinutes(-6);
        Guid noteId;
        string body;
        var queue = new AgentTaskLandQueue();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var holder = await AddRequestAsync(db, requested, now, LandRequestState.Running, SameRepository, AgentTaskStatus.Working);
            var waiting = await AddRequestAsync(db, requested, requested, LandRequestState.Queued, SameRepository, AgentTaskStatus.Succeeded);
            await db.SaveChangesAsync();
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == waiting.RequestId);
            request.ReplyTo = AgentTaskReplyTo.Session;
            request.ParentSessionId = caller.SessionId;
            await db.SaveChangesAsync();
            queue.TryEnqueue(holder.TaskId, null, holder.RequestId).ShouldBeTrue();
            queue.TryDequeue(out _).ShouldBeTrue();
            queue.TryEnqueue(waiting.TaskId, null, waiting.RequestId).ShouldBeTrue();
            await new AgentTaskLandMonitorService(db, new FakeTimeProvider(now), Options.Create(new DelegationSettings()), new MockEventBus(), queue)
                .SweepAsync(CancellationToken.None);
            var note = await db.AgentTaskLandNotifications.AsNoTracking()
                .SingleAsync(n => n.RequestId == waiting.RequestId && n.Kind == LandNotificationKind.Aged);
            noteId = note.Id;
            body = note.Body;
            body.ShouldContain("queue position=1;");
            body.ShouldContain($"holder={holder.TaskId:N}");
        }

        if (busy)
            await caller.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "caller is mid-turn");

        var flushes = new CompletionNoteFlushQueue();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            await new AgentTaskLandNotificationService(db, caller.Queue, flushes, caller.Runtime, TimeProvider.System)
                .ReconcileAsync(noteId, CancellationToken.None);
        }

        var woke = await PumpFlushAsync(flushes, caller);
        woke.ShouldBeTrue("eligible caller wakeup was not enqueued");
        if (busy)
        {
            caller.Adapter.SubmittedBodies.ShouldBeEmpty();
            await caller.Queue.OnTurnEndAsync(caller.SessionId, CancellationToken.None);
        }

        var submitted = caller.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        PromptSubmissionMatch.IsCompleteIn(body, submitted).ShouldBeTrue();

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            await new AgentTaskLandNotificationService(db, caller.Queue, flushes, caller.Runtime, TimeProvider.System)
                .ReconcileAsync(noteId, CancellationToken.None);
            var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId);
            saved.State.ShouldBe(LandNotificationState.Confirmed);
            saved.LastErrorCode.ShouldBeNull();
            var prompts = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == caller.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
                .ToListAsync();
            var match = prompts.Where(p => PromptSubmissionMatch.IsCompleteIn(body, p.Text!)).ShouldHaveSingleItem();
            saved.ConfirmingPromptSequence.ShouldBe(match.Sequence);
            (await db.AgentTaskLandNotifications.CountAsync(n => n.Kind == LandNotificationKind.Aged)).ShouldBe(1);
        }
    }

    private static async Task<bool> PumpFlushAsync(CompletionNoteFlushQueue flushes, BridgeQueueHarness caller)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var id in flushes.ReadAllAsync(timeout.Token))
            {
                try
                {
                    if (id != caller.SessionId)
                        continue;
                    await caller.Queue.FlushIfIdleAsync(id, CancellationToken.None);
                    return true;
                }
                finally
                {
                    flushes.Complete(id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return false;
    }

    private static async Task<string> SweepAgedBodyAsync(string connectionString, AgentTaskLandQueue queue, DateTime now, Guid requestId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        await new AgentTaskLandMonitorService(db, new FakeTimeProvider(now), Options.Create(new DelegationSettings()), new MockEventBus(), queue)
            .SweepAsync(CancellationToken.None);
        return await AgedBodyAsync(connectionString, requestId);
    }

    private static async Task<string> AgedBodyAsync(string connectionString, Guid requestId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var note = await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.RequestId == requestId && n.Kind == LandNotificationKind.Aged)
            .OrderByDescending(n => n.CreatedAt)
            .SingleAsync();
        return note.Body;
    }

    private static async Task<int> AgedCountAsync(string connectionString, Guid requestId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        return await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == requestId && n.Kind == LandNotificationKind.Aged);
    }

    private static Task<Seeded> AddRequestAsync(AppDbContext db, DateTime requested, DateTime progress,
        LandRequestState state, string repository, AgentTaskStatus status, int attempt = 0)
    {
        var taskId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "C641 queue",
            Goal = "queue fixture",
            WorkingDirectory = Path.GetTempPath(),
            Status = status,
            CreatedAt = requested,
            LandRequestedAt = requested,
            CurrentLandRequestId = requestId,
            LandAttempt = attempt,
        });
        db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
        {
            Id = requestId,
            TaskId = taskId,
            State = state,
            IsPending = true,
            RequestedAt = requested,
            LastProgressAt = progress,
            LastEvaluatedAt = progress,
            ReplyTo = AgentTaskReplyTo.None,
            Attempt = attempt,
            RepositoryPathSnapshot = repository,
        });
        return Task.FromResult(new Seeded(taskId, requestId));
    }

    private sealed record Seeded(Guid TaskId, Guid RequestId);
}
