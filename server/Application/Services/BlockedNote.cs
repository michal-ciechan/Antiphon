using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The caller's Blocked note and the ask shared with the drawer DTO. Structured fields
/// take precedence over the question and last-line fallbacks for older reports.
/// </summary>
internal static class BlockedNote
{
    public const int AsksCap = 240;
    public const int AuthorityMaxChars = 2_000;

    public const string ReasonMarkedBlocked = "marked-blocked";
    public const string ReasonQuestionLine = "question-line";
    public const string ReasonWaitingUnmarked = "waiting-unmarked";
    public const string ReasonWaitingNoProgress = "waiting-no-progress";
    public const string ReasonQuestionAfterReply = "question-after-reply";

    public static string StandingAuthorityBlock(string authority) =>
        $"""
        --- standing authority from your caller ---
        "{authority}"
        Do not stop to ask for approval that this already grants. If you would otherwise pause for a
        go-ahead on something it covers, proceed and say so in one line of your report. If what you
        need is NOT covered, end with the blocked token and say exactly what is missing.
        """;

    public static string ContinueMessage(string authority) =>
        $"""
        Continue. Your caller's standing authority: "{authority}". It covers what you asked; proceed
        without further approval. If it does not, say exactly what is missing and end with the blocked token.
        """;

    public static string RepliedContinuePrefix => "continued with standing authority — ";

    /// <summary>
    /// True when this Blocked row is waiting on a human question rather than a merge, cost
    /// ceiling, routing exhaustion, or launch-failure repeat. Those writers set
    /// <see cref="AgentTask.FailureReason"/>.
    /// </summary>
    public static bool IsQuestionBlock(AgentTask task) =>
        task.Status == AgentTaskStatus.Blocked && string.IsNullOrWhiteSpace(task.FailureReason);

    /// <summary>
    /// CARD-1503: the question of a Block that kept the retained marked report (a question-shaped
    /// unmarked turn after a Reply). The task row still holds the report, so the question is the
    /// latest Block's <c>Delegate asked:</c> detail; null for every other Block.
    /// </summary>
    public static string? RetainedQuestion(AgentTask task, string? latestBlockDetail) =>
        task.ReportEvidence == AgentTaskReportEvidence.Marked
        && latestBlockDetail is not null
        && latestBlockDetail.StartsWith(BlockedQuestion.AskedPrefix, StringComparison.Ordinal)
            ? latestBlockDetail[BlockedQuestion.AskedPrefix.Length..]
            : null;

    public static string? ReasonWord(AgentTaskReportEvidence evidence) => evidence switch
    {
        AgentTaskReportEvidence.Marked => ReasonMarkedBlocked,
        AgentTaskReportEvidence.QuestionHeuristic => ReasonQuestionLine,
        AgentTaskReportEvidence.UnmarkedWaiting => ReasonWaitingUnmarked,
        _ => null,
    };

    public static string ReasonLine(AgentTask task, DelegationSettings settings, string? reasonWord = null)
    {
        var word = reasonWord ?? ReasonWord(task.ReportEvidence) ?? ReasonMarkedBlocked;
        var gloss = word switch
        {
            ReasonMarkedBlocked => "ended with the blocked token",
            ReasonQuestionLine => "last lines looked like a question",
            ReasonQuestionAfterReply => "ended unmarked with a question after the Reply; the retained marked report stands",
            ReasonWaitingUnmarked =>
                $"ended a turn without the closing line; asked once; idle {Math.Max(1, settings.UnmarkedWaitingMinutes)}m",
            ReasonWaitingNoProgress =>
                "ended a second turn without the closing line after the nudge, and the worktree shows no post-dispatch progress",
            _ => "waiting on a human",
        };
        return $"reason: {word} — {gloss}";
    }

    public static string ExtractAsks(string? report)
    {
        var lines = ReportLines(report);
        return ReadField(lines, "asks:") ?? LegacyAsk(lines);
    }

    private static string LegacyAsk(string[] lines)
    {
        var narrative = lines.TakeWhile(line => !IsFooter(line)).Where(line => !IsControlLine(line)).ToArray();
        if (BlockedQuestion.TryExtract(string.Join('\n', narrative), out var question, out _))
            return Cap(Flatten(question));
        var last = narrative.LastOrDefault(line => line.Length != 0);
        return string.IsNullOrWhiteSpace(last)
            ? "The delegate is blocked and gave no reason."
            : Cap(last);
    }

