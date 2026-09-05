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
/// not supported" from the service. There are no unversioned aliases in Codex's catalog (bare
/// <c>astra</c> 400s the same way), so the Codex ladder pins full slugs
/// (<c>gpt-6-astra</c> / <c>gpt-5.6-terra</c> / <c>gpt-5.6-luna</c>) and needs a deliberate bump
/// when the catalog's priority-1 model changes. <c>gpt-6-astra</c> requires <b>codex-cli 0.153.4+</b>
/// (older installs HTTP 400 "requires a newer version of Codex" on every Frontier Codex dispatch).
/// Grok's ladder already pins versioned ids, so this breaks no rule Grok did not break first.</para>
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
    /// Codex's ladder (CARD-0099 S3, CARD-0396). Verified against the live CLI's own catalog
    /// (<c>codex debug models --bundled</c>, codex-cli 0.153.4, 2026-09-05): the capability order is
    /// <b>Astra &gt; Sol &gt; Terra &gt; Luna</b> — priority 1/6/7/8. Dispatch is conservative:
    /// Frontier is the new flagship <c>gpt-6-astra</c>; High stays <c>gpt-5.6-terra</c> because Sol
    /// is still a supported slug, not retired, so High is not slid onto it; Medium and Low still
    /// share <c>gpt-5.6-luna</c>.
    ///
    /// <para>Medium and Low share Luna — a genuinely short rung, not an oversight, and it is why
    /// <c>AgentTaskService.SameModelEscalationNote</c> — which compares ALIASES, not kinds — tells a
    /// Low → Medium Codex escalation that it bought a fresh context at deeper reasoning effort
    /// rather than a bigger model (CARD-0289). High and Frontier are real model changes.
    /// <c>gpt-5.4-mini</c> exists if a cheaper bottom rung is ever wanted; Luna covers both. (Grok's
    /// own ladder no longer has rungs to compare against — CARD-0169 collapsed
    /// <see cref="ForGrok"/> to grok-4.6 for every level.)</para>
    ///
    /// <para><c>gpt-6-astra</c> is rejected by CLI &lt; 0.153.4. Do not pass the bare id
    /// <c>astra</c> — the backend 400s it the same way as a garbage slug.</para>
    /// </summary>
    public static string ForCodex(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "gpt-6-astra",
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
    /// one commit. Launch arguments deliberately do NOT come through here — use <see cref="ForLaunch"/>,
    /// whose null arm prevents an unsupported runner kind from receiving a wrong process argument.</para>
    /// </summary>
    public static string For(AgentKind kind, AgentModelLevel level) => kind switch
    {
        AgentKind.Grok => ForGrok(level),
        AgentKind.Codex => ForCodex(level),
        _ => ForClaude(level),
    };

    /// <summary>
    /// Maps a level for a process launch. Unlike <see cref="For"/>, this has no Claude fallback:
    /// display and interpreter text can preserve historical wording, but an unsupported runner kind
    /// must not receive a Claude model argument (CARD-0193).
    /// </summary>
    public static string? ForLaunch(AgentKind kind, AgentModelLevel level) => kind switch
    {
        AgentKind.Codex => ForCodex(level),
        AgentKind.Grok => ForGrok(level),
        AgentKind.ClaudeCode => ForClaude(level),
        _ => null,
    };
}
