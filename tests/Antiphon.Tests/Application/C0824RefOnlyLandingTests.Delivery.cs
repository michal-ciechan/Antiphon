using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class C0824RefOnlyLandingTests
{
    [Test]
    public async Task Land_outcome_reaches_caller_via_queue_and_recovers_handoffs()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var h = new LandingSafetyHarness { Clock = clock };
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var variants = new[] { "before-terminal", "terminal-committed", "before-enqueue", "queue-inserted",
            "queue-committed-before-wakeup", "receipt-before-save", "recovery-refusal", "lost-land-wakeup" };
        foreach (var variant in variants)
        {
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
                await h.Services.GetRequiredService<AgentTaskLandService>().SweepAsync(default);
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
            h.Boundary = null;
            AgentTaskLandNotification note;
            await using (var db = h.CreateContext())
            {
                note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
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
                    note.Body.ShouldContain("desktop=NotRequired", "caller-new-result-shape");
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
                var prompts = await db.TranscriptEntries.Where(p => p.AgentSessionId == caller.SessionId
                    && p.Kind == TranscriptKinds.UserPrompt && p.Sequence > floor).ToListAsync();
                var prompt = prompts.Where(p => PromptSubmissionMatch.IsCompleteIn(note.Body, p.Text ?? "")).ShouldHaveSingleItem();
                prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence!.Value);
                saved.ConfirmingPromptSequence.ShouldBe(prompt.Sequence);
                prompt.Timestamp.ShouldNotBeNull(); prompt.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(row.LastDeliveryStartedAt!.Value);
                saved.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(saved.Body));
            }
            async Task ReconcileAsync(LandDeliveryBoundary? cut)
            {
                await using var db = h.CreateContext();
                await new AgentTaskLandNotificationService(db, caller.Queue, flushes, caller.Runtime, clock, cut)
                    .ReconcileAsync(note.Id, default);
            }
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

