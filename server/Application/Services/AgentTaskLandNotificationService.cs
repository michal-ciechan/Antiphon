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
    IOptions<SupervisionSettings>? supervision = null,
    IAgentReportStore? reports = null, CheckCompactionBoundary? compactionBoundary = null,
    LandReceiptScanCache? scanCache = null)
{
    /// <summary>
    /// Kinds whose keyed row is also the caller's completion note: task-root conversation key,
    /// completion-note stamp. <see cref="LandNotificationKind.TaskCompletion"/> is CARD-0527's commit-outcome
    /// note and, when it carries a snapshot, the CARD-0544 D-9 obligation (<see cref="TaskCompletionNotification.IsProfiled"/>).
    /// </summary>
    internal static bool IsCompletionNoteKind(LandNotificationKind kind) =>
        kind is LandNotificationKind.DeliveryFailure or LandNotificationKind.TaskCompletion;

    /// <summary>
    /// CARD-0747. LastErrorCode of a terminal note settled <see cref="LandNotificationState.LegacyUnverified"/>
    /// because its outcome committed more than <c>LandNoteReceiptExpiryDays</c> ago without a receipt.
    /// </summary>
    public const string ReceiptExpiredCode = "receipt_expired_aged";

    /// <summary>
    /// CARD-0747. LastErrorCode of a Held snapshot settled <see cref="LandNotificationState.NotRequired"/>
    /// before typing because its land request already has a terminal outcome.
    /// </summary>
    public const string HeldSupersededCode = "held_superseded_by_terminal_outcome";

    private int MaxAttempts() => Math.Max(1,
        (supervision?.Value ?? new SupervisionSettings()).DeliveryVerification.MaxDeliveryAttempts);

    private static bool Actionable(SessionQueuedMessage row, int maxAttempts)
    {
        if (row.Status == QueuedMessageStatus.Canceled)
            return false;
        if (row.Status == QueuedMessageStatus.Pending && row.DeliveryAttempts >= maxAttempts)
            return false;
        if (row.Status == QueuedMessageStatus.Sent)
            return row.DeliveryVerdict is null;
        return row.Status == QueuedMessageStatus.Pending;
    }

    /// <summary>
    /// CARD-1705: how long one hosted scan waits for a caller's queue lock before leaving that
    /// caller's notices for the next scan.
    /// </summary>
    internal static readonly TimeSpan ScanQueueLockWait = TimeSpan.FromSeconds(1);

    /// <param name="busySessions">
    /// CARD-1705: set by the hosted scan only. Its enqueue waits at most <see cref="ScanQueueLockWait"/>
    /// for the caller's queue lock; a caller whose lock is still held joins this set, and its notices
    /// in the same scan are skipped unchanged, so they keep their order and retry on the next scan.
    /// Producers pass null and wait for the lock as before.
    /// </param>
    /// <param name="transcriptPullsThisPass">
    /// CARD-1773: set by the hosted scan only. The first note that needs a transcript for a session
    /// pulls it; later notes of that session in the same pass read the store. A direct reconcile
    /// passes null and pulls every time.
    /// </param>
    public async Task ReconcileAsync(Guid id, CancellationToken ct, ISet<Guid>? busySessions = null,
        ISet<Guid>? transcriptPullsThisPass = null)
    {
        var note = await LoadNoteAsync(id, ct);
        if (note.State is LandNotificationState.Confirmed or LandNotificationState.NotRequired
            or LandNotificationState.LegacyUnverified or LandNotificationState.Canceled)
        {
            scanCache?.Invalidate(note.Id);
            return;
        }
        if (note.Kind == LandNotificationKind.ProviderInputWait && note.ReplyTo != AgentTaskReplyTo.Session)
        {
            note.State = note.ReplyTo == AgentTaskReplyTo.None ? LandNotificationState.NotRequired : LandNotificationState.DestinationUnavailable;
            await db.SaveChangesAsync(ct);
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        // CARD-0747: an aged terminal note settles here instead of being typed weeks late.
        if (await ExpireOnReconcileAsync(note, now, busySessions, ct)) return;
        if (note.IsLegacy && note.QueueMessageId is null) return; // Historical evidence never creates a new submission.
        if (note.QueueMessageId is null && note.NextAttemptAt > now) return;
        (LandReceiptScanCache.Context Context, LandReceiptScanCache.StateStamp? Before, LandReceiptScanCache.StateStamp? After,
            bool Matched, long? CompletedAt)? negative = null;
        try
        {
            note.ConcurrencyToken = Guid.NewGuid();
            // CARD-1121 A-1: the one destination SELECT also projects the status and generation the
            // receipt scan cache binds; it adds no command.
            var destination = note.ParentSessionId is Guid parent
                ? await LandNotificationReconcileQueries.SessionScanById(db, parent, ct)
                : null;
            if (note.ParentSessionId is not Guid session || destination is null)
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
                var existing = await LandNotificationReconcileQueries.MessageBySourceNotification(db, note.Id, ct);
                if (existing is not null)
                {
                    if (existing.AgentSessionId != session || existing.ContentDigest != note.ContentDigest)
                        throw new ConflictException("Land notification identity has a different destination or payload.");
                    note.QueueMessageId = existing.Id;
                    note.EnqueuedAt = existing.CreatedAt;
                    note.State = LandNotificationState.AwaitingReceipt;
                    note.LastErrorCode = null;
                    if (IsCompletionNoteKind(note.Kind) && note.Kind != LandNotificationKind.LegacyCheckNote)
                        await CompletionNoteStamp.ApplyAsync(db, note.TaskId, note.ContentDigest, existing.CreatedAt, ct);
                    await db.SaveChangesAsync(ct);
                    // CARD-0481 F3 recovers a producer-committed row here; CARD-0550 observes the
                    // durable link through the same reuse boundary as keyed EnqueueAsync.
                    if (boundary is not null)
                        await boundary.ReachedAsync("queue-existing-key", note.TaskId, existing.Id, ct);
                    if (Actionable(existing, MaxAttempts()) && boundary?.DropWakeup("completion", note.Id) != true)
                        flushes.TryEnqueue(session);
                }
            }
            if (note.QueueMessageId is null)
            {
                // CARD-0747: a Held snapshot taken before the request's terminal outcome is not news.
                if (note.Kind == LandNotificationKind.Held && await RequestTerminalAsync(note, ct))
                {
                    await SettleHeldSupersededAsync(note, now, ct);
                    return;
                }
                var destinationStatus = await LandNotificationReconcileQueries.SessionStatusById(db, session, ct);
                if (destinationStatus is SessionStatus.Stopped or SessionStatus.Failed)
                {
                    note.State = LandNotificationState.DestinationUnavailable;
                    note.LastErrorCode = "destination_" + destinationStatus.ToString().ToLowerInvariant();
                    note.LastErrorAt = now;
                    note.NextAttemptAt = now.AddMinutes(5);
                    await db.SaveChangesAsync(ct);
                    return;
                }
                // CARD-0544 D-9: a Completion enqueues its immutable snapshot rendering — header and
                // hold deadline fixed at settlement — after making sure the report it points at is
                // the snapshot's raw result, never whatever the task row holds now.
                TaskCompletionNotification.Snapshot? completion = null;
                if (TaskCompletionNotification.IsProfiled(note))
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
                if (busySessions?.Contains(session) == true)
                {
                    db.ChangeTracker.Clear();
                    return;
                }
                if (boundary is not null) await boundary.ReachedAsync("before-enqueue", note.TaskId, note.Id, ct);
                if (note.Kind == LandNotificationKind.LegacyCheckNote && compactionBoundary is not null)
                    await compactionBoundary.ReachedAsync("before-note-enqueue", note.Id, ct);
                note.EnqueueAttempts++;
                var legacyCheck = note.Kind == LandNotificationKind.LegacyCheckNote;
                var conversationKey = legacyCheck
                    ? $"check:{note.TaskId:N}"
                    : IsCompletionNoteKind(note.Kind)
                    ? $"task:{await LandNotificationReconcileQueries.RootTaskIdById(db, note.TaskId, ct):N}"
                    : note.Kind == LandNotificationKind.ProviderInputWait ? $"provider-wait:{note.Id:N}" : $"land:{note.Id:N}";
                await messages.EnqueueAsync(session, note.Body, MessageSendMode.WhenIdle, ct,
                    legacyCheck ? QueuedMessageOrigin.Check : QueuedMessageOrigin.Delegation, conversationKey, note.TaskId, note.ContentDigest,
                    completion?.NoteHeader ?? note.Body.Split('\n')[0], onCreated: message => note.QueueMessageId = message,
                    deliverIfIdle: false, sourceLandNotificationId: note.Id,
                    afterLandQueueInsert: boundary is null ? null : (queueId, token) => boundary.ReachedAsync("queue-inserted", note.TaskId, queueId, token),
                    holdUntil: completion is { DistillRequested: true, DistillMode: OutputDistillerMode.Apply } ? completion.DistillDeadlineAt : null,
                    lockWait: busySessions is null ? null : ScanQueueLockWait);
                // This outbox kind is also the caller's completion note. Preserve the
                // check-suppression stamp, including recovery after a lost insert acknowledgement.
                if (IsCompletionNoteKind(note.Kind) && note.Kind != LandNotificationKind.LegacyCheckNote)
                    await CompletionNoteStamp.ApplyAsync(db, note.TaskId, note.ContentDigest, now, ct);
                note.EnqueuedAt = now;
                note.State = LandNotificationState.AwaitingReceipt;
                note.LastErrorCode = null;
                await db.SaveChangesAsync(ct);
                var created = note.QueueMessageId is Guid createdId
                    ? await LandNotificationReconcileQueries.MessageById(db, createdId, ct)
                    : throw new InvalidOperationException("Sequence contains no elements.");
                if (boundary is not null)
                    await boundary.ReachedAsync("queue-committed-before-wakeup", note.TaskId, created.Id, ct);
                if (Actionable(created, MaxAttempts()) && boundary?.DropWakeup("completion", note.Id) != true)
                    flushes.TryEnqueue(session);
            }
            var row = note.QueueMessageId is Guid queuedId
                ? await LandNotificationReconcileQueries.MessageOrDefaultById(db, queuedId, ct)
                : null;
            if (row is null)
            {
                note.LastErrorCode = "keyed_queue_row_missing";
                note.LastErrorAt = now;
                await db.SaveChangesAsync(ct);
                return; // Never replace a previously authoritative attempted/parked row.
            }
            if (note.Kind == LandNotificationKind.ProviderInputWait
                && (row.AgentSessionId != session || row.ContentDigest != note.ContentDigest))
            {
                note.LastErrorCode = "provider_wait_queue_identity_mismatch";
                note.LastErrorAt = now;
                await db.SaveChangesAsync(ct);
                return;
            }
            // CARD-0747: a completion row carried to a successor session was delivered there.
            var carried = ReceiptScanTarget.FollowsCarriedCompletion(note, row)
                ? await LandNotificationReconcileQueries.SessionScanById(db, row.AgentSessionId, ct)
                : null;
            var deliveredTo = carried is null ? session : row.AgentSessionId;
            if (TaskCompletionNotification.IsProfiled(note)
                && (row.AgentSessionId != deliveredTo || row.ContentDigest != note.ContentDigest
                    || row.SourceLandNotificationId != note.Id))
            {
                note.LastErrorCode = "completion_queue_identity_mismatch";
                note.LastErrorAt = now;
                await db.SaveChangesAsync(ct);
                return;
            }
            if (row.Status == QueuedMessageStatus.Canceled)
            {
                // CARD-1773. Canceled is terminal. A later prompt must not confirm the note.
                note.State = LandNotificationState.Canceled;
                note.LastErrorCode = "queue_canceled_unconfirmed";
                await db.SaveChangesAsync(ct);
                return;
            }
            if (note.Kind == LandNotificationKind.Held && row.Status == QueuedMessageStatus.Pending
                && row.DeliveryAttempts == 0 && await RequestTerminalAsync(note, ct))
            {
                // CARD-0747: never typed and already superseded. A row the queue took first stays.
                if (await messages.CancelPendingIfUntypedAsync(row.AgentSessionId, row.Id, ct,
                        busySessions is null ? null : ScanQueueLockWait))
                    await SettleHeldSupersededAsync(note, now, ct);
                else
                    db.ChangeTracker.Clear();
                return;
            }
            if (row.DeliveryVerdict == DeliveryVerdict.Truncated)
                note.LastErrorCode = "queue_truncated_unconfirmed";
            else if (row.Status == QueuedMessageStatus.Pending && row.DeliveryAttempts >= Math.Max(1,
                (supervision?.Value ?? new SupervisionSettings()).DeliveryVerification.MaxDeliveryAttempts))
                note.LastErrorCode = "queue_parked_unconfirmed";
            // CARD-0544 D-9: a Completion may legitimately be rendered (distilled, shrunk, batched,
            // spilled); its receipt compares the committed wire rendering instead of the raw Body.
            // Other kinds keep the immutable body, including when the queue typed its owned
            // spill pointer: the pointer is receipt only while its file still has those bytes.
            var expected = note.Body;
            TaskCompletionNotification.Delivery? rendering = null;
            if (TaskCompletionNotification.IsProfiled(note))
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
            else if (row.Body != note.Body)
            {
                var ownedPointer = row.Body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal)
                    && row.Body.Contains(TypedBodySpill.InboxRelativePath(row.Id.ToString("D")),
                        StringComparison.Ordinal);
                if (ownedPointer)
                {
                    var saved = row.RemoteSpillBody;
                    if (saved is null)
                    {
                        var cwd = await LandNotificationReconcileQueries.SessionCwdById(db, session, ct);
                        if (!string.IsNullOrWhiteSpace(cwd))
                        {
                            var path = TypedBodySpill.InboxAbsolutePath(cwd, row.Id.ToString("D"));
                            if (File.Exists(path)) saved = await File.ReadAllTextAsync(path, ct);
                        }
                    }
                    if (saved != note.Body)
                    {
                        note.LastErrorCode = "queue_pointer_content_mismatch";
                        note.LastErrorAt = now;
                        await db.SaveChangesAsync(ct);
                        return;
                    }
                    expected = row.Body;
                }
                else note.LastErrorCode = "queue_payload_changed_unconfirmed";
            }
            if (row.DeliveryAttempts > 0)
            {
                if (TaskCompletionNotification.IsProfiled(note) && row.LastDeliveryGeneration is null)
                {
                    note.LastErrorCode = "completion_delivery_generation_unknown";
                    note.LastErrorAt = now;
                    await db.SaveChangesAsync(ct);
                    return;
                }
                // A parked delegate's caller may restart while delivery is uncertain. A
                // prompt from that replacement generation cannot discharge the old attempt.
                if (note.Kind == LandNotificationKind.ProviderInputWait
                    && (row.LastDeliveryGeneration is not DateTime providerGeneration || !SessionGeneration.Equal(providerGeneration, destination.StartedAt)))
                {
                    note.LastErrorCode = "provider_wait_delivery_generation_unknown_or_changed";
                    note.LastErrorAt = now;
                    await db.SaveChangesAsync(ct);
                    return;
                }
                if (note.Kind == LandNotificationKind.TaskCompletion && row.LastDeliveryGeneration is DateTime deliveredGeneration
                    && (carried?.StartedAt ?? await LandNotificationReconcileQueries.SessionStartedAtById(db, session, ct)) != deliveredGeneration)
                {
                    note.LastErrorCode = "completion_delivery_generation_changed";
                    note.LastErrorAt = now;
                    await db.SaveChangesAsync(ct);
                    return;
                }
                // CARD-1157 D-1/D-3: a whitelisted keyed row is scanned on the session it was sent
                // to, when that session's row exists. Any other shape scans the parent with no added
                // command. Only a receipt found on the scanned session confirms.
                var scanSession = session;
                var scanStatus = destination.Status;
                var scanStartedAt = destination.StartedAt;
                if (ReceiptScanTarget.FollowsQueueDestination(note, row, expected))
                {
                    var followed = await LandNotificationReconcileQueries.SessionScanById(db, row.AgentSessionId, ct);
                    if (followed is not null)
                        (scanSession, scanStatus, scanStartedAt) = (row.AgentSessionId, followed.Status, followed.StartedAt);
                }
                else if (carried is not null)
                    (scanSession, scanStatus, scanStartedAt) = (row.AgentSessionId, carried.Status, carried.StartedAt);
                // CARD-1121: only a whitelisted shape bound to a terminal scanned session gets a scan
                // context, and its proof names the session the scan reads (CARD-1157 D-4). Every
                // other note keeps today's catch-up and scan.
                var scan = ReceiptScanContext(note, row, scanStatus, expected);
                if (scan is not null && scanSession != session) scan = scan with { ScanSessionId = scanSession };
                LandReceiptScanCache.StateStamp? current = null;
                // CARD-1773. One catch-up pull per session per hosted pass. Add before the await so a
                // thrown pull still bounds the rest of the pass. A direct reconcile passes null and pulls.
                var pullThisNote = transcriptPullsThisPass is null || transcriptPullsThisPass.Add(scanSession);
                if (scan is null)
                {
                    if (pullThisNote)
                        await runtime.CatchUpTranscriptAsync(scanSession, ct);
                }
                else if (pullThisNote)
                    current = ReceiptStamp(await runtime.CatchUpForReceiptAsync(scanSession,
                        scanStatus is SessionStatus.Stopped or SessionStatus.Failed, ct), scanStartedAt);
                else
                    current = ReceiptStamp(await runtime.ObserveReceiptStateAsync(scanSession, ct), scanStartedAt);
                // CARD-0641 D-2 kinds, destination and delivery floor: LandNoteReceipt, shared with
                // the CARD-0650 watchdog's read-only note-debt check.
                var prompts = LandNoteReceipt.Prompts(db.TranscriptEntries.AsNoTracking(), scanSession, note.IsLegacy,
                    note.Kind, row.LastDeliveryBaselineSequence, row.LastDeliveryStartedAt,
                    (supervision?.Value ?? new SupervisionSettings()).DeliveryVerification.UnobservableBaselineConfirmClockToleranceSeconds);
                if (prompts is null) return;
                ReceiptPrompt? evidence = null;
                // A proof omits exactly this one SELECT; the pull above, the bookkeeping and the
                // final UPDATE below run on a hit as on a miss.
                if (scan is null || current is null || !scanCache!.TryReuse(scan, current, out _))
                {
                    LandReceiptScanCache.StateStamp? before = null;
                    if (scan is not null)
                    {
                        if (boundary is not null) await boundary.ReachedAsync("receipt-scan-before-stamp", note.TaskId, note.Id, ct);
                        before = ReceiptStamp(await runtime.ObserveReceiptStateAsync(scanSession, ct), scanStartedAt);
                    }
                    evidence = await LandNoteReceipt.FirstReceiptAsync(prompts, expected, ct);
                    if (scan is not null)
                    {
                        // A certificate needs the same committed state on both sides of a scan that
                        // ran to exhaustion; it is published only after the final save below.
                        var matched = evidence is not null;
                        var completedAt = scanCache!.TryGetTimestamp();
                        LandReceiptScanCache.StateStamp? after = null;
                        if (!matched)
                        {
                            if (boundary is not null) await boundary.ReachedAsync("receipt-scan-exhausted", note.TaskId, note.Id, ct);
                            after = ReceiptStamp(await runtime.ObserveReceiptStateAsync(scanSession, ct), scanStartedAt);
                        }
                        // A-2/W-5: only this pass's catch-up stamp, unchanged at the before-stamp, can be
                        // certified. A NeedsReload persist (a 23505 recovered by a reseed), a retained
                        // failure or a commit after catch-up publishes nothing; the next pass rescans.
                        if (current is not null && before == current)
                            negative = (scan, before, after, matched, completedAt);
                        else
                            scanCache!.RecordRefusal("certificate:CatchUp");
                    }
                }
                // A pointer prompt proves receipt of the pointer only; the referenced file must still
                // hold exactly the content that was spilled behind it.
                if (evidence is not null && rendering?.SpillPath is { } spillPath
                    && !await FileHasSha256Async(spillPath, rendering.SpillSha256, ct))
                {
                    note.LastErrorCode = "completion_pointer_content_mismatch";
                    note.LastErrorAt = now;
                    evidence = null;
                }
                if (evidence is { } hit)
                {
                    if (boundary is not null) await boundary.ReachedAsync("receipt-before-save", note.TaskId, note.Id, ct);
                    if (note.Kind == LandNotificationKind.ProviderInputWait)
                    {
                        var receiptGeneration = await LandNotificationReconcileQueries.SessionStartedAtOrDefaultById(db, session, ct);
                        if (row.LastDeliveryGeneration is not DateTime attempted || receiptGeneration is not DateTime generation
                            || !SessionGeneration.Equal(attempted, generation)) return;
                    }
                    note.ConfirmedAt = now;
                    note.ConfirmingPromptSequence = hit.Sequence;
                    note.State = LandNotificationState.Confirmed;
                    note.LastErrorCode = null;
                }
            }
            await db.SaveChangesAsync(ct);
            if (scanCache is null) return;
            if (note.State == LandNotificationState.Confirmed)
                scanCache.Invalidate(note.Id);
            else if (negative is { } certificate)
            {
                if (boundary is not null) await boundary.ReachedAsync("receipt-scan-before-certificate", note.TaskId, note.Id, ct);
                scanCache.Publish(certificate.Context, certificate.Before, certificate.After, certificate.Matched,
                    saveCompleted: true, certificate.CompletedAt);
            }
        }
        catch (SessionQueueBusyException busy) when (busySessions is not null)
        {
            // Nothing was written under the lock. Not a failed attempt: no bookkeeping, no backoff.
            db.ChangeTracker.Clear();
            busySessions.Add(busy.SessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An insert can have committed even when its acknowledgement failed. Keep its key.
            db.ChangeTracker.Clear();
            note = await LoadNoteAsync(id, ct);
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

    /// <summary>CARD-0747. What <see cref="ExpireAgedAsync"/> did, or would do, with one note.</summary>
    public enum ExpiryOutcome { NotAged, NotOpen, NonTerminal, TypedPending, Changed, WouldSettle, Settled }

    /// <summary>
    /// CARD-0747 operator sweep. Applies the aged-note rule to one note created at or before
    /// <paramref name="cutoff"/>. Without <paramref name="apply"/> it only classifies and writes nothing.
    /// </summary>
    public async Task<(ExpiryOutcome Outcome, LandNotificationKind Kind, LandNotificationState State)> ExpireAgedAsync(
        Guid id, DateTime cutoff, bool apply, CancellationToken ct)
    {
        var note = await LoadNoteAsync(id, ct);
        var (kind, state) = (note.Kind, note.State);
        var outcome = await ExpireCoreAsync(note, cutoff, clock.GetUtcNow().UtcDateTime, apply, null, ct);
        return (outcome, kind, state);
    }

    private async Task<bool> ExpireOnReconcileAsync(AgentTaskLandNotification note, DateTime now, ISet<Guid>? busySessions,
        CancellationToken ct)
    {
        var days = (supervision?.Value ?? new SupervisionSettings()).DeliveryVerification.LandNoteReceiptExpiryDays;
        if (days <= 0) return false;
        try
        {
            var outcome = await ExpireCoreAsync(note, now.AddDays(-days), now, apply: true,
                busySessions is null ? null : ScanQueueLockWait, ct);
            if (outcome == ExpiryOutcome.Changed) db.ChangeTracker.Clear();
            return outcome is ExpiryOutcome.Settled or ExpiryOutcome.Changed;
        }
        catch (SessionQueueBusyException busy) when (busySessions is not null)
        {
            db.ChangeTracker.Clear();
            busySessions.Add(busy.SessionId);
            return true;
        }
    }

    /// <summary>
    /// Settles an open terminal note whose outcome committed at or before <paramref name="cutoff"/>.
    /// A never-typed Pending row is canceled under the queue lock first; a row typed once and still
    /// Pending is the queue's to finish, so that note is left alone.
    /// </summary>
    private async Task<ExpiryOutcome> ExpireCoreAsync(AgentTaskLandNotification note, DateTime cutoff, DateTime now, bool apply,
        TimeSpan? lockWait, CancellationToken ct)
    {
        if (note.CreatedAt > cutoff) return ExpiryOutcome.NotAged;
        if (note.State is not (LandNotificationState.AwaitingReceipt or LandNotificationState.DestinationUnavailable))
            return ExpiryOutcome.NotOpen;
        if (!await IsTerminalAsync(note, ct)) return ExpiryOutcome.NonTerminal;
        var row = note.QueueMessageId is Guid queued
            ? await LandNotificationReconcileQueries.MessageOrDefaultById(db, queued, ct)
            : await LandNotificationReconcileQueries.MessageBySourceNotification(db, note.Id, ct);
        if (row is { Status: QueuedMessageStatus.Pending, DeliveryAttempts: > 0 }) return ExpiryOutcome.TypedPending;
        if (!apply) return ExpiryOutcome.WouldSettle;
        if (row is { Status: QueuedMessageStatus.Pending }
            && !await messages.CancelPendingIfUntypedAsync(row.AgentSessionId, row.Id, ct, lockWait))
            return ExpiryOutcome.Changed;
        note.State = LandNotificationState.LegacyUnverified;
        note.LastErrorCode = ReceiptExpiredCode;
        note.LastErrorAt = now;
        note.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        scanCache?.Invalidate(note.Id);
        return ExpiryOutcome.Settled;
    }

    /// <summary>
    /// A land note is terminal once its request is no longer pending; a note without a request
    /// (completion, delivery failure, dispatch base) once its task is Succeeded, Failed or Canceled.
    /// A missing request or task, a provider wait and a legacy Check note never are.
    /// </summary>
    private async Task<bool> IsTerminalAsync(AgentTaskLandNotification note, CancellationToken ct)
    {
        if (note.Kind is LandNotificationKind.ProviderInputWait or LandNotificationKind.LegacyCheckNote) return false;
        if (note.RequestId is not null) return await RequestTerminalAsync(note, ct);
        var status = await db.AgentTasks.AsNoTracking().Where(t => t.Id == note.TaskId)
            .Select(t => (AgentTaskStatus?)t.Status).SingleOrDefaultAsync(ct);
        return status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;
    }

    private async Task<bool> RequestTerminalAsync(AgentTaskLandNotification note, CancellationToken ct) =>
        note.RequestId is Guid requestId
        && await db.AgentTaskLandRequests.AsNoTracking().Where(r => r.Id == requestId)
            .Select(r => (bool?)r.IsPending).SingleOrDefaultAsync(ct) == false;

    private async Task SettleHeldSupersededAsync(AgentTaskLandNotification note, DateTime now, CancellationToken ct)
    {
        note.State = LandNotificationState.NotRequired;
        note.LastErrorCode = HeldSupersededCode;
        note.LastErrorAt = now;
        await db.SaveChangesAsync(ct);
        scanCache?.Invalidate(note.Id);
    }

    /// <summary>
    /// One fresh tracked read. A context that does not already track the note (the hosted scan's
    /// per-note scope) uses the compiled query. A caller that already tracks it keeps that instance:
    /// reloading it is the single fresh read, so a stale concurrency token is not written back.
    /// </summary>
    private async Task<AgentTaskLandNotification> LoadNoteAsync(Guid id, CancellationToken ct)
    {
        var tracked = db.AgentTaskLandNotifications.Local.FirstOrDefault(n => n.Id == id);
        if (tracked is not null)
        {
            await db.Entry(tracked).ReloadAsync(ct);
            return tracked;
        }

        return await LandNotificationReconcileQueries.NoteById(db, id, ct);
    }

    /// <summary>
    /// CARD-0544 G-112. When the frozen note names a report file, that file must hold the
    /// snapshot's raw result. A missing or different file is regenerated from the snapshot through
    /// the existing report store; a regenerated path that differs from the one the note names
    /// leaves delivery unresolved rather than pointing at other content.
    /// CARD-1605 bounds that one store attempt at five seconds. The preliminary usability read
    /// stays on the caller token. Allowance expiry becomes completion_report_unavailable so the
    /// existing retry row can be saved; caller shutdown still propagates.
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
        using var allowance = CancellationTokenSource.CreateLinkedTokenSource(ct);
        allowance.CancelAfter(SnapshotReportStoreAllowance);
        try
        {
            var regenerated = await reports.StoreAsync(new AgentTask
            {
                Id = snapshot.TaskId,
                RootTaskId = snapshot.RootTaskId,
                Result = snapshot.RawResult,
                RepoPath = snapshot.RepoPath,
                WorkingDirectory = snapshot.WorkingDirectory,
                WorktreePath = snapshot.WorktreePath,
            }, allowance.Token);
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            if (allowance.IsCancellationRequested) return "completion_report_unavailable";
            return regenerated.Path is { } path
                && string.Equals(Path.GetFullPath(path), Path.GetFullPath(snapshot.ReportFilePath), StringComparison.OrdinalIgnoreCase)
                ? null : "completion_report_unavailable";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "completion_report_unavailable";
        }
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

    // CARD-1121 W-10: a cache fault is a refusal, never a failed note; the caller keeps today's scan.
    private LandReceiptScanCache.Context? ReceiptScanContext(AgentTaskLandNotification note, SessionQueuedMessage row,
        SessionStatus destinationStatus, string expected)
    {
        if (scanCache is null) return null;
        try
        {
            if (LandReceiptScanCache.TryBuildContext(note, row, destinationStatus, expected, out var context, out var refusal))
                return context;
            scanCache.RecordRefusal(refusal);
        }
        catch (Exception) { }
        return null;
    }

    private LandReceiptScanCache.StateStamp? ReceiptStamp(LandReceiptScanCache.Observation observation, DateTime? destinationStartedAt)
    {
        try
        {
            if (LandReceiptScanCache.StateStamp.TryCreate(observation, destinationStartedAt, out var stamp, out var refusal))
                return stamp;
            scanCache?.RecordRefusal(refusal);
        }
        catch (Exception) { }
        return null;
    }

    internal static int RetrySeconds(int attempt) => (int)Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attempt - 1, 0, 10)));

    /// <summary>Total preparation-plus-I/O allowance for one snapshot regeneration store attempt.</summary>
    private static readonly TimeSpan SnapshotReportStoreAllowance = TimeSpan.FromSeconds(5);
}
