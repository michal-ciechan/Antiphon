using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Read seam; tests can vary one typed predicate independently of wire validation.</summary>
internal interface IHerdrLabelReader
{
    Task<HerdrPaneInfo> PaneGetAsync(string paneId, CancellationToken ct);
    Task<HerdrPaneProcessInfo> PaneProcessInfoAsync(string paneId, CancellationToken ct);
    Task<HerdrTabInfo> TabGetAsync(string tabId, CancellationToken ct);
    Task<HerdrWorkspaceInfo> WorkspaceGetAsync(string workspaceId, CancellationToken ct);
    Task<IReadOnlyList<HerdrTabInfo>> TabListAsync(string workspaceId, CancellationToken ct);
    Task<IReadOnlyList<HerdrPaneInfo>> PaneListAsync(string? workspaceId, CancellationToken ct);
    Task<IReadOnlyList<HerdrWorkspaceInfo>> WorkspaceListAsync(CancellationToken ct);
}

internal sealed record HerdrLabelCandidate(string ResultCode, string? TabLabel = null, string? WorkspaceLabel = null);

/// <summary>Read-only same-binding validation. Admission, persistence and lifecycle live in the child.</summary>
internal sealed class HerdrLabelObserver(IHerdrLabelReader reader, StringComparer comparer)
{
    internal static bool Eligible(HerdrPaneSidecar sidecar) =>
        sidecar.Origin == HerdrPaneOrigins.Launched && !sidecar.LaunchPending
        && sidecar.AcceptedStartedAt is not null
        && sidecar.LabelFollow is { Version: 1, Intent: { Version: 1 } intent }
        && intent.StandingAgentId != Guid.Empty
        && (intent.TabLabel is not null || intent.WorkspaceLabel is not null);

    internal static bool RepresentsExactly(string? label) => !string.IsNullOrWhiteSpace(label)
        && label.Length <= 256 && !label.Any(char.IsControl)
        && string.Equals(label, label.Trim(), StringComparison.Ordinal);

    internal static bool SamePane(HerdrPaneSidecar binding, HerdrPaneInfo pane) =>
        pane.PaneId == binding.PaneId && pane.TabId == binding.TabId && pane.WorkspaceId == binding.WorkspaceId;

    internal static string? WorkspaceToken(HerdrWorkspaceInfo workspace) =>
        workspace.Tokens?.GetValueOrDefault("antiphon-ws");

    internal async Task<HerdrLabelCandidate> CollectAsync(HerdrPaneSidecar binding, CancellationToken ct)
    {
        if (!Eligible(binding)) return new("ineligible");
        try
        {
            var pane = await reader.PaneGetAsync(binding.PaneId, ct);
            if (!SamePane(binding, pane)) return new("binding_changed");
            var process = await reader.PaneProcessInfoAsync(binding.PaneId, ct);
            if (binding.ChildPid is not > 0 || process.PaneId != binding.PaneId
                || process.ForegroundProcesses?.Any(p => p.Pid == binding.ChildPid) != true)
                return new("child_unverified");
            var tab = await reader.TabGetAsync(binding.TabId, ct);
            var workspace = await reader.WorkspaceGetAsync(binding.WorkspaceId, ct);
            if (tab.TabId != binding.TabId || tab.WorkspaceId != binding.WorkspaceId
                || workspace.WorkspaceId != binding.WorkspaceId) return new("binding_changed");
            if (!RepresentsExactly(tab.Label) || !RepresentsExactly(workspace.Label)) return new("label_invalid");

            var tabs = await reader.TabListAsync(binding.WorkspaceId, ct);
            var panes = await reader.PaneListAsync(binding.WorkspaceId, ct);
            var selected = new HerdrNamedTabResolver(comparer).PickUniqueSinglePaneTab(tabs, panes, binding.WorkspaceId, tab.Label);
            if (selected is null) return new("tab_missing");
            if (selected.Tab.TabId != binding.TabId || selected.Pane.PaneId != binding.PaneId) return new("binding_changed");
            if (selected.Tab.Label != tab.Label || selected.Tab.PaneCount != tab.PaneCount) return new("read_changed");

            var workspaces = await reader.WorkspaceListAsync(ct);
            var listed = workspaces.SingleOrDefault(w => w.WorkspaceId == binding.WorkspaceId);
            if (listed is null || listed.Label != workspace.Label || WorkspaceToken(listed) != WorkspaceToken(workspace))
                return new("read_changed");

            var intent = binding.LabelFollow!.Intent;
            string? workspaceLabel = null;
            if (intent.WorkspaceLabel is not null
                && binding.LabelFollow.WorkspaceSelection == HerdrWorkspaceSelection.UniqueUntaggedLabel
                && string.IsNullOrWhiteSpace(WorkspaceToken(workspace)))
            {
                var matching = workspaces.Where(w => string.Equals(w.Label, workspace.Label, StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(WorkspaceToken(w))).ToArray();
                if (matching.Length != 1 || matching[0].WorkspaceId != binding.WorkspaceId
                    || workspaces.Any(w => WorkspaceToken(w) == binding.WorkspaceKey))
                    return new("workspace_ambiguous");
                workspaceLabel = workspace.Label;
            }

            var finalPane = await reader.PaneGetAsync(binding.PaneId, ct);
            var finalTab = await reader.TabGetAsync(binding.TabId, ct);
            var finalWorkspace = await reader.WorkspaceGetAsync(binding.WorkspaceId, ct);
            if (!SamePane(binding, finalPane) || finalTab.TabId != tab.TabId || finalTab.WorkspaceId != tab.WorkspaceId
                || finalTab.Label != tab.Label || finalTab.PaneCount != tab.PaneCount
                || finalWorkspace.WorkspaceId != workspace.WorkspaceId || finalWorkspace.Label != workspace.Label
                || WorkspaceToken(finalWorkspace) != WorkspaceToken(workspace)) return new("read_changed");
            return new("validated", intent.TabLabel is null ? null : tab.Label, workspaceLabel);
        }
        catch (HerdrLaunchException ex) { return new(ex.Code ?? "read_failed"); }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException or HerdrProtocolException
                                     or IOException or System.Text.Json.JsonException)
        { return new("read_failed"); }
    }
}

public static class HerdrWorkspaceSelection
{
    public const string ManagedToken = "managed-token";
    public const string UniqueUntaggedLabel = "unique-untagged-label";
}

/// <summary>Versioned durable attempt state; absent legacy state never infers launch intent.</summary>
public sealed record HerdrLabelFollowState(
    int Version,
    HerdrLabelFollowIntent Intent,
    string WorkspaceSelection,
    long Sequence = 0,
    DateTime? LastAttemptAtUtc = null,
    DateTime? NextDueAtUtc = null,
    HerdrLabelObservation? Observation = null,
    bool LastPaneRepairPending = false);