    public static string? ContextBeforeAsks(string? report, string asks)
    {
        if (string.IsNullOrWhiteSpace(report) || string.IsNullOrWhiteSpace(asks))
            return null;
        var normalized = report.ReplaceLineEndings("\n").Trim();
        var idx = normalized.LastIndexOf(asks, StringComparison.Ordinal);
        if (idx <= 0)
            return null;
        var before = normalized[..idx].Trim();
        return before.Length == 0 ? null : before;
    }

    /// <param name="retainedQuestion">CARD-1503: the question of a Block that kept the retained report;
    /// the <c>asks:</c> line and the reason come from it, never from the retained report.</param>
    public static string Format(AgentTask task, string report, DelegationSettings settings, string? retainedQuestion = null)
    {
        var shortId = DelegationReportFormatter.Short(task.Id);
        // A retained report's old recommendation cannot answer the newly asked question.
        var lines = ReportLines(retainedQuestion ?? report);
        var reason = ReadField(lines, "reason:");
        var asks = ReadField(lines, "asks:") ?? LegacyAsk(lines);
        var recommended = ReadField(lines, "recommended:", PipelineHandoff.MaxHandoffChars)
            ?? ReadField(lines, "recommended action:", PipelineHandoff.MaxHandoffChars);
        var reportedAuthority = ReadField(lines, "authority:");
        var reportedNext = ReadField(lines, "next:");
        if (retainedQuestion is null && recommended is null)
        {
            var handoff = PipelineHandoff.TryParse(report);
            if (handoff.Kind == PipelineHandoffKind.Decide && handoff.Handoff is { } proposal)
                recommended = Cap(Flatten(proposal), PipelineHandoff.MaxHandoffChars);
        }
        var authority = string.IsNullOrWhiteSpace(task.StandingAuthority)
            ? null
            : Flatten(task.StandingAuthority.Trim());

        var authorityLine = authority is null
            ? "authority: none given at dispatch"
            : $"authority: \"{authority}\" (given at dispatch)";

        var next = authority is null
            ? $"""
              next: reply {shortId} "<decision>" using the recommended action. Escalate only repository-defined hard stops.
              """
            : $"""
              next: scripts/antiphon delegate continue {shortId} — replay applicable standing authority
                    or reply {shortId} "<decision>". Escalate only repository-defined hard stops.
              """;

        var note = new List<string>
        {
            reason is null ? ReasonLine(task, settings, retainedQuestion is null ? null : ReasonQuestionAfterReply)
                : $"reason: {reason}",
            $"asks: {asks}",
        };
        if (recommended is not null) note.Add($"recommended: {recommended}");
        if (reportedAuthority is not null) note.Add($"reported authority: {reportedAuthority}");
        if (reportedNext is not null) note.Add($"reported next: {reportedNext}");
        note.Add(authorityLine);
        note.Add(next.ReplaceLineEndings("\n"));
        return string.Join('\n', note);
    }

    public static string WaitingNoteAuthorityLine(AgentTask task)
    {
        var shortId = DelegationReportFormatter.Short(task.Id);
        return $"authority on file — `continue {shortId}` becomes available if it Blocks; `refine` now if you want it typed sooner.";
    }

    private static string[] ReportLines(string? report) =>
        (report ?? "").ReplaceLineEndings("\n").Split('\n', StringSplitOptions.TrimEntries);

    private static string? ReadField(string[] lines, string prefix, int limit = AsksCap) => lines
        .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(line => line[prefix.Length..].Trim())
        .Where(value => value.Length != 0 && !IsControlLine(value))
        .Select(value => Cap(value, limit))
        .LastOrDefault();

    private static readonly string[] FieldPrefixes =
        ["reason:", "asks:", "recommended:", "recommended action:", "authority:", "next:", "handoff:", "artifact:"];

    private static bool IsControlLine(string line) =>
        line.StartsWith("[antiphon-", StringComparison.OrdinalIgnoreCase)
        || IsFooter(line)
        || FieldPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsFooter(string line) =>
        line.Equals("--- next stage ---", StringComparison.OrdinalIgnoreCase)
        || line.Equals("--- review evidence ---", StringComparison.OrdinalIgnoreCase);

    private static string Flatten(string text) =>
        string.Join(' ', text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cap(string text, int limit = AsksCap) =>
        text.Length <= limit ? text : text[..limit];
}
