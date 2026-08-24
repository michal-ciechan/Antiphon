using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// How a linked card cites its tracker issue in prompts. Unlinked cards get an empty suffix so
/// the default template stays <c>Work on card CARD-nnnn: title</c>.
/// </summary>
public static class TrackerIssueCitation
{
    public static string DisplayName(TrackerKind kind) =>
        kind switch
        {
            TrackerKind.GitHubIssues => "GitHub",
            TrackerKind.Linear => "Linear",
            TrackerKind.Jira => "Jira",
            _ => kind.ToString()
        };

    public static string Suffix(Card card)
    {
        var ext = card.ExternalIssueRef;
        if (ext is null || string.IsNullOrWhiteSpace(ext.ExternalKey))
            return string.Empty;

        var tracker = DisplayName(ext.TrackerKind);
        var url = ext.Url?.Trim();
        return string.IsNullOrWhiteSpace(url)
            ? $" ({tracker} issue {ext.ExternalKey})"
            : $" ({tracker} issue {ext.ExternalKey}, {url})";
    }
}
