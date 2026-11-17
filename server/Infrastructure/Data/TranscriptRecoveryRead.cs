using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// CARD-1969: one snapshot of a session's recovery stamp and its newest
/// <see cref="TailRows"/> rows. The stale-Working probe used to materialise every
/// column of every row. The stamp columns are repeated on each returned row so the
/// statement stays inside the two-observation row bound.
/// </summary>
internal static class TranscriptRecoveryRead
{
    internal const int TailRows = 64;

    /// <summary>Tag on the tail statement. The stamp-only added read is a different shape and is not tagged.</summary>
    internal const string ObserveTag = "rt.recovery.observe";

    internal readonly record struct Stamp(long Count, long? MaxSequence, Guid? FirstRowId, Guid? TopRowId);

    internal readonly record struct Added(Stamp Stamp, long AddedCount);

    internal sealed record ObservedRow(
        Guid Id, long Sequence, string Kind, string? Uuid, string? ParentUuid, DateTime? Timestamp, string? Role,
        string? Text, string? ToolName, string? ToolInput, string? ToolUseId, bool? ToolIsError, string? StopReason,
        string? ApiCallId, int? InputTokens, int? OutputTokens, int? CacheReadTokens, int? CacheCreationTokens,
        bool? IsApiError, string? ApiErrorClass, int? ApiErrorStatus, string? ApiErrorTimeZoneId, string? Model,
        int? ModelCalls, DateTime CreatedAt);

