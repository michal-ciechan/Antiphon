using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

public sealed partial class ChannelBridgeService
{
    internal const string WithheldReason = "ChannelInputWithheld";
    internal const string NoOutboundReason = "ChannelNoOutboundReply";

    private Guid _withheldClosureCursor;

    private static string ChannelRecoveryRoute(Guid agent, Guid? session, Guid? pending)
    {
        if (session is not Guid id)
            return $"Inspect GET /api/agents/{agent:D}. If stopped, resume with POST /api/agents/{agent:D}/start {{}}.";
        var resume = $"For a verified idle session that still needs recovery, POST /api/agents/{agent:D}/stop, "
            + $"then POST /api/agents/{agent:D}/start with {{\"resumeSessionId\":\"{id:D}\"}}.";
        return pending is Guid row
            ? $"Once the guard clears, retry POST /api/sessions/{id:D}/messages/{row:D}/send-now. {resume}"
            : $"Inspect GET /api/sessions/{id:D}/transcript for the owning turn. {resume}";
    }

    // The oldest accepted inbound still awaiting its correlated reply owns the deadline.
    // More inbounds cannot reset the deadline. Select only current, enabled bindings.
    internal async Task ScanChannelReplyAlertsAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_settings.GroupReplyDeadlineMinutes > 0)
        {
            try { await RecordOverdueGroupsAsync(db, now, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // The existing withheld incidents still need their push if this lookup times out.
                db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Group reply deadline scan failed; retrying");
            }
        }

        try { await CloseDeliveredWithholdsAsync(db, scope.ServiceProvider.GetRequiredService<ChannelReplyDispatcher>(), now, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Withheld closure scan failed; retrying");
        }

        // Pre-upgrade withheld incidents used independent IDs. Match the committed stamp,
        // agent, session and complete native identity before attaching a recovery route.
        var pending = await db.Database.SqlQuery<PendingChannelPage>($"""
            SELECT a."Id", a."AgentId", q."AgentSessionId" AS "SessionId", q."Id" AS "QueueId",
                   a."Message", a."FailureReason", a."CreatedAt"
            FROM "AgentIncidents" AS a
            JOIN "ChannelInbounds" AS i ON i."Id" = a."Id" OR (a."FailureReason" = {NoOutboundReason}
                AND a."Id" = md5('channel-no-outbound/' || i."Id"::text)::uuid) OR (
                a."FailureReason" = {WithheldReason} AND i."AgentId" = a."AgentId"
                AND i."InputWithheldIncidentAt" = a."CreatedAt"
                AND starts_with(a."Message", 'Inbound message ' || i."Provider" || ':' || i."ConversationId" ||
                    '/' || i."NativeMessageId" || ' is withheld:'))
            LEFT JOIN "SessionQueuedMessages" AS q ON q."Id" = i."QueueMessageId"
            LEFT JOIN "ChatChannels" AS c ON c."Id" = i."ChatChannelId"
            LEFT JOIN "ChannelOutboundDeliveries" AS d ON d."Id" = q."ChannelOutboundDeliveryId"
            WHERE a."Kind" = {(int)AgentIncidentKind.ChannelReplyLost}
              AND a."Severity" = {(int)AlertSeverity.Critical} AND a."HumanNotifiedAt" IS NULL
              AND a."FailureReason" IN ({WithheldReason}, {NoOutboundReason})
              AND NOT EXISTS (SELECT 1 FROM "AgentIncidents" AS closed WHERE closed."Kind" = {(int)AgentIncidentKind.ChannelInputWithheldClosed}
                  AND closed."FailureReason" = a."Id"::text)
              AND (a."SessionId" IS NULL OR a."SessionId" = q."AgentSessionId")
              AND ((a."FailureReason" = {WithheldReason} AND q."Status" = 0 AND q."DeliveryAttempts" = 0)
                OR (a."FailureReason" = {NoOutboundReason} AND c."Enabled" AND c."AgentId" = i."AgentId" AND
                    d."State" IS DISTINCT FROM {(int)ChannelOutboundDeliveryState.Suppressed} AND NOT EXISTS (
                        SELECT 1 FROM "ChannelProviderAnswers" AS receipt WHERE receipt."Provider" = i."Provider"
                        AND receipt."ConversationId" = i."ConversationId" AND receipt."NativeMessageId" = i."NativeMessageId")))
            ORDER BY a."CreatedAt", a."Id" LIMIT {WithheldIncidentPageSize}
            """).ToListAsync(ct);
        foreach (var page in pending)
        {
            var detail = page.Message;
            if (page.FailureReason == WithheldReason && !page.Message.Contains("/send-now", StringComparison.Ordinal))
            {
                detail = ColumnText.Clip(page.Message + ". " + ChannelRecoveryRoute(page.AgentId, page.SessionId,
                    page.QueueId), AgentIncident.MessageMaxLength);
                await db.AgentIncidents.Where(i => i.Id == page.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Message, detail), ct);
            }
            // AlertRaised displays the operator toast and, for a hidden tab, a desktop notification.
            // The append-only incident owns persistence. Chat alert routers do not participate.
            var alert = new
            {
                id = page.Id, severity = nameof(AlertSeverity.Critical), source = "ChannelBridge",
                title = page.FailureReason == WithheldReason ? "Channel input withheld" : "Channel reply overdue",
                detail, agentId = page.AgentId, createdAt = page.CreatedAt,
            };
            var open = db.AgentIncidents.Where(i => i.Id == page.Id && i.HumanNotifiedAt == null);
            if (await open.Where(i => i.OperatorToastClaimedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.OperatorToastClaimedAt, now), ct) == 1)
            {
                try { await _eventBus.PublishToAllAsync("AlertRaised", alert, ct); }
                catch
                {
                    await open.ExecuteUpdateAsync(s => s.SetProperty(i => i.OperatorToastClaimedAt, (DateTime?)null), ct);
                    throw;
                }
            }
            // A stopped publisher can leave a durable claim before the toast reaches the bus.
            // The attention invalidation also carries that toast, keyed by the same incident ID.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await open.ExecuteUpdateAsync(s => s.SetProperty(i => i.HumanNotifiedAt, now), ct) == 1)
                await _eventBus.PublishToAllAsync("AgentChanged",
                    new { agentId = page.AgentId, sessionId = page.SessionId, incidentId = page.Id, operatorAlert = alert }, ct);
            await tx.CommitAsync(ct);
        }
    }

    private async Task CloseDeliveredWithholdsAsync(AppDbContext db, ChannelReplyDispatcher dispatcher,
        DateTime now, CancellationToken ct)
    {
        var closing = await ChannelWithheldResolution.Candidates(db)
            .Where(c => c.IncidentId.CompareTo(_withheldClosureCursor) > 0)
            .OrderBy(c => c.IncidentId).Take(WithheldIncidentPageSize).ToListAsync(ct);
        foreach (var candidate in closing)
        {
            if (await ChannelWithheldResolution.CloseWithEvidenceAsync(db, dispatcher, candidate, null, now, ct) is Guid closed)
                await _eventBus.PublishToAllAsync("AgentChanged", new { agentId = candidate.AgentId, incidentId = closed }, ct);
        }
        _withheldClosureCursor = closing.Count == WithheldIncidentPageSize ? closing[^1].IncidentId : Guid.Empty;
    }

    private async Task RecordOverdueGroupsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var duration = TimeSpan.FromMinutes(_settings.GroupReplyDeadlineMinutes);
        var timestamp = _timeProvider.GetTimestamp();
        var cutoffStamp = timestamp - (long)(duration.TotalSeconds * _timeProvider.TimestampFrequency);
        // Across process epochs, bootstrap age from the durable acceptance time once against
        // the start-of-process UTC clock. Elapsed time drives both cutoffs thereafter.
        var cutoff = (_createdAt + _timeProvider.GetElapsedTime(_createdTimestamp, timestamp) - duration).UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout = '100ms'; SET LOCAL statement_timeout = '2s'", ct);
        var overdue = await db.Database.SqlQuery<ReplyDeadlineRow>($"""
            SELECT identity."Id", i."AgentId", i."Provider", i."ConversationId", i."NativeMessageId",
                   q."AgentSessionId" AS "SessionId",
                   CASE WHEN q."Status" = 0 AND q."DeliveryAttempts" = 0 THEN q."Id" END AS "PendingId"
            FROM "ChatChannels" AS c
            JOIN LATERAL (
                SELECT r."Id" FROM "ChannelInbounds" AS r
                LEFT JOIN "SessionQueuedMessages" AS source ON source."Id" = r."QueueMessageId"
                LEFT JOIN "ChannelOutboundDeliveries" AS delivery ON delivery."Id" = source."ChannelOutboundDeliveryId"
                WHERE r."Provider" = c."Provider" AND r."ConversationId" = c."ExternalId"
                  AND r."AgentId" = c."AgentId" AND r."ChatChannelId" = c."Id"
                  AND (r."QueueMessageId" IS NULL OR source."Id" IS NOT NULL)
                  AND delivery."State" IS DISTINCT FROM {(int)ChannelOutboundDeliveryState.Suppressed}
                  AND NOT EXISTS (SELECT 1 FROM "ChannelProviderAnswers" AS receipt
                      WHERE receipt."Provider" = r."Provider" AND receipt."ConversationId" = r."ConversationId"
                      AND receipt."NativeMessageId" = r."NativeMessageId")
                ORDER BY r."AcceptanceSequence" LIMIT 1
            ) AS head ON true
            JOIN "ChannelInbounds" AS i ON i."Id" = head."Id"
            LEFT JOIN "SessionQueuedMessages" AS q ON q."Id" = i."QueueMessageId"
            CROSS JOIN LATERAL (SELECT CASE WHEN i."InputWithheldIncidentAt" IS NULL THEN i."Id"
                ELSE md5('channel-no-outbound/' || i."Id"::text)::uuid END AS "Id") AS identity
            WHERE c."Enabled" AND c."Provider" = 'telegram' AND c."Kind" = {(int)ChatChannelKind.Group}
              AND CASE WHEN i."ReplyClockId" = {_replyClockId} AND i."ReplyClockTimestamp" IS NOT NULL
                  THEN i."ReplyClockTimestamp" <= {cutoffStamp} ELSE i."AcceptedAt" <= {cutoff} END
              AND (i."InputWithheldIncidentAt" IS NULL OR (q."Status" = {(int)QueuedMessageStatus.Sent}
                  AND q."DeliveryVerdict" IN ({(int)DeliveryVerdict.Delivered},{(int)DeliveryVerdict.LateConfirmed})))
              AND NOT EXISTS (SELECT 1 FROM "AgentIncidents" AS a WHERE a."Id" = identity."Id")
            ORDER BY i."AcceptanceSequence" LIMIT {WithheldIncidentPageSize}
            FOR UPDATE OF i SKIP LOCKED
            """).ToListAsync(ct);
        // A concurrent scanner can commit between our query snapshot and its inbound lock.
        var ids = overdue.Select(r => r.Id).ToArray();
        var claimed = await db.AgentIncidents.Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToHashSetAsync(ct);
        foreach (var row in overdue.Where(r => !claimed.Contains(r.Id)))
        {
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = row.Id, AgentId = row.AgentId, SessionId = row.SessionId,
                Severity = AlertSeverity.Critical, Kind = AgentIncidentKind.ChannelReplyLost,
                FailureReason = NoOutboundReason, CreatedAt = now,
                Message = $"No outbound reply since inbound {row.Provider}:{row.ConversationId}/{row.NativeMessageId}. "
                    + ChannelRecoveryRoute(row.AgentId, row.SessionId, row.PendingId),
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private sealed class ReplyDeadlineRow
    {
        public Guid Id { get; set; }
        public Guid AgentId { get; set; }
        public Guid? SessionId { get; set; }
        public Guid? PendingId { get; set; }
        public string Provider { get; set; } = "";
        public string ConversationId { get; set; } = "";
        public string NativeMessageId { get; set; } = "";
    }

    private sealed class PendingChannelPage
    {
        public Guid Id { get; set; }
        public Guid AgentId { get; set; }
        public Guid? SessionId { get; set; }
        public Guid? QueueId { get; set; }
        public string Message { get; set; } = "";
        public string FailureReason { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
