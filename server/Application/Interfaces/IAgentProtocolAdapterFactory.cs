using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Constructs a fresh <see cref="IAgentProtocolAdapter"/> per session.
/// Selection is by resolved <see cref="AgentKind"/> only — registry/config lookup
/// happens earlier in <c>AgentRegistry.Resolve</c>.
/// </summary>
public interface IAgentProtocolAdapterFactory
{
    IAgentProtocolAdapter Create(AgentKind kind);
}
