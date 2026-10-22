using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The dispatch and optional-specialist preflight use the same pinned-model rule.</summary>
public static class DispatchModelAlias
{
    public static string Resolve(AgentKind kind, AgentModelLevel level, string? modelId) =>
        ModelAlias.Normalize(kind, modelId) ?? ModelLevelAliases.For(kind, level);

    /// <summary>
    /// Canonical alias a queued task launches at <paramref name="level"/> on <paramref name="agentId"/>.
    /// A specialist's recorded alias wins; then a standing agent's exact <see cref="Agent.ModelId"/>
    /// when it normalizes; otherwise the tier ladder. The dispatcher tick checks this alias against
    /// the holds.
    /// </summary>
    public static async Task<string> ForTaskAsync(
        AppDbContext db, AgentTask task, AgentModelLevel level, Guid? agentId, CancellationToken ct)
    {
        if (task.SpecialistModelAlias is { } snapshot) return snapshot;
        if (agentId is not Guid id) return Resolve(task.AgentKind, level, null);
        var modelId = await db.Agents.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.ModelId)
            .FirstOrDefaultAsync(ct);
        return Resolve(task.AgentKind, level, modelId);
    }

    /// <summary>
    /// CARD-1490: the alias the tick will check once a retry or escalation requeues the task at
    /// <paramref name="level"/>. The requeue keeps a pinned agent and retires an ephemeral one.
    /// </summary>
    public static Task<string> AfterRequeueAsync(
        AppDbContext db, AgentTask task, AgentModelLevel level, CancellationToken ct) =>
        ForTaskAsync(db, task, level, task.Ephemeral ? null : task.AgentId, ct);

    /// <summary>
    /// CARD-1496: the model a list-governed task keeps on its standing agent wherever the walk
    /// sends it on <paramref name="kind"/>: the agent's <see cref="Agent.ModelId"/> when it
    /// normalizes (the <see cref="ForTaskAsync"/> rule), else null. Null for an ephemeral agent,
    /// which the requeue retires.
    /// </summary>
    public static async Task<string?> StandingAgentModelAsync(
        AppDbContext db, AgentTask task, AgentKind kind, CancellationToken ct)
    {
        if (task.Ephemeral || task.AgentId is not Guid id) return null;
        var modelId = await db.Agents.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.ModelId)
            .FirstOrDefaultAsync(ct);
        return ModelAlias.Normalize(kind, modelId);
    }
}
