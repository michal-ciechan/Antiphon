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
    Guid? ApiKeyProjectId = null,
    // Overlay for THIS launch (CARD-0106 gap 1). Merged after the agent's LaunchEnvJson and before
    // ExtraEnv, in both resolvers. Null or empty means "no overlay". Never replaces the stored
    // agent env — that is AgentEnv's internal ??-replace hook, which this does not reuse.
    IReadOnlyDictionary<string, string>? LaunchEnvOverride = null,
    // Project-level default env (CARD-0106 gap 2). Merged after definition/profile env and before
    // the agent's LaunchEnvJson. Null means "none" (no project identity, or not yet fetched).
    IReadOnlyDictionary<string, string>? ProjectDefaultEnv = null,
    // CARD-0182 D2: the tier alias the caller wants appended as a model argument. Null means "do
    // not offer one" (card-spawn today, or an exact ModelId already won). The profile resolver
    // honours a blank ModelArgumentName by dropping this; AgentRegistry.Resolve appends
    // ["--model", alias] on the profile-less path. Callers must never put --model in ExtraArgs.
    string? TierModelAlias = null,
    // CARD-0193: the agent's TIER, for a caller that cannot compute an alias because it learns the
    // kind as an OUTPUT of resolution. The appenders map it against the kind they resolve. Null =
    // no tier on offer. A pre-computed TierModelAlias still wins where a caller supplies one,
    // because it may be keyed on a kind this launch does not resolve to (the dispatcher keys on
    // session.AgentKind, not profile.Kind).
    AgentModelLevel? ModelTier = null);

/// <summary>
/// How the resolved launch treated the model argument (CARD-0182 D4).
/// <see cref="None"/> is no alias and no exact model; <see cref="ProfileOwned"/> is D1 rule 1
/// suppressing a supplied alias; <see cref="Exact"/> and <see cref="Tier"/> are the two append
/// arms. <c>EffectiveModelId</c> stays the exact model or null — the drift badge depends on it.
/// </summary>
public enum LaunchModelArgument
{
    None = 0,
    ProfileOwned = 1,
    Exact = 2,
    Tier = 3
}

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
    Guid? SessionId = null,
    // CARD-0160: which lane hosts the child. PtyHost is the default — herdr stays opt-in.
    SessionBackend Backend = SessionBackend.PtyHost,
    // Required when Backend == Herdr; ignored otherwise. Resolved server-side (runner has no DB).
    global::Antiphon.SessionRunner.Contracts.HerdrLaunchOptions? Herdr = null);
