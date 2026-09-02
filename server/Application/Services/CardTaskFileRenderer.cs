using System.Globalization;
using System.Text;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure CARD-0004 renderer: board slug, card filename, card markdown, INDEX.md. No I/O, no
/// database. Byte-deterministic — no YAML library on this path; the schema is fixed.
/// </summary>
/// <remarks>
/// Frontmatter carries the record, not the runtime. Fields that change on an agent claim, a
/// queue shuffle or a workflow tick (<c>OwnerSessionId</c>, <c>CurrentWorktreeId</c>,
/// <c>AssignedAgent*</c>, <c>AgentQueuePosition</c>, <c>ActiveWorkflowRun*</c>,
/// <c>ConcurrencyToken</c>, <c>UpdatedAt</c>, <c>AutoDispatchHeldAt</c>,
/// <c>DecisionNotifiedAt</c>, <c>RevisionCount</c>, sessions) are excluded so those writes never
/// churn a commit. <c>ExternalIssueRef</c> is included; archived cards keep their file.
/// </remarks>
internal static class CardTaskFileRenderer
{
    internal const int SlugMaxLength = 60;
    internal const string CardsRoot = "docs/cards";
    internal const string IndexFileName = "INDEX.md";

    private static readonly (string Header, Func<Card, bool> Match)[] IndexGroups =
    [
        ("Needs decision", c => c.ArchivedAt is null && c.Status == CardStatus.NeedsDecision),
        ("In progress", c => c.ArchivedAt is null && c.Status == CardStatus.InProgress),
        ("Review", c => c.ArchivedAt is null && c.Status == CardStatus.Review),
        ("Backlog", c => c.ArchivedAt is null && c.Status == CardStatus.Backlog),
        ("Done", c => c.ArchivedAt is null && c.Status == CardStatus.Done),
        ("Canceled", c => c.ArchivedAt is null && c.Status == CardStatus.Canceled),
        ("Archived", c => c.ArchivedAt is not null),
    ];

    internal static string BoardSlug(string name) => Slugify(name);

    internal static string CardFileName(string identifier, string title)
    {
        var id = SanitizeIdentifier(identifier);
        var slug = Slugify(title);
        return string.IsNullOrEmpty(slug) ? $"{id}.md" : $"{id}-{slug}.md";
    }

    internal static string SanitizeIdentifier(string identifier)
    {
        var chars = identifier.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray();
        return new string(chars);
    }

