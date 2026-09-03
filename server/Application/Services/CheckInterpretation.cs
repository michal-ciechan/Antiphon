using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// What the check interpreter IS, as code (CARD-0047 slice 4 amendment §1.4).
///
/// <para>The specialist's standing instructions live in <see cref="Agent.SystemPromptAppend"/>,
/// which <c>AgentControlService</c> renders into <c>--append-system-prompt</c> on EVERY launch —
/// fresh and resume alike — so the contract survives compaction and re-arms on every restart. That
/// makes the agent row a PROJECTION of the constant below, not the source of truth:
/// <c>CheckInterpreterProvisioner.EnsureAsync</c> reconciles the row against it on every call, so
/// editing this file in a PR updates the live agent, and a hand-edit in the UI is overwritten.</para>
///
/// <para>The instructions are not the enforcement. The specialist needs zero tools, so it gets zero:
/// the provisioner also writes a deny-all <c>PreToolUse</c> hook into its scratch working directory
/// (<see cref="DenyAllToolsSettingsJson"/>). Prose asks; the hook refuses.</para>
/// </summary>
public static class CheckInterpretation
{
    /// <summary>
    /// Bumped whenever the contract changes meaningfully. It rides IN the contract text so an
    /// operator reading the agent row can see which version that agent is running without diffing
    /// prose, and so a stale row is obvious at a glance.
    /// </summary>
    /// <remarks>
    /// It is now a legacy label beside the bundle's content hash (CARD-0058), which needs no bumping
    /// at all. The number below and the literal <c>contract v4</c> in
    /// <c>server/Bundles/check-interpreter.md</c> are two copies of one fact, held together by
    /// <c>CheckInterpreterProvisionerTests</c>: bump this without editing the bundle and that test
    /// goes red.
    /// </remarks>
    public const string ContractVersion = "4";

    /// <summary>
    /// The standing contract. Triage, not diagnosis: the specialist reads a bundle of facts about
    /// SOMEONE ELSE'S running delegate and says which of five shapes it is. Everything it must not
    /// do is there because each one has a failure attached — claiming completion would let a caller
    /// move on from work still in flight, and investigating beyond the bundle would turn a cheap
    /// read into a second agent doing the delegate's job over its shoulder.
    ///
    /// <para>A FORWARD to bundle <c>check-interpreter</c> since CARD-0058: the text moved to
    /// <c>server/Bundles/check-interpreter.md</c> and is composed like any other bundle, while every
    /// call site here keeps reading one constant. The forward is deliberately the bundle's TEXT and
    /// not its rendered form — no <c>[bundle:…]</c> header — so the reconciled agent row is what it
    /// has always been. Line endings are LF (the bundle is normalised on load, the old constant took
    /// whatever the checkout had), so the first reconcile after this ships rewrites the row once.</para>
    /// </summary>
    public static string Contract => InstructionBundles.TextOf(InstructionBundles.CheckInterpreter);

    /// <summary>The one-line format reminder that rides every brief, so the shape survives compaction.</summary>
    public const string OutputFormatReminder =
        "Return exactly one physical line, at most 240 characters: start with On track, Needs "
        + "attention, Unclear, or Settled at capture, then one evidence-backed clause and an "
        + "optional short action cue. Do not repeat elapsed, expected, session status, working "
        + "state, or last activity. Never claim the checked task is complete. Close with the "
        + "Check task's `done` token after the reading; never `blocked`.";

    /// <summary>
    /// Stderr the deny-all hook feeds back when a tool is refused. The JSON wrapper lives on
    /// <see cref="SpecialistSpec"/> so Distill / Diagnose seats share the same shape.
    /// </summary>
    public const string DenyHookStderr =
        "This session is the Antiphon check interpreter: it reads a fact bundle and answers in prose. It has no tools. Answer from the bundle alone.";

    /// <summary>
    /// A deny-all <c>PreToolUse</c> hook — the hard half of "use no tools". Same mechanism as
    /// <c>DelegationWorktreeService.ArmDenyHookAsync</c>, matcher widened from the edit tools to
    /// everything, and written to <c>.claude/settings.json</c> in a scratch directory this
    /// provisioner owns outright rather than to a shared repo.
    ///
    /// <para>powershell.exe (5.1) on purpose, as in the worktree hook: always present on Windows and
    /// invocable identically from cmd or sh, whichever shell runs the hook. Exit code 2 is what
    /// makes Claude Code feed the stderr line back to the model instead of silently dropping it.</para>
    /// </summary>
    public static string DenyAllToolsSettingsJson =>
        SpecialistSpec.BuildDenyAllToolsSettingsJson(DenyHookStderr);

    /// <summary>Where the hook file goes, relative to the specialist's working directory.</summary>
    public const string DenyHookRelativePath = SpecialistSpec.DenyHookRelativePath;

    /// <summary>
    /// The per-check brief: the bundle, and the reminder of what to do with it. Nothing else —
    /// the standing contract is already in the system prompt, and repeating it per check would
    /// spend the bundle's budget on text the agent already has.
    ///
    /// <para>The digest is scrubbed of task markers HERE rather than at the call site, because
    /// forgetting is the whole hazard: the digest quotes the delegate's own brief, which opens with
    /// <c>[antiphon-task:xxxxxxxx]</c>, and a live-looking marker riding into the specialist's
    /// session would correlate its turn to somebody else's task. The only marker on this brief is
    /// the interpretation task's own, added by the dispatcher like any other.</para>
    /// </summary>
    public static string BuildGoal(AgentTask checkedTask, int checkNumber, string digest) =>
        $"""
        Check #{checkNumber} on task {DelegationReportFormatter.Short(checkedTask.Id)}.

        {AgentTaskCheckService.ScrubTaskMarkers(digest)}

        {OutputFormatReminder}
        """.ReplaceLineEndings("\n");

    /// <summary>The interpretation task's title — names the checked task so the link survives on the board.</summary>
    public static string BuildTitle(AgentTask checkedTask, int checkNumber) =>
        $"check #{checkNumber} on task {DelegationReportFormatter.Short(checkedTask.Id)}";
}
