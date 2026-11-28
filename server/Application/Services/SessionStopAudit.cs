using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

public static class SessionStopAudit
{
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

    internal static void SetOutcome(AuditRecord audit, string outcome) =>
        audit.Summary = audit.Summary[..(audit.Summary.LastIndexOf('\n') + 1)] + "Outcome: " + outcome;
}
