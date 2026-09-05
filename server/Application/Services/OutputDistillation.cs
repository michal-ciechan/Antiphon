using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// What the output distiller IS, as code (CARD-0330 S2).
///
/// <para>The specialist's standing instructions live in <see cref="Agent.SystemPromptAppend"/>,
/// which <c>AgentControlService</c> renders into <c>--append-system-prompt</c> on EVERY launch —
/// fresh and resume alike — so the contract survives compaction and re-arms on every restart. That
/// makes the agent row a PROJECTION of the constant below, not the source of truth:
/// <c>OutputDistillerProvisioner.EnsureAsync</c> reconciles the row against it on every call, so
/// editing the bundle in a PR updates the live agent, and a hand-edit in the UI is overwritten.</para>
///
/// <para>The instructions are not the enforcement. The specialist needs zero tools, so it gets zero:
/// the provisioner also writes a deny-all <c>PreToolUse</c> hook into its scratch working directory
/// (<see cref="DenyAllToolsSettingsJson"/>). Prose asks; the hook refuses. The gates in
/// <c>OutputDistillationGate</c> (S3) decide whether a distillation is allowed to replace the
/// note; this file does not enqueue, write, or spend.</para>
/// </summary>
public static class OutputDistillation
{
    /// <summary>
    /// Bumped whenever the contract changes meaningfully. It rides IN the contract text so an
    /// operator reading the agent row can see which version that agent is running without diffing
    /// prose. Held together with the literal <c>contract v1</c> in
    /// <c>server/Bundles/output-distiller.md</c> by <c>OutputDistillerProvisionerTests</c>
    /// and <c>InstructionBundleTests</c>.
    /// </summary>
    public const string ContractVersion = "1";

    /// <summary>
    /// The standing contract. A FORWARD to bundle <c>output-distiller</c>: the text lives in
    /// <c>server/Bundles/output-distiller.md</c> and is composed like any other bundle, while every
    /// call site here keeps reading one constant. The forward is the bundle's TEXT and not its
    /// rendered form — no <c>[bundle:…]</c> header — so the reconciled agent row is the contract
    /// alone.
    /// </summary>
    public static string Contract => InstructionBundles.TextOf(InstructionBundles.OutputDistiller);

    /// <summary>The one-line format reminder that rides every brief, so the shape survives compaction.</summary>
    public const string OutputFormatReminder =
        "Keep the signal in at most 12 bullets, one fact each. Copy every identifier. "
        + "If the report has a `--- next stage ---` block, copy `next:` and `handoff:` verbatim. "
        + "Close with the Distill task's `done` token after the last bullet; `failed` if nothing "
        + "usable; never `blocked`.";

    /// <summary>
    /// Stderr the deny-all hook feeds back when a tool is refused. The JSON wrapper lives on
    /// <see cref="SpecialistSpec"/> so Check / Distill / Diagnose seats share the same shape.
    /// </summary>
    public const string DenyHookStderr =
        "This session is the Antiphon output distiller: it reads a finished report and answers in bullets. It has no tools. Answer from the report alone.";

    /// <summary>
    /// A deny-all <c>PreToolUse</c> hook — the hard half of "use no tools". Same mechanism as
    /// the check interpreter: matcher <c>*</c>, exit 2 so Claude Code feeds the stderr line back.
    /// </summary>
    public static string DenyAllToolsSettingsJson =>
        SpecialistSpec.BuildDenyAllToolsSettingsJson(DenyHookStderr);

    /// <summary>Where the hook file goes, relative to the specialist's working directory.</summary>
    public const string DenyHookRelativePath = SpecialistSpec.DenyHookRelativePath;

    /// <summary>The distillation task's title — names the source task so the link survives on the board.</summary>
    public static string BuildTitle(AgentTask source) =>
        $"distill task {DelegationReportFormatter.Short(source.Id)}";

    /// <summary>
    /// The per-request brief: one line naming the source, the scrubbed report, and the format
    /// reminder. Task and report markers are scrubbed HERE because a delegate's report opens and
    /// closes with its own tokens, and a live-looking marker riding into the specialist's session
    /// would correlate its turn to somebody else's task.
    /// </summary>
    public static string BuildGoal(AgentTask source, string report)
    {
        var ended = source.Status.ToString();
        return $"""
            Distill task {DelegationReportFormatter.Short(source.Id)} ({source.Role} {source.AgentKind}/{source.ModelLevel} {ended}).

            {Scrub(report)}

            {OutputFormatReminder}
            """.ReplaceLineEndings("\n");
    }

    internal static string Scrub(string? body)
    {
        var text = AgentTaskCheckService.ScrubTaskMarkers(body ?? "");
        return ReportMarkerPattern.Replace(text, "[report-marker removed]");
    }

    private static readonly Regex ReportMarkerPattern = new(
        @"\[antiphon-report:[^\]]+\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
