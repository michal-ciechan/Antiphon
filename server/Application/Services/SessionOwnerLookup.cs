using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Standing agents own a session via <c>PersistentSessionId</c>; a delegate task session often
/// does not — the pool/ephemeral agent is on the task row, and a later spawn can overwrite
/// the standing pointer. Same two-step lookup AttentionService uses for session owners.
/// </summary>
internal static class SessionOwnerLookup
{
    public static async Task<Guid?> ResolveOwningAgentIdAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var sessionIdText = sessionId.ToString("D");
        var standing = await db.Agents
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (standing is Guid standingId)
            return standingId;

        return await db.AgentTasks
            .Where(t => t.AgentSessionId == sessionId && t.AgentId != null)
            .OrderByDescending(t => t.DispatchedAt ?? t.CreatedAt)
            .Select(t => t.AgentId)
            .FirstOrDefaultAsync(ct);
    }
}
