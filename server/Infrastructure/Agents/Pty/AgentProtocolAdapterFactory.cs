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
    private readonly ISessionRunnerDirectory? _directory;
    private readonly IOptions<SupervisionSettings>? _supervisionSettings;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IOptions<Antiphon.SessionRunner.Contracts.GrokRulesSettings>? _rulesSettings;

    public AgentProtocolAdapterFactory(
        IOptions<AgentRegistrySettings> options,
        ISessionRunnerClient sessionRunnerClient,
        IOptions<SupervisionSettings>? supervisionSettings = null,
        ILoggerFactory? loggerFactory = null,
        IOptions<Antiphon.SessionRunner.Contracts.GrokRulesSettings>? rulesSettings = null,
        ISessionRunnerDirectory? directory = null)
    {
        _options = options;
        _sessionRunnerClient = sessionRunnerClient;
        _supervisionSettings = supervisionSettings;
        _loggerFactory = loggerFactory;
        _rulesSettings = rulesSettings;
        _directory = directory;
    }

    public IAgentProtocolAdapter Create(AgentKind kind) => Create(kind, runnerId: null);

    public IAgentProtocolAdapter Create(AgentKind kind, string? runnerId)
    {
        var client = string.IsNullOrWhiteSpace(runnerId) || _directory is null
            ? _sessionRunnerClient
            : RemoteClient(_directory, runnerId);
        return kind switch
        {
            AgentKind.Raw => new RunnerRawAdapter(client),
            AgentKind.ClaudeCode => new RunnerClaudeAdapter(
                client, _options, _supervisionSettings,
                _loggerFactory?.CreateLogger<RunnerClaudeAdapter>()),
            AgentKind.Codex => new RunnerCodexAdapter(
                client, _options,
                _loggerFactory?.CreateLogger<RunnerCodexAdapter>()),
            AgentKind.OpenCode => new RunnerOpenCodeAdapter(client, _options),
            AgentKind.Grok => new RunnerGrokAdapter(
                client, _options, _supervisionSettings,
                _loggerFactory?.CreateLogger<RunnerGrokAdapter>(), _rulesSettings),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No adapter is registered for AgentKind '{kind}'."),
        };
    }

    /// <summary>
    /// CARD-0679 D-6: a remote adapter routes every call to the runner's current connection, so it
    /// follows a reconnect. Resolving once here keeps today's refusal of an adapter for a runner
    /// that is unavailable at creation.
    /// </summary>
    private static ISessionRunnerClient RemoteClient(ISessionRunnerDirectory directory, string runnerId)
    {
        var resolved = directory.Resolve(runnerId);
        return ReferenceEquals(resolved, directory.Local)
            ? resolved
            : new RunnerScopedSessionRunnerClient(directory, runnerId);
    }
}
