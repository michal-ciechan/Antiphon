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

    // The oldest accepted inbound after the last outbound owns this silence episode.
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

        // Pre-upgrade withheld incidents used independent IDs. Match the committed stamp,
        // agent, session and complete native identity before attaching a recovery route.
        var pending = await db.Database.SqlQuery<PendingChannelPage>($"""
            SELECT a."Id", a."AgentId", q."AgentSessionId" AS "SessionId", q."Id" AS "QueueId",
                   a."Message", a."FailureReason"
            FROM "AgentIncidents" AS a
            JOIN "ChannelInbounds" AS i ON i."Id" = a."Id" OR (
                a."FailureReason" = {WithheldReason} AND i."AgentId" = a."AgentId"
                AND i."InputWithheldIncidentAt" = a."CreatedAt"
                AND starts_with(a."Message", 'Inbound message ' || i."Provider" || ':' || i."ConversationId" ||
                    '/' || i."NativeMessageId" || ' is withheld:'))
            LEFT JOIN "SessionQueuedMessages" AS q ON q."Id" = i."QueueMessageId"
            LEFT JOIN "ChatChannels" AS c ON c."Id" = i."ChatChannelId"
            WHERE a."Kind" = {(int)AgentIncidentKind.ChannelReplyLost}
              AND a."Severity" = {(int)AlertSeverity.Critical} AND a."HumanNotifiedAt" IS NULL
              AND a."FailureReason" IN ({WithheldReason}, {NoOutboundReason})
              AND (a."SessionId" IS NULL OR a."SessionId" = q."AgentSessionId")
              AND ((a."FailureReason" = {WithheldReason} AND q."Status" = 0 AND q."DeliveryAttempts" = 0)
                OR (c."Enabled" AND c."AgentId" = i."AgentId" AND
                    (c."LastReplyAt" IS NULL OR c."LastReplyAt" < i."AcceptedAt")))
            ORDER BY a."CreatedAt", a."Id" LIMIT {WithheldIncidentPageSize}
            """).ToListAsync(ct);
        foreach (var page in pending)
        {
            if (page.FailureReason == WithheldReason && !page.Message.Contains("/send-now", StringComparison.Ordinal))
            {
                var message = ColumnText.Clip(page.Message + ". " + ChannelRecoveryRoute(page.AgentId, page.SessionId,
                    page.QueueId), AgentIncident.MessageMaxLength);
                await db.AgentIncidents.Where(i => i.Id == page.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Message, message), ct);
            }
            // The attention client subscribes to AgentChanged. A crash after publishing may
            // repeat the invalidation. Clients refetch the same durable incident ID.
            await _eventBus.PublishToAllAsync("AgentChanged",
                new { agentId = page.AgentId, sessionId = page.SessionId, incidentId = page.Id }, ct);
            await db.AgentIncidents.Where(i => i.Id == page.Id && i.HumanNotifiedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.HumanNotifiedAt, now), ct);
        }
    }

    private async Task RecordOverdueGroupsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-_settings.GroupReplyDeadlineMinutes);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout = '100ms'; SET LOCAL statement_timeout = '2s'", ct);
        var overdue = await db.Database.SqlQuery<ReplyDeadlineRow>($"""
            SELECT i."Id", i."AgentId", i."Provider", i."ConversationId", i."NativeMessageId",
                   q."AgentSessionId" AS "SessionId",
                   CASE WHEN q."Status" = 0 AND q."DeliveryAttempts" = 0 THEN q."Id" END AS "PendingId"
            FROM "ChatChannels" AS c
            JOIN LATERAL (
                SELECT r."Id" FROM "ChannelInbounds" AS r
                WHERE r."Provider" = c."Provider" AND r."ConversationId" = c."ExternalId"
                  AND r."AgentId" = c."AgentId" AND r."ChatChannelId" = c."Id"
                  AND (c."LastReplyAt" IS NULL OR r."AcceptedAt" > c."LastReplyAt")
                ORDER BY r."AcceptanceSequence" LIMIT 1
            ) AS head ON true
            JOIN "ChannelInbounds" AS i ON i."Id" = head."Id"
            LEFT JOIN "SessionQueuedMessages" AS q ON q."Id" = i."QueueMessageId"
            WHERE c."Enabled" AND c."Provider" = 'telegram' AND c."Kind" = {(int)ChatChannelKind.Group}
              AND i."AcceptedAt" <= {cutoff} AND i."InputWithheldIncidentAt" IS NULL
              AND NOT EXISTS (SELECT 1 FROM "AgentIncidents" AS a WHERE a."Id" = i."Id")
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
    }
}
