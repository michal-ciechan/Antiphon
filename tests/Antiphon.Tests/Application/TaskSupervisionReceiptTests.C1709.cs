using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class TaskSupervisionReceiptTests
{
    /// <summary>
    /// CARD-1709: one recipient's held queue lock (a delivery stuck before typing) must not stall the
    /// completion recovery pass for every other session. The held recipient's supervision notice and
    /// completion note stay exactly as they were, with no failure bookkeeping, and the next pass
    /// enqueues them once the lock is free.
    /// </summary>
    [Test]
    public async Task C1709_Held_recipient_lock_does_not_stall_the_recovery_pass()
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role: AgentTaskRole.Deploy,
            kind: TranscriptKinds.ToolCall, lastAgeMinutes: 6);
        await using var parent = await OpenAsync(h, h.ParentId);
        await h.SweepAsync();
        await parent.MarkWorkingAsync();
        var busyNote = await SeedSourcedCompletionAsync(h, h.ParentId);
        var freeNote = await SeedSourcedCompletionAsync(h, h.SessionId);
        TaskSupervisionNotice before;
        await using (var db = h.Context())
            before = await db.TaskSupervisionNotices.AsNoTracking().SingleAsync(n => n.TaskId == h.TaskId);
        before.State.ShouldBe(TaskSupervisionNoticeState.Pending);
        before.RecipientSessionId.ShouldBe(h.ParentId);

        var queueLock = parent.Provider.GetRequiredService<SessionMessageQueueService>().GetLock(h.ParentId);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = false;
        try
        {
            await RecoverAsync(parent, h.Clock).WaitAsync(TimeSpan.FromSeconds(20));
            await using (var db = h.Context())
            {
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == freeNote))
                    .AgentSessionId.ShouldBe(h.SessionId, "the free session's completion note is enqueued in the same pass");
                (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.ParentId)).ShouldBe(0);
                var after = await db.TaskSupervisionNotices.AsNoTracking().SingleAsync(n => n.Id == before.Id);
                after.State.ShouldBe(before.State);
                after.NextAttemptAt.ShouldBe(before.NextAttemptAt, "a busy lock is not a failed attempt: no backoff");
                after.ReasonCode.ShouldBe(before.ReasonCode);
                after.QueueMessageId.ShouldBe(before.QueueMessageId);
                (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == busyNote)).CompletionNoteQueuedAt.ShouldBeNull();
            }

            queueLock.Release();
            released = true;
            await RecoverAsync(parent, h.Clock).WaitAsync(TimeSpan.FromSeconds(20));
            await using (var db = h.Context())
            {
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskSupervisionNoticeId == before.Id))
                    .AgentSessionId.ShouldBe(h.ParentId, "the next pass retries the held recipient's notice");
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == busyNote))
                    .AgentSessionId.ShouldBe(h.ParentId, "the next pass retries the held recipient's completion note");
            }
        }
        finally
        {
            if (!released) queueLock.Release();
        }
        h.Stopper.Killed.ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-1709: once a recipient's supervision notice times out on its lock, that recipient's later
    /// notices in the same pass are skipped even if the lock frees meanwhile, so no later notice jumps
    /// ahead of the one left for the next pass.
    /// </summary>
    [Test]
    public async Task C1709_Busy_recipient_keeps_supervision_notice_order_when_its_lock_frees_mid_pass()
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role: AgentTaskRole.Deploy,
            kind: TranscriptKinds.ToolCall, lastAgeMinutes: 6);
        var release = new ReleaseOnNoticeRead();
        await using var parent = await OpenAsync(h, h.ParentId, configure: b => b.AddInterceptors(release));
        await h.SweepAsync();
        await parent.MarkWorkingAsync();
        Guid first, second;
        await using (var db = h.Context())
        {
            var original = await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId);
            var now = h.Clock.GetUtcNow().UtcDateTime;
            var later = new TaskSupervisionNotice();
            db.Entry(later).CurrentValues.SetValues(original);
            later.Id = Guid.NewGuid();
            later.SourceEventId = Guid.NewGuid();
            later.Body = original.Body + " (second)";
            later.Digest = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
            later.NextAttemptAt = now.AddMinutes(-1);
            original.NextAttemptAt = now.AddMinutes(-2);
            db.TaskSupervisionNotices.Add(later);
            await db.SaveChangesAsync();
            (first, second) = (original.Id, later.Id);
        }

        var queueLock = parent.Provider.GetRequiredService<SessionMessageQueueService>().GetLock(h.ParentId);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        // The first notice has already timed out when the pass reads the second; the lock frees there.
        var released = 0;
        release.NoticeId = second;
        release.OnRead = () =>
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        };
        try
        {
            await RecoverAsync(parent, h.Clock).WaitAsync(TimeSpan.FromSeconds(20));
            Volatile.Read(ref released).ShouldBe(1, "the pass read the second notice and freed the lock mid-pass");
            await using (var db = h.Context())
                (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.ParentId)).ShouldBe(0,
                    "no notice is enqueued in the pass where the first one timed out, even after the lock frees");

            await RecoverAsync(parent, h.Clock).WaitAsync(TimeSpan.FromSeconds(20));
            await using (var db = h.Context())
            {
                var rows = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceTaskSupervisionNoticeId == first || m.SourceTaskSupervisionNoticeId == second)
                    .OrderBy(m => m.Sequence).Select(m => m.SourceTaskSupervisionNoticeId).ToListAsync();
                rows.ShouldBe([first, second], "the recipient's queue keeps the notices' order");
            }
        }
        finally
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        }
    }

    /// <summary>
    /// CARD-1709: the same order rule for completion notes. A1 times out on the held caller's lock,
    /// the lock frees while B (another caller) is processed, and A2 must not be enqueued before A1.
    /// </summary>
    [Test]
    public async Task C1709_Busy_caller_keeps_completion_note_order_when_its_lock_frees_mid_pass()
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        var scan = new CompletionScanSignal();
        await using var parent = await OpenAsync(h, h.ParentId, services: s => s.AddSingleton<LandDeliveryBoundary>(scan));
        await parent.MarkWorkingAsync();
        // The pass walks owed tasks in database id order (uuid byte order): A1 < B < A2.
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var (a1, b, a2) = (ids[0], ids[1], ids[2]);
        await SeedSourcedCompletionAsync(h, h.ParentId, a1);
        await SeedSourcedCompletionAsync(h, h.SessionId, b);
        await SeedSourcedCompletionAsync(h, h.ParentId, a2);

        var queueLock = parent.Provider.GetRequiredService<SessionMessageQueueService>().GetLock(h.ParentId);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = 0;
        scan.OnTask = id =>
        {
            if (id == b && Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        };
        try
        {
            await RecoverAsync(parent, h.Clock).WaitAsync(TimeSpan.FromSeconds(20));
            Volatile.Read(ref released).ShouldBe(1, "B reached its enqueue and freed A's lock mid-pass");
            await using (var db = h.Context())
            {
                (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == b)).ShouldBe(1, "B is enqueued in the first pass");
                (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.ParentId)).ShouldBe(0,
                    "no A note is enqueued in the pass where A1 timed out, even after A's lock frees");
            }

            await RecoverAsync(parent, h.Clock).WaitAsync(TimeSpan.FromSeconds(20));
            await using (var db = h.Context())
            {
                var rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == h.ParentId)
                    .OrderBy(m => m.Sequence).Select(m => m.SourceTaskId).ToListAsync();
                rows.ShouldBe([a1, a2], "A's queue rows keep the notes' order: A1 before A2");
            }
        }
        finally
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        }
    }

    /// <summary>
    /// CARD-1709: a pass that left a busy session's work behind retries it soon, not at the
    /// fifteen-minute backstop; the skipped work is not a failure, so it takes no failure wakeup.
    /// </summary>
    [Test]
    public async Task C1709_Busy_pass_retries_skipped_work_before_the_backstop()
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await using var parent = await OpenAsync(h, h.ParentId);
        await parent.MarkWorkingAsync();
        var busyNote = await SeedSourcedCompletionAsync(h, h.ParentId);
        var clock = new TimerClock(h.Clock);
        var queueLock = parent.Provider.GetRequiredService<SessionMessageQueueService>().GetLock(h.ParentId);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = false;
        using var worker = new CompletionNoteWorkHostedService(parent.Provider.GetRequiredService<IServiceScopeFactory>(),
            new CompletionNoteFlushQueue(), new SpecialistFailureQueue(), clock, NullLogger<CompletionNoteWorkHostedService>.Instance);
        await worker.StartAsync(default);
        try
        {
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(20));
            clock.DueTimes.First().ShouldBe(TimeSpan.FromSeconds(5), "the busy pass schedules its retry, not the fifteen-minute backstop");
            await using (var db = h.Context())
                (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == busyNote)).ShouldBe(0);

            queueLock.Release();
            released = true;
            h.Clock.Advance(TimeSpan.FromSeconds(5));
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (true)
            {
                await using var db = h.Context();
                if (await db.SessionQueuedMessages.AnyAsync(m => m.SourceTaskId == busyNote)) break;
                DateTime.UtcNow.ShouldBeLessThan(deadline, "the retry pass enqueues the skipped note");
                await Task.Delay(50);
            }
        }
        finally
        {
            if (!released) queueLock.Release();
            await worker.StopAsync(default);
        }
    }

    private static async Task<Guid> SeedSourcedCompletionAsync(TaskCeilingHarness h, Guid parent, Guid? id = null)
    {
        var taskId = id ?? Guid.NewGuid();
        await using var seed = h.Context();
        var operationId = Guid.NewGuid();
        var now = h.Clock.GetUtcNow().UtcDateTime;
        seed.AgentTaskLandings.Add(new AgentTaskLanding { Id = operationId, TaskId = taskId,
            Active = false, CreatedAt = now, UpdatedAt = now });
        seed.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId,
            Title = "Sourced completion recovery", Goal = "Recover the owed completion note",
            WorkingDirectory = Path.GetTempPath(), CreatedAt = now,
            Status = AgentTaskStatus.Succeeded, ParentSessionId = parent, ReplyTo = AgentTaskReplyTo.Session,
            SourceLandingOperationId = operationId,
            Result = "Complete sourced result.\n" + DelegationReportFormatter.ReportToken(taskId, "done") });
        await seed.SaveChangesAsync();
        return taskId;
    }

    private sealed class ReleaseOnNoticeRead : DbCommandInterceptor
    {
        public Guid? NoticeId { get; set; }
        public Action? OnRead { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (NoticeId is { } id && command.CommandText.Contains("FROM \"TaskSupervisionNotices\"", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid value && value == id))
                OnRead?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CompletionScanSignal : LandDeliveryBoundary
    {
        public Action<Guid>? OnTask { get; set; }
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "completion-scan") OnTask?.Invoke(taskId);
            return Task.CompletedTask;
        }
    }

    private sealed class TimerClock(TimeProvider inner) : TimeProvider
    {
        public ConcurrentQueue<TimeSpan> DueTimes { get; } = new();
        public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            DueTimes.Enqueue(dueTime);
            TimerCreated.TrySetResult();
            return timer;
        }
    }
}
