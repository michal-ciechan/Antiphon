using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable handoff and transcript-only receipt. All typing belongs to the existing queue.</summary>
public sealed class AgentTaskLandNotificationService(AppDbContext db, SessionMessageQueueService messages,
    CompletionNoteFlushQueue flushes, AgentSessionRuntime runtime, TimeProvider clock, LandDeliveryBoundary? boundary = null,
    IRepositoryMutationLease? leases = null, IOptions<SupervisionSettings>? supervision = null,
    IAgentReportStore? reports = null)
{
    /// <summary>
    /// Kinds whose keyed row is also the caller's completion note: task-root conversation key,
    /// completion-note stamp. <see cref="LandNotificationKind.Completion"/> is CARD-0544 D-9.
    /// </summary>
    internal static bool IsCompletionNoteKind(LandNotificationKind kind) =>
        kind is LandNotificationKind.DeliveryFailure or LandNotificationKind.Completion;

    public async Task ReconcileAsync(Guid id, CancellationToken ct)
    {
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == id, ct);
        await db.Entry(note).ReloadAsync(ct);
        if (note.State is LandNotificationState.Confirmed or LandNotificationState.NotRequired or LandNotificationState.LegacyUnverified) return;
        if (note.IsLegacy && note.QueueMessageId is null) return; // Historical evidence never creates a new submission.
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
            // The producer can commit and deliver the keyed row before this outbox links it.
            // Recover that evidence even if the caller has since stopped; status gates new input only.
            if (note.QueueMessageId is null)
            {
                var existing = await db.SessionQueuedMessages.AsNoTracking()
                    .SingleOrDefaultAsync(m => m.SourceLandNotificationId == note.Id, ct);
                if (existing is not null)
                {
                    if (existing.AgentSessionId != session || existing.ContentDigest != note.ContentDigest)
                        throw new ConflictException("Land notification identity has a different destination or payload.");
                    note.QueueMessageId = existing.Id;
                    note.EnqueuedAt = existing.CreatedAt;
                    note.State = LandNotificationState.AwaitingReceipt;
                    note.LastErrorCode = null;
                    if (IsCompletionNoteKind(note.Kind))
                        await CompletionNoteStamp.ApplyAsync(db, note.TaskId, note.ContentDigest, existing.CreatedAt, ct);
                    await db.SaveChangesAsync(ct);
                }
            }
            if (note.QueueMessageId is null)
            {
                var destinationStatus = await db.AgentSessions.Where(s => s.Id == session).Select(s => s.Status).SingleAsync(ct);
                if (destinationStatus is SessionStatus.Stopped or SessionStatus.Failed)
                {
                    note.State = LandNotificationState.DestinationUnavailable;
                    note.LastErrorCode = "destination_" + destinationStatus.ToString().ToLowerInvariant();
                    note.LastErrorAt = now;
                    note.NextAttemptAt = now.AddMinutes(5);
                    await db.SaveChangesAsync(ct);
                    return;
                }
                // A scanning worker can see the commit before the producer leaves its lease.
                // Probe only terminal/conflict handoffs; a held note must remain deliverable while a lease is occupied.
                if (leases is not null && note.Kind is LandNotificationKind.Outcome or LandNotificationKind.Conflict)
                {
                    var repository = await db.AgentTasks.Where(t => t.Id == note.TaskId).Select(t => t.RepoPath).SingleAsync(ct);
                    if (repository is not null)
                    {
                        try
                        {
                            var released = await leases.TryAcquireAsync(repository, ct);
                            if (released is null) return;
                            await released.DisposeAsync();
                        }
                        catch (IOException)
                        {
                            // A failed land can leave RepoPath unreadable; the producer is no longer holding a lease.
                        }
                    }
                }
                // CARD-0544 D-9: a Completion enqueues its immutable snapshot rendering — header and
                // hold deadline fixed at settlement — after making sure the report it points at is
                // the snapshot's raw result, never whatever the task row holds now.
                TaskCompletionNotification.Snapshot? completion = null;
                if (note.Kind == LandNotificationKind.Completion)
                {
                    completion = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson);
                    var unresolved = completion is null ? "completion_snapshot_invalid"
                        : await EnsureSnapshotReportAsync(completion, ct);
                    if (unresolved is not null)
                    {
                        note.State = LandNotificationState.RetryPending;
                        note.LastErrorCode = unresolved;
                        note.LastErrorAt = now;
                        note.EnqueueAttempts++;
                        note.NextAttemptAt = now.AddSeconds(RetrySeconds(note.EnqueueAttempts));
                        await db.SaveChangesAsync(ct);
                        return;
                    }
                }
                if (boundary is not null) await boundary.ReachedAsync("before-enqueue", note.TaskId, note.Id, ct);
                note.EnqueueAttempts++;
                var conversationKey = IsCompletionNoteKind(note.Kind)
                    ? $"task:{await db.AgentTasks.Where(t => t.Id == note.TaskId).Select(t => t.RootTaskId).SingleAsync(ct):N}"
                    : $"land:{note.Id:N}";
                await messages.EnqueueAsync(session, note.Body, MessageSendMode.WhenIdle, ct,
                    QueuedMessageOrigin.Delegation, conversationKey, note.TaskId, note.ContentDigest,
                    completion?.NoteHeader ?? note.Body.Split('\n')[0], onCreated: message => note.QueueMessageId = message,
                    deliverIfIdle: false, sourceLandNotificationId: note.Id,
                    afterLandQueueInsert: boundary is null ? null : (queueId, token) => boundary.ReachedAsync("queue-inserted", note.TaskId, queueId, token),
                    holdUntil: completion is { DistillRequested: true, DistillMode: OutputDistillerMode.Apply } ? completion.DistillDeadlineAt : null);
                // This outbox kind is also the caller's completion note. Preserve the
                // check-suppression stamp, including recovery after a lost insert acknowledgement.
                if (IsCompletionNoteKind(note.Kind))
                    await CompletionNoteStamp.ApplyAsync(db, note.TaskId, note.ContentDigest, now, ct);
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
            else if (row.DeliveryVerdict == DeliveryVerdict.Truncated)
                note.LastErrorCode = "queue_truncated_unconfirmed";
            else if (row.Status == QueuedMessageStatus.Pending && row.DeliveryAttempts >= Math.Max(1,
                (supervision?.Value ?? new SupervisionSettings()).DeliveryVerification.MaxDeliveryAttempts))
                note.LastErrorCode = "queue_parked_unconfirmed";
            // CARD-0544 D-9: a Completion may legitimately be rendered (distilled, shrunk, batched,
            // spilled); its receipt compares the committed wire rendering instead of the raw Body.
            // Every other kind keeps the immutable-Body rule.
            var expected = note.Body;
            TaskCompletionNotification.Delivery? rendering = null;
            if (note.Kind == LandNotificationKind.Completion)
            {
                rendering = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson);
                if (rendering is null)
                {
                    if (row.DeliveryAttempts > 0)
                    {
                        note.LastErrorCode = "completion_rendering_unrecorded";
                        note.LastErrorAt = now;
                        await db.SaveChangesAsync(ct);
                        return;
                    }
                }
                else if (!rendering.MemberQueueIds.Contains(row.Id))
                {
                    note.LastErrorCode = "completion_rendering_membership_mismatch";
                    note.LastErrorAt = now;
                    await db.SaveChangesAsync(ct);
                    return;
                }
                else
                    expected = rendering.WireText;
            }
            else if (row.Body != note.Body) note.LastErrorCode = "queue_payload_changed_unconfirmed";
            if (row.DeliveryAttempts > 0)
            {
                await runtime.CatchUpTranscriptAsync(session, ct);
                var prompts = db.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == session
                    && p.Kind == TranscriptKinds.UserPrompt && p.Text != null);
                if (row.LastDeliveryBaselineSequence is long floor)
                    prompts = prompts.Where(p => p.Sequence > floor);
                else if (row.LastDeliveryStartedAt is DateTime started)
                {
                    var floorTime = started.AddSeconds(-Math.Max(0, (supervision?.Value ?? new SupervisionSettings())
                        .DeliveryVerification.UnobservableBaselineConfirmClockToleranceSeconds));
                    prompts = prompts.Where(p => p.Timestamp >= floorTime);
                }
                else return;
                var evidence = (await prompts.OrderBy(p => p.Sequence).ToListAsync(ct))
                    .FirstOrDefault(p => PromptSubmissionMatch.IsConfirmedBy(expected, p.Text!)
                        && PromptSubmissionMatch.IsCompleteIn(expected, p.Text!));
                // A pointer prompt proves receipt of the pointer only; the referenced file must still
                // hold exactly the content that was spilled behind it.
                if (evidence is not null && rendering?.SpillPath is { } spillPath
                    && !await FileHasSha256Async(spillPath, rendering.SpillSha256, ct))
                {
                    note.LastErrorCode = "completion_pointer_content_mismatch";
                    note.LastErrorAt = now;
                    evidence = null;
                }
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

    /// <summary>
    /// CARD-0544 G-112. When the frozen note names a report file, that file must hold the
    /// snapshot's raw result. A missing or different file is regenerated from the snapshot through
    /// the existing report store; a regenerated path that differs from the one the note names
    /// leaves delivery unresolved rather than pointing at other content.
    /// </summary>
    private async Task<string?> EnsureSnapshotReportAsync(TaskCompletionNotification.Snapshot snapshot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ReportFilePath)
            || !snapshot.RawBody.Contains(snapshot.ReportFilePath, StringComparison.Ordinal))
            return null;
        if (reports is null)
            return await FileHasSha256Async(snapshot.ReportFilePath, snapshot.RawSha256, ct) ? null : "completion_report_unavailable";
        if (await reports.IsUsableAsync(snapshot.ReportFilePath, snapshot.RawResult, ct))
            return null;
        var regenerated = await reports.StoreAsync(new AgentTask
        {
            Id = snapshot.TaskId,
            RootTaskId = snapshot.RootTaskId,
            Result = snapshot.RawResult,
            RepoPath = snapshot.RepoPath,
            WorkingDirectory = snapshot.WorkingDirectory,
            WorktreePath = snapshot.WorktreePath,
        }, ct);
        return regenerated.Path is { } path
            && string.Equals(Path.GetFullPath(path), Path.GetFullPath(snapshot.ReportFilePath), StringComparison.OrdinalIgnoreCase)
            ? null : "completion_report_unavailable";
    }

    internal static async Task<bool> FileHasSha256Async(string path, string? sha256, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return false;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
                .Equals(sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static int RetrySeconds(int attempt) => (int)Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attempt - 1, 0, 10)));
}
