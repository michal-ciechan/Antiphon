using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Maps the generic <see cref="AgentModelLevel"/> to a provider's model-family ALIAS for launch
/// args. One method per agent kind, plus <see cref="For"/> which picks between them for a caller
/// that holds an <see cref="AgentKind"/>.
///
/// <para>Claude and Grok ride family ALIASES — never versioned model ids — so every launch picks up
/// the family's current model (Claude aliases verified against the CLI 2026-07-31: fable →
/// claude-fable-5, opus → claude-opus-5, sonnet → claude-sonnet-5, haiku → claude-haiku-4-5).
/// <b>Codex cannot</b>: measured 2026-08-20 against codex-cli 0.147.0, <c>-m luna</c> is rejected
/// twice over — "Model metadata for `luna` not found" locally, then HTTP 400 "the 'luna' model is
/// not supported" from the service. There are no unversioned aliases in Codex's catalog, so the
/// Codex ladder pins full <c>gpt-5.6-*</c> slugs and needs a deliberate bump when 5.7 ships. Grok's
/// ladder already pins versioned ids, so this breaks no rule Grok did not break first.</para>
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

    // CARD-0169: every level maps to grok-4.6 — the operator's own instruction, not a
    // capability/cost-derived rung. grok-4.5 stays a valid, selectable model id elsewhere
    // (the AgentTuiRunnerCatalog listing, historical records) — this only removes it from the
    // level ladder new dispatches resolve through.
    public static string ForGrok(AgentModelLevel level) => "grok-4.6";

    /// <summary>
    /// Codex's ladder (CARD-0099 S3). Verified against the live CLI's own catalog
    /// (<c>~/.codex/models_cache.json</c>, codex-cli 0.147.0, read 2026-08-20): the capability order
    /// is <b>Sol &gt; Terra &gt; Luna</b> — priority 1/2/3, described as "latest frontier agentic
    /// coding model", "balanced … for everyday work" and "fast and affordable" — which is NOT the
    /// Sol/Luna/Terra order the card names them in.
    ///
    /// <para>Medium and Low share <c>gpt-5.6-luna</c> — a genuinely short rung, not an oversight,
    /// and it is why <c>AgentTaskService.SameModelEscalationNote</c> — which compares ALIASES, not
    /// kinds — tells a Low → Medium Codex escalation that it bought a fresh context rather than a
    /// bigger model. The three rungs above it are all real model changes. <c>gpt-5.4-mini</c>
    /// exists if a cheaper bottom rung is ever wanted; three names were asked for, so Luna covers
    /// both. (Grok's own ladder no longer has rungs to compare against — CARD-0169 collapsed
    /// <see cref="ForGrok"/> to grok-4.6 for every level.)</para>
    /// </summary>
    public static string ForCodex(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "gpt-5.6-sol",
        AgentModelLevel.High => "gpt-5.6-terra",
        AgentModelLevel.Medium or AgentModelLevel.Low => "gpt-5.6-luna",
        _ => "gpt-5.6-terra",
    };

    /// <summary>
    /// The alias for the program a task or session ACTUALLY runs on (CARD-0084 S4). Every place that
    /// NAMES a tier to a human or to an interpreter — task events, retry/escalation texts, the check
    /// digest, the completion note's header, the handoff block — goes through this rather than
    /// <see cref="ForClaude"/>, or a Grok delegate's own events tell it it is running on <c>fable</c>.
    /// That is not a cosmetic slip: the escalation text is read as a promise about which model the
    /// next attempt gets, and the check digest is the evidence an interpreter reasons over.
    ///
    /// <para>Anything with no arm here takes the Claude ladder, which keeps every pre-CARD-0084
    /// string byte-identical. That fallback is safe only while <c>AgentTaskService.DelegatableKinds</c>
    /// admits exactly the kinds that DO have an arm; a fourth delegatable kind must add its arm HERE
    /// at the same time, or its tasks will silently read as Claude. Codex is the case that proves the
    /// contract is real rather than decorative (CARD-0099 S3): it was admitted and given its arm in
    /// one commit. Launch arguments deliberately do NOT come through here — they branch explicitly at
    /// the sites that build them (CARD-0084 S3, CARD-0099 S3), because a wrong alias there is a wrong
    /// process, not a wrong word.</para>
    /// </summary>
    public static string For(AgentKind kind, AgentModelLevel level) => kind switch
    {
        AgentKind.Grok => ForGrok(level),
        AgentKind.Codex => ForCodex(level),
        _ => ForClaude(level),
    };
}
