using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Application.Services;

public sealed partial class ChannelBridgeService
{
    private const int WithheldIncidentPageSize = 64;
    private const int WithheldScanFailureThreshold = 3;
    private DateTime? _withheldCursorAt;
    private Guid _withheldCursorId;
    private int _withheldScanFailures;
    private Guid? _withheldFailureIncidentId;
    private bool _withheldFailureReported;

    // A transient cursor limits inspected queue rows as well as inserted incidents.
    // It wraps after the indexed range ends; durable stamps, never this hint, deduplicate.
    internal const string WithheldIncidentQueuePageSql = """
        SELECT q."Id", q."FirstWithheldAt"
        FROM "SessionQueuedMessages" AS q
        WHERE q."Origin" = 1 AND q."Status" = 0
          AND q."DeliveryAttempts" = 0 AND q."LastDeliveryStartedAt" IS NULL
          AND q."LastDeliveryBaselineSequence" IS NULL AND q."DeliveryVerdict" IS NULL
          AND q."WithholdCount" > 0 AND q."FirstWithheldAt" IS NOT NULL
          AND q."FirstWithheldAt" <= @agedAt
          AND EXISTS (
              SELECT 1 FROM "ChannelInbounds" AS i
              WHERE i."QueueMessageId" = q."Id" AND i."AgentId" IS NOT NULL
                AND i."InputWithheldIncidentAt" IS NULL)
          AND EXISTS (
              SELECT 1 FROM "AgentSessions" AS s
              WHERE s."Id" = q."AgentSessionId" AND s."Status" = 2)
        """;
    internal const string WithheldIncidentQueuePageEndSql = """
        ORDER BY q."FirstWithheldAt", q."Id"
        LIMIT @pageSize
        FOR UPDATE OF q SKIP LOCKED
        """;

    // Keep the static queue predicates literal so PostgreSQL can use the partial index.
    // Both mutable ownership rows are locked in the same statement. SKIP LOCKED never
    // waits on a disposition/wake writer; those writers cannot pass our queue lock
    // between the eligibility read and the incident/stamp commit.
    internal const string WithheldIncidentCandidateSql = $"""
        SELECT i."Id" AS "InboundId", i."AgentId", q."AgentSessionId" AS "SessionId",
               i."Provider", i."ConversationId", i."NativeMessageId", q."LastWithholdReason"
        FROM "SessionQueuedMessages" AS q
        JOIN "ChannelInbounds" AS i ON i."QueueMessageId" = q."Id"
        JOIN "AgentSessions" AS s ON s."Id" = q."AgentSessionId"
        LEFT JOIN LATERAL (
            SELECT "Sequence" FROM "TranscriptEntries"
            WHERE "AgentSessionId" = s."Id" AND ({TranscriptWorkingStateQuery.EndPredicate})
            ORDER BY "Sequence" DESC LIMIT 1
        ) AS end_seq ON true
        LEFT JOIN LATERAL (
            SELECT "Timestamp" FROM "TranscriptEntries"
            WHERE "AgentSessionId" = s."Id" AND ({TranscriptWorkingStateQuery.EndPredicate})
              AND "Timestamp" IS NOT NULL
            ORDER BY "Timestamp" DESC LIMIT 1
        ) AS end_ts ON true
        WHERE q."Id" = ANY(@queueIds)
          AND q."Origin" = 1 AND q."Status" = 0
          AND q."DeliveryAttempts" = 0 AND q."LastDeliveryStartedAt" IS NULL
          AND q."LastDeliveryBaselineSequence" IS NULL AND q."DeliveryVerdict" IS NULL
          AND q."WithholdCount" > 0 AND q."FirstWithheldAt" IS NOT NULL
          AND q."FirstWithheldAt" <= @agedAt
          AND i."AgentId" IS NOT NULL AND i."InputWithheldIncidentAt" IS NULL
          AND i."ContinuityHoldIncidentAt" IS NULL AND i."SuspensionHoldIncidentAt" IS NULL
          AND i."LivenessHoldIncidentAt" IS NULL AND s."Status" = 2
          AND NOT EXISTS (
              SELECT 1 FROM "AgentSupervisionStates" AS h WHERE h."AgentId" = i."AgentId"
                AND (h."Suspended" OR h."ContinuityHeldAt" IS NOT NULL
                     OR h."LivenessLatchedAt" IS NOT NULL OR h."ActiveCompactionRecoveryId" IS NOT NULL
                     OR h."HerdrFailureHeldAt" IS NOT NULL))
          AND NOT EXISTS (
              SELECT 1 FROM "TranscriptEntries" AS a
              WHERE a."AgentSessionId" = s."Id" AND {TranscriptWorkingStateQuery.ActivityPredicate}
                AND (end_seq."Sequence" IS NULL OR (
                    a."Sequence" > end_seq."Sequence"
                    AND (a."Timestamp" IS NULL OR end_ts."Timestamp" IS NULL
                         OR a."Timestamp" >= end_ts."Timestamp"))))
        ORDER BY q."FirstWithheldAt", q."Id", i."Id"
        LIMIT @pageSize
        FOR UPDATE OF q, i SKIP LOCKED
        """;

