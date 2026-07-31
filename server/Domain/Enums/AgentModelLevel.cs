namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Generic model CAPABILITY LEVEL for an agent's sessions. Each agent kind maps the level to its
/// provider's ladder at launch time (see <c>ModelLevelAliases</c>) — Claude Code today:
/// Frontier → fable, High → opus, Medium → sonnet, Low → haiku; a future GPT kind would map
/// e.g. Sol = Frontier, Terra = High, Luna = Medium. Always passed as the provider's FAMILY
/// alias, never a full versioned model id, so launches pick up each family's current model.
/// High (the Opus tier) is the default.
/// </summary>
public enum AgentModelLevel
{
    Frontier = 0,
    High = 1,
    Medium = 2,
    Low = 3,
}
