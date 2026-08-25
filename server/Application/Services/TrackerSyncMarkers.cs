using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0166: hidden HTML markers that make outbound artifacts structurally identifiable on IN.</summary>
public static partial class TrackerSyncMarkers
{
    public const string CommentPrefix = "<!-- antiphon:comment=";
    public const string SystemCommentPrefix = "<!-- antiphon:system-comment=";
    public const string CardPrefix = "<!-- antiphon:card=";

    public static string AppendCommentMarker(string body, Guid commentId) =>
        $"{body.TrimEnd()}\n\n{CommentPrefix}{commentId:N} -->";

    /// <summary>
    /// Marks a tracker comment generated from card state rather than a <see cref="Domain.Entities.CardComment"/>.
    /// The card is its durable identity, so an echo can be discarded without creating a synthetic comment row.
    /// </summary>
    public static string AppendSystemCommentMarker(string body, Guid cardId) =>
        $"{body.TrimEnd()}\n\n{SystemCommentPrefix}{cardId:N} -->";

    public static string AppendCardMarkerFooter(string description, Guid cardId, string identifier, string? boardLink)
    {
        var link = string.IsNullOrWhiteSpace(boardLink) ? identifier : $"{identifier} ({boardLink})";
        return $"{description.TrimEnd()}\n\n---\n{CardPrefix}{cardId:N} -->\n_Mirrored from Antiphon card {link}_";
    }

    public static bool TryReadTrailingCommentMarker(string body, out Guid commentId)
    {
        commentId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var match = TrailingCommentMarkerRegex().Match(body);
        if (!match.Success)
            return false;

        return Guid.TryParseExact(match.Groups[1].Value, "N", out commentId);
    }

    public static bool TryReadTrailingSystemCommentMarker(string body, out Guid cardId)
    {
        cardId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var match = TrailingSystemCommentMarkerRegex().Match(body);
        if (!match.Success)
            return false;

        return Guid.TryParseExact(match.Groups[1].Value, "N", out cardId);
    }

    public static bool TryReadCardMarker(string body, out Guid cardId)
    {
        cardId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var match = CardMarkerRegex().Match(body);
        if (!match.Success)
            return false;

        return Guid.TryParseExact(match.Groups[1].Value, "N", out cardId);
    }

    public static bool IsManagedLabel(string label)
    {
        var normalized = label.Trim().ToLowerInvariant();
        return normalized.StartsWith("status:", StringComparison.Ordinal)
            || normalized.StartsWith("priority:", StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> StripManagedLabels(IEnumerable<string> labels) =>
        labels
            .Where(l => !string.IsNullOrWhiteSpace(l) && !IsManagedLabel(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string StatusLabel(Domain.Enums.CardStatus status) =>
        "status:" + status switch
        {
            Domain.Enums.CardStatus.Backlog => "backlog",
            Domain.Enums.CardStatus.InProgress => "in-progress",
            Domain.Enums.CardStatus.Review => "review",
            Domain.Enums.CardStatus.Done => "done",
            Domain.Enums.CardStatus.Blocked => "blocked",
            Domain.Enums.CardStatus.Canceled => "canceled",
            _ => status.ToString().ToLowerInvariant()
        };

    public static string? PriorityLabel(int priority) =>
        priority == 0 ? null : $"priority:{priority}";

    [GeneratedRegex(@"<!--\s*antiphon:comment=([0-9a-fA-F]{32})\s*-->\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingCommentMarkerRegex();

    [GeneratedRegex(@"<!--\s*antiphon:system-comment=([0-9a-fA-F]{32})\s*-->\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSystemCommentMarkerRegex();

    [GeneratedRegex(@"<!--\s*antiphon:card=([0-9a-fA-F]{32})\s*-->", RegexOptions.CultureInvariant)]
    private static partial Regex CardMarkerRegex();
}
