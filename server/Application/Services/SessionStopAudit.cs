using System.Globalization;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public static class SessionStopAudit
{
    // The permanent audit summary also carries the active intent. The archivable JSON tier
    // is optional, so it must not carry any state required for admission after restart.
    private const string PendingPrefix = "Verified stop pending ";

    public static string Reason(string? supplied, string fallback)
    {
        var reason = string.IsNullOrWhiteSpace(supplied) ? fallback : supplied.Trim().ReplaceLineEndings(" ");
        if (reason.Length > 1000)
            throw new ValidationException("reason", "The stop reason must be at most 1000 characters.");
        return reason;
    }

    internal static AuditRecord Begin(
        AppDbContext db, Guid sessionId, DateTime generation, string actor, string reason, DateTime now)
    {
        var audit = new AuditRecord
        {
            Id = Guid.NewGuid(),
            EventType = AuditEventType.SessionStop,
            CreatedAt = now,
            Summary = $"Session {sessionId:D} generation {generation:O} requested by {actor}. Reason: {reason}\nOutcome: Requested",
        };
        db.AuditRecords.Add(audit);
        return audit;
    }

    internal static void BeginIntent(AuditRecord audit, Guid sessionId, DateTime generation) =>
        audit.Summary = PendingKey(sessionId, generation) + audit.Summary;

    internal static void SetOutcome(AuditRecord audit, string outcome, bool settleIntent = true)
    {
        var firstLine = settleIntent && audit.Summary.StartsWith(PendingPrefix, StringComparison.Ordinal)
            ? audit.Summary.IndexOf('\n') + 1
            : 0;
        audit.Summary = audit.Summary[firstLine..(audit.Summary.LastIndexOf('\n') + 1)] + "Outcome: " + outcome;
    }

    private static string PendingKey(Guid sessionId, DateTime generation) =>
        $"{PendingPrefix}{sessionId:D} {SessionGeneration.Normalize(generation):O}\n";

    private static IQueryable<AuditRecord> Pending(AppDbContext db) =>
        db.AuditRecords.Where(a => a.EventType == AuditEventType.SessionStop && a.Summary.StartsWith(PendingPrefix));

    internal static IQueryable<AuditRecord> Pending(AppDbContext db, Guid sessionId, DateTime generation)
    {
        var key = PendingKey(sessionId, generation);
        return Pending(db).Where(a => a.Summary.StartsWith(key));
    }

    internal static async Task<bool> IsPendingAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var generation = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => (DateTime?)s.StartedAt).SingleOrDefaultAsync(ct);
        return generation is { } value && await Pending(db, sessionId, value).AnyAsync(ct);
    }

    internal static async Task<List<(Guid SessionId, DateTime Generation, AuditRecord Audit)>> ReadPendingAsync(
        AppDbContext db, CancellationToken ct)
    {
        var rows = await Pending(db).AsNoTracking().ToListAsync(ct);
        var result = new List<(Guid, DateTime, AuditRecord)>(rows.Count);
        foreach (var row in rows)
        {
            var firstLine = row.Summary.Split('\n', 2)[0][PendingPrefix.Length..];
            var parts = firstLine.Split(' ', 2);
            if (parts.Length == 2 && Guid.TryParseExact(parts[0], "D", out var id)
                && DateTime.TryParseExact(parts[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var generation))
                result.Add((id, generation, row));
        }
        return result;
    }

    internal static bool IsMatchingExit(AgentSession session, SessionRunnerSessionDto? runner) =>
        runner is { Status: "Exited" } && runner.SessionId == session.Id
        && SessionGeneration.Equal(runner.AcceptedStartedAt, session.StartedAt);

    internal static void RecordExit(AgentSession session, SessionRunnerSessionDto exited, AuditRecord audit, DateTime now)
    {
        SessionExitEvidence.Apply(session, exited.ExitCode, exited.ExitSignal, exited.ExitStopIntent);
        SessionTermination.Record(session, SessionTerminationSource.OperatorRequest);
        session.Status = SessionStatus.Stopped;
        session.FailureReason = null;
        session.EndedAt ??= now;
        session.LastSeenAt = now;
        SetOutcome(audit, "Exited");
    }
}
