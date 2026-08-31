using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The Grok-shaped half of a launch command line (CARD-0289). Grok has no <c>-c</c> config-override
/// channel the way Codex does; reasoning effort is a first-class CLI flag on the same invocation
/// Antiphon launches with <c>--always-approve --no-alt-screen</c>. The two sites that launch a
/// Grok process — a delegate task (<c>AgentTaskDispatcher.ComposeDelegateArgs</c>) and a named
/// agent (<c>AgentSessionLaunchComposer.ComposeForAgentAsync</c>) — share this rather than each
/// spelling the flag out.
///
/// <para>Everything here was measured against grok CLI 1.0.13 (<c>~/.grok/bin/grok.exe</c>) on
/// 2026-08-31. <c>grok --help</c> lists a top-level <c>--reasoning-effort &lt;EFFORT&gt;</c>
/// ("Reasoning effort for reasoning models", alias <c>--effort</c>). The model catalog
/// (<c>~/.grok/models_cache.json</c>, fetched the same day from
/// <c>https://cli-chat-proxy.grok.com/v1/models</c>): grok-4.6 has
/// <c>supports_reasoning_effort: true</c> with efforts <c>xhigh / high / medium / low</c>, default
/// <c>high</c>. grok-4.5 offers only high / medium / low — no xhigh — default high. The operator's
/// <c>~/.grok/config.toml</c> sets <c>[models] default_reasoning_effort = "high"</c>, so a Low-tier
/// delegate left alone inherits high and a Frontier delegate is capped at high while xhigh exists.
/// Explicit beats both. Values are NOT validated at parse time: <c>grok --reasoning-effort bogus
/// doctor</c> runs cleanly, so this mapping emits catalog values only.</para>
/// </summary>
public static class GrokLaunchArgs
{
    /// <summary>
    /// Canonical flag name. The CLI also accepts <c>--effort</c> as an alias; Antiphon uses the
    /// long form so a reader of the argv can tell Grok's flag from Claude's <c>--effort</c>.
    /// </summary>
    public const string ReasoningEffortFlag = "--reasoning-effort";

    /// <summary>
    /// Reasoning effort, set EXPLICITLY on every launch (CARD-0289). Grok's per-model default and
    /// the operator's config.toml are both <c>high</c>, which is wrong at both ends of the ladder:
    /// a Low-tier delegate would think at High, and a Frontier delegate would never reach xhigh.
    /// Neither default tracks the tier the caller asked for, so the tier sets it — and the launch
    /// stops depending on a config file nothing in this repo owns.
    ///
    /// <para>Every value is in grok-4.6's catalog, and <see cref="ModelLevelAliases.ForGrok"/> pins
    /// grok-4.6 at every rung (CARD-0169), so the ladder cannot emit an out-of-catalog value.
    /// grok-4.5 + Frontier → xhigh is only reachable via an explicit profile/agent ModelId of
    /// grok-4.5 under a Frontier-level dispatch. Live probe 2026-08-31:
    /// <c>grok -m grok-4.5 --reasoning-effort xhigh -p "say ok"</c> exited 1 with
    /// <c>--effort/--reasoning-effort: unknown effort level 'xhigh'; use one of: high, medium,
    /// low</c> — a hard launch refusal, not Claude-style degradation. Compose sites therefore
    /// call <see cref="ReasoningEffortForModel"/> so that pairing clamps to <c>high</c>.</para>
    /// </summary>
    public static string ReasoningEffort(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "xhigh",
        AgentModelLevel.High => "high",
        AgentModelLevel.Medium => "medium",
        AgentModelLevel.Low => "low",
        _ => "high",
    };

    /// <summary>
    /// <see cref="ReasoningEffort"/> with the grok-4.5 clamp: that model's catalog has no
    /// <c>xhigh</c>, and the CLI refuses to launch rather than degrading. Other grok-4.5 efforts
    /// pass through; any other model id (including null, which the ladder pins to grok-4.6)
    /// keeps the table value.
    /// </summary>
    public static string ReasoningEffortForModel(AgentModelLevel level, string? effectiveModelId)
    {
        var effort = ReasoningEffort(level);
        if (string.Equals(effort, "xhigh", StringComparison.Ordinal)
            && IsGrok45(effectiveModelId))
            return "high";
        return effort;
    }

    private static bool IsGrok45(string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId)
        && modelId.Trim().Equals("grok-4.5", StringComparison.OrdinalIgnoreCase);
}
