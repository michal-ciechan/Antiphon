using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable handoff and transcript-only receipt. All typing belongs to the existing queue.</summary>
public sealed class AgentTaskLandNotificationService(AppDbContext db, SessionMessageQueueService messages,
    CompletionNoteFlushQueue flushes, AgentSessionRuntime runtime, TimeProvider clock, LandDeliveryBoundary? boundary = null,
    IRepositoryMutationLease? leases = null)
{
    public async Task ReconcileAsync(Guid id, CancellationToken ct)
    {
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == id, ct);
        await db.Entry(note).ReloadAsync(ct);
        if (note.State is LandNotificationState.Confirmed or LandNotificationState.NotRequired) return;
        var now = clock.GetUtcNow().UtcDateTime;
        if (note.QueueMessageId is null && note.NextAttemptAt > now) return;
        try
        {
            note.ConcurrencyToken = Guid.NewGuid();
            if (note.ParentSessionId is not Guid session
                || !await db.AgentSessions.AnyAsync(s => s.Id == session, ct))
            {
                note.State = LandNotificationState.DestinationUnavailable;
                note.LastErrorCode = "destination_unavailable";
                note.LastErrorAt = now;
                note.NextAttemptAt = now.AddMinutes(5);
                await db.SaveChangesAsync(ct);
                return;
            }
            if (note.QueueMessageId is null)
            {
                // A scanning worker can see the commit before the producer leaves its lease.
                // Probe only terminal/conflict handoffs; a held note must remain deliverable while a lease is occupied.
                if (leases is not null && note.Kind is LandNotificationKind.Outcome or LandNotificationKind.Conflict)
                {
                    var repository = await db.AgentTasks.Where(t => t.Id == note.TaskId).Select(t => t.RepoPath).SingleAsync(ct);
                    if (repository is not null)
                    {
                        var released = await leases.TryAcquireAsync(repository, ct);
                        if (released is null) return;
                        await released.DisposeAsync();
                    }
                }
                if (boundary is not null) await boundary.ReachedAsync("before-enqueue", note.TaskId, note.Id, ct);
                note.EnqueueAttempts++;
                await messages.EnqueueAsync(session, note.Body, MessageSendMode.WhenIdle, ct,
                    QueuedMessageOrigin.Delegation, $"land:{note.Id:N}", note.TaskId, note.ContentDigest,
                    note.Body.Split('\n')[0], onCreated: message => note.QueueMessageId = message,
                    deliverIfIdle: false, sourceLandNotificationId: note.Id,
                    afterLandQueueInsert: boundary is null ? null : (queueId, token) => boundary.ReachedAsync("queue-inserted", note.TaskId, queueId, token));
                note.EnqueuedAt = now;
                note.State = LandNotificationState.AwaitingReceipt;
                note.LastErrorCode = null;
                await db.SaveChangesAsync(ct);
                if (boundary?.DropWakeup("completion", note.Id) != true) flushes.TryEnqueue(session);
            }
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == note.QueueMessageId, ct);
            if (row is null)
            {
                note.LastErrorCode = "keyed_queue_row_missing";
                note.LastErrorAt = now;
                await db.SaveChangesAsync(ct);
                return; // Never replace a previously authoritative attempted/parked row.
            }
            if (row.Status == QueuedMessageStatus.Canceled)
            {
                note.State = LandNotificationState.Canceled;
                note.LastErrorCode = "queue_canceled_unconfirmed";
            }
            if (row.DeliveryAttempts > 0)
            {
                await runtime.CatchUpTranscriptAsync(session, ct);
                var prompts = db.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == session
                    && p.Kind == TranscriptKinds.UserPrompt && p.Text != null);
                if (row.LastDeliveryBaselineSequence is long floor)
                    prompts = prompts.Where(p => p.Sequence > floor);
                else if (row.LastDeliveryStartedAt is DateTime started)
                    prompts = prompts.Where(p => p.Timestamp >= started);
                else return;
                var evidence = (await prompts.OrderBy(p => p.Sequence).ToListAsync(ct))
                    .FirstOrDefault(p => PromptSubmissionMatch.IsConfirmedBy(row.Body, p.Text!)
                        && PromptSubmissionMatch.IsCompleteIn(row.Body, p.Text!));
                if (evidence is not null)
                {
                    if (boundary is not null) await boundary.ReachedAsync("receipt-before-save", note.TaskId, note.Id, ct);
                    note.ConfirmedAt = now;
                    note.ConfirmingPromptSequence = evidence.Sequence;
                    note.State = LandNotificationState.Confirmed;
                    note.LastErrorCode = null;
                }
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An insert can have committed even when its acknowledgement failed. Keep its key.
            db.ChangeTracker.Clear();
            note = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == id, ct);
            if (note.ConfirmedAt is not null) return;
            note.EnqueueAttempts++;
            note.ConcurrencyToken = Guid.NewGuid();
            note.State = note.QueueMessageId is null ? LandNotificationState.RetryPending : LandNotificationState.AwaitingReceipt;
            note.LastErrorCode = "notification_reconcile_failed:" + ex.GetType().Name;
            note.LastErrorAt = now;
            note.NextAttemptAt = now.AddSeconds(RetrySeconds(note.EnqueueAttempts));
            await db.SaveChangesAsync(ct);
        }
    }

    internal static int RetrySeconds(int attempt) => (int)Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attempt - 1, 0, 10)));
}
