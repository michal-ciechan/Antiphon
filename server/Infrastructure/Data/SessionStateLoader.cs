using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Infrastructure.Data;

public sealed class SessionStateLoader(IServiceScopeFactory scopes) : ISessionStateLoader
{
    // Trusted enum literals, matching the persisted int representation. This is pinning,
    // never liveness: a phone-home outage must not evict an active conversation's evidence.
    private const string Pinned = """
        (s."Status" IN (1, 2, 3)
         OR EXISTS (SELECT 1 FROM "AgentTasks" task WHERE task."Status" IN (0, 1, 2, 3)
             AND (task."AgentSessionId" = s."Id" OR task."ParentSessionId" = s."Id"))
         OR EXISTS (SELECT 1 FROM "SessionQueuedMessages" q WHERE q."AgentSessionId" = s."Id"
             AND (q."Status" = 0 OR (q."Status" = 1 AND q."DeliveryVerdict" IS NULL)))
         OR EXISTS (SELECT 1 FROM "AgentTaskLandNotifications" n WHERE n."ParentSessionId" = s."Id"
             AND n."ConfirmedAt" IS NULL AND n."State" <> 4))
        """;

    private const string Sql = $"""
        SELECT requested."SessionId", s."Id" IS NOT NULL AS "Exists",
            COALESCE({Pinned}, false) AS "Pinned", s."StartedAt" AS "AcceptedGeneration",
            totals."Count", totals."NewestEffectiveTimestamp",
            COALESCE(last_row."Sequence", 0) AS "LastSequence", last_row."Id" AS "LastEntryId",
            last_row."Kind" AS "LastKind", last_row."Timestamp" AS "LastTimestamp", last_row."CreatedAt" AS "LastCreatedAt",
            end_seq."Sequence" AS "EndSequence", end_ts."Timestamp" AS "EndTimestamp",
            activity."HasActivity", activity."HasNullTimestampActivity", activity."ActivityTimestamp",
            turn_end."Sequence" AS "LastTurnEndSequence", title."Sequence" AS "LastTurnTitleSequence",
            prompt."Sequence" AS "LastUserPromptSequence"
        FROM unnest(@sessionIds) AS requested("SessionId")
        LEFT JOIN "AgentSessions" s ON s."Id" = requested."SessionId"
        LEFT JOIN LATERAL (
            SELECT count(*) AS "Count", max(COALESCE(t."Timestamp", t."CreatedAt")) AS "NewestEffectiveTimestamp"
            FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
        ) totals ON true
        LEFT JOIN LATERAL (
            SELECT t."Id", t."Sequence", t."Kind", t."Timestamp", t."CreatedAt" FROM "TranscriptEntries" t
            WHERE t."AgentSessionId" = requested."SessionId" ORDER BY t."Sequence" DESC LIMIT 1
        ) last_row ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND ({TranscriptWorkingStateQuery.EndPredicate}) ORDER BY t."Sequence" DESC LIMIT 1
        ) end_seq ON true
        LEFT JOIN LATERAL (
            SELECT t."Timestamp" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND ({TranscriptWorkingStateQuery.EndPredicate}) AND t."Timestamp" IS NOT NULL
            ORDER BY t."Timestamp" DESC LIMIT 1
        ) end_ts ON true
        LEFT JOIN LATERAL (
            SELECT count(*) > 0 AS "HasActivity", count(*) FILTER (WHERE a."Timestamp" IS NULL) > 0 AS "HasNullTimestampActivity",
                max(a."Timestamp") AS "ActivityTimestamp"
            FROM "TranscriptEntries" a WHERE a."AgentSessionId" = requested."SessionId"
                AND (end_seq."Sequence" IS NULL OR a."Sequence" > end_seq."Sequence")
                AND {TranscriptWorkingStateQuery.ActivityPredicate}
        ) activity ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND t."Kind" = 'TurnEnd' ORDER BY t."Sequence" DESC LIMIT 1
        ) turn_end ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND t."Kind" = 'TurnTitle' ORDER BY t."Sequence" DESC LIMIT 1
        ) title ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND t."Kind" = 'UserPrompt' ORDER BY t."Sequence" DESC LIMIT 1
        ) prompt ON true
        """;

    public async Task<IReadOnlyDictionary<Guid, SessionStateSnapshot>> LoadAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0) return new Dictionary<Guid, SessionStateSnapshot>();
        if (sessionIds.Count > 512) throw new ArgumentOutOfRangeException(nameof(sessionIds));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = new NpgsqlParameter("sessionIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = sessionIds.ToArray() };
        var rows = await db.Database.SqlQueryRaw<SeedRow>(Sql, ids).TagWith("session-state.seed").ToListAsync(ct);
        return rows.ToDictionary(r => r.SessionId, r => new SessionStateSnapshot(r.SessionId)
        {
            Readiness = r.Exists ? SessionStateReadiness.Ready : SessionStateReadiness.Missing,
            Pinned = r.Pinned, AcceptedGeneration = r.AcceptedGeneration, Count = r.Count,
            LastSequence = r.LastSequence, LastEntryId = r.LastEntryId, LastKind = r.LastKind,
            LastTimestamp = r.LastTimestamp, LastCreatedAt = r.LastCreatedAt, NewestEffectiveTimestamp = r.NewestEffectiveTimestamp,
            EndSequence = r.EndSequence, EndTimestamp = r.EndTimestamp, HasActivity = r.HasActivity,
            HasNullTimestampActivity = r.HasNullTimestampActivity, ActivityTimestamp = r.ActivityTimestamp,
            LastTurnEndSequence = r.LastTurnEndSequence, LastTurnTitleSequence = r.LastTurnTitleSequence,
            LastUserPromptSequence = r.LastUserPromptSequence
        });
    }

    public async Task<IReadOnlySet<Guid>> LoadPinnedIdsAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Database.SqlQueryRaw<Guid>($"SELECT s.\"Id\" AS \"Value\" FROM \"AgentSessions\" s WHERE {Pinned}")
            .TagWith("session-state.pins").ToListAsync(ct)).ToHashSet();
    }

    private sealed class SeedRow
    {
        public Guid SessionId { get; set; }
        public bool Exists { get; set; }
        public bool Pinned { get; set; }
        public DateTime? AcceptedGeneration { get; set; }
        public long Count { get; set; }
        public long LastSequence { get; set; }
        public Guid? LastEntryId { get; set; }
        public string? LastKind { get; set; }
        public DateTime? LastTimestamp { get; set; }
        public DateTime? LastCreatedAt { get; set; }
        public DateTime? NewestEffectiveTimestamp { get; set; }
        public long? EndSequence { get; set; }
        public DateTime? EndTimestamp { get; set; }
        public bool HasActivity { get; set; }
        public bool HasNullTimestampActivity { get; set; }
        public DateTime? ActivityTimestamp { get; set; }
        public long? LastTurnEndSequence { get; set; }
        public long? LastTurnTitleSequence { get; set; }
        public long? LastUserPromptSequence { get; set; }
    }
}