    internal static string Slugify(string name, int maxLength = SlugMaxLength)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length <= maxLength)
            return slug;
        return slug[..maxLength].Trim('-');
    }

    /// <summary>YAML double-quoted form: <c>\</c> and <c>"</c> (and C0 escapes) so <c>:</c>, <c>#</c> and quotes never break the block.</summary>
    internal static string YamlQuote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                default: sb.Append(ch); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    internal static string FormatTimestamp(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    internal static string RenderCard(Card card)
    {
        var labels = BoardService.ParseLabels(card.LabelsJson);
        var sb = new StringBuilder();
        AppendLine(sb, "---");
        AppendLine(sb, $"id: {card.Id:D}");
        AppendLine(sb, $"identifier: {card.Identifier}");
        AppendLine(sb, $"title: {YamlQuote(card.Title)}");
        AppendLine(sb, $"status: {card.Status}");
        AppendLine(sb, $"importance: {card.Importance}");
        AppendLine(sb, $"importance_provenance: {card.ImportanceProvenance}");
        AppendLine(sb, $"urgency: {card.Urgency}");
        if (card.DueAt is { } due)
            AppendLine(sb, $"due: {FormatTimestamp(due)}");
        AppendLine(sb, $"labels: [{string.Join(", ", labels.Select(YamlQuote))}]");
        AppendLine(sb, $"created: {FormatTimestamp(card.CreatedAt)}");
        if (card.StartedAt is { } started)
            AppendLine(sb, $"started: {FormatTimestamp(started)}");
        if (card.CompletedAt is { } completed)
            AppendLine(sb, $"completed: {FormatTimestamp(completed)}");
        if (card.ExternalIssueRef is { } ext)
        {
            AppendLine(sb, $"external_tracker: {ext.TrackerKind}");
            AppendLine(sb, $"external_key: {YamlQuote(ext.ExternalKey)}");
            AppendLine(sb, $"external_url: {YamlQuote(ext.Url)}");
            if (ext.Author is not null)
                AppendLine(sb, $"external_author: {YamlQuote(ext.Author)}");
            if (BoardService.NeedsHumanReview(card))
                AppendLine(sb, "needs_human_review: true");
        }
        if (card.ArchivedAt is { } archived)
        {
            AppendLine(sb, $"archived: {FormatTimestamp(archived)}");
            if (card.ArchivedBy is not null)
                AppendLine(sb, $"archived_by: {YamlQuote(card.ArchivedBy)}");
            if (card.ArchivedReason is not null)
                AppendLine(sb, $"archived_reason: {YamlQuote(card.ArchivedReason)}");
        }
        AppendLine(sb, "---");
        sb.Append('\n');
        AppendLine(sb, $"# {card.Identifier} — {card.Title}");
        var description = NormalizeNewlines(card.Description);
        if (description.Length > 0)
        {
            sb.Append('\n');
            sb.Append(description);
            if (!description.EndsWith('\n'))
                sb.Append('\n');
        }

        if (card.TerminalReason is { } outcome && outcome.Length > 0)
        {
            sb.Append('\n');
            AppendLine(sb, "## Outcome");
            sb.Append('\n');
            var reason = NormalizeNewlines(outcome);
            sb.Append(reason);
            if (!reason.EndsWith('\n'))
                sb.Append('\n');
        }

        return WithSingleTrailingLf(sb.ToString());
    }

    internal static string RenderIndex(
        string boardName,
        IReadOnlyList<Card> cards,
        IReadOnlyDictionary<Guid, string> fileNames)
    {
        var archived = cards.Count(c => c.ArchivedAt is not null);
        var cardWord = cards.Count == 1 ? "card" : "cards";
        var sb = new StringBuilder();
        AppendLine(sb, $"# {boardName} — cards");
        sb.Append('\n');
        AppendLine(sb,
            "Generated by Antiphon from the board on every sync. Do not edit files in this directory — edit the " +
            $"card (`scripts/card.ps1`), and the next sync overwrites them. {cards.Count} {cardWord}, {archived} archived.");

        var now = DateTime.UtcNow;
        foreach (var (header, match) in IndexGroups)
        {
            var group = cards
                .Where(match)
                .OrderBy(c => CardRanking.Rank(c, now))
                .ThenBy(c => CardRanking.DueAtSortKey(c.DueAt))
                .ThenBy(c => c.CreatedAt)
                .ThenBy(c => c.Identifier, StringComparer.Ordinal)
                .ToList();
            if (group.Count == 0)
                continue;

            sb.Append('\n');
            AppendLine(sb, $"## {header} ({group.Count})");
            foreach (var card in group)
            {
                if (!fileNames.TryGetValue(card.Id, out var fileName))
                    continue;
                var labels = BoardService.ParseLabels(card.LabelsJson);
                var labelBits = string.Concat(labels.Select(l => $" `{l}`"));
                AppendLine(sb, $"- [{card.Identifier}]({fileName}) — {card.Title}{IndexBits(card, now)}{labelBits}");
            }
        }

        return WithSingleTrailingLf(sb.ToString());
    }

    private static string IndexBits(Card card, DateTime now)
    {
        var bits = new StringBuilder();
        if (card.Importance != CardImportance.Normal)
            bits.Append($" `{card.Importance.ToString().ToLowerInvariant()}`");
        var effective = CardRanking.EffectiveUrgency(card, now);
        if (effective != CardUrgency.Normal)
            bits.Append($" `{effective.ToString().ToLowerInvariant()}`");
        if (BoardService.NeedsHumanReview(card))
            bits.Append(" `review`");
        return bits.ToString();
    }

    private static void AppendLine(StringBuilder sb, string text)
    {
        sb.Append(text);
        sb.Append('\n');
    }

    private static string NormalizeNewlines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string WithSingleTrailingLf(string text)
    {
        var trimmed = text.TrimEnd('\n', '\r');
        return trimmed + "\n";
    }
}
