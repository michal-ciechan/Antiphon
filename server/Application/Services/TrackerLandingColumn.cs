using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Where tracker-imported cards land. Default is the board's Backlog column — the same rule
/// <c>CardService.CreateAsync</c> uses. <c>tracker.import_column: active</c> restores the E10
/// "tracker is the queue" behaviour (first <c>IsActive &amp;&amp; !IsTerminal</c>).
/// </summary>
public static class TrackerLandingColumn
{
    public const string OptionKey = "import_column";
    public const string BacklogValue = "backlog";
    public const string ActiveValue = "active";

    public static bool TrackerOwnsNonTerminalColumn(IssueTrackerConfig? config)
    {
        if (config?.Options is null)
            return false;
        return config.Options.TryGetValue(OptionKey, out var raw)
            && string.Equals(raw.Trim(), ActiveValue, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidImportColumn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var trimmed = value.Trim();
        return trimmed.Equals(BacklogValue, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(ActiveValue, StringComparison.OrdinalIgnoreCase);
    }

    public static BoardColumn? Resolve(Board board, IssueTrackerConfig? config)
    {
        var columns = board.Columns.OrderBy(c => c.ColumnOrder).ToList();
        if (columns.Count == 0)
            return null;

        if (TrackerOwnsNonTerminalColumn(config))
        {
            return columns.FirstOrDefault(c => c.IsActive && !c.IsTerminal)
                ?? columns[0];
        }

        return columns.FirstOrDefault(c => c.CardStatus == CardStatus.Backlog)
            ?? columns.FirstOrDefault(c => !c.IsActive && !c.IsTerminal)
            ?? columns[0];
    }
}
