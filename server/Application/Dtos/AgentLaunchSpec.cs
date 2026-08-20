using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Per-session inputs supplied at launch time. Combined with an <see cref="Antiphon.Server.Application.Settings.AgentDefinition"/>
/// by <c>AgentRegistry.Resolve</c> to produce a fully-formed <see cref="AgentLaunchSpec"/>.
/// </summary>
public sealed record AgentLaunchOptions(
    string? Cwd = null,
    int Cols = 120,
    int Rows = 30,
    IReadOnlyList<string>? ExtraArgs = null,
    IReadOnlyDictionary<string, string>? ExtraEnv = null,
    // The agent's own launch environment (CARD-0106 S2), merged BEFORE ExtraEnv in both resolvers.
    // Null means "derive it from the agent" where an agent is in hand, and "none" where one is not
    // — deliberately not the same as an empty dictionary, which an explicit caller can pass to say
    // "this launch carries no agent env" and have it stick.
    //
    // The order is the point: ExtraEnv is Antiphon's own orchestration block (ANTIPHON_SESSION_ID,
    // ANTIPHON_TASK_TOKEN, ...) and must outrank anything an operator typed into an agent's
    // settings, or a per-agent override could point a delegate at another session's identity.
    IReadOnlyDictionary<string, string>? AgentEnv = null,
    // The project whose API keys this launch resolves against (CARD-0106 S2). Null = derive it from
    // the agent's board, which for an agent with no board means GLOBAL keys only. Set explicitly by
    // the CARD paths, where the card's own board names the project even when no agent is involved.
    Guid? ApiKeyProjectId = null);

/// <summary>
/// Fully-resolved launch instruction passed to <c>IAgentProtocolAdapter.StartAsync</c>.
/// All fields are immutable; collection exposure is read-only.
/// </summary>
public sealed record AgentLaunchSpec(
    string DefinitionName,
    AgentKind Kind,
    string Exe,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    string Cwd,
    int Cols,
    int Rows,
    int MemoryLimitMb = 0,
    Guid? SessionId = null);
