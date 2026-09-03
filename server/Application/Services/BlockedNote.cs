using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0294 S2: the four fixed lines above a Blocked-on-question parent note, and the
/// positional <c>asks:</c> extraction shared with the drawer DTO. No NLP — a trailing
/// <c>?</c> uses <see cref="BlockedQuestion.TryExtract"/>, otherwise the last non-empty
/// line, capped at 240 characters.
/// </summary>
internal static class BlockedNote
{
    public const int AsksCap = 240;
    public const int AuthorityMaxChars = 2_000;

    public const string ReasonMarkedBlocked = "marked-blocked";
    public const string ReasonQuestionLine = "question-line";
    public const string ReasonWaitingUnmarked = "waiting-unmarked";
    public const string ReasonWaitingNoProgress = "waiting-no-progress";

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

    public static string? ReasonWord(AgentTaskReportEvidence evidence) => evidence switch
    {
        AgentTaskReportEvidence.Marked => ReasonMarkedBlocked,
        AgentTaskReportEvidence.QuestionHeuristic => ReasonQuestionLine,
        AgentTaskReportEvidence.UnmarkedWaiting => ReasonWaitingUnmarked,
        _ => null,
    };

    public static string ReasonLine(AgentTask task, DelegationSettings settings)
    {
        var word = ReasonWord(task.ReportEvidence) ?? ReasonMarkedBlocked;
        var gloss = word switch
        {
            ReasonMarkedBlocked => "ended with the blocked token",
            ReasonQuestionLine => "last lines looked like a question",
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
        if (BlockedQuestion.TryExtract(report, out var question, out _))
            return Cap(Flatten(question));
        if (string.IsNullOrWhiteSpace(report))
            return "The delegate is blocked and gave no reason.";

        var last = LastNonEmptyLine(report);
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

    public static string Format(AgentTask task, string report, DelegationSettings settings)
    {
        var shortId = DelegationReportFormatter.Short(task.Id);
        var asks = ExtractAsks(report);
        var authority = string.IsNullOrWhiteSpace(task.StandingAuthority)
            ? null
            : Flatten(task.StandingAuthority.Trim());

        var authorityLine = authority is null
            ? "authority: none given at dispatch"
            : $"authority: \"{authority}\" (given at dispatch)";

        var next = authority is null
            ? $"""
              next: -Reply {shortId} "<answer>" if you can answer it; otherwise put `asks:` in your reply to
                    the chat now — do not answer this note with NO_REPLY
              """
            : $"""
              next: pwsh -File scripts/delegate.ps1 -Continue {shortId}  — replays the authority as the answer
                    or -Reply {shortId} "<answer>" · or relay `asks:` to your chat now and end your turn
              """;

        return string.Join('\n',
        [
            ReasonLine(task, settings),
            $"asks: {asks}",
            authorityLine,
            next.ReplaceLineEndings("\n"),
        ]);
    }

    public static string WaitingNoteAuthorityLine(AgentTask task)
    {
        var shortId = DelegationReportFormatter.Short(task.Id);
        return $"authority on file — `-Continue {shortId}` becomes available if it Blocks; `-Refine` now if you want it typed sooner.";
    }

    private static string LastNonEmptyLine(string report)
    {
        var last = report.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Length > 0);
        return last ?? string.Empty;
    }

    private static string Flatten(string text) =>
        string.Join(' ', text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cap(string text) =>
        text.Length <= AsksCap ? text : text[..AsksCap];
}
