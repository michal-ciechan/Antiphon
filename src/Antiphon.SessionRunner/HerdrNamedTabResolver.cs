using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0384: read-only named-tab selection and occupant classification. Mutation lives in
/// <see cref="HerdrPaneChild"/>. The comparer is explicit so unit tests pin both Windows
/// ignore-case and ordinal without depending on the host OS.
/// </summary>
internal sealed class HerdrNamedTabResolver
{
    public const string ActionCreate = "create";
    public const string ActionRelaunch = "relaunch";
    public const string ActionAdopt = "adopt";

    private readonly StringComparer _labels;

    public HerdrNamedTabResolver(StringComparer labelComparer)
    {
        _labels = labelComparer ?? throw new ArgumentNullException(nameof(labelComparer));
    }

    public static StringComparer HostLabelComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public IReadOnlyList<HerdrTabInfo> MatchTabs(
        IReadOnlyList<HerdrTabInfo> tabs,
        string workspaceId,
        string tabLabel)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabLabel);

        return tabs
            .Where(t =>
                string.Equals(t.WorkspaceId, workspaceId, StringComparison.Ordinal)
                && _labels.Equals(t.Label, tabLabel))
            .ToList();
    }

    /// <summary>
    /// Null means no match (caller may create). Throws
    /// <see cref="HerdrProblemTypes.TabAmbiguous"/> / <see cref="HerdrProblemTypes.TabInvalid"/>.
    /// </summary>
    public NamedTabPick? PickUniqueSinglePaneTab(
        IReadOnlyList<HerdrTabInfo> tabs,
        IReadOnlyList<HerdrPaneInfo> panes,
        string workspaceId,
        string tabLabel)
    {
        ArgumentNullException.ThrowIfNull(panes);
        var matches = MatchTabs(tabs, workspaceId, tabLabel);
        if (matches.Count == 0)
            return null;
        if (matches.Count > 1)
        {
            var listing = string.Join(", ", matches.Select(t => $"{t.TabId} ({t.Label})"));
            throw new HerdrLaunchException(
                $"tab label '{tabLabel}' is ambiguous in workspace {workspaceId}: {listing}",
                HerdrProblemTypes.TabAmbiguous);
        }

        var tab = matches[0];
        var tabPanes = panes
            .Where(p =>
                string.Equals(p.TabId, tab.TabId, StringComparison.Ordinal)
                && string.Equals(p.WorkspaceId, workspaceId, StringComparison.Ordinal))
            .ToList();
        if (tab.PaneCount != 1 || tabPanes.Count != 1)
        {
            throw new HerdrLaunchException(
                $"tab {tab.TabId} ('{tab.Label}') is not a dedicated single pane (reported {tab.PaneCount}, enumerated {tabPanes.Count})",
                HerdrProblemTypes.TabInvalid);
        }

        return new NamedTabPick(tab, tabPanes[0]);
    }

    public async Task<NamedTabPick?> TryPickAsync(
        string workspaceId,
        string tabLabel,
        Func<CancellationToken, Task<IReadOnlyList<HerdrTabInfo>>> listTabs,
        Func<CancellationToken, Task<IReadOnlyList<HerdrPaneInfo>>> listPanes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(listTabs);
        ArgumentNullException.ThrowIfNull(listPanes);
        var tabs = await listTabs(ct);
        var panes = await listPanes(ct);
        return PickUniqueSinglePaneTab(tabs, panes, workspaceId, tabLabel);
    }

    public NamedOccupant Classify(
        HerdrPaneInfo pane,
        HerdrPaneProcessInfo proc,
        string expectedKind,
        Guid sessionId,
        string? shellName)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(proc);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKind);

        var nonShell = (proc.ForegroundProcesses ?? [])
            .Where(p => proc.ShellPid is not int shell || p.Pid != shell)
            .ToList();

        if (nonShell.Count == 0)
        {
            if (proc.ShellPid is not int shellPid || shellPid <= 0
                || !HerdrPaneChild.IsPowerShellProcessName(shellName))
            {
                return NamedOccupant.Occupied(
                    pane.PaneId,
                    $"pane {pane.PaneId} has no live PowerShell shell");
            }

            return NamedOccupant.Idle(pane.WorkspaceId, pane.TabId, pane.PaneId, shellPid);
        }

        if (string.Equals(pane.Agent, expectedKind, StringComparison.Ordinal)
            && nonShell.Count == 1
            && HerdrPaneChild.TryReadNativeSessionId(nonShell[0].Argv, out var native)
            && native == sessionId)
        {
            return NamedOccupant.Adopt(
                pane.WorkspaceId, pane.TabId, pane.PaneId, nonShell[0], proc.ShellPid);
        }

        var occupant = nonShell[0];
        var nativeText = HerdrPaneChild.TryReadNativeSessionId(occupant.Argv, out var foreignId)
            ? foreignId.ToString("D")
            : "no --session-id";
        return NamedOccupant.Occupied(
            pane.PaneId,
            $"pane {pane.PaneId} is occupied by {occupant.Name} pid {occupant.Pid} ({nativeText}); not stolen");
    }
}

internal sealed record NamedTabPick(HerdrTabInfo Tab, HerdrPaneInfo Pane);

internal enum NamedOccupantKind { IdlePowerShell, Adopt, Occupied }

internal sealed record NamedOccupant(
    NamedOccupantKind Kind,
    string WorkspaceId,
    string TabId,
    string PaneId,
    HerdrPaneProcess? Occupant = null,
    int? ShellPid = null,
    string? OccupiedDetail = null)
{
    public static NamedOccupant Idle(string workspaceId, string tabId, string paneId, int? shellPid) =>
        new(NamedOccupantKind.IdlePowerShell, workspaceId, tabId, paneId, ShellPid: shellPid);

    public static NamedOccupant Adopt(
        string workspaceId, string tabId, string paneId, HerdrPaneProcess occupant, int? shellPid) =>
        new(NamedOccupantKind.Adopt, workspaceId, tabId, paneId, occupant, shellPid);

    public static NamedOccupant Occupied(string paneId, string detail) =>
        new(NamedOccupantKind.Occupied, "", "", paneId, OccupiedDetail: detail);
}
