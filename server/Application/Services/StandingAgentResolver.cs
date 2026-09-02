using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0291 / CARD-0057: resolve a caller-typed standing-agent reference — a guid, an exact slug,
/// or a case-insensitive exact name, tried in that order. Shared so schedules do not copy the
/// task-create lookup.
/// </summary>
internal static class StandingAgentResolver
{
    public static async Task<Agent> ResolveAsync(
        AppDbContext db, string reference, string fieldName, CancellationToken ct)
    {
        var value = reference.Trim();
        List<Agent> matches;
        if (Guid.TryParse(value, out var id))
        {
            matches = await db.Agents.AsNoTracking().Where(a => a.Id == id).ToListAsync(ct);
        }
        else
        {
            matches = await db.Agents.AsNoTracking().Where(a => a.Slug == value).ToListAsync(ct);
            if (matches.Count == 0)
            {
                var lowered = value.ToLowerInvariant();
                matches = await db.Agents.AsNoTracking()
                    .Where(a => a.Name.ToLower() == lowered)
                    .ToListAsync(ct);
            }
        }

        if (matches.Count == 0)
        {
            throw new ValidationException(
                fieldName,
                $"No agent matches '{value}' (tried guid, exact slug, then case-insensitive "
                + "name). Check the agent's name, or pass its guid.");
        }

        if (matches.Count > 1)
        {
            var candidates = string.Join(
                ", ", matches.Select(a => $"'{a.Name}' (slug '{a.Slug}', {a.Id})"));
            throw new ValidationException(
                fieldName,
                $"'{value}' is ambiguous — it matches {matches.Count} agents: {candidates}. "
                + "Pass the guid of the one you mean.");
        }

        var agent = matches[0];
        if (agent.IsPoolDelegate)
        {
            throw new ValidationException(
                fieldName,
                $"'{agent.Name}' is a pool delegate, not a standing agent. For a follow-up on "
                + "the delegate that ran an earlier task, use followUpOnTask (-OnAgent <taskId>).");
        }

        return agent;
    }
}
