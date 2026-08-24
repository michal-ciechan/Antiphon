using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// How a linked card names its tracker issue in prose an agent will read (CARD-0175 decision 6).
/// </summary>
/// <remarks>
/// After CARD-0175 a card's <c>Identifier</c> is always <c>CARD-nnnn</c>, so the GitHub number
/// that used to BE the identifier now has to be carried alongside it — otherwise an agent working
/// an imported card has no way to write <c>Fixes #3</c> and get GitHub's autolink. One helper so
/// the workflow prompt, the card-spawn prompt and the orchestrator's dispatch prompt cannot drift.
/// </remarks>
public static class CardExternalReference
{
    /// <summary>
    /// <c> (GitHub issue #3, https://…)</c> — leading space included so it appends straight after
    /// the identifier. Empty string when the card is not linked to a tracker issue.
    /// </summary>
    public static string Clause(ExternalIssueRef? externalRef)
    {
        if (externalRef is null || string.IsNullOrWhiteSpace(externalRef.ExternalKey))
            return string.Empty;

        var noun = TrackerNoun(externalRef.TrackerKind);
        return string.IsNullOrWhiteSpace(externalRef.Url)
            ? $" ({noun} {externalRef.ExternalKey.Trim()})"
            : $" ({noun} {externalRef.ExternalKey.Trim()}, {externalRef.Url.Trim()})";
    }

    /// <summary>The same clause from loose parts, for projections that never load the entity.</summary>
    public static string Clause(TrackerKind? trackerKind, string? externalKey, string? url)
    {
        if (trackerKind is not TrackerKind kind || string.IsNullOrWhiteSpace(externalKey))
            return string.Empty;

        var noun = TrackerNoun(kind);
        return string.IsNullOrWhiteSpace(url)
            ? $" ({noun} {externalKey.Trim()})"
            : $" ({noun} {externalKey.Trim()}, {url!.Trim()})";
    }

    /// <summary>The <c>issue.tracker</c> template variable: a human name for the tracker.</summary>
    public static string TrackerName(TrackerKind kind) =>
        kind switch
        {
            TrackerKind.GitHubIssues => "GitHub",
            TrackerKind.Jira => "Jira",
            TrackerKind.Linear => "Linear",
            _ => "Antiphon"
        };

    private static string TrackerNoun(TrackerKind kind) => $"{TrackerName(kind)} issue";
}
