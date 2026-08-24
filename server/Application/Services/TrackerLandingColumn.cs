using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Which column a tracker sync may put a card in, per board (CARD-0170).</summary>
public enum TrackerImportColumn
{
    /// <summary>
    /// Default. Imports and cursor-proven reopens land where a manual create lands — the board's
    /// <see cref="CardStatus.Backlog"/> column — and the tracker owns only the terminal boundary.
    /// </summary>
    Backlog = 0,

    /// <summary>
    /// The original E10 behaviour, kept as an opt-in: the tracker IS this board's queue, so an
    /// open issue belongs in the first active column and tracker state owns the non-terminal
    /// column on every sync.
    /// </summary>
    Active = 1
}

/// <summary>
/// Resolves the column an external-tracker sync lands a card in.
/// </summary>
/// <remarks>
/// Before CARD-0170 all three push sites — create in <c>UpsertIssuesAsync</c>, the
/// <c>shouldMoveForTrackerState</c> drag in <c>UpdateExisting</c>, and
/// <c>TrackerBidirectionalSyncService.ApplyExternalReopens</c> — independently resolved "first
/// <c>IsActive &amp;&amp; !IsTerminal</c>". That is a mis-reading of <see cref="BoardColumn.IsActive"/>,
/// which means "auto-dispatch MAY start an agent here" and never meant "new work lands here": on
/// every default-shaped board in this deployment it made "an issue is open on GitHub" equivalent
/// to "start an agent on it", re-opening the CARD-0087 hole through the sync. Worse, fixing only
/// the create site leaves <c>UpdateExisting</c> dragging every unowned non-terminal card back on
/// the next tick — measured live at 15:07:25 on all eleven imported cards.
///
/// <para>Intake is <see cref="CardStatus.Backlog"/>, the rule <c>CardService.CreateAsync</c> has
/// used since the board model existed. A board whose tracker genuinely is its queue opts back in
/// with <c>tracker.import_column: active</c>.</para>
/// </remarks>
public static class TrackerLandingColumn
{
    /// <summary>The <c>tracker:</c> key that selects the mode.</summary>
    public const string OptionKey = "import_column";

    public static bool TryParseMode(string? raw, out TrackerImportColumn mode)
    {
        mode = TrackerImportColumn.Backlog;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "backlog":
                mode = TrackerImportColumn.Backlog;
                return true;
            case "active":
                mode = TrackerImportColumn.Active;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The board's mode. An unrecognised value is rejected on workflow SAVE
    /// (<c>WorkflowDefinitionLoader</c>), so reaching here it can only be a row written before that
    /// validation existed — treated as the default rather than failing the whole sync.
    /// </summary>
    public static TrackerImportColumn ModeFor(IssueTrackerConfig config)
    {
        config.Options.TryGetValue(OptionKey, out var raw);
        return TryParseMode(raw, out var mode) ? mode : TrackerImportColumn.Backlog;
    }

    /// <summary>
    /// Where a create or a reopen lands. <see cref="TrackerImportColumn.Backlog"/>: the first
    /// <see cref="CardStatus.Backlog"/> column, else the first non-active non-terminal column, else
    /// the first column. <see cref="TrackerImportColumn.Active"/>: the first active non-terminal
    /// column, else the first column — byte-for-byte the pre-CARD-0170 rule.
    /// </summary>
    public static BoardColumn? Resolve(Board board, TrackerImportColumn mode)
    {
        var ordered = board.Columns.OrderBy(c => c.ColumnOrder).ToList();
        if (mode == TrackerImportColumn.Active)
        {
            return ordered.FirstOrDefault(c => c.IsActive && !c.IsTerminal)
                ?? ordered.FirstOrDefault();
        }

        return ordered.FirstOrDefault(c => c.CardStatus == CardStatus.Backlog)
            ?? ordered.FirstOrDefault(c => !c.IsActive && !c.IsTerminal)
            ?? ordered.FirstOrDefault();
    }

    public static BoardColumn? Resolve(Board board, IssueTrackerConfig config) =>
        Resolve(board, ModeFor(config));
}