    /// <summary>Independent of the wake worker and the session queue semaphore; never flushes.</summary>
    internal async Task RunWithheldIncidentLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanWithheldChannelIncidentsAsync(ct);
                _withheldScanFailures = 0;
                _withheldFailureIncidentId = null;
                _withheldFailureReported = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Withheld channel incident scan failed; retrying");
                _withheldScanFailures = Math.Min(WithheldScanFailureThreshold, _withheldScanFailures + 1);
                if (_withheldScanFailures >= WithheldScanFailureThreshold && !_withheldFailureReported)
                {
                    try
                    {
                        await ReportWithheldScanFailureAsync(ct);
                        _withheldFailureReported = true;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception reportError)
                    {
                        _logger.LogWarning(reportError, "Withheld channel scan failure incident could not be saved; retrying");
                    }
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _timeProvider, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ReportWithheldScanFailureAsync(CancellationToken ct)
    {
        // The failed scan transaction has unwound. A small independent write can succeed even
        // when the indexed scan exceeds its deadline. If the database is unavailable too, the
        // loop retains this identity and retries; an uncertain commit cannot duplicate it.
        var id = _withheldFailureIncidentId ??= Guid.NewGuid();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(TimeSpan.FromSeconds(5));
        var token = bound.Token;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout = '100ms'; SET LOCAL statement_timeout = '2s'", token);
        var kind = (int)AgentIncidentKind.ChannelReplyLost;
        var severity = (int)AlertSeverity.Critical;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var message = $"Channel bridge withheld-incident scan failed {WithheldScanFailureThreshold} consecutive passes. "
            + "Withheld input monitoring is degraded; inspect database load and channel bridge logs.";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentIncidents" ("Id", "Kind", "Severity", "FailureReason", "Message", "CreatedAt")
            VALUES ({id}, {kind}, {severity}, {"ChannelWithheldScanFailed"}, {message}, {now})
            ON CONFLICT ("Id") DO NOTHING
            """, token);
        await tx.CommitAsync(token);
        _logger.LogWarning("Raised channel withheld scan failure incident after {Count} consecutive failed passes",
            WithheldScanFailureThreshold);
    }

    internal async Task ScanWithheldChannelIncidentsAsync(CancellationToken ct)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(TimeSpan.FromSeconds(5));
        var token = bound.Token;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var agedAt = now - TimeSpan.FromMinutes(Math.Max(0, _settings.AgedHoldIncidentMinutes));
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        // SKIP LOCKED handles row contention; these also bound relation locks and query work.
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout = '100ms'; SET LOCAL statement_timeout = '2s'", token);
        var pageSql = WithheldIncidentQueuePageSql;
        var pageParameters = new List<object>
        {
            new NpgsqlParameter("agedAt", agedAt),
            new NpgsqlParameter("pageSize", WithheldIncidentPageSize),
        };
        if (_withheldCursorAt is DateTime after)
        {
            pageSql += "\nAND (q.\"FirstWithheldAt\", q.\"Id\") > (@after, @afterId)";
            pageParameters.Add(new NpgsqlParameter("after", after));
            pageParameters.Add(new NpgsqlParameter("afterId", _withheldCursorId));
        }
        var page = await db.Database.SqlQueryRaw<WithheldQueueHead>(
            pageSql + "\n" + WithheldIncidentQueuePageEndSql, pageParameters.ToArray()).ToListAsync(token);
        if (page.Count == 0)
        {
            _withheldCursorAt = null;
            return; // Wrap on the next tick, retaining a strict one-page work budget.
        }
        _withheldCursorAt = page[^1].FirstWithheldAt;
        _withheldCursorId = page[^1].Id;
        var candidates = await db.Database.SqlQueryRaw<WithheldIncidentCandidate>(
            WithheldIncidentCandidateSql, new NpgsqlParameter("agedAt", agedAt),
            new NpgsqlParameter("pageSize", WithheldIncidentPageSize),
            new NpgsqlParameter("queueIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            { Value = page.Select(q => q.Id).ToArray() }).ToListAsync(token);
        var working = await SessionMessageQueueService.IsWorkingBatchAsync(db,
            candidates.Select(c => c.SessionId).Distinct().ToArray(), token);
        var eligible = candidates.Where(c => !working[c.SessionId]).ToList();
        if (eligible.Count == 0)
            return;

        foreach (var row in eligible)
        {
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = row.AgentId,
                SessionId = row.SessionId,
                Kind = AgentIncidentKind.ChannelReplyLost,
                Severity = AlertSeverity.Critical,
                FailureReason = "ChannelInputWithheld",
                CreatedAt = now,
                Message = ColumnText.Clip(
                    $"Inbound message {row.Provider}:{row.ConversationId}/{row.NativeMessageId} is withheld: {row.LastWithholdReason}",
                    AgentIncident.MessageMaxLength),
            });
        }
        var inboundIds = eligible.Select(c => c.InboundId).ToArray();
        await db.ChannelInbounds.Where(i => inboundIds.Contains(i.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.InputWithheldIncidentAt, now), token);
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        _logger.LogInformation("Raised {Count} withheld channel incidents", eligible.Count);
    }

    private sealed class WithheldQueueHead
    {
        public Guid Id { get; set; }
        public DateTime FirstWithheldAt { get; set; }
    }

    private sealed class WithheldIncidentCandidate
    {
        public Guid InboundId { get; set; }
        public Guid AgentId { get; set; }
        public Guid SessionId { get; set; }
        public string Provider { get; set; } = "";
        public string ConversationId { get; set; } = "";
        public string NativeMessageId { get; set; } = "";
        public string? LastWithholdReason { get; set; }
    }
}
