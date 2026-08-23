namespace Antiphon.SessionRunner;

/// <summary>
/// Pure, deterministic quad-tab allocator for Antiphon-created herdr panes (CARD-0160 §4).
/// Within a workspace, fill the lowest-numbered Antiphon tab with a free slot (&lt; 4 live
/// Antiphon panes); none free → create a new tab. Operator tabs (no Antiphon panes) are never
/// split into. Gaps from stopped panes are left (no reflow) and refilled by the next launch.
/// </summary>
public static class HerdrPaneAllocator
{
    public const int MaxPanesPerTab = 4;

    /// <summary>A live Antiphon pane already recorded in a sidecar and verified against pane.list.</summary>
    public sealed record LivePane(Guid SessionId, string TabId, string PaneId, int TabNumber);

    public abstract record Decision;

    /// <summary>No Antiphon tab with a free slot — create a new tab (its initial pane is pane #1).</summary>
    public sealed record CreateTab : Decision;

    /// <summary>Split an existing Antiphon pane to grow toward a 2×2.</summary>
    public sealed record Split(string TargetPaneId, string Direction, double Ratio) : Decision;

    /// <summary>
    /// Pick the next placement given the workspace's live Antiphon panes (already filtered to
    /// this WorkspaceKey and verified against pane.list). Tabs are ordered by TabNumber ascending.
    /// </summary>
    public static Decision Allocate(IReadOnlyList<LivePane> liveAntiphonPanes)
    {
        var byTab = liveAntiphonPanes
            .GroupBy(p => p.TabId, StringComparer.Ordinal)
            .Select(g => new
            {
                TabId = g.Key,
                TabNumber = g.Min(p => p.TabNumber),
                Panes = g.OrderBy(p => p.PaneId, StringComparer.Ordinal).ToList(),
            })
            .OrderBy(t => t.TabNumber)
            .ThenBy(t => t.TabId, StringComparer.Ordinal)
            .ToList();

        var target = byTab.FirstOrDefault(t => t.Panes.Count < MaxPanesPerTab);
        if (target is null)
            return new CreateTab();

        var count = target.Panes.Count;
        return count switch
        {
            // First Antiphon pane in the tab → split it right at 0.5.
            1 => new Split(target.Panes[0].PaneId, "right", 0.5),
            // Two panes → split the FIRST pane down at 0.5 (converge on 2×2).
            2 => new Split(target.Panes[0].PaneId, "down", 0.5),
            // Three panes → split the remaining un-split pane down at 0.5.
            // After a gap-refill the "remaining" pane is whichever is not the geometric twin of
            // the first; we approximate with the last pane in stable PaneId order.
            3 => new Split(target.Panes[^1].PaneId, "down", 0.5),
            _ => new CreateTab(), // unreachable: count < 4 and not 1/2/3
        };
    }
}
