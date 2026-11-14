using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    /// <summary>An explicit operator decision, never a delivery verdict or an input operation.</summary>
    public async Task<AbandonQueuedMessageResult> AbandonAsync(
        Guid sessionId, Guid messageId, string? reason, string abandonedBy, CancellationToken ct)
    {
        var why = reason?.Trim();
        if (string.IsNullOrWhiteSpace(why) || why.Length > 1000)
            throw new ValidationException("reason", "An operator reason of 1 to 1000 characters is required.");
        if (string.IsNullOrWhiteSpace(abandonedBy) || abandonedBy.Length > 200)
            throw new ValidationException("operator", "An authenticated operator is required.");

        AbandonQueuedMessageResult result;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Same queue -> session lock order as standing history selection. Confirmation
            // owns this queue semaphore too, so its verdict is rechecked before cancellation.
            _ = await db.AgentSessions.FromSqlInterpolated(
                $"SELECT * FROM \"AgentSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new NotFoundException(nameof(AgentSession), sessionId);
            var message = await db.SessionQueuedMessages.SingleOrDefaultAsync(
                m => m.Id == messageId && m.AgentSessionId == sessionId, ct)
                ?? throw new NotFoundException(nameof(SessionQueuedMessage), messageId);
            var auditKey = $"operator-abandon:{messageId}; session={sessionId};";
            if (message.Status == QueuedMessageStatus.Canceled && message.DeliveryVerdict == null && message.CanceledAt is { } canceled
                && await db.AuditRecords.AnyAsync(i => i.Id == messageId
                    && i.EventType == AuditEventType.ToolInvocation && i.Summary.StartsWith(auditKey), ct))
            {
                result = AbandonResult(sessionId, messageId, canceled);
            }
            else
            {
                if (message.Status != QueuedMessageStatus.Sent || message.DeliveryVerdict != null || message.DeliveryAttempts < 1)
                    throw new ConflictException(
                        "Only a Sent message with an unconfirmed delivery attempt can be abandoned. Use ordinary DELETE cancellation for Pending messages.",
                        "queue_abandon_not_unconfirmed");
                // PostgreSQL persists microseconds; return that same instant on the first
                // request and every idempotent retry.
                var now = new DateTime(UtcNow().Ticks / 10 * 10, DateTimeKind.Utc);
                message.Status = QueuedMessageStatus.Canceled;
                message.CanceledAt = now;
                // Keep attempt identity and null verdict: cancellation makes no delivery claim.
                // The permanent relational summary is the idempotency witness; incident
                // recency/retention must not make an already-abandoned message actionable again.
                db.AuditRecords.Add(new AuditRecord
                {
                    Id = messageId, EventType = AuditEventType.ToolInvocation, CreatedAt = now,
                    Summary = $"{auditKey} abandoned by {abandonedBy.Trim()}. Reason: {why}. "
                        + "The message may have been delivered to the agent; abandonment does not recall it. No input was sent."
                });
                await db.SaveChangesAsync(ct);
                result = AbandonResult(sessionId, messageId, now);
            }
            await tx.CommitAsync(ct);
        }
        finally { sem.Release(); }

        await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        return result;
    }

    private static AbandonQueuedMessageResult AbandonResult(Guid sessionId, Guid messageId, DateTime at) =>
        new(sessionId, messageId, nameof(QueuedMessageStatus.Canceled), at,
            "The message may have been delivered to the agent. Abandoning does not recall it; no input was sent.");
}
