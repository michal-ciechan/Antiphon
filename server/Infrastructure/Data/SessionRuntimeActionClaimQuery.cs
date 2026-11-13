using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Infrastructure.Data;

internal static class SessionRuntimeActionClaimQuery
{
    // One statement/snapshot, as with the original LINQ selector. Execution ownership and
    // retry/phase transitions remain with the coordinator; this SELECT takes no row locks.
    // Expand only this session's channel keys once, rather than OR-correlating the queue
    // table repeatedly (even when no channel action exists).
    private const string Sql = """
        WITH candidates AS MATERIALIZED (
            SELECT * FROM "SessionRuntimeActions" WHERE "AgentSessionId" = @sessionId
        ), channel AS MATERIALIZED (
            SELECT * FROM candidates WHERE "TriggerKind" = @channelKind
        ), destinations AS MATERIALIZED (
            SELECT "Id" AS action_id, "ConversationKey" AS destination
            FROM channel WHERE "ConversationKey" IS NOT NULL
            UNION
            SELECT c."Id", m."ConversationKey"
            FROM channel c
            CROSS JOIN LATERAL unnest(c."LateConfirmedChannelMessageIds") AS late(id)
            JOIN "SessionQueuedMessages" m ON m."Id" = late.id AND m."AgentSessionId" = @sessionId
            WHERE m."ConversationKey" IS NOT NULL
        )
        SELECT a.* FROM candidates a
        WHERE a."TriggerKind" <> @channelKind
            OR (a."DueAt" <= @now AND NOT EXISTS (
                SELECT 1 FROM channel b WHERE b."Sequence" < a."Sequence"
                    AND (b."ConversationKey" IS NULL OR a."ConversationKey" IS NULL
                        OR EXISTS (SELECT 1 FROM destinations da JOIN destinations db
                            ON db.destination = da.destination
                            WHERE da.action_id = a."Id" AND db.action_id = b."Id"))))
        ORDER BY (a."Phase" >= @queuePhase), a."Sequence", a."TriggerKind"
        LIMIT 1
        """;

    internal static IQueryable<SessionRuntimeAction> Query(AppDbContext db, Guid sessionId, DateTime now) =>
        db.SessionRuntimeActions.FromSqlRaw(Sql,
            new NpgsqlParameter("sessionId", NpgsqlDbType.Uuid) { Value = sessionId },
            new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now },
            new NpgsqlParameter("queuePhase", NpgsqlDbType.Integer) { Value = SessionRuntimeActionCoordinator.QueuePhase },
            new NpgsqlParameter("channelKind", NpgsqlDbType.Text) { Value = SessionRuntimeActionCoordinator.ChannelReplyDebt });
}
