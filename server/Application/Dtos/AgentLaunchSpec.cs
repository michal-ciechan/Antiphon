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
    IReadOnlyDictionary<string, string>? ExtraEnv = null);

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
    int MemoryLimitMb = 0);
