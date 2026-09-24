using System.Globalization;
using System.Text;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One aggregate prompt per directive nudge. The identity header and the ACK instruction are never
/// truncated; condition lines are dropped from the end, with a count and the status route, when the
/// ceiling is reached. A fence explains its queue and capacity symptoms in the same body.
/// The body is frozen by the ledger once committed; this type only renders it.
/// </summary>
public static class ExpectationPromptFormatter
{
    public const int DefaultCeilingChars = 6000;
    public const int MaxLineChars = 600;

    public static string AckMarker(Guid nudgeId) => "[expectation-ack:" + nudgeId.ToString("D") + "]";

    public static string Format(
        Guid nudgeId,
        string directiveId,
        Guid boardId,
        IReadOnlyList<ExpectationCondition> conditions,
        DateTime asOf,
        int ceilingChars = DefaultCeilingChars)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var ordered = conditions
            .OrderBy(condition => Rank(condition.Kind))
            .ThenBy(condition => condition.SubjectKey, StringComparer.Ordinal)
            .ToList();
        var fenced = ordered.Any(condition => condition.Kind == ExpectationEpisodeKind.DispatchFence);

        var header =
            "[expectation-nudge:" + nudgeId.ToString("D") + "] Directive " + directiveId.Trim()
            + ": " + ordered.Count.ToString(CultureInfo.InvariantCulture)
            + " condition(s) need attention as of " + asOf.ToString("O", CultureInfo.InvariantCulture) + ".\n";
        var footer =
            "Status: GET /api/expectation-watchdog?boardId=" + boardId.ToString("D") + "\n"
            + "Reply with a line " + AckMarker(nudgeId)
            + " followed by the action you are taking or why you are waiting."
            + " This watchdog detects only; it does not dispatch, recover, cancel or move cards.";

        var budget = ceilingChars - header.Length - footer.Length;
        var body = new StringBuilder(header);
        var written = 0;
        foreach (var condition in ordered)
        {
            var line = Line(condition, fenced);
            var reserve = written + 1 < ordered.Count ? 80 : 0;
            if (line.Length + reserve > budget)
                break;
            body.Append(line);
            budget -= line.Length;
            written++;
        }

        if (written < ordered.Count)
        {
            body.Append("- and ")
                .Append((ordered.Count - written).ToString(CultureInfo.InvariantCulture))
                .Append(" more condition(s); see the status route.\n");
        }

        body.Append(footer);
        return body.ToString();
    }

    private static string Line(ExpectationCondition condition, bool fenced)
    {
        var explained = fenced
            && condition.Kind is ExpectationEpisodeKind.StalledPipeline or ExpectationEpisodeKind.CapacityDeficit
            ? " (explained by the fence above)"
            : string.Empty;
        var text = "- " + condition.Kind + " " + condition.ReasonCode + explained + ": " + condition.Evidence.Trim();
        if (text.Length > MaxLineChars)
            text = text[..(MaxLineChars - 3)] + "...";
        return text + "\n";
    }

    private static int Rank(ExpectationEpisodeKind kind) => kind switch
    {
        ExpectationEpisodeKind.DispatchFence => 0,
        ExpectationEpisodeKind.SilentInFlight => 1,
        ExpectationEpisodeKind.UndeliveredNote => 2,
        ExpectationEpisodeKind.StalledPipeline => 3,
        ExpectationEpisodeKind.CapacityDeficit => 4,
        _ => 5,
    };
}
