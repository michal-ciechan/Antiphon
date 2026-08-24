using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0171: renders a bidirectional sync run into the plain-text change summary a chat channel
/// receives. Pure and static — no DB, no channel catalog, no I/O — so the message shape is
/// unit-testable on its own (<c>TrackerSyncSummaryFormatterTests</c>).
///
/// Deliberately NOT the alert digest voice (<c>AlertDigestFlusher</c>): a successful sync is not an
/// incident, and the card-naming content here does not fit a 200-char alert detail.
/// </summary>
public static class TrackerSyncSummaryFormatter
{
    /// <summary>Telegram rejects >4096; the cap leaves room for the trailing ellipsis.</summary>
    public const int MaxChars = 3500;

    /// <summary>Identifiers listed inline before the line collapses to "+N more".</summary>
    public const int MaxIdentifiersPerLine = 5;

    /// <summary>Longest tracker key still worth showing next to an identifier.</summary>
    private const int MaxExternalKeyChars = 12;

    /// <summary>Kinds in the order their lines appear. Labels last — see §5/§7 of the plan.</summary>
    private static readonly TrackerSyncChangeKind[] LineOrder =
    [
        TrackerSyncChangeKind.CommentIn,
        TrackerSyncChangeKind.CommentOut,
        TrackerSyncChangeKind.ClosedOnGitHub,
        TrackerSyncChangeKind.ReopenedFromGitHub,
        TrackerSyncChangeKind.ReopenedOnGitHub,
        TrackerSyncChangeKind.Created,
        TrackerSyncChangeKind.ContentPushed,
        TrackerSyncChangeKind.LabelsChanged
    ];

    /// <summary>
    /// One message for the given boards. Boards with no changes are omitted; null when no board
    /// has any (nothing to say ⇒ nothing sent).
    /// </summary>
    public static string? Format(IReadOnlyList<(TrackerSyncBoardResult Board, IssueTrackerConfig? Config)> boards)
    {
        var blocks = new List<string>();
        foreach (var (board, config) in boards)
        {
            if (board.Changes.Count == 0)
                continue;

            blocks.Add(FormatBoard(board, config));
        }

        if (blocks.Count == 0)
            return null;

        return Cap(string.Join("\n\n", blocks));
    }

    private static string FormatBoard(TrackerSyncBoardResult board, IssueTrackerConfig? config)
    {
        var tracker = TrackerLabel(config?.Kind);
        var sb = new StringBuilder();
        sb.Append("Antiphon <-> ").Append(tracker).Append(" sync: ").Append(board.BoardName);

        foreach (var kind in LineOrder)
        {
            var items = board.Changes.Where(c => c.Kind == kind).ToList();
            if (items.Count == 0)
                continue;

            sb.Append("\n- ").Append(Line(kind, items, tracker));
        }

        if (IssuesLink(config) is { } link)
            sb.Append('\n').Append(link);

        return sb.ToString();
    }

    private static string Line(
        TrackerSyncChangeKind kind, IReadOnlyList<TrackerSyncChange> items, string tracker)
    {
        var n = items.Count;
        return kind switch
        {
            TrackerSyncChangeKind.CommentIn =>
                $"{n} comment{S(n)} in from {tracker}: {Identifiers(items, withKey: false)}",
            TrackerSyncChangeKind.CommentOut =>
                $"{n} comment{S(n)} posted to {tracker}: {Identifiers(items, withKey: false)}",
            TrackerSyncChangeKind.ClosedOnGitHub =>
                $"{n} issue{S(n)} closed on {tracker}: {Identifiers(items, withKey: true)}",
            TrackerSyncChangeKind.ReopenedFromGitHub =>
                $"{n} issue{S(n)} reopened from {tracker}: {Identifiers(items, withKey: true)}",
            TrackerSyncChangeKind.ReopenedOnGitHub =>
                $"{n} issue{S(n)} reopened on {tracker}: {Identifiers(items, withKey: true)}",
            TrackerSyncChangeKind.Created =>
                $"{n} issue{S(n)} created on {tracker}: {Identifiers(items, withKey: true)}",
            TrackerSyncChangeKind.ContentPushed =>
                $"content updated on {tracker}: {Identifiers(items, withKey: true)}",
            // Count-only and last: labels are the least interesting kind and the flap-prone one
            // (a human re-adding a managed label makes this line fire every run).
            TrackerSyncChangeKind.LabelsChanged =>
                $"labels updated on {n} issue{S(n)}",
            _ => $"{n} change{S(n)}"
        };
    }

    /// <summary>Deduplicated, first-seen order, at most <see cref="MaxIdentifiersPerLine"/> then "+N more".</summary>
    private static string Identifiers(IReadOnlyList<TrackerSyncChange> items, bool withKey)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rendered = new List<string>();
        foreach (var item in items)
        {
            var identifier = string.IsNullOrWhiteSpace(item.CardIdentifier) ? "(unnamed card)" : item.CardIdentifier;
            if (!seen.Add(identifier))
                continue;

            var key = item.ExternalKey;
            rendered.Add(withKey
                         && !string.IsNullOrWhiteSpace(key)
                         && key.Length <= MaxExternalKeyChars
                ? $"{identifier} ({key})"
                : identifier);
        }

        if (rendered.Count <= MaxIdentifiersPerLine)
            return string.Join(", ", rendered);

        var head = rendered.Take(MaxIdentifiersPerLine);
        return $"{string.Join(", ", head)}, +{rendered.Count - MaxIdentifiersPerLine} more";
    }

    /// <summary>
    /// One repo-level issues link per board — only for github.com-hosted GitHub Issues (a custom
    /// <c>base_url</c> is GitHub Enterprise or a fake, and github.com/... would be a lie).
    /// </summary>
    private static string? IssuesLink(IssueTrackerConfig? config)
    {
        if (config is null || config.Kind != TrackerKind.GitHubIssues)
            return null;
        if (string.IsNullOrWhiteSpace(config.Repository))
            return null;
        if (!string.IsNullOrWhiteSpace(config.BaseUrl)
            && !config.BaseUrl.TrimEnd('/').Equals("https://api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"https://github.com/{config.Repository}/issues";
    }

    private static string TrackerLabel(TrackerKind? kind) => kind switch
    {
        TrackerKind.GitHubIssues => "GitHub",
        TrackerKind.Jira => "Jira",
        TrackerKind.Linear => "Linear",
        null => "tracker",
        _ => kind.Value.ToString()
    };

    private static string S(int count) => count == 1 ? "" : "s";

    private static string Cap(string text) =>
        text.Length <= MaxChars ? text : text[..MaxChars] + "…";
}
