using System.Collections.Concurrent;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CompletionNoteDurableSweepTests
{
    [Test]
    public async Task C1360_DurableSweepRecoversLostFlushOnIdleCaller()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var flushes = new CompletionNoteFlushQueue();
        var boundary = new LostFlushBoundary();
        var clock = new SweepClock();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton(flushes);
                services.AddSingleton<LandDeliveryBoundary>(boundary);
            },
        });
        using var worker = new CompletionNoteWorkHostedService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(), flushes,
            new SpecialistFailureQueue(), clock, NullLogger<CompletionNoteWorkHostedService>.Instance);
        await worker.StartAsync(default);
        try
        {
            await UntilAsync(() => Task.FromResult(clock.Timers == 1), "startup sweep did not arm its timer");
            clock.DueTimes.ShouldContain(TimeSpan.FromMinutes(15));

            var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
            await using var db = new AppDbContext(options);
            // Keep the complete outcome inside the inbox-conhost envelope: this test observes
            // the submitted body itself, while the default receipt seed exercises spill transport.
            var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId,
                detail: "durable sweep beginning\n" +
                    "publication is complete and the committed outcome remains owed to this idle caller\n" +
                    "both in-memory flush hints are lost; the durable scan must deliver this entire note\n" +
                    "durable sweep distinct final tail");
            // SeedAsync uses wall time. Make the note due at the frozen worker clock without
            // advancing that clock or changing the empty startup sweep's deadline.
            note.NextAttemptAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
            await new AgentTaskLandNotificationService(db, h.Queue, flushes, h.Runtime, clock, boundary)
                .ReconcileAsync(note.Id, default);
            await db.Entry(note).ReloadAsync();
            note.QueueMessageId.ShouldNotBeNull();
            var rowId = note.QueueMessageId.Value;

            // Assertion A: the unmodified product never consults this call-site seam.
            boundary.Dropped.ShouldContain(("completion-schedule", rowId),
                "assertion A: the committed row must have a completion-schedule drop record");
            boundary.Dropped.ShouldContain(("completion", note.Id));

            await UntilAsync(() => Task.FromResult(clock.Timers == 2), "Check wake did not re-arm the timer");
            h.Adapter.SubmittedBodies.ShouldBeEmpty("both flush hints were dropped before the durable sweep");
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == rowId);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryAttempts.ShouldBe(0);
            boundary.Reached.ShouldNotContain(r => r.Boundary == "completion-scan" && r.Identity == h.SessionId);
            var scanCount = boundary.Reached.Length;

            clock.Inner.Advance(TimeSpan.FromMinutes(15));
            await UntilAsync(() => Task.FromResult(h.Adapter.SubmittedBodies.Count == 1),
                "durable sweep did not deliver");
            PromptSubmissionMatch.IsCompleteIn(note.Body, h.Adapter.SubmittedBodies[0]).ShouldBeTrue();
            boundary.Reached.Skip(scanCount).ShouldContain(("completion-scan", Guid.Empty, h.SessionId));
            await UntilAsync(async () =>
            {
                await db.Entry(row).ReloadAsync();
                return row.Status != QueuedMessageStatus.Pending;
            }, "durable sweep delivery did not settle its queue row");
            row.DeliveryAttempts.ShouldBe(1);
            row.Status.ShouldNotBe(QueuedMessageStatus.Pending);

            long? promptSequence = null;
            await UntilAsync(async () =>
            {
                await using var receiptDb = new AppDbContext(options);
                var prompts = await receiptDb.TranscriptEntries.AsNoTracking()
                    .Where(t => t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
                    .ToListAsync();
                var matches = prompts.Where(p => PromptSubmissionMatch.IsCompleteIn(note.Body, p.Text!)).ToArray();
                if (matches.Length == 0) return false;
                promptSequence = matches.ShouldHaveSingleItem().Sequence;
                return true;
            }, "complete UserPrompt receipt did not arrive");
            await using var freshDb = new AppDbContext(options);
            await new AgentTaskLandNotificationService(freshDb, h.Queue, flushes, h.Runtime, clock, boundary)
                .ReconcileAsync(note.Id, default);
            var confirmed = await freshDb.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
            confirmed.State.ShouldBe(LandNotificationState.Confirmed);
            confirmed.ConfirmingPromptSequence.ShouldBe(promptSequence);
            h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
            Console.WriteLine($"CARD-1360 completion-scan task={Guid.Empty} session={h.SessionId}; " +
                $"submittedBodies={h.Adapter.SubmittedBodies.Count}; attempts={row.DeliveryAttempts}; " +
                $"state={confirmed.State}; promptSequence={confirmed.ConfirmingPromptSequence}");
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    private sealed class LostFlushBoundary : LandDeliveryBoundary
    {
        private readonly object _gate = new();
        private readonly List<(string Boundary, Guid Identity)> _dropped = [];
        private readonly List<(string Boundary, Guid TaskId, Guid Identity)> _reached = [];
        public (string Boundary, Guid Identity)[] Dropped { get { lock (_gate) return _dropped.ToArray(); } }
        public (string Boundary, Guid TaskId, Guid Identity)[] Reached { get { lock (_gate) return _reached.ToArray(); } }

        public override bool DropWakeup(string boundary, Guid identity)
        {
            lock (_gate) _dropped.Add((boundary, identity));
            return boundary is "completion" or "completion-schedule";
        }

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            lock (_gate) _reached.Add((boundary, taskId, identity));
            return Task.CompletedTask;
        }
    }

    private sealed class SweepClock : TimeProvider
    {
        private int _timers;
        public FakeTimeProvider Inner { get; } = new(DateTimeOffset.UtcNow);
        public ConcurrentQueue<TimeSpan> DueTimes { get; } = new();
        public int Timers => Volatile.Read(ref _timers);
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = Inner.CreateTimer(callback, state, dueTime, period);
            DueTimes.Enqueue(dueTime);
            Interlocked.Increment(ref _timers);
            return timer;
        }
    }

    private static async Task UntilAsync(Func<Task<bool>> condition, string because, int seconds = 10)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!budget.Token.IsCancellationRequested)
        {
            if (await condition()) return;
            try { await Task.Delay(50, budget.Token); }
            catch (OperationCanceledException) { break; }
        }
        throw new TimeoutException(because);
    }
}
