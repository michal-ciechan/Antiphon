using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Maps the generic <see cref="AgentModelLevel"/> to a provider's model-family ALIAS for launch
/// args. One method per agent kind, plus <see cref="For"/> which picks between them for a caller
/// that holds an <see cref="AgentKind"/>; a future GPT kind adds its own ladder here
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

    public static string ForGrok(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier or AgentModelLevel.High => "grok-4.6",
        AgentModelLevel.Medium or AgentModelLevel.Low => "grok-4.5",
        _ => "grok-4.6",
    };

    /// <summary>
    /// The alias for the program a task or session ACTUALLY runs on (CARD-0084 S4). Every place that
    /// NAMES a tier to a human or to an interpreter — task events, retry/escalation texts, the check
    /// digest, the completion note's header, the handoff block — goes through this rather than
    /// <see cref="ForClaude"/>, or a Grok delegate's own events tell it it is running on <c>fable</c>.
    /// That is not a cosmetic slip: the escalation text is read as a promise about which model the
    /// next attempt gets, and the check digest is the evidence an interpreter reasons over.
    ///
    /// <para>Anything that is not <see cref="AgentKind.Grok"/> takes the Claude ladder, which keeps
    /// every pre-CARD-0084 string byte-identical. That fallback is safe only because
    /// <c>AgentTaskService.DelegatableKinds</c> admits ClaudeCode and Grok alone; a third
    /// delegatable kind must add its arm HERE at the same time, or its tasks will silently read as
    /// Claude. Launch arguments deliberately do NOT come through here — they branch explicitly at
    /// the one site that builds them (CARD-0084 S3), because a wrong alias there is a wrong process,
    /// not a wrong word.</para>
    /// </summary>
    public static string For(AgentKind kind, AgentModelLevel level) => kind switch
    {
        AgentKind.Grok => ForGrok(level),
        _ => ForClaude(level),
    };
}
