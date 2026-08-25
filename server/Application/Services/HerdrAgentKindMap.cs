using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Single owner of <see cref="AgentKind"/> → herdr agent-kind string (CARD-0187). The gate, the
/// launch spec, and the wire field all call this — no second table.
/// </summary>
public static class HerdrAgentKindMap
{
    /// <summary>
    /// Kinds the herdr lane refuses. A new <see cref="AgentKind"/> member that is neither mapped
    /// nor listed here fails <c>HerdrAgentKindMapTests</c> rather than a live launch.
    /// </summary>
    public static IReadOnlyList<AgentKind> Refused { get; } = [AgentKind.OpenCode, AgentKind.Raw];

    public static bool TryMap(AgentKind kind, out string herdrKind)
    {
        herdrKind = kind switch
        {
            AgentKind.ClaudeCode => HerdrAgentKinds.Claude,
            AgentKind.Grok => HerdrAgentKinds.Grok,
            AgentKind.Codex => HerdrAgentKinds.Codex,
            _ => null!,
        };
        return herdrKind is not null;
    }
}
