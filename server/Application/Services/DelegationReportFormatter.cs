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
    /// Closing line of a finished report (CARD-0159). Distinct from <see cref="TaskMarker"/>: that
    /// one correlates the PROMPT and is scrubbed from bodies typed into other sessions; this one
    /// is read from <c>AssistantText</c> — the transcript, not the pty — so CARD-0027 clipping
    /// cannot eat it.
    /// </summary>
    public static string ReportToken(Guid taskId, string verdict) =>
        $"[antiphon-report:{Short(taskId)} {verdict.Trim().ToLowerInvariant()}]";

    public static string ReportToken(string shortId, string verdict) =>
        $"[antiphon-report:{shortId} {verdict.Trim().ToLowerInvariant()}]";

    /// <summary>The 8-char id inside a <c>[antiphon-task:…]</c> marker, or null when none is present.</summary>
    public static string? TryReadTaskMarkerId(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        const string prefix = "[antiphon-task:";
        var start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += prefix.Length;
        if (start + 8 > text.Length || text[start + 8] != ']')
            return null;
        return text.Substring(start, 8);
    }

    /// <summary>
    /// Reads the closing verdict line <c>[antiphon-report:&lt;id&gt; done|blocked|failed]</c> from
    /// the last non-empty line of <paramref name="text"/>. A token naming a DIFFERENT task id is
    /// not a verdict (a sub-orchestrator quoting its own delegate). On a match,
    /// <paramref name="body"/> is the text with that line stripped.
    /// </summary>
    public static bool TryReadReportVerdict(
        Guid taskId, string? text, out string verdict, out string body)
    {
        verdict = string.Empty;
        body = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.ReplaceLineEndings("\n");
        var lines = normalized.Split('\n');
        var last = -1;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Trim().Length > 0)
            {
                last = i;
                break;
            }
        }
        if (last < 0)
            return false;

        var line = lines[last].Trim();
        const string prefix = "[antiphon-report:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)
            || !line.EndsWith(']')
            || line.Length < prefix.Length + 8 + 3)
        {
            return false;
        }

        var inner = line[prefix.Length..^1];
        var space = inner.IndexOf(' ');
        if (space != 8)
            return false;

        var id = inner[..8];
        if (!id.Equals(Short(taskId), StringComparison.OrdinalIgnoreCase))
            return false;

        var word = inner[(space + 1)..].Trim();
        if (!word.Equals("done", StringComparison.OrdinalIgnoreCase)
            && !word.Equals("blocked", StringComparison.OrdinalIgnoreCase)
            && !word.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        verdict = word.ToLowerInvariant();
        var kept = new StringBuilder();
        for (var i = 0; i < last; i++)
        {
            if (kept.Length > 0)
                kept.Append('\n');
            kept.Append(lines[i]);
        }
        body = kept.ToString().TrimEnd();
        return true;
    }

    /// <summary>
    /// Folded into the marked brief when a reuse lands on a kind that does not implement a typed
    /// <c>/compact</c> as housekeeping (CARD-0117). Marked, correlated, costs no extra turn.
    /// </summary>
    internal const string UnrelatedWorkRefocusLine =
        "This session previously worked on UNRELATED work — ignore that context; everything you need is in this brief.";

    internal const string SharedWriteCommitLine =
        "When finished: git add the files you changed, commit with the real outcome in the message, and push, before your final report.";

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
    /// <param name="refocus">
    /// When true, a one-line "ignore the previous unrelated work" note is inserted right after the
    /// marker header (CARD-0117 D2). Used for pool reuse onto a kind whose <c>RefocusCompact</c>
    /// axis is not Supported — never a second unmarked prompt, never in
    /// <see cref="BuildBriefPointer"/> (the pointer stays one transport chunk; the spill file is
    /// built from this method, so the line rides along for free).
    /// </param>
    public static string BuildBrief(
        AgentTask task, DelegationSettings settings, int? replyInlineMaxChars = null, bool refocus = false)
    {
        var sb = new StringBuilder();
        sb.Append(TaskMarker(task.Id))
          .Append(" role=").Append(task.Role)
          .Append(" tier=").Append(task.ModelLevel)
          .Append(" workspace=").Append(task.Workspace);
        if (!string.IsNullOrWhiteSpace(task.Scope))
            sb.Append(" areas=").Append(task.Scope);
        sb.AppendLine().AppendLine();

        if (refocus)
            sb.AppendLine(UnrelatedWorkRefocusLine).AppendLine();

        sb.AppendLine(task.Goal.Trim()).AppendLine();

        if (BuildHandoff(task) is { } handoff)
            sb.AppendLine(handoff).AppendLine();

        if (task.Workspace == WorkspaceMode.ReadOnly)
            sb.AppendLine("Do NOT modify any files. This is a read-only task — report findings only.").AppendLine();

        if (task.Workspace == WorkspaceMode.Shared
            && task.Role is AgentTaskRole.Plan or AgentTaskRole.Docs or AgentTaskRole.Code)
        {
            sb.AppendLine(SharedWriteCommitLine).AppendLine();
        }

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
            ? $"at {ModelLevelAliases.For(task.AgentKind, from)}, "
                + $"escalated to {ModelLevelAliases.For(task.AgentKind, task.ModelLevel)}"
            : $"at {ModelLevelAliases.For(task.AgentKind, task.ModelLevel)}";

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
            session is forwarded, and the caller cannot see your screen. If a file is your
            deliverable, give its absolute path; an `[[attach:]]` marker here reaches only your
            caller as text — it is never sent to any chat.

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

            End your final message with one line, on its own: `{ReportToken(taskId, "done")}` if the work
            is complete, `{ReportToken(taskId, "blocked")}` if you need a decision or an answer to continue,
            `{ReportToken(taskId, "failed")}` if you could not do it. Nothing after it. Without that line the
            harness cannot tell your report from a status update and will ask you once.

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

    public sealed record Note(string Body, bool Excerpted, string Header);

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
    /// <param name="overlappingRunning">
    /// Short ids of tasks that were still running when this one settled and whose areas it touched
    /// (CARD-0063 S3). This is the WHOLE of the card's merge-ordering deliverable: the operator
    /// merges by hand, so being told which live task shares this one's ground is what lets them
    /// pick an order in which the second rebase is trivial. No queue, no lock.
    /// </param>
    public static Note BuildCompletionNote(
        AgentTask task, DelegationSettings settings, string report, string? workspaceNote = null,
        int? replyInlineMaxChars = null, string? warning = null, string? overlappingRunning = null,
        string? drift = null, string? reportEvidence = null, string? git = null)
    {
        var header = new StringBuilder();
        header.Append('[').Append("task ").Append(Short(task.Id)).Append(' ')
              .Append(StatusWord(task.Status)).Append(']');

        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.Title)) bits.Add(task.Title.Trim());
        bits.Add(ModelLevelAliases.For(task.AgentKind, task.ModelLevel));
        if (task.DispatchedAt is { } started && task.CompletedAt is { } finished)
            bits.Add(FormatDuration(finished - started));
        if (task.CostUsd > 0) bits.Add($"${task.CostUsd:0.000}");
        if (!string.IsNullOrWhiteSpace(workspaceNote)) bits.Add(workspaceNote.Trim());
        if (!string.IsNullOrWhiteSpace(overlappingRunning))
            bits.Add($"overlapping-running={overlappingRunning.Trim()}");
        // CARD-0063 S4: what this task actually touched outside what it declared. A fact for the
        // caller, never a verdict on the work.
        if (!string.IsNullOrWhiteSpace(drift))
            bits.Add($"drift={drift.Trim()}");
        if (!string.IsNullOrWhiteSpace(reportEvidence))
            bits.Add($"report={reportEvidence.Trim()}");
        if (!string.IsNullOrWhiteSpace(git))
            bits.Add($"git={git.Trim()}");
        if (bits.Count > 0) header.Append(' ').Append(string.Join(" · ", bits));

        var (body, excerpted) = FitReport(report ?? string.Empty, task, settings, replyInlineMaxChars);
        var normalizedHeader = header.ToString().ReplaceLineEndings("\n");
        if (!string.IsNullOrWhiteSpace(warning))
            normalizedHeader = $"{normalizedHeader}\n\n{warning.Trim()}".ReplaceLineEndings("\n");
        return new Note($"{normalizedHeader}\n\n{body}".ReplaceLineEndings("\n"), excerpted, normalizedHeader);
    }

    /// <summary>
    /// The short, lossless replacement for a completion report whose parent session already read
    /// that exact report through a status poll. The supplied header is preserved verbatim so a
    /// caller-facing warning cannot be lost at flush time.
    /// </summary>
    public static string BuildPolledNoteBody(string noteHeader, AgentTask task, int reportChars, DateTime polledAt) =>
        $"{noteHeader}\n\n" +
        $"Report withheld — you already read it: this task's result was returned to your status poll at {polledAt:O} ({reportChars:N0} chars).\n" +
        $"Re-read it with: pwsh -File scripts/delegate.ps1 -Status {Short(task.Id)}";

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
    /// <param name="agentKind">
    /// Whose composer this will be typed into (CARD-0084 S1; default-deny since CARD-0099 S2). A
    /// kind whose composer is not trusted to keep our newlines
    /// (<see cref="PtyDeliveryCeilings.RequiresJoinSafeDelivery"/> — measured for Grok, assumed for
    /// every other non-Claude kind) gets the same words rendered as ONE line, with the spill path
    /// quoted — see <see cref="FlattenForJoiningComposer"/>. Default
    /// <see cref="AgentKind.ClaudeCode"/>, so every existing caller renders byte-identically.
    /// </param>
    public static string BuildBriefPointer(
        AgentTask task, DelegationSettings settings, string? spillPath, int fullLength,
        AgentKind agentKind = AgentKind.ClaudeCode)
    {
        var joins = PtyDeliveryCeilings.RequiresJoinSafeDelivery(agentKind);
        var where = string.IsNullOrWhiteSpace(spillPath)
            ? $"GET {settings.ApiBaseUrl.TrimEnd('/')}/api/agent-tasks/{task.Id} and read the \"goal\" field"
            : joins ? $"'{spillPath}'" : spillPath;

        var sb = new StringBuilder();
        sb.Append(TaskMarker(task.Id))
          .Append(" role=").Append(task.Role)
          .Append(" tier=").Append(task.ModelLevel)
          .Append(" workspace=").Append(task.Workspace);
        if (!string.IsNullOrWhiteSpace(task.Scope))
            sb.Append(" areas=").Append(task.Scope);
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
        return joins ? FlattenForJoiningComposer(sb.ToString()) : sb.ToString();
    }

    /// <summary>
    /// The same words, rendered for a composer that will DROP every newline and join the lines with
    /// no separator (CARD-0084 S1; measured for grok 1.0.5). We do the join ourselves, with a single
    /// space, so the separator is one we chose: the alternative is
    /// <c>…task-7f3a2b91-brief.mdEverything you need is there</c>, where a path — or a command, or a
    /// test filter — has silently grown the next line's first word.
    ///
    /// <para>Blank lines collapse and leading indentation goes, because after the join neither
    /// exists anyway. What survives is exactly what has to: the <c>[antiphon-task:id]</c> markers
    /// (bracketed, so they are unambiguous with a space either side of them and are the ONE thing
    /// correlation depends on), the quoted spill path, and the closing marker LAST — the fragment
    /// measured to survive every pty loss.</para>
    ///
    /// <para>Only pointer text goes through here, never a brief or a refinement body: those spill to
    /// a file for this kind (<see cref="PtyDeliveryCeilings.ForAgentKind"/>) precisely because
    /// flattening a 6 KB brief would destroy the structure this preserves in a pointer.</para>
    /// </summary>
    internal static string FlattenForJoiningComposer(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(trimmed);
        }

        return sb.ToString();
    }

    /// <summary>
    /// A mid-flight refinement to a RUNNING delegate (CARD-0062). Two constraints shape the wording.
    /// The marker must ride it, because the delegate's next finished turn correlates back to the
    /// task through the prompt it answered — a refinement without it would make that turn read as a
    /// human's, raising the uncorrelated-report incident. And the delegate must be told NOT to end
    /// its turn just to acknowledge: a turn end after a marked prompt IS settlement, so a bare
    /// "noted, adjusting" turn would settle the task with the acknowledgment as its report. The
    /// acknowledgment the caller needs — did this arrive before or after the relevant decision —
    /// goes at the head of the eventual report instead, where ordering is on the record.
    /// Marker at BOTH ends for the same reason briefs carry it twice: the tail is the only fragment
    /// that survived every measured pty loss (2026-08-11).
    /// </summary>
    public static string BuildRefinement(AgentTask task, string message)
    {
        var marker = TaskMarker(task.Id);
        return $"""
            {marker} REFINEMENT

            Your caller refined this task while you are working. Your brief stands except as amended
            below. Fold it in and continue — do NOT end your turn just to acknowledge it, because
            ending a turn is read as your report. When you do report, open with one line saying this
            refinement arrived and how it affected the work.

            {message.Trim()}

            {marker}
            """.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// A refinement too long to type intact — same shape as <see cref="BuildBriefPointer"/>, and
    /// like it the pointer never grows a body of its own. <paramref name="spillPath"/> null falls
    /// back to the task's event timeline via the API, where the refinement's head is on record.
    /// </summary>
    /// <param name="agentKind">See <see cref="BuildBriefPointer"/> — the same join-safe rendering.</param>
    public static string BuildRefinementPointer(
        AgentTask task, DelegationSettings settings, string? spillPath, int fullLength,
        AgentKind agentKind = AgentKind.ClaudeCode)
    {
        var joins = PtyDeliveryCeilings.RequiresJoinSafeDelivery(agentKind);
        var marker = TaskMarker(task.Id);
        var where = string.IsNullOrWhiteSpace(spillPath)
            ? $"GET {settings.ApiBaseUrl.TrimEnd('/')}/api/agent-tasks/{task.Id} and read the newest \"Refined\" event"
            : joins ? $"'{spillPath}'" : spillPath;

        var pointer = $"""
            {marker} REFINEMENT

            Your caller refined this task while you are working, but the refinement is
            {fullLength:N0} characters — too long to type into a terminal without the transport
            dropping part of it. Read it in full before continuing:

                {where}

            Your brief stands except as amended there. Fold it in and continue — do NOT end your
            turn just to acknowledge it. When you report, open with one line saying the refinement
            arrived and how it affected the work.

            {marker}
            """.ReplaceLineEndings("\n");

        return joins ? FlattenForJoiningComposer(pointer) : pointer;
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
