using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>Indexed, statement-snapshot working state over persisted transcript rows.</summary>
internal static class TranscriptWorkingStateQuery
{
    // Trusted classification literals must stay literal in SQL: a generic prepared plan cannot
    // prove a parameterized predicate implies the partial-index filter. Migrations freeze their
    // own historical copies. Keep server/client/in-memory classification semantics in sync.
    internal const string EndPredicate = """
        "Kind" IN ('TurnEnd', 'SessionRestartBoundary')
        OR ("Kind" = 'CompactBoundary' AND "Text" IS NOT NULL AND strpos("Text", '(manual)') > 0)
        OR ("Kind" = 'UserPrompt' AND "Text" IS NOT NULL AND "Text" LIKE '[Request interrupted%')
        """;

    private const string ActivityPredicate = """
        a."Kind" NOT IN ('TurnEnd', 'TurnTitle', 'SessionRestartBoundary', 'QueuedUserPrompt',
                        'QueueEnqueue', 'QueueDequeue', 'QueueRemove', 'CompactBoundary')
        AND (a."Kind" <> 'UserPrompt' OR a."Text" IS NULL OR
            (a."Text" NOT LIKE '<command-name>%'
             AND a."Text" NOT LIKE '<local-command-stdout>%'
             AND a."Text" NOT LIKE 'This session is being continued from a previous conversation%'
             AND a."Text" NOT LIKE '[Request interrupted%'))
        """;

    private const string Sql = $"""
        SELECT s."SessionId",
            CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS (
                SELECT 1 FROM "TranscriptEntries" AS a
                WHERE a."AgentSessionId" = s."SessionId" AND {ActivityPredicate}
            ) ELSE EXISTS (
                SELECT 1 FROM "TranscriptEntries" AS a
                WHERE a."AgentSessionId" = s."SessionId"
                  AND a."Sequence" > end_seq."Sequence"
                  AND (a."Timestamp" IS NULL OR end_ts."Timestamp" IS NULL
                       OR a."Timestamp" >= end_ts."Timestamp")
                  AND {ActivityPredicate}
            ) END AS "Working"
        FROM unnest(@sessionIds) AS s("SessionId")
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" AS t
            WHERE t."AgentSessionId" = s."SessionId" AND ({EndPredicate})
            ORDER BY t."Sequence" DESC LIMIT 1
        ) AS end_seq ON true
        LEFT JOIN LATERAL (
            SELECT t."Timestamp" FROM "TranscriptEntries" AS t
            WHERE t."AgentSessionId" = s."SessionId" AND ({EndPredicate})
                AND t."Timestamp" IS NOT NULL
            ORDER BY t."Timestamp" DESC LIMIT 1
        ) AS end_ts ON true
        """;

    internal static async Task<IReadOnlyDictionary<Guid, bool>> ReadAsync(
        AppDbContext db, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0) return new Dictionary<Guid, bool>();

        // Sequence and timestamp are independent end maxima. The activity EXISTS must apply
        // BOTH tests to one row: stale catch-up can arrive above an end in stored sequence.
        // Null timestamps cannot prove stale. Empty transcripts stay idle, regardless of status.
        var ids = new NpgsqlParameter("sessionIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = sessionIds.ToArray()
        };
        var rows = await db.Database.SqlQueryRaw<WorkingStateRow>(Sql, ids).ToListAsync(ct);
        // Deliberately retain ToDictionary's rejection of duplicate requested IDs.
        return rows.ToDictionary(row => row.SessionId, row => row.Working);
    }

    private sealed class WorkingStateRow
    {
        public Guid SessionId { get; set; }
        public bool Working { get; set; }
    }
}
