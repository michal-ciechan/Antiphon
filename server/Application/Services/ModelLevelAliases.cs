using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Maps the generic <see cref="AgentModelLevel"/> to a provider's model-family ALIAS for launch
/// args. One method per agent kind; a future GPT kind adds its own ladder here
/// (Sol = Frontier, Terra = High, Luna = Medium). Aliases only — never versioned model ids —
/// so every launch picks up the family's current model (Claude aliases verified against the CLI
/// 2026-07-31: fable → claude-fable-5, opus → claude-opus-5, sonnet → claude-sonnet-5,
/// haiku → claude-haiku-4-5).
/// </summary>
public static class ModelLevelAliases
{
    public static string ForClaude(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "fable",
        AgentModelLevel.High => "opus",
        AgentModelLevel.Medium => "sonnet",
        AgentModelLevel.Low => "haiku",
        _ => "opus",
    };
}
