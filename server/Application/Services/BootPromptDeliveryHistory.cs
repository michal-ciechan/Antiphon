using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable diagnostic history for a boot brief. Retry admission uses the queue's charged count.</summary>
internal static class BootPromptDeliveryHistory
{
    internal const string MissingComposerEvidence = "required composer evidence was not observed";

    private const string Attempt = "delivery attempt started";
    private const string AttemptAfterEarlierEvidence = "delivery attempt started after earlier attempt evidence";
    private const string ComposerFailureRefunded = MissingComposerEvidence + ", attempt refunded";

    internal static void RecordAttempt(AppDbContext db, SessionQueuedMessage row, DateTime at)
    {
        if (!IsDispatchBrief(row)) return;

        var earlier = row.LastDeliveryStartedAt is not null || row.DeliveryAttempts > 0 || row.DeliveryVerdict is not null;
        Add(db, row.Id, at, earlier ? AttemptAfterEarlierEvidence : Attempt);
    }

    // Only the provisionally stamped attempt owned by this context can be discarded.
    internal static void DiscardUnwrittenAttempt(AppDbContext db, SessionQueuedMessage row)
    {
        if (!IsDispatchBrief(row)) return;

        var prefix = Prefix(row.Id) + Attempt;
        var attempt = db.AuditRecords.Local.SingleOrDefault(a =>
            a.EventType == AuditEventType.BootPromptDelivery && a.Summary.StartsWith(prefix, StringComparison.Ordinal));
        if (attempt is not null)
            db.AuditRecords.Remove(attempt);
    }

    internal static void RecordComposerFailure(AppDbContext db, SessionQueuedMessage row, DateTime at, bool refunded)
    {
        if (IsDispatchBrief(row))
            Add(db, row.Id, at, refunded ? ComposerFailureRefunded : MissingComposerEvidence);
    }

    internal static async Task<string?> DescribeAsync(AppDbContext db, Guid rowId, DateTime queuedAt,
        int chargedAttempts, DateTime? lastStartedAt, DeliveryVerdict? verdict, CancellationToken ct)
    {
        var prefix = Prefix(rowId);
        var history = await db.AuditRecords.AsNoTracking()
            .Where(a => a.EventType == AuditEventType.BootPromptDelivery
                && a.CreatedAt >= queuedAt && a.Summary.StartsWith(prefix))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.CreatedAt, a.Summary })
            .ToListAsync(ct);

        var attempts = history.Where(a => a.Summary.StartsWith(prefix + Attempt, StringComparison.Ordinal)).ToList();
        var first = attempts.FirstOrDefault();
        var exact = first?.Summary == prefix + Attempt;
        var count = exact ? attempts.Count : Math.Max(attempts.Count + 1, chargedAttempts);
        var composerFailure = history.Any(a => a.Summary.StartsWith(prefix + MissingComposerEvidence, StringComparison.Ordinal))
            || verdict == DeliveryVerdict.NoComposerEvidence;
        if (first is null && chargedAttempts <= 0 && lastStartedAt is null && !composerFailure)
            return null;

        var detail = $"{(exact ? "attempted" : "attempted at least")} {count} {(count == 1 ? "time" : "times")}";
        if (exact && first is not null)
            detail += $", first attempt at {first.CreatedAt:O}";
        else if (lastStartedAt is { } last)
            detail += $", last recorded attempt at {last:O}";
        if (composerFailure)
            detail += $", and the last composer-evidence failure reported that {MissingComposerEvidence}";
        var refunds = history.Count(a => a.Summary == prefix + ComposerFailureRefunded);
        if (refunds > 0)
            detail += refunds == 1 ? ", with 1 attempt refunded" : $", with {refunds} attempts refunded";
        return detail;
    }

    private static bool IsDispatchBrief(SessionQueuedMessage row) =>
        row.Origin == QueuedMessageOrigin.Delegation && (row.ExecutionTaskId ?? row.SourceTaskId) is { } taskId
        && (row.ExecutionTaskId is not null || row.ContentDigest is null)
        && DispatchBriefEvidence.IsRecognized(
            SessionMessageQueueService.ToBriefEvidence(row), taskId, DelegationReportFormatter.TaskMarker(taskId));

    private static string Prefix(Guid rowId) => $"boot-prompt queue={rowId:D} ";

    private static void Add(AppDbContext db, Guid rowId, DateTime at, string detail) =>
        // The caller commits this entry and the queue stamp or refund in one SaveChanges.
        db.AuditRecords.Add(new AuditRecord
        {
            Id = Guid.NewGuid(),
            EventType = AuditEventType.BootPromptDelivery,
            CreatedAt = at,
            Summary = Prefix(rowId) + detail,
        });
}
