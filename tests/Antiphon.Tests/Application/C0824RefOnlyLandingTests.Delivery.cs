using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class C0824RefOnlyLandingTests
{
    [Test]
    public async Task Land_outcome_reaches_caller_via_queue_and_recovers_handoffs()
    {
        // Queue delivery owns real timers; a frozen fake clock strands its poll/settle delays.
        var clock = new ScaledTimeProvider(1);
        await using var h = new LandingSafetyHarness { Clock = clock };
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var variants = new[] { "before-terminal", "terminal-committed", "before-enqueue", "queue-inserted",
            "queue-committed-before-wakeup", "receipt-before-save", "recovery-refusal", "lost-land-wakeup" };
        foreach (var variant in variants)
        {
            Console.WriteLine("C0824_RECEIPT variant=" + variant + " stage=start");
            if (variant != variants[0]) await h.ResetDatabaseAsync();
            await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: variant == variants[0]);
            var busy = Array.IndexOf(variants, variant) % 2 == 1;
            await using var caller = await ClaudeDialogFixture.CreateBridgeAsync(new()
            { AlwaysOn = false, ConnectionString = h.Schema.ConnectionString, TimeProvider = clock });
            await caller.InsertTurnAsync("previous visible input", "previous visible response");
            var floor = await caller.CurrentTranscriptMaxSequenceAsync();
            if (busy) await caller.MarkWorkingAsync(); else await caller.MarkIdleAsync();
            await using (var db = h.CreateContext())
            {
                await db.AgentTasks.Where(t => t.Id == h.Fixture.TaskId).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.ReplyTo, AgentTaskReplyTo.Session).SetProperty(t => t.ParentSessionId, (Guid?)caller.SessionId));
            }
            h.Messages = caller.Queue;
            var boundary = new ReceiptCut(variant);
            if (variant == "before-terminal") h.Fault.TerminalCut = "commit";
            h.Boundary = variant is "terminal-committed" or "lost-land-wakeup" ? boundary : null;
            Guid? review = null;
            if (variant == "recovery-refusal") review = (await RecoveryAuthorityAsync(h, source, false)).ReviewId;
            var requested = await h.RequestAsync(Filter, source, review, recoverReviewedSource: variant == "recovery-refusal");
            if (variant == "lost-land-wakeup")
            {
                h.Queue.TryDequeue(out _).ShouldBeFalse("lost-land-wakeup-is-real");
                await h.RestartServicesAsync(); h.Messages = caller.Queue; h.Boundary = null;
                await h.SweepAsync();
            }
            try { await h.RunQueuedAsync(); }
            catch (LandingSafetyHarness.InjectedSaveFailure) when (variant == "before-terminal") { }
            catch (IOException) when (variant == "terminal-committed" && boundary.Hit) { }
            if (variant == "before-terminal")
            {
                h.Fault.Triggered.ShouldBeTrue("cut-before-terminal-commit-reached");
                await using (var rollback = h.CreateContext())
                    (await rollback.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId)).ShouldBe(0, "terminal-rollback-has-no-note");
                h.Fault.TerminalCut = null;
                await h.RestartServicesAsync(); h.Messages = caller.Queue;
                await h.RunRequestAsync(requested.RequestId);
            }
            if (variant == "terminal-committed") boundary.Hit.ShouldBeTrue("cut-terminal-committed-reached");
            Console.WriteLine("C0824_RECEIPT variant=" + variant + " stage=producer-terminal");
            h.Boundary = null;
            AgentTaskLandNotification note;
            await using (var db = h.CreateContext())
            {
                note = (await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome).ToListAsync()).ShouldHaveSingleItem("caller-whole-userprompt:terminal-commit-cut");
                note.ParentSessionId.ShouldBe(caller.SessionId); note.RequestId.ShouldBe(requested.RequestId);
                var terminal = await db.AgentTaskEvents.SingleAsync(e => e.Id == note.SourceEventId);
                terminal.LandRequestId.ShouldBe(requested.RequestId); terminal.AgentTaskId.ShouldBe(h.Fixture.TaskId);
                note.Body.ShouldContain(note.Id.ToString("N"));
                note.Body.ShouldContain(requested.RequestId.ToString("N"));
                if (variant == "recovery-refusal") note.Body.ShouldContain("ref_only_land_recovery_unsupported");
                else
                {
                    var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Active);
                    op.DesktopCleanupDisposition.ShouldBe(DesktopCleanupDisposition.NotRequired, "real-ref-only-outcome");
                    note.Body.ShouldContain("desktop=NotRequired", customMessage: "caller-new-result-shape");
                    op.RemoteConfirmedAt.ShouldNotBeNull("producer-land-published");
                }
                note.ConfirmedAt.ShouldBeNull();
            }
            var flushes = new CompletionNoteFlushQueue();
            if (variant is "before-enqueue" or "queue-inserted" or "queue-committed-before-wakeup")
            {
                await ReconcileAsync(boundary);
                boundary.Hit.ShouldBeTrue("notification-cut-reached:" + variant);
                await using (var db = h.CreateContext())
                {
                    (await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldBeNull();
                    (await db.SessionQueuedMessages.CountAsync(q => q.SourceLandNotificationId == note.Id))
                        .ShouldBe(variant == "before-enqueue" ? 0 : 1);
                }
                clock.Advance(TimeSpan.FromMinutes(6));
                await h.RestartServicesAsync(); h.Messages = caller.Queue;
            }
            await ReconcileAsync(null);
            SessionQueuedMessage queued;
            await using (var db = h.CreateContext())
            {
                queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.SourceLandNotificationId == note.Id);
                queued.AgentSessionId.ShouldBe(caller.SessionId); queued.Body.ShouldBe(note.Body);
                queued.ContentDigest.ShouldBe(note.ContentDigest);
            }
            if (variant == "receipt-before-save") await NegativeReceiptEvidenceAsync(h, caller, note, flushes, clock);
            // Actual queue submission writes the only new positive UserPrompt via default OnSubmitted.
            await caller.Queue.FlushIfIdleAsync(caller.SessionId, default);
            if (busy)
            {
                caller.Adapter.SubmittedBodies.ShouldBeEmpty("busy-recipient-does-not-receive-yet");
                await ReconcileAsync(null);
                await using (var db = h.CreateContext())
                    (await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldBeNull();
                await caller.MarkIdleAsync();
                await caller.Queue.OnTurnEndAsync(caller.SessionId, default);
            }
            if (variant == "receipt-before-save")
            {
                await ReconcileAsync(boundary);
                boundary.Hit.ShouldBeTrue("receipt-save-cut-reached");
                await using var pending = h.CreateContext();
                (await pending.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldBeNull();
            }
            var submitted = caller.Adapter.SubmittedBodies.Count;
            await ReconcileAsync(null); await ReconcileAsync(null);
            caller.Adapter.SubmittedBodies.Count.ShouldBe(submitted, "no-retype-after-receipt");
            await using (var db = h.CreateContext())
            {
                var saved = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
                var row = await db.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
                saved.State.ShouldBe(LandNotificationState.Confirmed, "caller-whole-userprompt:" + variant);
                saved.QueueMessageId.ShouldBe(row.Id); row.Id.ShouldBe(queued.Id, "one-keyed-row");
                saved.Body.ShouldBe(note.Body); row.Body.ShouldBe(note.Body);
                row.LastDeliveryBaselineSequence.ShouldNotBeNull();
                var deliveryFloor = row.LastDeliveryBaselineSequence.Value;
                var prompts = await db.TranscriptEntries.Where(p => p.AgentSessionId == caller.SessionId
                    && p.Kind == TranscriptKinds.UserPrompt && p.Sequence > floor && p.Sequence > deliveryFloor).ToListAsync();
                var prompt = prompts.Where(p => PromptSubmissionMatch.IsCompleteIn(note.Body, p.Text ?? "")).ShouldHaveSingleItem();
                prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence!.Value);
                saved.ConfirmingPromptSequence.ShouldBe(prompt.Sequence);
                prompt.Timestamp.ShouldNotBeNull(); prompt.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(row.LastDeliveryStartedAt!.Value);
                saved.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(
                    "Session:" + caller.SessionId.ToString("N") + "\n" + note.Body), "destination-bound-note-digest");
            }
            Console.WriteLine("C0824_RECEIPT variant=" + variant + " stage=confirmed");
            async Task ReconcileAsync(LandDeliveryBoundary? cut)
            {
                await using var db = h.CreateContext();
                await new AgentTaskLandNotificationService(db, caller.Queue, flushes, caller.Runtime, clock, cut)
                    .ReconcileAsync(note.Id, default);
            }
        }
        await AutonomousWakeupAsync(h, source, recoveredBusy: false);
        await AutonomousWakeupAsync(h, source, recoveredBusy: true);
    }

    private static async Task AutonomousWakeupAsync(LandingSafetyHarness h, string source, bool recoveredBusy)
    {
        Console.WriteLine("C0824_RECEIPT autonomous=" + recoveredBusy + " stage=start");
        await h.ResetDatabaseAsync();
        await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: false);
        var flushes = new CompletionNoteFlushQueue();
        var scan = new WarmScan();
        await using var caller = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false, ConnectionString = h.Schema.ConnectionString, TimeProvider = h.Clock,
            ConfigureDbContext = builder => builder.AddInterceptors(scan),
            ConfigureServices = services =>
            {
                services.AddSingleton(flushes);
                services.AddSingleton<LandDeliveryBoundary>(new QueueHintOnly());
            }
        });
        await caller.InsertTurnAsync("prior wakeup input", "prior wakeup reply");
        if (recoveredBusy) await caller.MarkWorkingAsync(); else await caller.MarkIdleAsync();
        await using (var db = h.CreateContext())
            await db.AgentTasks.Where(t => t.Id == h.Fixture.TaskId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ReplyTo, AgentTaskReplyTo.Session).SetProperty(t => t.ParentSessionId, (Guid?)caller.SessionId));
        var worker = new Antiphon.Server.Infrastructure.Orchestration.CompletionNoteWorkHostedService(
            caller.Provider.GetRequiredService<IServiceScopeFactory>(), flushes, new SpecialistFailureQueue(), h.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Antiphon.Server.Infrastructure.Orchestration.CompletionNoteWorkHostedService>.Instance);
        scan.Armed = true;
        await worker.StartAsync(default);
        try
        {
            // The empty startup query is already executed before the producer creates a queue row.
            (await SeenAsync(() => Task.FromResult(scan.Seen), TimeSpan.FromSeconds(5))).ShouldBeTrue("initial-recovery-sweep-warmed");
            h.Messages = caller.Queue; h.Boundary = null;
            await h.RequestAsync(Filter, source); await h.RunQueuedAsync();
            AgentTaskLandNotification note;
            await using (var db = h.CreateContext())
                note = (await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == h.Fixture.TaskId
                    && n.Kind == LandNotificationKind.Outcome).ToListAsync()).ShouldHaveSingleItem("real-producer-note");
            if (recoveredBusy)
            {
                await ReconcileAsync(new ReceiptCut("queue-inserted"));
                await using (var db = h.CreateContext())
                {
                    var pending = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
                    pending.QueueMessageId.ShouldBeNull("cut-after-key-before-link");
                    pending.ConfirmedAt.ShouldBeNull(); caller.Adapter.SubmittedBodies.ShouldBeEmpty();
                }
                await caller.MarkIdleAsync();
                ((ScaledTimeProvider)h.Clock).Advance(TimeSpan.FromMinutes(6)); // Still before the warmed 15-minute backstop.
            }
            await ReconcileAsync(null);
            var label = recoveredBusy ? "busy-recovery" : "already-idle";
            (await SeenAsync(async () =>
            {
                await using var db = h.CreateContext();
                var delivery = await db.SessionQueuedMessages.AsNoTracking()
                    .SingleOrDefaultAsync(q => q.SourceLandNotificationId == note.Id);
                if (delivery?.LastDeliveryBaselineSequence is not long baseline
                    || delivery.LastDeliveryStartedAt is not DateTime started) return false;
                var texts = await db.TranscriptEntries.Where(p => p.AgentSessionId == caller.SessionId
                    && p.Kind == TranscriptKinds.UserPrompt && p.Sequence > baseline && p.Timestamp >= started)
                    .Select(p => p.Text).ToListAsync();
                return texts.Any(text => PromptSubmissionMatch.IsCompleteIn(note.Body, text ?? ""));
            }, TimeSpan.FromSeconds(5))).ShouldBeTrue("caller-whole-userprompt:" + label);
            await ReconcileAsync(null);
            await using (var db = h.CreateContext())
            {
                var saved = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
                saved.State.ShouldBe(LandNotificationState.Confirmed, "caller-whole-userprompt:" + label);
                var row = await db.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
                saved.QueueMessageId.ShouldBe(row.Id);
                row.AgentSessionId.ShouldBe(caller.SessionId);
                saved.ParentSessionId.ShouldBe(caller.SessionId);
                var prompt = await db.TranscriptEntries.SingleAsync(p => p.AgentSessionId == caller.SessionId
                    && p.Kind == TranscriptKinds.UserPrompt && p.Sequence == saved.ConfirmingPromptSequence);
                PromptSubmissionMatch.IsCompleteIn(note.Body, prompt.Text!).ShouldBeTrue();
                prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence!.Value);
                prompt.Timestamp.ShouldNotBeNull();
                prompt.Timestamp.Value.ShouldBeGreaterThanOrEqualTo(row.LastDeliveryStartedAt!.Value);
            }
            caller.Adapter.SubmittedBodies.ShouldHaveSingleItem("one-autonomous-submission");
            async Task ReconcileAsync(LandDeliveryBoundary? boundary)
            {
                await using var db = h.CreateContext();
                await new AgentTaskLandNotificationService(db, caller.Queue, flushes, caller.Runtime, h.Clock, boundary)
                    .ReconcileAsync(note.Id, default);
            }
        }
        finally { await worker.StopAsync(default); worker.Dispose(); }
    }

    private static async Task<bool> SeenAsync(Func<Task<bool>> observed, TimeSpan budget)
    {
        var bound = System.Diagnostics.Stopwatch.StartNew();
        while (bound.Elapsed < budget)
        {
            if (await observed()) return true;
            await Task.Delay(10);
        }
        return await observed();
    }

    private sealed class QueueHintOnly : LandDeliveryBoundary
    {
        public override bool DropWakeup(string boundary, Guid identity) => boundary == "completion-schedule";
    }

    private sealed class WarmScan : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public volatile bool Seen;
        public override ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData data,
            System.Data.Common.DbDataReader result, CancellationToken ct = default)
        {
            if (Armed && command.CommandText.StartsWith("SELECT s.\"Id\", s.\"AgentSessionId\", s.\"HoldUntil\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"DeliveryAttempts\" = 0", StringComparison.Ordinal)
                && command.CommandText.Contains("\"SessionQueuedMessages\"", StringComparison.Ordinal))
                Seen = true;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReceiptCut(string cut) : LandDeliveryBoundary
    {
        public bool Hit { get; private set; }
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (!Hit && boundary == cut)
            {
                Hit = true;
                throw new IOException("owned cut:" + cut);
            }
            return Task.CompletedTask;
        }
        public override bool DropWakeup(string boundary, Guid identity) => cut == "lost-land-wakeup" && boundary == "land-request";
    }
}
