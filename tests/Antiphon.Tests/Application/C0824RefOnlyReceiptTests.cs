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

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
// CARD-0824. Nine cuts share one budget. Linux completions on server2 (24 cores, 2026-11-11):
// 42.739s alone at loadavg 14.8 22.9 24.4 (n=1) and 26.546s beside the group-56 filter (n=1;
// git-lane neighbors finished within 9.5s). Windows rc 6591e378 was still in this method at
// 81.042s. The same chunk's C971 methods ran 1.9x-3.9x those Linux durations (median 3.3x),
// which scales the 26.546s completion to about 87s. 170s is 2x the censored Windows lower
// bound. Provisional until a Windows completion is collected.
[Timeout(170_000)]
public sealed class C0824RefOnlyReceiptTests
{
    [Test]
    public async Task Settlement_note_reaches_caller_and_recovers_handoffs()
    {
        var variants = new[] { "eligible", "busy", "settlement-before-save", "settlement-before-commit", "settlement-saved",
            "before-enqueue", "queue-inserted", "queue-committed-before-wakeup", "receipt-before-save" };
        foreach (var cut in variants)
        {
            // The class timeout's stdout is the only frame a censored run keeps. Name the cut
            // before any await so the next Windows censor identifies which settlement was in flight.
            Console.WriteLine("c0824-receipt-cut=" + cut);
            Console.Out.Flush();
            var review = cut == "settlement-before-commit";
            await using var w = await RunnerSettlementWorld.CreateAsync(review ? AgentTaskRole.Review : AgentTaskRole.Code,
                profiled: true, controlledSyncClock: true);
            await w.RecordRefOnlyAsync();
            var source = await w.Git.RunnerPushAsync("receipt.txt", "exact caller source");
            var raw = cut == "busy" ? "LONG_HEAD\n" + new string('x', 18000) + "\nLONG_MIDDLE\n" + new string('y', 18000) + "\nLONG_TAIL" : "Immutable caller report " + cut;
            if (review)
            {
                // The independent author owns S. Seed its object before the existing subject
                // fixture publishes from desktop; this does not move the task's desktop ref.
                await w.Git.RunAsync(w.Git.Desktop, "fetch", "origin", w.Git.FullRef);
                var subject = await w.AddReviewSubjectAsync(source);
                raw += "\n" + DelegationReportFormatter.FindingToken(w.TaskId, "clean")
                    + $"\n--- review evidence ---\nsubjectTaskId: {subject:D}\nreviewedSourceSha: {source}\nreviewedSourceClean: true\nordinaryScopeCompleted: Full\n";
            }
            var report = RunnerSettlementWorld.Report(raw + "\n" + w.Claim(source), review ? "none" : "review");
            var fault = new OnceBoundary(cut);
            w.ConfigureServices = services => services.AddSingleton<LandDeliveryBoundary>(fault);
            await w.RestartServicesAsync();
            await using var recipient = await RestartableQueueRecipient.CreateAsync(new()
            {
                AlwaysOn = false, ConnectionString = w.Schema.ConnectionString,
                AttachSessionId = w.CallerSessionId, AttachAgentId = Guid.NewGuid(),
            });
            var bridge = recipient.Current;
            await bridge.InsertStoreVisibleTurnAsync("prior caller turn", "prior completed turn");
            if (cut == "busy") await bridge.MarkWorkingAsync();
            if (cut == "busy") await w.MintRefOnlyDebtAsync(report);
            else await w.SettleAsync(report);
            if (cut is "settlement-before-save" or "settlement-before-commit")
            {
                fault.Hit.ShouldBeTrue("reached:W1:" + cut);
                w.Task.Status.ShouldBe(AgentTaskStatus.Working, "uncommitted-settlement-rolls-back");
                (await w.ObligationsAsync()).ShouldBeEmpty();
                w.ConfigureServices = null; await w.RestartServicesAsync();
                await w.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(w.SessionId, default);
                await w.ReloadAsync();
            }
            else if (cut is not ("eligible" or "busy" or "receipt-before-save"))
                fault.Hit.ShouldBeTrue("reached:W1:" + cut);
            w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, w.Task.CompletionProgressEvidenceJson);
            w.Task.Result.ShouldNotBeNull().ShouldContain(raw, Case.Sensitive, "full-result-retained");
            var obligation = (await w.ObligationsAsync()).ShouldHaveSingleItem();
            obligation.TaskId.ShouldBe(w.TaskId); obligation.ConfirmedAt.ShouldBeNull();
            var digest = obligation.ContentDigest;
            var eventId = obligation.SourceEventId;
            bridge.Adapter.SubmittedBodies.ShouldBeEmpty("a queue row is not a caller receipt");
            w.ConfigureServices = null; await w.RestartServicesAsync();
            await ReconcileAsync(w, bridge, obligation.Id);
            var queued = (await w.QueuedForAsync(obligation.Id)).ShouldNotBeNull();
            // Queued text and a Sent bit without a UserPrompt are independently insufficient.
            await BridgeQueueHarness.InsertEntryAsync(w.CallerSessionId, TranscriptKinds.QueuedUserPrompt, queued.Body,
                timestamp: DateTime.UtcNow, connectionString: w.Schema.ConnectionString);
            await using (var db = w.CreateContext())
            {
                await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.Status, QueuedMessageStatus.Sent).SetProperty(m => m.SentAt, DateTime.UtcNow)
                    .SetProperty(m => m.LastDeliveryBaselineSequence, (long?)0).SetProperty(m => m.LastDeliveryStartedAt, DateTime.UtcNow));
            }
            await ReconcileAsync(w, bridge, obligation.Id);
            (await w.ObligationsAsync()).Single().ConfirmedAt.ShouldBeNull("Sent-and-QueuedUserPrompt-are-not-receipts");
            await using (var db = w.CreateContext())
            {
                await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.Status, QueuedMessageStatus.Pending).SetProperty(m => m.SentAt, (DateTime?)null)
                    .SetProperty(m => m.LastDeliveryBaselineSequence, (long?)null).SetProperty(m => m.LastDeliveryStartedAt, (DateTime?)null));
            }
            await recipient.RestartAsync();
            bridge = recipient.Current;
            recipient.SubmittedCount.ShouldBe(0, "recipient-graph-rebuilt-before-input");
            if (cut == "busy")
            {
                await bridge.Queue.FlushIfIdleAsync(w.CallerSessionId, default);
                bridge.Adapter.SubmittedBodies.ShouldBeEmpty("busy caller defers input");
                await bridge.MarkIdleAsync();
            }
            await bridge.Queue.FlushIfIdleAsync(w.CallerSessionId, default);
            bridge.Adapter.SubmittedBodies.Count.ShouldBe(1, "actual queue submission is required");
            await using var verify = w.CreateContext();
            var accepted = (await verify.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == w.CallerSessionId
                && p.Kind == TranscriptKinds.UserPrompt && p.Text != "prior caller turn").ToListAsync()).ShouldHaveSingleItem();
            var delivery = TaskCompletionNotification.TryReadDelivery((await w.ObligationsAsync()).Single().CompletionDeliveryJson)
                .ShouldNotBeNull("immutable rendering was committed");
            // The receipt must contain this independently authored report, not merely agree
            // with a producer rendering that could itself have lost the report/source.
            delivery.LogicalNote.ShouldContain(source, Case.Sensitive, "caller-source-is-authored-S");
            delivery.LogicalNote.ShouldContain(DelegationReportFormatter.Short(w.TaskId), Case.Sensitive, "caller-task-identity");
            if (cut == "busy")
            {
                delivery.LogicalNote.ShouldContain("LONG_HEAD", Case.Sensitive, "caller-long-report-head");
                delivery.LogicalNote.ShouldContain("LONG_TAIL", Case.Sensitive, "caller-long-report-tail");
                delivery.LogicalNote.ShouldContain("GET /api/agent-tasks/" + w.TaskId, Case.Sensitive, "caller-retained-report-pointer");
                delivery.LogicalNote.ShouldContain("desktop-sync=pending", Case.Sensitive, "caller-pending-is-not-confirmed");
            }
            else delivery.LogicalNote.ShouldContain("Immutable caller report " + cut, Case.Sensitive, "caller-own-report-content");
            PromptSubmissionMatch.Normalize(accepted.Text!).ShouldBe(PromptSubmissionMatch.Normalize(delivery.WireText), "caller-received-once");
            var row = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
            accepted.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence!.Value, "attempt-floor");
            if (cut == "receipt-before-save")
            {
                await ReconcileAsync(w, bridge, obligation.Id, fault);
                fault.Hit.ShouldBeTrue("reached:W1:after-input-before-receipt-save");
                (await w.ObligationsAsync()).Single().ConfirmedAt.ShouldBeNull();
            }
            if (cut == "eligible")
            {
                foreach (var dimension in new[] { "body", "kind", "destination", "digest", "source-key", "generation", "missing-generation", "floor" })
                {
                    await using (var db = w.CreateContext())
                    {
                        if (dimension == "body") await db.TranscriptEntries.Where(p => p.Id == accepted.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.Text, delivery.WireText.Substring(0, delivery.WireText.Length / 2)));
                        if (dimension == "kind") await db.TranscriptEntries.Where(p => p.Id == accepted.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.Kind, TranscriptKinds.QueuedUserPrompt));
                        if (dimension == "destination") await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.AgentSessionId, w.SessionId));
                        if (dimension == "digest") await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.ContentDigest, "different-digest"));
                        if (dimension == "source-key") await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.SourceLandNotificationId, (Guid?)Guid.NewGuid()));
                        if (dimension == "generation") await db.AgentSessions.Where(s => s.Id == w.CallerSessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, row.LastDeliveryGeneration!.Value.AddMinutes(1)));
                        if (dimension == "missing-generation") await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.LastDeliveryGeneration, (DateTime?)null));
                        if (dimension == "floor") await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.LastDeliveryBaselineSequence, (long?)accepted.Sequence));
                    }
                    await ReconcileAsync(w, bridge, obligation.Id);
                    (await w.ObligationsAsync()).Single().ConfirmedAt.ShouldBeNull("receipt-refused:" + dimension);
                    await using (var db = w.CreateContext())
                    {
                        await db.TranscriptEntries.Where(p => p.Id == accepted.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.Text, accepted.Text).SetProperty(p => p.Kind, TranscriptKinds.UserPrompt));
                        await db.SessionQueuedMessages.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.AgentSessionId, w.CallerSessionId)
                            .SetProperty(m => m.ContentDigest, row.ContentDigest).SetProperty(m => m.SourceLandNotificationId, row.SourceLandNotificationId)
                            .SetProperty(m => m.LastDeliveryGeneration, row.LastDeliveryGeneration).SetProperty(m => m.LastDeliveryBaselineSequence, row.LastDeliveryBaselineSequence));
                        await db.AgentSessions.Where(s => s.Id == w.CallerSessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, row.LastDeliveryGeneration!.Value));
                    }
                }
            }
            await w.RestartServicesAsync();
            await recipient.RestartAsync();
            bridge = recipient.Current;
            await ReconcileAsync(w, bridge, obligation.Id);
            var confirmed = (await w.ObligationsAsync()).Single();
            confirmed.State.ShouldBe(LandNotificationState.Confirmed, "caller-receipt-after-rebuild");
            confirmed.ConfirmingPromptSequence.ShouldBe(accepted.Sequence);
            confirmed.ContentDigest.ShouldBe(digest); confirmed.SourceEventId.ShouldBe(eventId);
            confirmed.QueueMessageId.ShouldBe(queued.Id);
            await bridge.Queue.FlushIfIdleAsync(w.CallerSessionId, default);
            await ReconcileAsync(w, bridge, obligation.Id);
            recipient.SubmittedCount.ShouldBe(1, "recovery-never-retypes-across-recipient-graphs");
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == obligation.Id)).ShouldBe(1);
            w.Task.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
            Directory.Exists(w.Task.WorktreePath!).ShouldBeFalse();
        }
    }

    private static async Task ReconcileAsync(RunnerSettlementWorld w, BridgeQueueHarness recipient, Guid id,
        LandDeliveryBoundary? boundary = null)
    {
        await using var db = w.CreateContext();
        await db.AgentTaskLandNotifications.Where(n => n.Id == id).ExecuteUpdateAsync(u => u.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
        var notifications = new AgentTaskLandNotificationService(db, recipient.Queue,
            recipient.Provider.GetRequiredService<CompletionNoteFlushQueue>(), recipient.Runtime, TimeProvider.System, boundary);
        await notifications.ReconcileAsync(id, default);
    }
    private sealed class OnceBoundary(string selected) : LandDeliveryBoundary
    {
        public bool Hit { get; private set; }
        public override Task ReachedAsync(string site, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (site != selected || Hit) return Task.CompletedTask;
            Hit = true; throw new IOException("owned handoff cut " + selected);
        }
    }
}
