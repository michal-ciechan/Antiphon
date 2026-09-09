using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Only server-authored execution links and lifecycle evidence prove legacy ownership.</summary>
public sealed class StandingSessionOwnership(AppDbContext db)
{
    public async Task<(Guid? Owner, string Evidence)> ResolveAsync(AgentSession session, CancellationToken ct)
    {
        if (session.CardId is not null || session.WorktreeId is not null) return (null, "Ineligible");
        var id = session.Id.ToString("D");
        var pointers = await db.Agents.AsNoTracking().Where(a => a.PersistentSessionId == id)
            .Select(a => new { a.Id, a.IsPoolDelegate }).ToListAsync(ct);
        if (session.StandingAgentId is { } stamped)
            return pointers.Any(a => a.Id != stamped) ? (null, "Conflicting") : (stamped, "Stamped");
        var executions = await (from task in db.AgentTasks.AsNoTracking()
            join agent in db.Agents.AsNoTracking() on task.AgentId equals agent.Id
            where task.AgentSessionId == session.Id && !agent.IsPoolDelegate
            select agent.Id).Distinct().ToListAsync(ct);
        var incidents = await (from incident in db.AgentIncidents.AsNoTracking()
            join agent in db.Agents.AsNoTracking() on incident.AgentId equals agent.Id
            where incident.SessionId == session.Id && !agent.IsPoolDelegate
                && (incident.Kind == AgentIncidentKind.Crash || incident.Kind == AgentIncidentKind.RestartScheduled
                    || incident.Kind == AgentIncidentKind.Recovered)
            select agent.Id).Distinct().ToListAsync(ct);
        var owners = pointers.Select(a => a.Id).Concat(executions).Concat(incidents).Distinct().ToArray();
        return owners.Length == 1 && !pointers.Any(a => a.IsPoolDelegate)
            ? (owners[0], "Legacy") : (null, owners.Length > 1 ? "Conflicting" : "Unproven");
    }

    public async Task RequireAsync(Agent agent, AgentSession session, CancellationToken ct)
    {
        if (agent.IsPoolDelegate || session.CardId is not null || session.WorktreeId is not null)
            throw new ConflictException("Only standing cardless sessions can be selected.", "standing_resume_ineligible");
        if (session.StandingAgentId is { } stamped && stamped != agent.Id)
            throw new ConflictException("This conversation belongs to another standing agent.", "standing_resume_not_owned");
        var proof = await ResolveAsync(session, ct);
        if (proof.Owner != agent.Id)
            throw new ConflictException("Historical ownership cannot be uniquely proven.", "standing_resume_owner_unproven");
    }

    public string? Refusal(Agent agent, AgentSession session, AgentKind kind, string cwd)
    {
        if (agent.IsPoolDelegate || session.CardId is not null || session.WorktreeId is not null)
            return "standing_resume_ineligible";
        if (kind is not (AgentKind.ClaudeCode or AgentKind.Grok)) return "standing_resume_unsupported";
        if (session.AgentKind != kind || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(session.Cwd)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(cwd)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return "standing_resume_incompatible";
        return session.Status is SessionStatus.Stopped or SessionStatus.Failed ? null : "standing_resume_target_active";
    }
}
