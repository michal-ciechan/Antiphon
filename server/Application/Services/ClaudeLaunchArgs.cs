using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The Claude-shaped half of a launch command line (CARD-0289). claude 2.1.251 exposes
/// <c>--effort &lt;level&gt;</c> as a first-class CLI launch flag, so effort is an ARGUMENT the
/// same way Codex's <c>-c model_reasoning_effort=</c> and Grok's <c>--reasoning-effort</c> are —
/// no boot-time slash-command typing, no picker to answer, no CARD-0055/0056-class
/// typed-into-a-live-session risk. The two sites that launch a Claude process — a delegate task
/// (<c>AgentTaskDispatcher.ComposeDelegateArgs</c>) and a named agent
/// (<c>AgentSessionLaunchComposer.ComposeForAgentAsync</c>) — share this rather than each spelling
/// the flag out.
///
/// <para>Everything here was measured against claude 2.1.251 on 2026-08-31. <c>claude --help</c>
/// lists <c>--effort &lt;level&gt;</c> ("Effort level for the current session (low, medium, high,
/// xhigh, max)"). Live probe: <c>claude -p "…" --model haiku --effort low</c> answered normally,
/// so the flag is accepted on the lowest ladder rung and can be passed unconditionally for all
/// four tier models (fable/opus/sonnet/haiku). Degradation is graceful: <c>--effort bogus</c>
/// prints a warning naming the valid values and the session runs at the default — a bad value
/// cannot wedge a Claude launch. <c>max</c> is deliberately unused, the same way Codex's
/// <c>max</c>/<c>ultra</c> are — headroom for a manual session, not a tier. Frontier maps to
/// <c>xhigh</c> (the Codex precedent for "Frontier depth"), not <c>max</c>: the Claude ladder
/// already scales the MODEL per tier, and mapping Frontier to <c>max</c> is a cost/latency
/// decision the card did not ask for.</para>
/// </summary>
public static class ClaudeLaunchArgs
{
    /// <summary>Claude's session-effort flag. Space-separated: <c>--effort</c> then the value.</summary>
    public const string EffortFlag = "--effort";

    /// <summary>
    /// Effort, set EXPLICITLY on every launch (CARD-0289). The same four-value table Codex and
    /// Grok use, so a given tier means the same depth on every provider. The Claude CLI accepts
    /// these four plus <c>max</c>; we never emit <c>max</c>.
    /// </summary>
    public static string Effort(AgentModelLevel level) => level switch
    {
        AgentModelLevel.Frontier => "xhigh",
        AgentModelLevel.High => "high",
        AgentModelLevel.Medium => "medium",
        AgentModelLevel.Low => "low",
        _ => "high",
    };
}
