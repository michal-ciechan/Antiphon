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

    /// <summary>
    /// CARD-1888: part 0 also reads the stored-prefix facts when the caller asks, as conditional SQL
    /// text in this same statement, so they share the tail probe's snapshot (D-11). A record
    /// candidate reads the lowest row, the top row and the session's row count; a skip candidate
    /// also reads the row at the memo's stored max and the count at or below it. Both counts come
    /// from one range scan of the unique (AgentSessionId, Sequence) index.
    /// </summary>
    private static string SessionSql(bool record, bool verify) => $"""
        SELECT 0 AS "Part", s."StartedAt" AS "StartedAt",
            (SELECT max(m."Sequence") FROM "TranscriptEntries" AS m
             WHERE m."AgentSessionId" = @sessionId) AS "Sequence",
            NULL::text AS "Uuid", NULL::text AS "Kind",
            {(record || verify ? FirstRowSql : "NULL::uuid")} AS "FirstRowId",
            {(record ? TopRowSql : "NULL::uuid")} AS "TopRowId",
            {(verify ? PrefixRowSql : "NULL::uuid")} AS "PrefixRowId",
            {(record ? "c.\"SessionCount\"" : "NULL::bigint")} AS "SessionCount",
            {(verify ? "c.\"PrefixCount\"" : "NULL::bigint")} AS "PrefixCount"
        FROM "AgentSessions" AS s{(record || verify ? CountsSql(record, verify) : "")}
        WHERE s."Id" = @sessionId
        """;

    private const string FirstRowSql = """
        (SELECT f."Id" FROM "TranscriptEntries" AS f
             WHERE f."AgentSessionId" = @sessionId ORDER BY f."Sequence" LIMIT 1)
        """;

    private const string TopRowSql = """
        (SELECT x."Id" FROM "TranscriptEntries" AS x
             WHERE x."AgentSessionId" = @sessionId ORDER BY x."Sequence" DESC LIMIT 1)
        """;

    private const string PrefixRowSql = """
        (SELECT p."Id" FROM "TranscriptEntries" AS p
             WHERE p."AgentSessionId" = @sessionId AND p."Sequence" = @memoPrefixMax)
        """;

    private static string CountsSql(bool record, bool verify) => $"""

        CROSS JOIN (SELECT {(record ? "count(*)" : "NULL::bigint")} AS "SessionCount",
                {(verify ? "count(*) FILTER (WHERE n.\"Sequence\" <= @memoPrefixMax)" : "NULL::bigint")} AS "PrefixCount"
            FROM "TranscriptEntries" AS n
            WHERE n."AgentSessionId" = @sessionId{(record ? "" : " AND n.\"Sequence\" <= @memoPrefixMax")}) AS c
        """;

    private const string IdentitySql = """
        UNION ALL
        SELECT 1, NULL, NULL, t."Uuid", t."Kind", NULL::uuid, NULL::uuid, NULL::uuid, NULL::bigint, NULL::bigint
        FROM "TranscriptEntries" AS t
        WHERE t."AgentSessionId" = @sessionId AND t."Uuid" IS NOT NULL AND t."Uuid" = ANY(@uuids)
        """;

    private const string SequenceSql = """
        UNION ALL
        SELECT 2, NULL, t."Sequence", NULL, NULL, NULL::uuid, NULL::uuid, NULL::uuid, NULL::bigint, NULL::bigint
        FROM "TranscriptEntries" AS t
        WHERE t."AgentSessionId" = @sessionId AND t."Sequence" = ANY(@sequences)
        """;

    /// <param name="StartedAt">The session's start, or null when the session row does not exist.</param>
    /// <param name="MaxSequence">The session's stored maximum sequence, or null with no rows.</param>
    /// <param name="Identities">Stored (UUID, kind) pairs among the probed UUIDs.</param>
    /// <param name="Sequences">Stored sequences among the probed null-UUID sequences.</param>
    /// <param name="FirstRowId">The lowest-sequence row, when a prefix record or check was asked for.</param>
    /// <param name="TopRowId">The highest-sequence row, when a prefix record was asked for.</param>
    /// <param name="SessionCount">The session's row count, when a prefix record was asked for.</param>
    /// <param name="PrefixRowId">The row at the memo's stored max, when a prefix check was asked for.</param>
    /// <param name="PrefixCount">The rows at or below the memo's stored max, when a prefix check was asked for.</param>
    internal sealed record Result(
        DateTime? StartedAt, long? MaxSequence, HashSet<(string Uuid, string Kind)> Identities, HashSet<long> Sequences,
        Guid? FirstRowId = null, Guid? TopRowId = null, long? SessionCount = null, Guid? PrefixRowId = null, long? PrefixCount = null);

    /// <param name="uuids">At most one 512-key chunk; the caller probes any further chunk itself.</param>
    /// <param name="sequences">The batch's null-UUID sequences.</param>
    /// <param name="recordPrefix">CARD-1888: read what a new prefix memo records.</param>
    /// <param name="memoPrefixMax">CARD-1888: the memo's stored max to check the stored prefix at, or null.</param>
    internal static async Task<Result> ReadAsync(AppDbContext db, Guid sessionId,
        IReadOnlyCollection<string> uuids, IReadOnlyCollection<long> sequences, bool recordPrefix, long? memoPrefixMax,
        CancellationToken ct)
    {
        if (uuids.Count > 512) throw new ArgumentOutOfRangeException(nameof(uuids));
        var sql = SessionSql(recordPrefix, memoPrefixMax is not null);
        var parameters = new List<object>
        {
            new NpgsqlParameter("sessionId", NpgsqlDbType.Uuid) { Value = sessionId }
        };
        if (memoPrefixMax is { } prefixMax)
            parameters.Add(new NpgsqlParameter("memoPrefixMax", NpgsqlDbType.Bigint) { Value = prefixMax });
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
            rows.Where(r => r.Part == 2).Select(r => r.Sequence!.Value).ToHashSet(),
            session?.FirstRowId, session?.TopRowId, session?.SessionCount, session?.PrefixRowId, session?.PrefixCount);
    }

    private sealed class Row
    {
        public int Part { get; set; }
        public DateTime? StartedAt { get; set; }
        public long? Sequence { get; set; }
        public string? Uuid { get; set; }
        public string? Kind { get; set; }
        public Guid? FirstRowId { get; set; }
        public Guid? TopRowId { get; set; }
        public Guid? PrefixRowId { get; set; }
        public long? SessionCount { get; set; }
        public long? PrefixCount { get; set; }
    }
}
