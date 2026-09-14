using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0508 S2b. Captures immutable dispatch-base warning intent in the claim transaction
/// and materializes the Warning/notification pair later. Never types or enqueues.
/// </summary>
public sealed class DispatchBaseWarningIntentService(
    AppDbContext db,
    TimeProvider clock,
    LandDeliveryBoundary? boundary = null)
{
    internal async Task<IReadOnlyList<Guid>> CaptureAsync(
        AgentTask task,
        AgentTaskEvent dispatchEvent,
        IReadOnlyList<DispatchWarningDraft> drafts,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(dispatchEvent);
        ArgumentNullException.ThrowIfNull(drafts);
        if (dispatchEvent.AgentTaskId != task.Id)
            throw new ValidationException(nameof(dispatchEvent), "Dispatch event does not belong to the claimed task.");
        if (dispatchEvent.Type != AgentTaskEventType.Dispatched)
            throw new ValidationException(nameof(dispatchEvent), "Warning intent requires the final agent-dispatch event.");

        var createdAt = dispatchEvent.At == default ? clock.GetUtcNow().UtcDateTime : dispatchEvent.At;
        var ids = new List<Guid>(drafts.Count);
        foreach (var draft in drafts)
        {
            if (string.IsNullOrWhiteSpace(draft.WarningKey) || string.IsNullOrWhiteSpace(draft.Detail))
                continue;
            var existing = db.AgentTaskDispatchWarningIntents.Local
                .FirstOrDefault(i => i.DispatchEventId == dispatchEvent.Id && i.WarningKey == draft.WarningKey)
                ?? await db.AgentTaskDispatchWarningIntents
                    .FirstOrDefaultAsync(i => i.DispatchEventId == dispatchEvent.Id && i.WarningKey == draft.WarningKey, ct);
            if (existing is not null)
            {
                ids.Add(existing.Id);
                continue;
            }

            var warningId = Guid.NewGuid();
            var notificationId = Guid.NewGuid();
            var payload = DispatchBaseNotificationPayload.Capture(
                warningId, notificationId, task.Id, dispatchEvent.Id, task.Attempt, draft.WarningKey,
                task.ReplyTo, task.ParentSessionId, draft.Detail, createdAt);
            db.AgentTaskDispatchWarningIntents.Add(new AgentTaskDispatchWarningIntent
            {
                Id = payload.WarningEventId,
                DispatchEventId = payload.DispatchEventId,
                TaskId = payload.TaskId,
                Attempt = payload.Attempt,
                WarningKey = payload.WarningKey,
                NotificationId = payload.NotificationId,
                ReplyTo = payload.ReplyTo,
                ParentSessionId = payload.ParentSessionId,
                Detail = payload.Detail,
                Body = payload.Body,
                ContentDigest = payload.ContentDigest,
                CreatedAt = payload.CreatedAt,
                InitialState = payload.InitialState,
                MaterializationAttempts = 0,
                NextAttemptAt = payload.CreatedAt,
                ConcurrencyToken = Guid.NewGuid(),
            });
            ids.Add(warningId);
        }

        return ids;
    }

    public async Task MaterializeAsync(Guid intentId, CancellationToken ct)
    {
        if (boundary is not null)
            await boundary.ReachedAsync("dispatch-warning-before-materialize", Guid.Empty, intentId, ct);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AgentTaskDispatchWarningIntents\" WHERE \"Id\" = {intentId} FOR UPDATE", ct);
            var intent = await db.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == intentId, ct);
            await db.Entry(intent).ReloadAsync(ct);
            if (intent.MaterializedAt is not null)
            {
                await transaction.CommitAsync(ct);
                return;
            }

            if (!TryValidate(intent, out var error))
            {
                await transaction.RollbackAsync(ct);
                await RecordErrorAsync(intentId, error, ct);
                return;
            }

            var owner = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.Id == intent.DispatchEventId)
                .Select(e => (Guid?)e.AgentTaskId)
                .SingleOrDefaultAsync(ct);
            if (owner != intent.TaskId)
            {
                await transaction.RollbackAsync(ct);
                await RecordErrorAsync(intentId, "intent_dispatch_binding", ct);
                return;
            }

            var warningExists = await db.AgentTaskEvents.AnyAsync(e => e.Id == intent.Id, ct);
            var noteExists = await db.AgentTaskLandNotifications.AnyAsync(n => n.Id == intent.NotificationId, ct);
            if (warningExists || noteExists)
            {
                await transaction.RollbackAsync(ct);
                await RecordErrorAsync(intentId, "intent_projection_collision", ct);
                return;
            }

            db.AgentTaskEvents.Add(DispatchBaseNotificationPayload.WarningEvent(intent));
            db.AgentTaskLandNotifications.Add(DispatchBaseNotificationPayload.Materialize(intent));
            intent.MaterializedAt = clock.GetUtcNow().UtcDateTime;
            intent.ConcurrencyToken = Guid.NewGuid();
            if (boundary is not null)
                await boundary.ReachedAsync("dispatch-warning-before-commit", intent.TaskId, intent.Id, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (boundary is not null)
                await boundary.ReachedAsync("dispatch-warning-materialized", intent.TaskId, intent.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            await RecordErrorAsync(intentId, "intent_materialize_failed:" + ex.GetType().Name, ct);
        }
    }

    private static bool TryValidate(AgentTaskDispatchWarningIntent intent, out string error)
    {
        var expectedHeader = DispatchBaseNotificationPayload.Header(
            intent.NotificationId, intent.TaskId, intent.Id);
        var firstLine = intent.Body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')[0];
        if (!string.Equals(firstLine, expectedHeader, StringComparison.Ordinal))
        {
            error = "intent_header_mismatch";
            return false;
        }

        var expectedDigest = DispatchBaseNotificationPayload.Digest(
            intent.ReplyTo, intent.ParentSessionId, intent.Body);
        if (!string.Equals(intent.ContentDigest, expectedDigest, StringComparison.Ordinal))
        {
            error = "intent_digest_mismatch";
            return false;
        }

        error = "";
        return true;
    }

    public async Task ValidateDispatchBindingAsync(AgentTaskDispatchWarningIntent intent, CancellationToken ct)
    {
        var owner = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.Id == intent.DispatchEventId)
            .Select(e => e.AgentTaskId)
            .SingleOrDefaultAsync(ct);
        if (owner != intent.TaskId)
            throw new ValidationException(nameof(intent.DispatchEventId), "Dispatch event does not belong to the claimed task.");
    }

    private async Task RecordErrorAsync(Guid intentId, string error, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AgentTaskDispatchWarningIntents\" WHERE \"Id\" = {intentId} FOR UPDATE", ct);
            var intent = await db.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == intentId, ct);
            await db.Entry(intent).ReloadAsync(ct);
            if (intent.MaterializedAt is not null)
            {
                await transaction.CommitAsync(ct);
                return;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            intent.MaterializationAttempts++;
            intent.LastErrorCode = error;
            intent.LastErrorAt = now;
            intent.NextAttemptAt = now.AddSeconds(
                AgentTaskLandNotificationService.RetrySeconds(intent.MaterializationAttempts));
            intent.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            // Bookkeeping failure must not discharge the pending row; the next scan retries it.
            _ = ex;
        }
    }
}
