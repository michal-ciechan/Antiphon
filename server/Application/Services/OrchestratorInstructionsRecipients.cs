using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>One standing agent as the recipient selector sees it. No entity, no database.</summary>
public sealed record OrchestratorInstructionsAgent(
    Guid Id,
    bool CarriesOrchestratorBundle,
    bool IsPoolDelegate,
    bool IsSpecialist,
    Guid? PersistentSessionId,
    PolicyRefreshMode? PolicyRefreshMode);

/// <summary>One session row. <paramref name="InstructionsVersion"/> null means the column was never stamped.</summary>
public sealed record OrchestratorInstructionsSession(
    Guid Id,
    Guid? AgentId,
    SessionStatus Status,
    SessionBackend Backend,
    Guid? CardId,
    string? InstructionsVersion);

/// <summary>One task row that may name a session.</summary>
public sealed record OrchestratorInstructionsTask(
    AgentTaskKind Kind,
    AgentTaskStatus Status,
    Guid? SessionId);

/// <summary>
/// Standing orchestrators plus orchestrator task sessions. Workers, specialists, Herdr panes,
/// card sessions, and sessions already at the new version are out. <see cref="PolicyRefreshMode"/>
/// null means Auto.
/// </summary>
public static class OrchestratorInstructionsRecipients
{
    public static IReadOnlyList<Guid> Select(
        IReadOnlyList<OrchestratorInstructionsAgent> agents,
        IReadOnlyList<OrchestratorInstructionsSession> sessions,
        IReadOnlyList<OrchestratorInstructionsTask> tasks,
        string? currentVersion,
        OrchestratorInstructionsNotify notify)
    {
        if (notify == OrchestratorInstructionsNotify.Off)
            return [];

        var bySession = sessions.ToDictionary(s => s.Id);
        var byAgent = agents.ToDictionary(a => a.Id);
        var chosen = new List<Guid>();
        var seen = new HashSet<Guid>();

        if (notify is OrchestratorInstructionsNotify.All or OrchestratorInstructionsNotify.Standing)
        {
            foreach (var agent in agents)
            {
                if (!agent.CarriesOrchestratorBundle || agent.IsPoolDelegate || agent.IsSpecialist)
                    continue;
                if (agent.PolicyRefreshMode == PolicyRefreshMode.Off)
                    continue;
                if (agent.PersistentSessionId is not Guid sessionId)
                    continue;
                if (!bySession.TryGetValue(sessionId, out var session))
                    continue;
                if (!Accept(session, byAgent, currentVersion))
                    continue;
                if (seen.Add(session.Id))
                    chosen.Add(session.Id);
            }
        }

        if (notify == OrchestratorInstructionsNotify.All)
        {
            foreach (var task in tasks)
            {
                if (task.Kind != AgentTaskKind.Orchestrator)
                    continue;
                if (task.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working))
                    continue;
                if (task.SessionId is not Guid sessionId)
                    continue;
                if (!bySession.TryGetValue(sessionId, out var session))
                    continue;
                if (!Accept(session, byAgent, currentVersion))
                    continue;
                if (seen.Add(session.Id))
                    chosen.Add(session.Id);
            }
        }

        return chosen;
    }

    private static bool Accept(
        OrchestratorInstructionsSession session,
        IReadOnlyDictionary<Guid, OrchestratorInstructionsAgent> agents,
        string? currentVersion)
    {
        if (session.Status != SessionStatus.Running)
            return false;
        if (session.Backend != SessionBackend.PtyHost)
            return false;
        if (session.CardId is not null)
            return false;
        if (session.AgentId is Guid agentId
            && agents.TryGetValue(agentId, out var agent)
            && (agent.IsSpecialist || agent.PolicyRefreshMode == PolicyRefreshMode.Off))
            return false;
        if (!string.IsNullOrEmpty(currentVersion)
            && string.Equals(session.InstructionsVersion, currentVersion, StringComparison.Ordinal))
            return false;
        return true;
    }
}
