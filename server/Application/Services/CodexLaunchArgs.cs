using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The Codex-shaped half of a launch command line (CARD-0099 S3). Codex has no <c>--name</c> and no
/// <c>--append-system-prompt</c>/<c>--rules</c>; everything that is not the model rides <c>-c</c>
/// TOML config overrides, so the two sites that launch a Codex process — a delegate task
/// (<c>AgentTaskDispatcher.BuildLaunchSpec</c>) and a named agent (<c>AgentControlService</c>) —
/// share this rather than each spelling the config keys out.
///
/// <para>Everything here was measured against codex-cli 0.147.0 on 2026-08-20 through
/// <c>codex debug prompt-input</c>, which renders the model-visible prompt list as JSON and costs no
/// model turns.</para>
/// </summary>
public static class CodexLaunchArgs
{
    /// <summary>Codex's config-override flag. The value is parsed as TOML and falls back to the raw string.</summary>
    public const string ConfigFlag = "-c";

    /// <summary>
    /// The standing-instructions channel. <b>Measured, not chosen from the docs.</b>
    /// <c>-c developer_instructions=&lt;text&gt;</c> lands as an ADDITIONAL <c>input_text</c> block at
    /// the head of the first developer message, with Codex's own base instructions and the
    /// skills block that follow it byte-identical — it appends, it does not replace. The neighbouring
    /// key <c>instructions</c> is INERT in this CLI version: setting it produced a prompt list
    /// byte-identical to the baseline apart from generated message ids, so a bundle sent that way
    /// would be silently dropped. (The plan's §9 recorded both as reaching the model, from a codeword
    /// probe through <c>codex exec</c>; the prompt dump is the stronger evidence and disagrees.)
    ///
    /// <para>The value is passed as ONE argv element with no quoting of our own. Codex parses it as
    /// TOML and falls back to the raw literal when that fails, which is what a multi-line markdown
    /// bundle always does — verified to survive embedded newlines, tabs, double quotes, backticks and
    /// Windows path backslashes unchanged.</para>
    /// </summary>
    public static string DeveloperInstructions(string text) => $"developer_instructions={text}";

    /// <summary>
    /// Reasoning effort, set EXPLICITLY on every launch (plan §4 S3). Codex's per-model defaults are
    /// wrong for a delegate at both ends: <c>gpt-6-astra</c>'s own default is <c>low</c> (same as
    /// Sol before it), so a Frontier delegate left alone would reason at the shallowest setting the
    /// frontier model has, while the operator's <c>~/.codex/config.toml</c> here says <c>xhigh</c>
    /// and would be inherited by a Low-tier delegate. Neither default tracks the tier the caller
    /// asked for, so the tier sets it — and the launch stops depending on a config file nothing in
    /// this repo owns.
    ///
    /// <para>Every value is in the catalog's <c>supported_reasoning_levels</c> for the slug
    /// <see cref="ModelLevelAliases.ForCodex"/> pairs it with (read 2026-09-05 on 0.153.4: astra, sol
    /// and terra support low/medium/high/xhigh/max/ultra, luna all but ultra). Frontier stays at
    /// <c>xhigh</c>; do not wire <c>ultra</c> without an explicit ask.</para>
    /// </summary>
    public static string ReasoningEffort(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "xhigh",
        AgentModelLevel.High => "high",
        AgentModelLevel.Medium => "medium",
        AgentModelLevel.Low => "low",
        _ => "high",
    };

    /// <summary>The <c>-c</c> value that sets <see cref="ReasoningEffort"/>.</summary>
    public static string ReasoningEffortOverride(AgentModelLevel level) =>
        $"model_reasoning_effort={ReasoningEffort(level)}";

    /// <summary>
    /// CARD-0133: Codex's PasteBurst logic suppresses Enter for 120ms after a typed burst and
    /// extends the window on every suppressed Enter - our queue's ~20ms body-then-Enter gap lands
    /// inside it, which is the mechanism behind the measured 9/78 cold-launch boot wedge. This
    /// config key makes every Enter submit unconditionally.
    /// </summary>
    public const string DisablePasteBurst = "disable_paste_burst=true";
}
