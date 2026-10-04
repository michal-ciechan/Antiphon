using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Maps the generic <see cref="AgentModelLevel"/> to a provider's model-family ALIAS for launch
/// args. One method per agent kind, plus <see cref="For"/> which picks between them for a caller
/// that holds an <see cref="AgentKind"/>.
///
/// <para>Claude and Grok ride family ALIASES — never versioned model ids — so every launch picks up
/// the family's current model as known by the installed CLI (on 2026-09-27, Claude Code 2.1.280's
/// <c>opus</c> alias resolved to <c>claude-opus-5-5</c>; an older CLI can still resolve Opus 5.
/// Likewise Claude Code 2.1.284+ resolves <c>sonnet</c> to <c>claude-sonnet-5-5</c>, measured
/// 2026-09-29; 2.1.280-2.1.283 still resolve it to <c>claude-sonnet-5</c>).
/// <b>Codex cannot</b>: measured 2026-08-20 against codex-cli 0.147.0, <c>-m luna</c> is rejected
/// twice over — "Model metadata for `luna` not found" locally, then HTTP 400 "the 'luna' model is
/// not supported" from the service. There are no unversioned aliases in Codex's catalog (bare
/// <c>astra</c> 400s the same way), so the Codex ladder pins full slugs
/// (<c>gpt-6-astra</c> / <c>gpt-6.1-sol</c> / <c>gpt-6.1-sol</c> / <c>gpt-5.6-luna</c>) and needs
/// deliberate catalog bumps. <c>gpt-6-astra</c> requires codex-cli 0.153.4+; <c>gpt-6.1-sol</c>
/// has recorded floor 0.159.1. CARD-0959 reports runner CLI observations and retains floor
/// metadata as data only; it does not enforce compatibility or require a fleet upgrade.</para>
/// </summary>
public static class ModelLevelAliases
{
    public sealed record CodexLadderEntry(string ModelId, CodexCliVersion? MinimumCliVersion);
    private static readonly CodexLadderEntry CodexFrontier = new(ModelAlias.Gpt6Astra, null);
    private static readonly CodexLadderEntry CodexSol = new(ModelAlias.Gpt61Sol, CodexCliVersion.Parse("0.159.1")!);
    private static readonly CodexLadderEntry CodexLow = new(ModelAlias.Gpt56Luna, null);

    public static CodexLadderEntry ForCodexEntry(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => CodexFrontier,
        AgentModelLevel.Low => CodexLow,
        _ => CodexSol,
    };

    /// <summary>The floor belongs to the actual explicit model, including exact profile pins.</summary>
    public static CodexCliVersion? MinimumCodexCliVersion(AgentKind kind, string? actualModel) =>
        kind == AgentKind.Codex && string.Equals(actualModel?.Trim(), CodexSol.ModelId, StringComparison.OrdinalIgnoreCase)
            ? CodexSol.MinimumCliVersion : null;
    public static string ForClaude(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "fable",
        AgentModelLevel.High => "opus",
        AgentModelLevel.Medium => "sonnet",
        AgentModelLevel.Low => "haiku",
        _ => "opus",
    };

    // CARD-0169 collapsed every level onto one Grok id; 2026-09-21 operator instruction
    // bumped that pin from grok-4.6 to grok-4.7. grok-4.6 and grok-4.5 stay valid,
    // selectable model ids elsewhere (the AgentTuiRunnerCatalog listing, historical
    // records) — this only removes them from the level ladder new dispatches resolve through.
    public static string ForGrok(AgentModelLevel level) => "grok-4.7";

    /// <summary>
    /// Codex's ladder (CARD-0099 S3, CARD-0396, CARD-0611, CARD-0903). Frontier remains
    /// <c>gpt-6-astra</c> and Low remains <c>gpt-5.6-luna</c>. The operator pinned both High
    /// and Medium to <c>gpt-6.1-sol</c> on 2026-10-02. They use the same model but retain distinct
    /// reasoning efforts (high and medium). The old <c>gpt-6-sol</c> and <c>gpt-5.6-terra</c>
    /// slugs remain selectable profile ids and recognizable historical aliases.
    ///
    /// <para>Low → Medium and High → Frontier are real model changes. Medium → High and High → Medium
    /// use <c>AgentTaskService.SameModelEscalationNote</c>'s same-alias fresh-context wording;
    /// the former increases reasoning effort. The note compares aliases for every kind.</para>
    ///
    /// <para><c>gpt-6.1-sol</c> is bundled from codex-cli 0.159.1 onward and supports both
    /// medium and high effort in 0.160.0. Do not pass bare <c>astra</c> or <c>sol</c> to Codex.</para>
    /// </summary>
    public static string ForCodex(AgentModelLevel level) => ForCodexEntry(level).ModelId;

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
