using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
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
    private const string PreparedOutcome = "Prepared, command not sent";
    internal const string PreparationExpired = "Preparation expired before command";
    internal static readonly TimeSpan PreparationLifetime = TimeSpan.FromSeconds(60);
    private const string SettledPrefix = "Verified stop settled ";

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

    internal static string RequestKey(VerifiedSessionStopRequest request, string actor, string reason)
    {
        var key = string.Join('\n', actor, SessionGeneration.Normalize(request.ExpectedGeneration).ToString("O"),
            request.ExpectedOwnerId?.ToString("D"), request.ExpectedLastTaskId?.ToString("D"),
            request.ExpectedLastTaskRevision?.ToString("D"), request.ExpectedTurnEndSequence.ToString(CultureInfo.InvariantCulture), reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    internal static bool MatchesRequest(AuditRecord audit, string key) =>
        audit.Summary.Contains(RequestLine(key), StringComparison.Ordinal);

    private static string RequestLine(string key) => $"\nRequest: {key}\n";

    internal static void BeginIntent(AuditRecord audit, Guid sessionId, DateTime generation, string requestKey)
    {
        audit.Summary = PendingKey(sessionId, generation)
            + audit.Summary[..audit.Summary.LastIndexOf('\n')] + RequestLine(requestKey) + "Outcome: " + PreparedOutcome;
    }

    internal static bool IsPrepared(AuditRecord audit) =>
        audit.Summary.EndsWith("\nOutcome: " + PreparedOutcome, StringComparison.Ordinal);

    internal static bool IsConfirmed(AuditRecord audit) =>
        audit.Summary.StartsWith(SettledPrefix, StringComparison.Ordinal)
        && audit.Summary.EndsWith("\nOutcome: Exited", StringComparison.Ordinal);

    internal static void MarkCommandPossiblySent(AuditRecord audit) =>
        SetOutcome(audit, "Command may have been sent", settleIntent: false);

    internal static void SetOutcome(AuditRecord audit, string outcome, bool settleIntent = true)
    {
        if (settleIntent && audit.Summary.StartsWith(PendingPrefix, StringComparison.Ordinal))
            audit.Summary = SettledPrefix + audit.Summary[PendingPrefix.Length..];
        audit.Summary = audit.Summary[..(audit.Summary.LastIndexOf('\n') + 1)] + "Outcome: " + outcome;
    }

    private static string PendingKey(Guid sessionId, DateTime generation) =>
        $"{PendingPrefix}{sessionId:D} {SessionGeneration.Normalize(generation):O}\n";

    private static IQueryable<AuditRecord> Confirmed(AppDbContext db, Guid id, DateTime generation)
    {
        var key = SettledPrefix + PendingKey(id, generation)[PendingPrefix.Length..];
        return db.AuditRecords.Where(a => a.EventType == AuditEventType.SessionStop
            && a.Summary.StartsWith(key) && a.Summary.EndsWith("\nOutcome: Exited"));
    }

    internal static Task<bool> HasVerifiedExitAsync(AppDbContext db, AgentSession session, CancellationToken ct) =>
        Confirmed(db, session.Id, session.StartedAt).AnyAsync(ct);

    internal static Task<bool> HasCompletedRequestAsync(
        AppDbContext db, Guid id, DateTime generation, string requestKey, CancellationToken ct)
    {
        var request = RequestLine(requestKey);
        return Confirmed(db, id, generation).AnyAsync(a => a.Summary.Contains(request), ct);
    }

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

    internal static Task<List<(Guid SessionId, DateTime Generation, AuditRecord Audit)>> ReadPendingAsync(
        AppDbContext db, CancellationToken ct) => ReadIntentsAsync(Pending(db), ct);

    internal static Task<List<(Guid SessionId, DateTime Generation, AuditRecord Audit)>> ReadExpiredPreparationsAsync(
        AppDbContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now - PreparationLifetime;
        return ReadIntentsAsync(Pending(db).Where(a => a.CreatedAt <= cutoff
            && a.Summary.EndsWith("\nOutcome: " + PreparedOutcome)), ct);
    }

    private static async Task<List<(Guid SessionId, DateTime Generation, AuditRecord Audit)>> ReadIntentsAsync(
        IQueryable<AuditRecord> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().ToListAsync(ct);
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
