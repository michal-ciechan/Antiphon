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
    /// <summary>
    /// Resolves the agent an incident for <paramref name="sessionId"/> may be hung on, and
    /// <b>only ever names an agent row that still exists</b> (CARD-0195).
    ///
    /// <para><c>AgentTasks.AgentId</c> carries NO foreign key to <c>Agents</c> — deliberately, so
    /// that retiring a warm delegate or reaping an ephemeral one leaves the delegation history
    /// intact rather than cascading it away. The cost is that the column dangles the moment the
    /// agent is deleted, which is the ordinary end state of a settled delegate task, not a rare
    /// race: measured on this deployment 2026-08-25, <b>447 of the 539</b> task rows carrying an
    /// <c>AgentId</c> pointed at an agent that no longer existed.</para>
    ///
    /// <para>Returning one of those ids looked like a successful lookup and then blew up at
    /// <c>SaveChanges</c> — <c>AgentIncident.AgentId</c> is a required FK with cascade delete, so
    /// the insert failed 23503 on <c>FK_AgentIncidents_Agents_AgentId</c> and every caller's
    /// catch swallowed the whole incident (session <c>8be1afc5</c>, 2026-08-25 02:50Z, and six
    /// earlier occurrences in the 2026-08-21 log). A dead id is never a usable answer, so it is
    /// filtered here rather than defended against at each of the three call sites; the callers'
    /// existing "nobody owns this session" branch then raises the standalone alert that keeps the
    /// fault visible.</para>
    /// </summary>
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
            .Where(t => t.AgentSessionId == sessionId
                && t.AgentId != null
                && db.Agents.Any(a => a.Id == t.AgentId))
            .OrderByDescending(t => t.DispatchedAt ?? t.CreatedAt)
            .Select(t => t.AgentId)
            .FirstOrDefaultAsync(ct);
    }
}
