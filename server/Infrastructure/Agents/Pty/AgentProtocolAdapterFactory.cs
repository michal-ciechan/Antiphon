using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Agents.Pty;

public sealed class AgentProtocolAdapterFactory : IAgentProtocolAdapterFactory
{
    private readonly IOptions<AgentRegistrySettings> _options;

    public AgentProtocolAdapterFactory(IOptions<AgentRegistrySettings> options)
    {
        _options = options;
    }

    public IAgentProtocolAdapter Create(AgentKind kind) => kind switch
    {
        AgentKind.Raw => new RawPtyAdapter(),
        AgentKind.ClaudeCode => new ClaudeAdapter(_options),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No adapter is registered for AgentKind '{kind}'."),
    };
}
