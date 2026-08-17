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
    /// <param name="replyInlineMaxChars">
    /// The report ceiling QUOTED TO THE DELEGATE, which must be the one its report will actually be
    /// measured against — that depends on the pseudoconsole serving the pty (CARD-0037), so the
    /// caller passes the resolved value. Null keeps <see cref="DelegationSettings.ReplyInlineMaxChars"/>,
    /// the conservative inbox-conhost number.
    /// </param>
    public static string BuildBrief(
        AgentTask task, DelegationSettings settings, int? replyInlineMaxChars = null)
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

        if (BuildHandoff(task) is { } handoff)
            sb.AppendLine(handoff).AppendLine();

        if (task.Workspace == WorkspaceMode.ReadOnly)
            sb.AppendLine("Do NOT modify any files. This is a read-only task — report findings only.").AppendLine();

        sb.Append(ReportingContract(
            task.Id, task.Kind, replyInlineMaxChars ?? settings.ReplyInlineMaxChars));
        return sb.ToString();
    }

    /// <summary>
    /// What the previous attempt found, carried into this one. Without it an escalation just pays a
    /// higher tier to rediscover the same dead end — which is the whole failure mode escalation is
    /// supposed to fix. Null on a first attempt, or when the last one left nothing to hand over.
    /// </summary>
    internal static string? BuildHandoff(AgentTask task)
    {
        if (task.Attempt <= 1)
            return null;

        var carried = !string.IsNullOrWhiteSpace(task.Result) ? task.Result!.Trim()
            : !string.IsNullOrWhiteSpace(task.FailureReason) ? task.FailureReason!.Trim()
            : null;
        if (carried is null)
            return null;

        // Enough to orient the next attempt; the full text stays on the task row either way.
        const int max = 4_000;
        if (carried.Length > max)
            carried = carried[..max] + $"\n[... clipped — full text: /api/agent-tasks/{task.Id} ...]";

        var tier = task.EscalatedFrom is { } from
            ? $"at {ModelLevelAliases.ForClaude(from)}, escalated to {ModelLevelAliases.ForClaude(task.ModelLevel)}"
            : $"at {ModelLevelAliases.ForClaude(task.ModelLevel)}";

        return $"""
            --- previous attempt ---
            Attempt {task.Attempt - 1} ran {tier} and did not settle this. Do not start cold — this is
            what it reported:

            {carried}
            """;
    }

    /// <summary>
    /// How to report back. The delegate's final message IS the report, so this is the highest-leverage
    /// text in the system — and the spill rule is the primary size mechanism, because the delegate is
    /// the only party that knows which 20 000 characters mattered.
    ///
    /// It CLOSES with the task marker, which is also the brief's opening token. That repetition is
    /// load-bearing, not tidiness: correlation reads the marker out of the delivered prompt, so a
    /// single copy at the head makes settlement depend on the head surviving the pty — and it does
    /// not always. Aligning what was queued against what four delegates actually recorded
    /// (2026-08-11) put the cut at byte 1024n-2 with only the FINAL chunk surviving: a 1 420-char
    /// brief arrived as its last 380 characters, marker gone. Those tasks then ran to completion and
    /// sat Dispatched forever, because every turn-end failed the marker gate. The tail survived in
    /// all seven deliveries measured across both mangling shapes, so a marker at each end
    /// correlates whichever fragment lands.
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

            {TaskMarker(taskId)}
            """;
    }

    /// <summary>
    /// The system prompt appended to every sub-orchestrator launch.
    ///
    /// <para>A FORWARD to bundle <c>orchestrator</c> since CARD-0058: the text moved verbatim to
    /// <c>server/Bundles/orchestrator.md</c>, where editing it is a PR that reaches every future
    /// launch, and the launch path composes it through <c>InstructionBundleComposer</c> alongside
    /// <c>delegate-basics</c>. The name stays because it is what this text IS to the rest of the
    /// codebase, and because a test that reads it can pin that the moved bundle still carries it.</para>
    /// </summary>
    public static string OrchestratorContract =>
        InstructionBundles.TextOf(InstructionBundles.Orchestrator);

    public sealed record Note(string Body, bool Excerpted);

    /// <summary>
    /// The completion note delivered to the parent. A header line carrying who/tier/duration/cost
    /// (and what happened to the branch, for a Worktree task), then the delegate's report — WHOLE
    /// when it fits, because the report is the deliverable and clipping it just forces a second
    /// call to read what was already paid for.
    /// </summary>
    /// <param name="replyInlineMaxChars">
    /// The ceiling the report is measured against. Depends on the pseudoconsole that will carry the
    /// note (CARD-0037); null keeps the conservative inbox-conhost number.
    /// </param>
    /// <param name="warning">
    /// A caveat about the report itself, placed between the header and the report so the caller
    /// reads it FIRST — currently "this may be preamble, not the verdict" when the turn-ending
    /// response never wrote its own text (CARD-0046 slice 3). Deliberately outside
    /// <see cref="FitReport"/>: the ceiling and the excerpt arithmetic are about the report, and a
    /// warning that could itself be excerpted away would be worthless.
    /// </param>
    public static Note BuildCompletionNote(
        AgentTask task, DelegationSettings settings, string report, string? workspaceNote = null,
        int? replyInlineMaxChars = null, string? warning = null)
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
        if (!string.IsNullOrWhiteSpace(workspaceNote)) bits.Add(workspaceNote.Trim());
        if (bits.Count > 0) header.Append(' ').Append(string.Join(" · ", bits));

        var (body, excerpted) = FitReport(report ?? string.Empty, task, settings, replyInlineMaxChars);
        if (!string.IsNullOrWhiteSpace(warning))
            body = $"{warning.Trim()}\n\n{body}";
        return new Note($"{header}\n\n{body}".ReplaceLineEndings("\n"), excerpted);
    }

    /// <summary>
    /// Head + tail excerpt when a report exceeds the ceiling — never a plain truncation. A hard cut
    /// at the limit severs the conclusion, and the conclusion is the part the caller needed.
    ///
    /// Both cuts snap to a whitespace boundary. A mid-word seam is what made the 2026-08-10 live
    /// miss so hard to see: "...cherry-pick co" welded onto "== a667cbcc, worktree list..." reads
    /// as prose damage, not as a boundary, so nobody can tell an excerpt from a corruption.
    /// </summary>
    public static (string Body, bool Excerpted) FitReport(
        string report, AgentTask task, DelegationSettings settings, int? replyInlineMaxChars = null)
    {
        var ceiling = replyInlineMaxChars ?? settings.ReplyInlineMaxChars;
        var trimmed = report.Trim();
        if (trimmed.Length <= ceiling)
            return (trimmed, false);

        var head = Math.Max(0, settings.ReplyExcerptHeadChars);
        var tail = Math.Max(0, settings.ReplyExcerptTailChars);
        // Degenerate config (head+tail >= the original) must not produce something LONGER than the
        // input by adding the elision banner — fall back to a head-only cut.
        if (head + tail >= trimmed.Length)
            return (trimmed[..Math.Min(trimmed.Length, ceiling)], true);

        var headEnd = SnapBack(trimmed, head);
        var tailStart = SnapForward(trimmed, trimmed.Length - tail);
        var omitted = tailStart - headEnd;
        var pointer = string.IsNullOrWhiteSpace(task.ResultFilePath)
            ? $"read it with: GET /api/agent-tasks/{task.Id}"
            : $"read it at: {task.ResultFilePath}";

        return ($"""
            {trimmed[..headEnd].TrimEnd()}

            [... THIS REPORT IS AN EXCERPT: {omitted:N0} of {trimmed.Length:N0} characters are
            missing from the middle. Do not treat what you see as the whole report — {pointer} ...]

            {trimmed[tailStart..].TrimStart()}
            """, true);
    }

    /// <summary>Nearest whitespace at or before <paramref name="index"/>, so a cut never lands mid-word.</summary>
    private static int SnapBack(string text, int index)
    {
        // Bounded search: on text with no whitespace for a long stretch (a base64 blob, a minified
        // line) there is no word boundary to find and the exact cut is the honest answer.
        var floor = Math.Max(0, index - WordSnapWindow);
        for (var i = Math.Min(index, text.Length); i > floor; i--)
        {
            if (char.IsWhiteSpace(text[i - 1]))
                return i;
        }
        return index;
    }

    /// <summary>Nearest whitespace at or after <paramref name="index"/>, so a resume never lands mid-word.</summary>
    private static int SnapForward(string text, int index)
    {
        var ceiling = Math.Min(text.Length, index + WordSnapWindow);
        for (var i = Math.Max(0, index); i < ceiling; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i + 1;
        }
        return index;
    }

    private const int WordSnapWindow = 200;

    /// <summary>
    /// A brief too long to type into a terminal intact. The full text always survives on the task
    /// row, so the delegate is pointed at it rather than handed a body the pty may splice.
    /// <paramref name="spillPath"/> is the file the caller managed to write, or null to fall back
    /// to the API (which needs no filesystem and is therefore always available).
    /// </summary>
    public static string BuildBriefPointer(
        AgentTask task, DelegationSettings settings, string? spillPath, int fullLength)
    {
        var where = string.IsNullOrWhiteSpace(spillPath)
            ? $"GET {settings.ApiBaseUrl.TrimEnd('/')}/api/agent-tasks/{task.Id} and read the \"goal\" field"
            : spillPath;

        var sb = new StringBuilder();
        sb.Append(TaskMarker(task.Id))
          .Append(" role=").Append(task.Role)
          .Append(" tier=").Append(task.ModelLevel)
          .Append(" workspace=").Append(task.Workspace);
        if (!string.IsNullOrWhiteSpace(task.ScopeGlob))
            sb.Append(" scope=").Append(task.ScopeGlob);
        sb.AppendLine().AppendLine();

        sb.AppendLine($"""
            {task.Title.Trim()}

            YOUR BRIEF IS NOT IN THIS MESSAGE. It is {fullLength:N0} characters — too long to type
            into a terminal without the transport dropping part of it, so it was written out
            instead. Read it in full before you do anything else:

                {where}

            Everything you need is there. Do not start from this summary.
            """).AppendLine();

        if (task.Workspace == WorkspaceMode.ReadOnly)
            sb.AppendLine("Do NOT modify any files. This is a read-only task — report findings only.").AppendLine();

        // Deliberately NOT the full ReportingContract: it is the largest part of this message, and
        // a pointer that grows past one 1024-byte transport chunk can lose its own head — the exact
        // failure it exists to prevent. The complete contract is in the spilled brief the delegate
        // is told to read first, so repeating it here buys nothing and risks the whole message.
        // The closing marker stays, because correlation must survive even if the head is lost.
        sb.AppendLine("""
            --- how to report back ---
            The full reporting contract is in the brief above — read it there.
            Your final message is the entire report the caller receives.
            """).AppendLine();

        sb.Append(TaskMarker(task.Id));
        return sb.ToString();
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