    internal static async Task<(Stamp Stamp, ObservedRow[] Tail)> ReadTailAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<TailRow>(TailSql, new object[]
        {
            new NpgsqlParameter("sessionId", NpgsqlDbType.Uuid) { Value = sessionId },
            new NpgsqlParameter("k", NpgsqlDbType.Integer) { Value = TailRows },
        }).TagWith(ObserveTag).ToListAsync(ct);
        var head = rows.FirstOrDefault();
        var stamp = head is null
            ? new Stamp(0, null, null, null)
            : new Stamp(head.Count, head.MaxSequence, head.FirstRowId, head.TopRowId);
        var tail = rows.Where(row => row.Id is not null)
            .OrderBy(row => row.Sequence)
            .Select(row => new ObservedRow(
                row.Id!.Value, row.Sequence!.Value, row.Kind!, row.Uuid, row.ParentUuid, row.Timestamp, row.Role,
                row.Text, row.ToolName, row.ToolInput, row.ToolUseId, row.ToolIsError, row.StopReason, row.ApiCallId,
                row.InputTokens, row.OutputTokens, row.CacheReadTokens, row.CacheCreationTokens, row.IsApiError,
                row.ApiErrorClass, row.ApiErrorStatus, row.ApiErrorTimeZoneId, row.Model, row.ModelCalls,
                row.CreatedAt!.Value))
            .ToArray();
        return (stamp, tail);
    }

    /// <summary>
    /// Stamp plus rows stored after <paramref name="beforeMax"/>. No tail columns, so the
    /// whole-row counter does not see this command.
    /// </summary>
    internal static async Task<Added> ReadAddedAsync(
        AppDbContext db, Guid sessionId, long beforeMax, CancellationToken ct)
    {
        var row = await db.Database.SqlQueryRaw<AddedRow>(AddedSql, new object[]
        {
            new NpgsqlParameter("sessionId", NpgsqlDbType.Uuid) { Value = sessionId },
            new NpgsqlParameter("beforeMax", NpgsqlDbType.Bigint) { Value = beforeMax },
        }).ToListAsync(ct);
        var head = row.FirstOrDefault();
        return head is null
            ? new Added(new Stamp(0, null, null, null), 0)
            : new Added(new Stamp(head.Count, head.MaxSequence, head.FirstRowId, head.TopRowId), head.AddedCount);
    }

    // One row of stamp aggregates, left-joined to the newest K rows. An empty session yields
    // one all-null tail row; the caller drops that row. No leading WITH: SqlQueryRaw may wrap
    // the text, and a wrapped WITH is not a statement.
    private const string TailSql = """
        SELECT s."Count", s."MaxSequence", s."FirstRowId", s."TopRowId",
            t."Id", t."Sequence", t."Kind", t."Uuid", t."ParentUuid", t."Timestamp", t."Role", t."Text",
            t."ToolName", t."ToolInput", t."ToolUseId", t."ToolIsError", t."StopReason", t."ApiCallId",
            t."InputTokens", t."OutputTokens", t."CacheReadTokens", t."CacheCreationTokens", t."IsApiError",
            t."ApiErrorClass", t."ApiErrorStatus", t."ApiErrorTimeZoneId", t."Model", t."ModelCalls", t."CreatedAt"
        FROM (
            SELECT count(*)::bigint AS "Count",
                max(e."Sequence") AS "MaxSequence",
                (SELECT f."Id" FROM "TranscriptEntries" AS f
                    WHERE f."AgentSessionId" = @sessionId ORDER BY f."Sequence" LIMIT 1) AS "FirstRowId",
                (SELECT x."Id" FROM "TranscriptEntries" AS x
                    WHERE x."AgentSessionId" = @sessionId ORDER BY x."Sequence" DESC LIMIT 1) AS "TopRowId"
            FROM "TranscriptEntries" AS e
            WHERE e."AgentSessionId" = @sessionId
        ) AS s
        LEFT JOIN LATERAL (
            SELECT i."Id", i."Sequence", i."Kind", i."Uuid", i."ParentUuid", i."Timestamp", i."Role", i."Text",
                i."ToolName", i."ToolInput", i."ToolUseId", i."ToolIsError", i."StopReason", i."ApiCallId",
                i."InputTokens", i."OutputTokens", i."CacheReadTokens", i."CacheCreationTokens", i."IsApiError",
                i."ApiErrorClass", i."ApiErrorStatus", i."ApiErrorTimeZoneId", i."Model", i."ModelCalls", i."CreatedAt"
            FROM "TranscriptEntries" AS i
            WHERE i."AgentSessionId" = @sessionId
            ORDER BY i."Sequence" DESC
            LIMIT @k
        ) AS t ON TRUE
        ORDER BY t."Sequence" ASC
        """;

    private const string AddedSql = """
        SELECT count(*)::bigint AS "Count",
            max(e."Sequence") AS "MaxSequence",
            (SELECT f."Id" FROM "TranscriptEntries" AS f
                WHERE f."AgentSessionId" = @sessionId ORDER BY f."Sequence" LIMIT 1) AS "FirstRowId",
            (SELECT x."Id" FROM "TranscriptEntries" AS x
                WHERE x."AgentSessionId" = @sessionId ORDER BY x."Sequence" DESC LIMIT 1) AS "TopRowId",
            count(*) FILTER (WHERE e."Sequence" > @beforeMax) AS "AddedCount"
        FROM "TranscriptEntries" AS e
        WHERE e."AgentSessionId" = @sessionId
        """;

    private sealed class TailRow
    {
        public long Count { get; set; }
        public long? MaxSequence { get; set; }
        public Guid? FirstRowId { get; set; }
        public Guid? TopRowId { get; set; }
        public Guid? Id { get; set; }
        public long? Sequence { get; set; }
        public string? Kind { get; set; }
        public string? Uuid { get; set; }
        public string? ParentUuid { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? Role { get; set; }
        public string? Text { get; set; }
        public string? ToolName { get; set; }
        public string? ToolInput { get; set; }
        public string? ToolUseId { get; set; }
        public bool? ToolIsError { get; set; }
        public string? StopReason { get; set; }
        public string? ApiCallId { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? CacheReadTokens { get; set; }
        public int? CacheCreationTokens { get; set; }
        public bool? IsApiError { get; set; }
        public string? ApiErrorClass { get; set; }
        public int? ApiErrorStatus { get; set; }
        public string? ApiErrorTimeZoneId { get; set; }
        public string? Model { get; set; }
        public int? ModelCalls { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private sealed class AddedRow
    {
        public long Count { get; set; }
        public long? MaxSequence { get; set; }
        public Guid? FirstRowId { get; set; }
        public Guid? TopRowId { get; set; }
        public long AddedCount { get; set; }
    }
}
