using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.Application;

public sealed partial class C0824RefOnlyLandingTests
{
    private static async Task NegativeReceiptEvidenceAsync(LandingSafetyHarness h, BridgeQueueHarness caller,
        AgentTaskLandNotification note, CompletionNoteFlushQueue flushes, ScaledTimeProvider clock)
    {
        var otherSession = Guid.NewGuid();
        await using (var db = h.CreateContext())
        {
            db.AgentSessions.Add(new AgentSession { Id = otherSession, Cwd = caller.TempRoot, Status = SessionStatus.Running,
                CreatedAt = clock.GetUtcNow().UtcDateTime, StartedAt = clock.GetUtcNow().UtcDateTime, LastSeenAt = clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync();
        }
        foreach (var variant in new[] { "wrong-destination", "wrong-digest" })
        {
            clock.Advance(TimeSpan.FromMinutes(6));
            var floor = await caller.CurrentTranscriptMaxSequenceAsync();
            await using (var db = h.CreateContext())
            {
                var saved = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
                saved.QueueMessageId = null; saved.NextAttemptAt = clock.GetUtcNow().UtcDateTime; saved.State = LandNotificationState.Queued;
                var row = await db.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
                row.AgentSessionId = variant == "wrong-destination" ? otherSession : caller.SessionId;
                row.ContentDigest = variant == "wrong-digest" ? DelegationNoteDigest.Compute("foreign digest") : note.ContentDigest;
                row.Status = QueuedMessageStatus.Sent; row.DeliveryAttempts = 1; row.DeliveryVerdict = DeliveryVerdict.Delivered;
                row.LastDeliveryBaselineSequence = floor; row.LastDeliveryStartedAt = clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
            // This complete text is negative evidence for a differently bound row, never the positive delivery.
            await caller.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, note.Body, timestamp: clock.GetUtcNow().UtcDateTime);
            await ReconcileAsync();
            await using (var db = h.CreateContext())
            {
                var saved = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
                saved.ConfirmedAt.ShouldBeNull(variant + "-unconfirmed");
                saved.QueueMessageId.ShouldBeNull(variant + "-not-linked");
                saved.LastErrorCode.ShouldBe("notification_reconcile_failed:ConflictException");
                var row = await db.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
                row.AgentSessionId = caller.SessionId; row.ContentDigest = note.ContentDigest;
                row.Status = QueuedMessageStatus.Pending; row.DeliveryAttempts = 0; row.DeliveryVerdict = null;
                row.LastDeliveryBaselineSequence = null; row.LastDeliveryStartedAt = null;
                saved.NextAttemptAt = clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync();
            }
        }
        await ReconcileAsync();
        foreach (var variant in new[] { "sent-without-prompt", "wrong-session", "wrong-identity", "truncated-middle", "equal-floor", "old-time" })
        {
            clock.Advance(TimeSpan.FromMinutes(2)); // Prior negative bodies cannot qualify the timestamp-only case.
            var floor = await caller.CurrentTranscriptMaxSequenceAsync();
            await using (var db = h.CreateContext())
            {
                var row = await db.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
                row.Status = QueuedMessageStatus.Sent; row.DeliveryAttempts = 1; row.DeliveryVerdict = DeliveryVerdict.Delivered;
                row.LastDeliveryBaselineSequence = variant == "old-time" ? null : variant == "equal-floor" ? floor + 1 : floor;
                row.LastDeliveryStartedAt = clock.GetUtcNow().UtcDateTime;
                if (variant != "sent-without-prompt")
                {
                    var text = variant == "wrong-identity" ? note.Body.Replace(note.Id.ToString("N"), Guid.NewGuid().ToString("N")) : note.Body;
                    if (variant == "truncated-middle")
                    {
                        var normalized = PromptSubmissionMatch.Normalize(note.Body);
                        normalized.Length.ShouldBeGreaterThan(PromptSubmissionMatch.MatchWindowChars + 80, "discriminating-long-body");
                        text = normalized.Remove(PromptSubmissionMatch.MatchWindowChars + 20, 20);
                        PromptSubmissionMatch.IsConfirmedBy(note.Body, text).ShouldBeTrue("truncated-head-still-matches");
                        PromptSubmissionMatch.IsCompleteIn(note.Body, text).ShouldBeFalse("truncated-middle-is-incomplete");
                    }
                    db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(),
                        AgentSessionId = variant == "wrong-session" ? otherSession : caller.SessionId,
                        Sequence = floor + 1, Kind = TranscriptKinds.UserPrompt, Text = text,
                        Timestamp = variant == "old-time" ? clock.GetUtcNow().UtcDateTime.AddHours(-1) : clock.GetUtcNow().UtcDateTime,
                        CreatedAt = clock.GetUtcNow().UtcDateTime });
                }
                await db.SaveChangesAsync();
            }
            await ReconcileAsync();
            await using var read = h.CreateContext();
            (await read.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).ConfirmedAt
                .ShouldBeNull(variant + "-unconfirmed");
        }
        await using (var db = h.CreateContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
            row.Status = QueuedMessageStatus.Pending; row.DeliveryAttempts = 0; row.DeliveryVerdict = null;
            row.LastDeliveryBaselineSequence = null; row.LastDeliveryStartedAt = null;
            await db.SaveChangesAsync();
        }
        async Task ReconcileAsync()
        {
            await using var db = h.CreateContext();
            await new AgentTaskLandNotificationService(db, caller.Queue, flushes, caller.Runtime, clock).ReconcileAsync(note.Id, default);
        }
    }
}
