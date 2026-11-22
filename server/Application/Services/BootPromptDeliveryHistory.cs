using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Diagnostic history for one queue row. Retry admission continues to use the queue's charged count.</summary>
internal static class BootPromptDeliveryHistory
{
    internal const string MissingComposerEvidence = "required composer evidence was not observed";

    private sealed record Snapshot(
        int Attempts,
        bool Complete,
        DateTime? FirstAttemptAt,
        string? LastComposerEvidenceFailure,
        int ComposerEvidenceRefunds);

    internal static async Task RecordAttemptAsync(AppDbContext db, SessionQueuedMessage row, DateTime at, CancellationToken ct)
    {
        if (!IsDispatchBrief(row)) return;

        var audit = await FindAsync(db, row.Id, ct);
        var history = Read(audit, row.Id) ?? FromLegacy(row.DeliveryAttempts, row.LastDeliveryStartedAt, row.DeliveryVerdict);
        Save(db, audit, row.Id, at, history with
        {
            Attempts = history.Attempts + 1,
            FirstAttemptAt = history is { Complete: true, Attempts: 0 } ? SessionGeneration.Normalize(at) : history.FirstAttemptAt,
        });
    }

    // Called in the same context as RecordAttemptAsync when the queue can prove its
    // provisional stamp never reached the terminal. Observed composer failures keep the stamp.
    internal static void DiscardUnwrittenAttempt(AppDbContext db, SessionQueuedMessage row)
    {
        if (!IsDispatchBrief(row)) return;

        var id = AuditId(row.Id);
        var audit = db.AuditRecords.Local.SingleOrDefault(a => a.Id == id);
        if (Read(audit, row.Id) is not { Attempts: > 0 } history) return;

        if (history.Attempts == 1)
            db.AuditRecords.Remove(audit!);
        else
            audit!.Summary = Prefix(row.Id) + JsonSerializer.Serialize(history with { Attempts = history.Attempts - 1 });
    }

    internal static async Task RecordComposerFailureAsync(
        AppDbContext db, SessionQueuedMessage row, DateTime at, bool refunded, CancellationToken ct)
    {
        if (!IsDispatchBrief(row)) return;

        var audit = await FindAsync(db, row.Id, ct);
        var history = Read(audit, row.Id) ?? FromLegacy(row.DeliveryAttempts, row.LastDeliveryStartedAt, DeliveryVerdict.NoComposerEvidence);
        Save(db, audit, row.Id, at, history with
        {
            LastComposerEvidenceFailure = MissingComposerEvidence,
            ComposerEvidenceRefunds = history.ComposerEvidenceRefunds + (refunded ? 1 : 0),
        });
    }

    internal static async Task<string?> DescribeAsync(
        AppDbContext db, Guid rowId, int chargedAttempts, DateTime? lastStartedAt, DeliveryVerdict? verdict, CancellationToken ct)
    {
        var id = AuditId(rowId);
        var audit = await db.AuditRecords.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        var history = Read(audit, rowId) ?? FromLegacy(chargedAttempts, lastStartedAt, verdict);
        if (history.Attempts == 0)
            return null;

        var count = history.Complete ? "attempted" : "attempted at least";
        var times = history.Attempts == 1 ? "time" : "times";
        var detail = $"{count} {history.Attempts} {times}";
        if (history.FirstAttemptAt is { } first)
            detail += $", first attempt at {first:O}";
        else if (lastStartedAt is { } last)
            detail += $", last recorded attempt at {last:O}";
        if (history.LastComposerEvidenceFailure is { } reason)
            detail += $", and the last composer-evidence failure reported that {reason}";
        if (history.ComposerEvidenceRefunds > 0)
            detail += history.ComposerEvidenceRefunds == 1
                ? ", with 1 attempt refunded"
                : $", with {history.ComposerEvidenceRefunds} attempts refunded";
        return detail;
    }

    private static bool IsDispatchBrief(SessionQueuedMessage row) =>
        row.Origin == QueuedMessageOrigin.Delegation && (row.ExecutionTaskId ?? row.SourceTaskId) is { } taskId
        && (row.ExecutionTaskId is not null || row.ContentDigest is null)
        && DispatchBriefEvidence.IsRecognized(
            SessionMessageQueueService.ToBriefEvidence(row), taskId, DelegationReportFormatter.TaskMarker(taskId));

    private static Snapshot FromLegacy(int chargedAttempts, DateTime? lastStartedAt, DeliveryVerdict? verdict)
    {
        var failedComposer = verdict == DeliveryVerdict.NoComposerEvidence;
        var attempted = chargedAttempts > 0 || lastStartedAt is not null || failedComposer;
        return new Snapshot(attempted ? Math.Max(1, chargedAttempts) : 0, Complete: !attempted,
            FirstAttemptAt: null, failedComposer ? MissingComposerEvidence : null, ComposerEvidenceRefunds: 0);
    }

    private static ValueTask<AuditRecord?> FindAsync(AppDbContext db, Guid rowId, CancellationToken ct) =>
        db.AuditRecords.FindAsync([AuditId(rowId)], ct);

    // This queue uses AuditRecords for durable diagnostics. A namespaced key keeps this
    // record independent of the operator abandonment audit, which uses the queue ID itself.
    private static Guid AuditId(Guid rowId) => new(SHA256.HashData(Encoding.UTF8.GetBytes(Prefix(rowId))).AsSpan(0, 16));

    private static string Prefix(Guid rowId) => $"Boot prompt delivery history for queue {rowId:D}: ";

    private static Snapshot? Read(AuditRecord? audit, Guid rowId)
    {
        var prefix = Prefix(rowId);
        if (audit?.EventType != AuditEventType.BootPromptDelivery || !audit.Summary.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Snapshot>(audit.Summary[prefix.Length..]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Save(AppDbContext db, AuditRecord? audit, Guid rowId, DateTime at, Snapshot history)
    {
        var prefix = Prefix(rowId);
        if (audit is null)
        {
            audit = new AuditRecord { Id = AuditId(rowId), EventType = AuditEventType.BootPromptDelivery, CreatedAt = at };
            db.AuditRecords.Add(audit);
        }
        else if (audit.EventType != AuditEventType.BootPromptDelivery || !audit.Summary.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }
        // Summary is retained when old audit payloads are archived.
        audit.Summary = prefix + JsonSerializer.Serialize(history);
    }
}
