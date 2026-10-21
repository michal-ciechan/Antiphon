using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// CARD-1403 S3: everything transcript ingestion reads before it inserts, in ONE command. A batch
/// used to pay four round trips here (session, UUID identities, null-UUID sequences, stored max)
/// before any insert, duplicate batches included. The statement reads one snapshot, so the four
/// answers can no longer straddle another writer's commit; the caller's lease, recovery fence and
/// save are unchanged.
/// </summary>
internal static class TranscriptIngestPrelude
{
    /// <summary>The UUID identity probe's tag; the existing <c>identity</c> counter keeps its meaning.</summary>
    internal const string IdentityTag = "session-state.identity";

    /// <summary>The prelude's tag when the batch carries no UUID (no identity probe rides in it).</summary>
    internal const string PreludeTag = "rt.ingest.prelude";

    private const string SessionSql = """
        SELECT 0 AS "Part", s."StartedAt" AS "StartedAt",
            (SELECT max(m."Sequence") FROM "TranscriptEntries" AS m
             WHERE m."AgentSessionId" = @sessionId) AS "Sequence",
            NULL::text AS "Uuid", NULL::text AS "Kind"
        FROM "AgentSessions" AS s
        WHERE s."Id" = @sessionId
        """;

    private const string IdentitySql = """
        UNION ALL
        SELECT 1, NULL, NULL, t."Uuid", t."Kind"
        FROM "TranscriptEntries" AS t
        WHERE t."AgentSessionId" = @sessionId AND t."Uuid" IS NOT NULL AND t."Uuid" = ANY(@uuids)
        """;

    private const string SequenceSql = """
        UNION ALL
        SELECT 2, NULL, t."Sequence", NULL, NULL
        FROM "TranscriptEntries" AS t
        WHERE t."AgentSessionId" = @sessionId AND t."Sequence" = ANY(@sequences)
        """;

    /// <param name="StartedAt">The session's start, or null when the session row does not exist.</param>
    /// <param name="MaxSequence">The session's stored maximum sequence, or null with no rows.</param>
    /// <param name="Identities">Stored (UUID, kind) pairs among the probed UUIDs.</param>
    /// <param name="Sequences">Stored sequences among the probed null-UUID sequences.</param>
    internal sealed record Result(
        DateTime? StartedAt, long? MaxSequence, HashSet<(string Uuid, string Kind)> Identities, HashSet<long> Sequences);

    /// <param name="uuids">At most one 512-key chunk; the caller probes any further chunk itself.</param>
    /// <param name="sequences">The batch's null-UUID sequences.</param>
    internal static async Task<Result> ReadAsync(AppDbContext db, Guid sessionId,
        IReadOnlyCollection<string> uuids, IReadOnlyCollection<long> sequences, CancellationToken ct)
    {
        if (uuids.Count > 512) throw new ArgumentOutOfRangeException(nameof(uuids));
        var sql = SessionSql;
        var parameters = new List<object>
        {
            new NpgsqlParameter("sessionId", NpgsqlDbType.Uuid) { Value = sessionId }
        };
        // Branches are added only when they have keys, so a null-UUID batch carries no UUID probe
        // and a UUID-only batch keeps the plain (session, UUID) index seek.
        if (uuids.Count > 0)
        {
            sql += "\n" + IdentitySql;
            parameters.Add(new NpgsqlParameter("uuids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = uuids.ToArray() });
        }
        if (sequences.Count > 0)
        {
            sql += "\n" + SequenceSql;
            parameters.Add(new NpgsqlParameter("sequences", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = sequences.ToArray() });
        }

        var rows = await db.Database.SqlQueryRaw<Row>(sql, parameters.ToArray())
            .TagWith(uuids.Count > 0 ? IdentityTag : PreludeTag)
            .ToListAsync(ct);
        var session = rows.FirstOrDefault(r => r.Part == 0);
        return new Result(
            session?.StartedAt,
            session?.Sequence,
            rows.Where(r => r.Part == 1).Select(r => (r.Uuid!, r.Kind!)).ToHashSet(),
            rows.Where(r => r.Part == 2).Select(r => r.Sequence!.Value).ToHashSet());
    }

    private sealed class Row
    {
        public int Part { get; set; }
        public DateTime? StartedAt { get; set; }
        public long? Sequence { get; set; }
        public string? Uuid { get; set; }
        public string? Kind { get; set; }
    }
}
