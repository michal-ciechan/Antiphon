using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Common selection for future launch/preview/policy callers. The caller supplies the resolved
/// profile kind; the stored Agent.Kind is deliberately not a fallback. Calling this pure helper
/// does not activate pins. Task/capability composition cannot inherit a standing agent's set.
/// </summary>
public sealed class NamedAgentInstructionSelection
{
    private NamedAgentInstructionSelection(Guid agentId, bool named, bool supported, bool toolsAllowed)
    {
        AgentId = agentId;
        IsNamed = named;
        RuntimeSupported = named && supported;
        CanReadLive = RuntimeSupported && toolsAllowed;
    }

    public Guid AgentId { get; }
    public bool IsNamed { get; }
    public bool RuntimeSupported { get; }
    public bool CanReadLive { get; }

    public static NamedAgentInstructionSelection Select(Agent agent, AgentKind? effectiveKind,
        bool toolsAllowed, bool isTask = false, bool isCapability = false) => new(agent.Id,
            !agent.IsPoolDelegate && !isTask && !isCapability,
            effectiveKind is AgentKind.ClaudeCode or AgentKind.Codex or AgentKind.Grok, toolsAllowed);

    public bool RequiresFile(AgentPinSnapshot snapshot)
    {
        if (snapshot.AgentId != AgentId)
            throw new InvalidOperationException("Pin snapshot belongs to another agent.");
        return IsNamed && snapshot.HasHistory;
    }
}
