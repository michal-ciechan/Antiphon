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
    private readonly IOptions<Antiphon.SessionRunner.Contracts.GrokRulesSettings>? _rulesSettings;

    public AgentProtocolAdapterFactory(
        IOptions<AgentRegistrySettings> options,
        ISessionRunnerClient sessionRunnerClient,
        IOptions<SupervisionSettings>? supervisionSettings = null,
        ILoggerFactory? loggerFactory = null,
        IOptions<Antiphon.SessionRunner.Contracts.GrokRulesSettings>? rulesSettings = null)
    {
        _options = options;
        _sessionRunnerClient = sessionRunnerClient;
        _supervisionSettings = supervisionSettings;
        _loggerFactory = loggerFactory;
        _rulesSettings = rulesSettings;
    }

    public IAgentProtocolAdapter Create(AgentKind kind) => kind switch
    {
        AgentKind.Raw => new RunnerRawAdapter(_sessionRunnerClient),
        AgentKind.ClaudeCode => new RunnerClaudeAdapter(
            _sessionRunnerClient, _options, _supervisionSettings,
            _loggerFactory?.CreateLogger<RunnerClaudeAdapter>()),
        AgentKind.Codex => new RunnerCodexAdapter(
            _sessionRunnerClient, _options,
            _loggerFactory?.CreateLogger<RunnerCodexAdapter>()),
        AgentKind.OpenCode => new RunnerOpenCodeAdapter(_sessionRunnerClient, _options),
        AgentKind.Grok => new RunnerGrokAdapter(
            _sessionRunnerClient, _options, _supervisionSettings,
            _loggerFactory?.CreateLogger<RunnerGrokAdapter>(), _rulesSettings),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No adapter is registered for AgentKind '{kind}'."),
    };
}
