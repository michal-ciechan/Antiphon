using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Resolves the error string on an API-error stub (CARD-0401). Claude stamps it on the sibling
/// <c>AssistantText</c> that shares the TurnEnd's uuid; Codex stamps it on the TurnEnd itself.
/// </summary>
internal static class ApiErrorStubText
{
    public static string? Prefer(string? assistantText, string? turnEndText)
    {
        if (!string.IsNullOrWhiteSpace(assistantText))
            return assistantText.Trim();
        return string.IsNullOrWhiteSpace(turnEndText) ? null : turnEndText.Trim();
    }

    public static async Task<string?> ResolveAsync(
        AppDbContext db,
        Guid sessionId,
        string? stubUuid,
        string? turnEndText,
        CancellationToken ct)
    {
        string? assistantText = null;
        if (!string.IsNullOrWhiteSpace(stubUuid))
        {
            assistantText = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == sessionId
                    && t.Uuid == stubUuid
                    && t.Kind == TranscriptKinds.AssistantText
                    && t.IsApiError == true)
                .OrderBy(t => t.Sequence)
                .Select(t => t.Text)
                .FirstOrDefaultAsync(ct);
        }

        return Prefer(assistantText, turnEndText);
    }

    public static async Task<Dictionary<(Guid SessionId, string Uuid), string?>> LoadSiblingsAsync(
        AppDbContext db,
        IReadOnlyCollection<(Guid SessionId, string? Uuid)> stubs,
        CancellationToken ct)
    {
        var uuids = stubs
            .Where(s => !string.IsNullOrWhiteSpace(s.Uuid))
            .Select(s => s.Uuid!)
            .Distinct()
            .ToList();
        if (uuids.Count == 0)
            return new Dictionary<(Guid, string), string?>();

        var sessionIds = stubs.Select(s => s.SessionId).Distinct().ToList();
        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => sessionIds.Contains(t.AgentSessionId)
                && t.Kind == TranscriptKinds.AssistantText
                && t.IsApiError == true
                && t.Uuid != null
                && uuids.Contains(t.Uuid))
            .Select(t => new { t.AgentSessionId, t.Uuid, t.Text, t.Sequence })
            .ToListAsync(ct);

        return rows
            .GroupBy(t => (t.AgentSessionId, Uuid: t.Uuid!))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Sequence).Select(x => x.Text).FirstOrDefault());
    }
}
