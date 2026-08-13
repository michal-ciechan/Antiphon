using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;

namespace Antiphon.Server.Infrastructure.Agents.Pty;

public sealed class AgentProtocolAdapterFactory : IAgentProtocolAdapterFactory
{
    private readonly IOptions<AgentRegistrySettings> _options;
    private readonly ISessionRunnerClient _sessionRunnerClient;
    private readonly IOptions<SupervisionSettings>? _supervisionSettings;
    private readonly ILoggerFactory? _loggerFactory;

    public AgentProtocolAdapterFactory(
        IOptions<AgentRegistrySettings> options,
        ISessionRunnerClient sessionRunnerClient,
        IOptions<SupervisionSettings>? supervisionSettings = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options;
        _sessionRunnerClient = sessionRunnerClient;
        _supervisionSettings = supervisionSettings;
        _loggerFactory = loggerFactory;
    }

    public IAgentProtocolAdapter Create(AgentKind kind) => kind switch
    {
        AgentKind.Raw => new RunnerRawAdapter(_sessionRunnerClient),
        AgentKind.ClaudeCode => new RunnerClaudeAdapter(
            _sessionRunnerClient, _options, _supervisionSettings,
            _loggerFactory?.CreateLogger<RunnerClaudeAdapter>()),
        AgentKind.Codex => new RunnerCodexAdapter(_sessionRunnerClient, _options),
        AgentKind.OpenCode => new RunnerOpenCodeAdapter(_sessionRunnerClient, _options),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No adapter is registered for AgentKind '{kind}'."),
    };
}
