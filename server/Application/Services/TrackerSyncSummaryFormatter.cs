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
/// CARD-0346: lists up to <see cref="MaxAffectedIssuesPerKind"/> distinct cards per kind, renders
/// label add/remove deltas, and fits the message by dropping complete kind-detail (labels and
/// comments first) rather than slicing mid-identifier. State transitions keep detail longest.
///
/// Deliberately NOT the alert digest voice (<c>AlertDigestFlusher</c>): a successful sync is not an
/// incident, and the card-naming content here does not fit a 200-char alert detail.
/// </summary>
public static class TrackerSyncSummaryFormatter
{
    /// <summary>Telegram rejects >4096; the cap leaves 595–596 chars of headroom.</summary>
    public const int MaxChars = 3500;

    /// <summary>
    /// Distinct affected cards listed per kind before that kind falls back to count-only.
    /// Comment events on the same card count once.
    /// </summary>
    public const int MaxAffectedIssuesPerKind = 20;

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
    /// Detail is dropped in this order when the combined message exceeds <see cref="MaxChars"/>.
    /// Labels and comments go first; close/reopen keep named cards the longest.
    /// </summary>
    private static readonly TrackerSyncChangeKind[] DowngradeOrder =
    [
        TrackerSyncChangeKind.LabelsChanged,
        TrackerSyncChangeKind.CommentIn,
        TrackerSyncChangeKind.CommentOut,
        TrackerSyncChangeKind.ContentPushed,
        TrackerSyncChangeKind.Created,
        TrackerSyncChangeKind.ClosedOnGitHub,
        TrackerSyncChangeKind.ReopenedFromGitHub,
        TrackerSyncChangeKind.ReopenedOnGitHub
    ];

    /// <summary>
    /// One message for the given boards. Boards with no changes are omitted; null when no board
    /// has any (nothing to say ⇒ nothing sent).
    /// </summary>
    public static string? Format(IReadOnlyList<(TrackerSyncBoardResult Board, IssueTrackerConfig? Config)> boards)
    {
        var blocks = new List<BoardBlock>();
        foreach (var (board, config) in boards)
        {
            if (board.Changes.Count == 0)
                continue;

            blocks.Add(BuildBoard(board, config));
        }

        if (blocks.Count == 0)
            return null;

        var text = Join(blocks);
        if (text.Length <= MaxChars)
            return text;

        foreach (var kind in DowngradeOrder)
        {
            var changed = false;
            foreach (var block in blocks)
            {
                foreach (var line in block.Lines)
                {
                    if (line.Kind != kind || !line.UseDetailed)
                        continue;
                    line.UseDetailed = false;
                    changed = true;
                }
            }

            if (!changed)
                continue;

            text = Join(blocks);
            if (text.Length <= MaxChars)
                return text;
        }

        return PackCompact(blocks);
    }

    private static string Join(IReadOnlyList<BoardBlock> blocks) =>
        string.Join("\n\n", blocks.Select(b => b.Render()));

    private static BoardBlock BuildBoard(TrackerSyncBoardResult board, IssueTrackerConfig? config)
    {
        var tracker = TrackerLabel(config?.Kind);
        var header = $"Antiphon <-> {tracker} sync: {board.BoardName}";
        var lines = new List<KindLine>();
        foreach (var kind in LineOrder)
        {
            var items = board.Changes.Where(c => c.Kind == kind).ToList();
            if (items.Count == 0)
                continue;

            var underCap = DistinctCardCount(items) <= MaxAffectedIssuesPerKind;
            lines.Add(new KindLine
            {
                Kind = kind,
                Detailed = Line(kind, items, tracker, detailed: underCap),
                Compact = Line(kind, items, tracker, detailed: false),
                UseDetailed = underCap
            });
        }

        return new BoardBlock(header, lines, IssuesLink(config));
    }

    private static string Line(
        TrackerSyncChangeKind kind, IReadOnlyList<TrackerSyncChange> items, string tracker, bool detailed)
    {
        var n = items.Count;
        var issueCount = DistinctCardCount(items);
        return kind switch
        {
            TrackerSyncChangeKind.CommentIn =>
                AppendIds($"{n} comment{S(n)} in from {tracker}", items, withKey: false, detailed),
            TrackerSyncChangeKind.CommentOut =>
                AppendIds($"{n} comment{S(n)} posted to {tracker}", items, withKey: false, detailed),
            TrackerSyncChangeKind.ClosedOnGitHub =>
                AppendIds($"{n} issue{S(n)} closed on {tracker}", items, withKey: true, detailed),
            TrackerSyncChangeKind.ReopenedFromGitHub =>
                AppendIds($"{n} issue{S(n)} reopened from {tracker}", items, withKey: true, detailed),
            TrackerSyncChangeKind.ReopenedOnGitHub =>
                AppendIds($"{n} issue{S(n)} reopened on {tracker}", items, withKey: true, detailed),
            TrackerSyncChangeKind.Created =>
                AppendIds($"{n} issue{S(n)} created on {tracker}", items, withKey: true, detailed),
            TrackerSyncChangeKind.ContentPushed =>
                AppendIds($"content updated on {tracker}", items, withKey: true, detailed),
            TrackerSyncChangeKind.LabelsChanged =>
                detailed
                    ? LabelsDetailed(items, issueCount)
                    : $"labels updated on {issueCount} issue{S(issueCount)}",
            _ => $"{n} change{S(n)}"
        };
    }

