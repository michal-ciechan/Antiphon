using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0650 S4 repair 3. The operator's way out of an expectation-watchdog composer hold, and the
/// audit trail both ends of it leave. A hold is the right default while an unconfirmed watchdog
/// prompt may still stand in the composer, but where the composer cannot be read the only release
/// evidence is a transcript record, and a submitted prompt whose record never arrives would hold
/// forever. The release is a human judgment ("I looked; the composer is clear"), so it takes a
/// reason, is recorded where the nudge itself is audited, and never types anything.
/// </summary>
public static class ExpectationHoldAudit
{
    public const string ReleasedMarkerPrefix = "[expectation-hold-released:";

    public static string ReleaseRoute(Guid sessionId) => $"/api/sessions/{sessionId:D}/expectation-hold/release";

    /// <summary>The safe next action for a held session: named in the Check note, the audit card and every refusal.</summary>
    public static string ReleaseHint(Guid sessionId) =>
        "If the composer is clear (the prompt was submitted or removed), an operator can release the hold with "
        + $"POST {ReleaseRoute(sessionId)} and a JSON body {{\"reason\": \"...\"}}. The release is audited and types nothing.";

    public static string HoldNote(Guid nudgeId, Guid sessionId, ExpectationAttemptState state) =>
        $"[expectation-nudge:{nudgeId:D}] The prompt to session {sessionId:D} is {state} with no transcript record, "
        + "so ordinary input to that session is held while it may still stand in the composer. "
        + ReleaseHint(sessionId);

    public static string ReleasedNote(
        Guid nudgeId, Guid sessionId, DateTime generation, ExpectationAttemptState previous, string reason) =>
        $"{ReleasedMarkerPrefix}{nudgeId:D}] Operator released the composer hold on session {sessionId:D} "
        + $"(generation {generation:O}, was {previous}). Nothing was typed; the prompt is still unconfirmed and "
        + $"any operator page stays due. Reason: {reason}";

    /// <summary>
    /// One comment on the nudge's audit card and one Check note on each task the nudge's own Check
    /// notes were written on. Added to <paramref name="db"/>; the caller saves.
    /// </summary>
    public static async Task AddAsync(
        AppDbContext db, Guid auditCommentId, string checkEventIdsJson, string text, string author, DateTime now,
        CancellationToken ct)
    {
        var cardId = await db.CardComments.AsNoTracking()
            .Where(c => c.Id == auditCommentId)
            .Select(c => (Guid?)c.CardId)
            .FirstOrDefaultAsync(ct);
        if (cardId is { } card)
        {
            db.CardComments.Add(new CardComment
            {
                Id = Guid.NewGuid(),
                CardId = card,
                Body = text,
                Author = author,
                Origin = CardCommentOrigin.Antiphon,
                CreatedAt = now,
            });
        }

        var checkIds = CheckEventIds(checkEventIdsJson);
        if (checkIds.Length == 0)
            return;
        var tasks = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => checkIds.Contains(e.Id))
            .Select(e => e.AgentTaskId)
            .Distinct()
            .ToListAsync(ct);
        var detail = text.Length <= ExpectationLedger.MaxCheckDetailChars
            ? text
            : text[..ExpectationLedger.MaxCheckDetailChars];
        foreach (var task in tasks)
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task,
                Type = AgentTaskEventType.Check,
                At = now,
                Detail = detail,
            });
        }
    }

    private static Guid[] CheckEventIds(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Guid[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
