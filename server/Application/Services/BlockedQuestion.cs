using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0033: isolate the question a blocked delegate asked from the report that preceded it.
/// Shares the trailing-<c>?</c> window with <see cref="AgentTaskReplyService.LooksLikeAQuestion"/>
/// so the detector and every surface that renders the question cannot disagree.
/// </summary>
internal static class BlockedQuestion
{
    public const string HistoricalQuestion = "(not recorded before CARD-0033)";
    public const string HistoricalAnswer = "(answered — text not recorded before CARD-0033)";
    public const string AskedPrefix = "Delegate asked: ";
    public const string LegacyAskedDetail = "Delegate asked a question.";
    public const string AnsweredViaPrefix = "Answered via ";
    public const string LegacyRepliedDetail = "Caller answered the delegate's question.";
    public const string OverlayRepliedDetail = "Caller answered an in-turn question-tool popup.";
    public const string CostCeilingPrefix = "Run cost ceiling";

    /// <summary>
    /// True when the report's last two non-empty lines contain a trailing <c>?</c>. On success
    /// (and on the DTO fallback when it is not a question) <paramref name="question"/> is the last
    /// paragraph if that paragraph's own last two lines contain a <c>?</c>, otherwise the last two
    /// non-empty lines; <paramref name="context"/> is everything before that, or null when empty.
    /// </summary>
    public static bool TryExtract(string? report, out string question, out string? context)
    {
        question = "";
        context = null;
        if (string.IsNullOrWhiteSpace(report))
            return false;

        var normalized = report.ReplaceLineEndings("\n");
        var nonEmpty = NonEmptyLines(normalized);
        if (nonEmpty.Count == 0)
            return false;

        var isQuestion = nonEmpty.TakeLast(2).Any(l => l.EndsWith('?'));
        var paragraphs = SplitParagraphs(normalized);
        var lastParagraph = paragraphs[^1];
        var lastParagraphIsQuestion = NonEmptyLines(lastParagraph).TakeLast(2).Any(l => l.EndsWith('?'));
        question = lastParagraphIsQuestion
            ? lastParagraph
            : string.Join('\n', nonEmpty.TakeLast(2));

        var idx = normalized.LastIndexOf(question, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var before = normalized[..idx].Trim();
            context = before.Length == 0 ? null : before;
        }
        else
        {
            var kept = nonEmpty.Count <= 2 ? [] : nonEmpty.Take(nonEmpty.Count - 2).ToList();
            context = kept.Count == 0 ? null : string.Join('\n', kept);
        }

        return isQuestion;
    }

    public static string BlockedEventDetail(string report) =>
        TryExtract(report, out var question, out _)
            ? AskedPrefix + question
            : LegacyAskedDetail;

    public static string RepliedEventDetail(AnswerOrigin origin, int round, string message) =>
        $"{AnsweredViaPrefix}{origin} (round {round}): {message}";

    public static bool IsBlockedAnswer(string? detail) =>
        !string.IsNullOrWhiteSpace(detail)
        && !detail.StartsWith(OverlayRepliedDetail, StringComparison.Ordinal)
        && (detail.StartsWith(AnsweredViaPrefix, StringComparison.Ordinal)
            || detail.Equals(LegacyRepliedDetail, StringComparison.Ordinal));

    public static string QuestionFromEventDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail) || detail.Equals(LegacyAskedDetail, StringComparison.Ordinal))
            return HistoricalQuestion;
        if (detail.StartsWith(AskedPrefix, StringComparison.Ordinal))
            return detail[AskedPrefix.Length..];
        return detail;
    }

    public static (string Answer, AnswerOrigin? Origin) AnswerFromEventDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail) || detail.Equals(LegacyRepliedDetail, StringComparison.Ordinal))
            return (HistoricalAnswer, null);
        if (!detail.StartsWith(AnsweredViaPrefix, StringComparison.Ordinal))
            return (HistoricalAnswer, null);

        var rest = detail[AnsweredViaPrefix.Length..];
        var space = rest.IndexOf(' ');
        var originToken = space < 0 ? rest : rest[..space];
        AnswerOrigin? origin = originToken switch
        {
            nameof(AnswerOrigin.Web) => AnswerOrigin.Web,
            nameof(AnswerOrigin.Cli) => AnswerOrigin.Cli,
            nameof(AnswerOrigin.Channel) => AnswerOrigin.Channel,
            _ => null,
        };
        var colon = rest.IndexOf(": ", StringComparison.Ordinal);
        var answer = colon < 0 ? rest : rest[(colon + 2)..];
        if (string.IsNullOrWhiteSpace(answer))
            answer = HistoricalAnswer;
        return (answer, origin);
    }

    private static List<string> NonEmptyLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

    private static List<string> SplitParagraphs(string text) =>
        text.Split(["\n\n"], StringSplitOptions.None)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
}