    private static string AppendIds(
        string prefix, IReadOnlyList<TrackerSyncChange> items, bool withKey, bool detailed) =>
        detailed ? $"{prefix}: {Identifiers(items, withKey)}" : prefix;

    private static string LabelsDetailed(IReadOnlyList<TrackerSyncChange> items, int issueCount)
    {
        var sb = new StringBuilder();
        sb.Append("labels updated on ").Append(issueCount).Append(" issue").Append(S(issueCount)).Append(':');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var identifier = CardId(item);
            if (!seen.Add(identifier))
                continue;

            sb.Append("\n  ").Append(FormatLabelIssue(item, identifier));
        }

        return sb.ToString();
    }

    private static string FormatLabelIssue(TrackerSyncChange item, string identifier)
    {
        var head = RenderIdentifier(item, identifier, withKey: true);
        var parts = new List<string>();
        if (item.Added is { Count: > 0 } added)
            parts.AddRange(added.Select(l => "+" + l));
        if (item.Removed is { Count: > 0 } removed)
            parts.AddRange(removed.Select(l => "-" + l));
        return parts.Count == 0 ? head : $"{head}: {string.Join(", ", parts)}";
    }

    /// <summary>Deduplicated, first-seen order. Caller has already applied the 20-card cap.</summary>
    private static string Identifiers(IReadOnlyList<TrackerSyncChange> items, bool withKey)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rendered = new List<string>();
        foreach (var item in items)
        {
            var identifier = CardId(item);
            if (!seen.Add(identifier))
                continue;

            rendered.Add(RenderIdentifier(item, identifier, withKey));
        }

        return string.Join(", ", rendered);
    }

    private static int DistinctCardCount(IReadOnlyList<TrackerSyncChange> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
            seen.Add(CardId(item));
        return seen.Count;
    }

    private static string CardId(TrackerSyncChange item) =>
        string.IsNullOrWhiteSpace(item.CardIdentifier) ? "(unnamed card)" : item.CardIdentifier;

    private static string RenderIdentifier(TrackerSyncChange item, string identifier, bool withKey)
    {
        var key = item.ExternalKey;
        return withKey
               && !string.IsNullOrWhiteSpace(key)
               && key.Length <= MaxExternalKeyChars
            ? $"{identifier} ({key})"
            : identifier;
    }

    /// <summary>
    /// Every included board is a complete compact block. Boards that still cannot fit are named
    /// only by how many were dropped — never a sliced identifier or label diff.
    /// </summary>
    private static string PackCompact(IReadOnlyList<BoardBlock> blocks)
    {
        var rendered = blocks.Select(b => b.Render()).ToList();
        var included = new List<string>();
        for (var i = 0; i < rendered.Count; i++)
        {
            var trial = included.Count == 0
                ? rendered[i]
                : string.Join("\n\n", included) + "\n\n" + rendered[i];
            var omitted = rendered.Count - included.Count - 1;
            var candidate = omitted == 0 ? trial : trial + "\n\n" + OmissionSummary(omitted);
            if (candidate.Length > MaxChars)
                break;

            included.Add(rendered[i]);
        }

        if (included.Count == 0 && rendered.Count > 0)
            included.Add(rendered[0]);

        var omittedCount = rendered.Count - included.Count;
        var body = string.Join("\n\n", included);
        return omittedCount == 0 ? body : body + "\n\n" + OmissionSummary(omittedCount);
    }

    private static string OmissionSummary(int count) =>
        count == 1 ? "(+1 more board omitted)" : $"(+{count} more boards omitted)";

    private sealed class BoardBlock(string header, List<KindLine> lines, string? link)
    {
        public List<KindLine> Lines { get; } = lines;

        public string Render()
        {
            var sb = new StringBuilder(header);
            foreach (var line in Lines)
                sb.Append("\n- ").Append(line.Current);
            if (link is { } url)
                sb.Append('\n').Append(url);
            return sb.ToString();
        }
    }

    private sealed class KindLine
    {
        public required TrackerSyncChangeKind Kind { get; init; }
        public required string Detailed { get; init; }
        public required string Compact { get; init; }
        public bool UseDetailed { get; set; }
        public string Current => UseDetailed ? Detailed : Compact;
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
}
