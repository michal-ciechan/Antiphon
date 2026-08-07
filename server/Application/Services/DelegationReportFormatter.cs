using System.Text;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Composes the two pieces of text that make delegation work: the CONTRACT handed to a delegate
/// (what to do, and how to report), and the NOTE the parent receives when it finishes.
///
/// Pure and static — every rule here is a behaviour worth pinning, and none of it needs a database.
/// </summary>
public static class DelegationReportFormatter
{
    /// <summary>Marker the reply sink matches on. Prompt-TEXT matching would misroute a human's turn.</summary>
    public static string TaskMarker(Guid taskId) => $"[antiphon-task:{Short(taskId)}]";

    public static string Short(Guid id) => id.ToString("N")[..8];

    /// <summary>
    /// The full brief: marker, metadata, the caller's goal verbatim, then the reporting contract.
    /// Composed SERVER-SIDE so a calling agent cannot forget it and every delegate gets the same one.
    /// </summary>
    public static string BuildBrief(AgentTask task, DelegationSettings settings)
    {
        var sb = new StringBuilder();
        sb.Append(TaskMarker(task.Id))
          .Append(" role=").Append(task.Role)
          .Append(" tier=").Append(task.ModelLevel)
          .Append(" workspace=").Append(task.Workspace);
        if (!string.IsNullOrWhiteSpace(task.ScopeGlob))
            sb.Append(" scope=").Append(task.ScopeGlob);
        sb.AppendLine().AppendLine();

        sb.AppendLine(task.Goal.Trim()).AppendLine();

        if (task.Workspace == WorkspaceMode.ReadOnly)
            sb.AppendLine("Do NOT modify any files. This is a read-only task — report findings only.").AppendLine();

        sb.Append(ReportingContract(task.Id, task.Kind, settings.ReplyInlineMaxChars));
        return sb.ToString();
    }

    /// <summary>
    /// How to report back. The delegate's final message IS the report, so this is the highest-leverage
    /// text in the system — and the spill rule is the primary size mechanism, because the delegate is
    /// the only party that knows which 20 000 characters mattered.
    /// </summary>
    public static string ReportingContract(Guid taskId, AgentTaskKind kind, int inlineMaxChars)
    {
        var rollup = kind == AgentTaskKind.Orchestrator
            ? """

              You ran your own delegates. Your report covers your whole subtree: what was
              accomplished, what each delegate concluded that the caller still needs, and what is
              unresolved. Do not paste your delegates' reports — you read them so the caller
              doesn't have to.
              """
            : string.Empty;

        return $"""
            --- how to report back ---
            Your final message is the entire report the caller receives. Nothing else from this
            session is forwarded, and the caller cannot see your screen.

            Lead with the outcome in one line: what you did or found, and whether it worked.
            Then only what the caller needs in order to act — files changed, commands to rerun,
            decisions that are theirs to make, what is blocking you.

            No preamble, no restating the task, no narrating the steps you took, no sign-off.
            If you ran tests or builds, give counts and the failures, not the passing output.
            If you could not finish, say that in the first line and say exactly what stopped you.
            {rollup}
            If your report would run past {inlineMaxChars:N0} characters, write the full detail to
            .antiphon/task-{Short(taskId)}.md and make your final message a summary that points
            at that path.
            """;
    }

    /// <summary>The system prompt appended to every sub-orchestrator launch.</summary>
    public const string OrchestratorContract = """
        You are an orchestrator. You do not do the work — you decompose it, delegate every piece,
        and integrate what comes back.

        Do yourself only: read enough to decompose (list files, read a spec, check git status);
        decide the plan and the roles; integrate delegate reports; talk to the caller.

        Delegate everything else — every code edit, every test run, every git operation, every
        investigation deeper than a single file read. If you are about to Edit, Write, or run a
        build, stop: that is a delegation.

        Reports arrive between your turns as `[task <id> done] ...`. Do not poll and do not wait —
        end your turn; the report will reach you. When a delegate asks a question, answer it with
        -Reply. Taking the work back is the failure mode this exists to prevent.

        If a piece is big enough to need its own decomposition, send a sub-orchestrator
        (-Orchestrator) rather than trying to run its steps yourself.

        Delegates run directly in the working directory by default. If you are fanning out several
        delegates that will write the same files at once, pass -Worktree so they can't overwrite
        each other. Work in another repo goes to a delegate with -Dir pointing there.
        """;

    public sealed record Note(string Body, bool Excerpted);

    /// <summary>
    /// The completion note delivered to the parent. A header line carrying who/tier/duration/cost,
    /// then the delegate's report — WHOLE when it fits, because the report is the deliverable and
    /// clipping it just forces a second call to read what was already paid for.
    /// </summary>
    public static Note BuildCompletionNote(AgentTask task, DelegationSettings settings, string report)
    {
        var header = new StringBuilder();
        header.Append('[').Append("task ").Append(Short(task.Id)).Append(' ')
              .Append(StatusWord(task.Status)).Append(']');

        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.Title)) bits.Add(task.Title.Trim());
        bits.Add(ModelLevelAliases.ForClaude(task.ModelLevel));
        if (task.DispatchedAt is { } started && task.CompletedAt is { } finished)
            bits.Add(FormatDuration(finished - started));
        if (task.CostUsd > 0) bits.Add($"${task.CostUsd:0.000}");
        if (bits.Count > 0) header.Append(' ').Append(string.Join(" · ", bits));

        var (body, excerpted) = FitReport(report ?? string.Empty, task, settings);
        return new Note($"{header}\n\n{body}".ReplaceLineEndings("\n"), excerpted);
    }

    /// <summary>
    /// Head + tail excerpt when a report exceeds the ceiling — never a plain truncation. A hard cut
    /// at the limit severs the conclusion, and the conclusion is the part the caller needed.
    /// </summary>
    public static (string Body, bool Excerpted) FitReport(string report, AgentTask task, DelegationSettings settings)
    {
        var trimmed = report.Trim();
        if (trimmed.Length <= settings.ReplyInlineMaxChars)
            return (trimmed, false);

        var head = Math.Max(0, settings.ReplyExcerptHeadChars);
        var tail = Math.Max(0, settings.ReplyExcerptTailChars);
        // Degenerate config (head+tail >= the original) must not produce something LONGER than the
        // input by adding the elision banner — fall back to a head-only cut.
        if (head + tail >= trimmed.Length)
            return (trimmed[..Math.Min(trimmed.Length, settings.ReplyInlineMaxChars)], true);

        var omitted = trimmed.Length - head - tail;
        var pointer = string.IsNullOrWhiteSpace(task.ResultFilePath)
            ? $"full report: /api/agent-tasks/{task.Id}"
            : $"full report: {task.ResultFilePath}";

        return ($"""
            {trimmed[..head]}

            [... {omitted:N0} characters omitted — {pointer} ...]

            {trimmed[^tail..]}
            """, true);
    }

    private static string StatusWord(AgentTaskStatus status) => status switch
    {
        AgentTaskStatus.Succeeded => "done",
        AgentTaskStatus.Failed => "failed",
        AgentTaskStatus.Blocked => "blocked",
        AgentTaskStatus.Canceled => "canceled",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string FormatDuration(TimeSpan span) =>
        span.TotalMinutes < 1 ? $"{(int)span.TotalSeconds}s"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m{span.Seconds:00}"
        : $"{(int)span.TotalHours}h{span.Minutes:00}m";
}
