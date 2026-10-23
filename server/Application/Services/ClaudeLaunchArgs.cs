using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The Claude-shaped half of a launch command line (CARD-0289). claude 2.1.251 exposes
/// <c>--effort &lt;level&gt;</c> as a first-class CLI launch flag, so effort is an ARGUMENT the
/// same way Codex's <c>-c model_reasoning_effort=</c> and Grok's <c>--reasoning-effort</c> are —
/// no boot-time slash-command typing. Claude can still ask whether to change its default;
/// the startup effort gate preserves the requested launch effort (CARD-0449). The two sites that launch a Claude process — a delegate task
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

    // S1 red foundation: implemented after the launch/reuse witnesses execute.
    public static string? ReadLaunchEffort(IReadOnlyList<string> args) => null;

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

    /// <summary>Every value claude 2.1.251 accepts for <see cref="EffortFlag"/>, in ladder order.</summary>
    public static readonly IReadOnlyList<string> CliEfforts = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>The catalog, as the validator and the launch error spell it.</summary>
    public const string CatalogSentence = "low, medium, high, xhigh or max";

    /// <summary>
    /// The catalog constant for an operator-supplied effort (trimmed, case-insensitive), or null when
    /// the CLI would not accept it. The CONSTANT, never the operator's string, is what reaches argv.
    /// </summary>
    public static string? Canonical(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is null
            ? null
            : CliEfforts.FirstOrDefault(effort => string.Equals(effort, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The effort a delegate TASK launches at (task 8333e231): the role's entry in
    /// <see cref="Settings.DelegationSettings.ClaudeEffortByRole"/> when there is one, else the tier's
    /// (<see cref="Effort(AgentModelLevel)"/>). An entry the CLI would not accept throws naming the
    /// setting: the validator refuses it at startup, and this is the last line before argv.
    /// </summary>
    public static string Effort(AgentModelLevel level, AgentTaskRole role, IReadOnlyDictionary<string, string>? byRole)
    {
        if (!TryGetOverride(byRole, role, out var configured))
            return Effort(level);
        return Canonical(configured) ?? throw new InvalidOperationException(
            $"Delegation:ClaudeEffortByRole:{role} is '{configured}', which is not a Claude effort. Use {CatalogSentence}.");
    }

    /// <summary>
    /// True when the role's override would launch at a different effort than the tier's — the case a
    /// warm process, which cannot change its <c>--effort</c>, cannot serve. An entry the validator
    /// would refuse is not a reason to decline reuse.
    /// </summary>
    public static bool RoleOverrideDiffersFromTier(AgentModelLevel level, AgentTaskRole role, IReadOnlyDictionary<string, string>? byRole)
        => TryGetOverride(byRole, role, out var configured)
           && Canonical(configured) is { } effort
           && !string.Equals(effort, Effort(level), StringComparison.Ordinal);

    /// <summary>
    /// The role's entry, keyed by <see cref="AgentTaskRole"/> name. Configuration keys are
    /// case-insensitive and the setting's dictionary is built that way; the scan is for a map built
    /// by hand with the default comparer.
    /// </summary>
    private static bool TryGetOverride(IReadOnlyDictionary<string, string>? byRole, AgentTaskRole role, out string configured)
    {
        configured = string.Empty;
        if (byRole is null || byRole.Count == 0)
            return false;
        var name = role.ToString();
        if (byRole.TryGetValue(name, out var exact))
        {
            configured = exact;
            return true;
        }

        foreach (var (key, value) in byRole)
        {
            if (string.Equals(key?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                configured = value;
                return true;
            }
        }

        return false;
    }
}
