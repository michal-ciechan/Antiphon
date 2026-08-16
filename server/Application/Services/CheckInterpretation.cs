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
    /// Bumped whenever <see cref="Contract"/> changes meaningfully. It rides IN the contract text so
    /// an operator reading the agent row can see which version that agent is running without
    /// diffing prose, and so a stale row is obvious at a glance.
    /// </summary>
    /// <remarks>A string, not an int, so <see cref="Contract"/> can stay a compile-time constant.</remarks>
    public const string ContractVersion = "1";

    /// <summary>
    /// The standing contract. Triage, not diagnosis: the specialist reads a bundle of facts about
    /// SOMEONE ELSE'S running delegate and says which of five shapes it is. Everything it must not
    /// do is here because each one has a failure attached — claiming completion would let a caller
    /// move on from work still in flight, and investigating beyond the bundle would turn a cheap
    /// read into a second agent doing the delegate's job over its shoulder.
    /// </summary>
    public const string Contract = $"""
        You are the Antiphon CHECK INTERPRETER (contract v{ContractVersion}).

        Every message you receive is a fact bundle about a delegated task that is STILL RUNNING,
        gathered by a read-only probe. The task belongs to someone else. Your one job is to read the
        bundle and tell that task's caller, in 3-5 lines, which of these it looks like:

        - DOING — it is working: recent turns, tool calls, output that is going somewhere.
        - PRODUCED — it has made something concrete: commits, files written, tests run.
        - LOOKS STUCK — repetition, an error it keeps hitting, a long quiet stretch, a full queue.
        - SETTLED — the bundle already shows the work finished or the task closed.
        - AMBIGUOUS — the bundle does not support any of the above.

        Say WHICH and WHY, citing the facts you used. Lead with the verdict word.

        Hard rules:
        - NEVER say the task is complete, done, or successful. You are looking at work in flight;
          completion is decided elsewhere, by the delegate's own report.
        - NEVER investigate beyond the bundle. Do not read files, run commands, or search. If the
          bundle does not say, the answer is AMBIGUOUS and you say what is missing.
        - USE NO TOOLS. You have none, and a tool call is refused before it runs.
        - No preamble, no sign-off, no restating the bundle. 3-5 lines of prose, nothing else.
        """;

    /// <summary>The one-line format reminder that rides every brief, so the shape survives compaction.</summary>
    public const string OutputFormatReminder =
        "Answer in 3-5 lines: the verdict word (DOING / PRODUCED / LOOKS STUCK / SETTLED / "
        + "AMBIGUOUS) and why, from these facts only. Never claim completion.";

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
    public const string DenyAllToolsSettingsJson = """
        {
          "hooks": {
            "PreToolUse": [
              {
                "matcher": "*",
                "hooks": [
                  {
                    "type": "command",
                    "command": "powershell -NoProfile -Command \"[Console]::Error.WriteLine('This session is the Antiphon check interpreter: it reads a fact bundle and answers in prose. It has no tools. Answer from the bundle alone.'); exit 2\""
                  }
                ]
              }
            ]
          }
        }
        """;

    /// <summary>Where the hook file goes, relative to the specialist's working directory.</summary>
    public const string DenyHookRelativePath = ".claude/settings.json";

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
