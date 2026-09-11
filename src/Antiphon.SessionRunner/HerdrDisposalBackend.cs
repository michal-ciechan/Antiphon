using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

internal sealed record HerdrDisposalPresence(bool OriginalAbsent, bool? ReplacementPresent);

internal interface IHerdrDisposalBackend
{
    Task<HerdrDisposalObservation> InspectAsync(string paneId, bool labels, CancellationToken cancellationToken);
    Task CloseAsync(string paneId, CancellationToken cancellationToken);
    Task<HerdrDisposalPresence> PresenceAsync(HerdrPaneDisposalPreview reviewed, CancellationToken cancellationToken);
}

internal sealed class HerdrDisposalBackend(HerdrClient client, SessionRunnerRuntime runtime,
    HerdrDisposalLocatorStore locators, IHerdrDisposalProcessInspector processes) : IHerdrDisposalBackend
{
    public async Task<HerdrDisposalObservation> InspectAsync(string paneId, bool labels, CancellationToken cancellationToken)
    {
        var backend = await client.ConnectAndValidateAsync(cancellationToken);
        if (backend.Protocol != 20 || backend.InstanceId is null)
            throw new HerdrLaunchException("The selected backend protocol or process identity cannot be verified.", HerdrPaneDisposalCodes.GuardUnavailable);
        var pane = await client.PaneGetAsync(paneId, cancellationToken);
        if (pane.PaneId != paneId || string.IsNullOrWhiteSpace(pane.TerminalId))
            throw new HerdrLaunchException("The selected pane identity cannot be verified.", HerdrProblemTypes.PaneChanged);
        string? wsLabel = null, tabLabel = null;
        bool? emptyTab = null;
        if (labels)
        {
            wsLabel = (await client.WorkspaceListAsync(cancellationToken)).SingleOrDefault(w => w.WorkspaceId == pane.WorkspaceId)?.Label;
            var tab = (await client.TabListAsync(pane.WorkspaceId, cancellationToken)).SingleOrDefault(t => t.TabId == pane.TabId);
            tabLabel = tab?.Label;
            emptyTab = tab is null ? null : tab.PaneCount == 1;
        }
        var agents = await client.AgentListAsync(cancellationToken);
        var captured = locators.Read(paneId);
        var claims = runtime.InspectHerdrDisposalClaims(paneId).Concat(captured.Claims).ToList();
        if (pane.Tokens?.TryGetValue("antiphon-session", out var token) == true
            && Guid.TryParseExact(token, "D", out var tokenId) && tokenId != Guid.Empty)
            claims.Add(new(tokenId, "antiphon-session-token", null, false));
        // Last read before returning: Herdr foreground and an independent OS descendant census.
        var foreground = await client.PaneProcessInfoAsync(paneId, cancellationToken);
        if (foreground.PaneId != paneId) throw new HerdrLaunchException("Process target changed.", HerdrProblemTypes.PaneChanged);
        var tree = processes.Inspect(foreground);
        return new(pane, backend, tree.Shell, tree.Foreground, tree.Affected, tree.Complete,
            pane.PendingInput == true, agents.Any(a => a.PaneId == paneId && a.LaunchPending),
            claims, captured.Files, captured.Complete, wsLabel, tabLabel, emptyTab);
    }

    public Task CloseAsync(string paneId, CancellationToken cancellationToken) => client.PaneCloseAsync(paneId, cancellationToken);

    public async Task<HerdrDisposalPresence> PresenceAsync(HerdrPaneDisposalPreview reviewed, CancellationToken cancellationToken)
    {
        var backend = await client.ConnectAndValidateAsync(cancellationToken);
        if (backend.InstanceId != reviewed.BackendInstanceId) return new(false, null);
        var panes = await client.PaneListAsync(null, cancellationToken);
        var replacement = panes.Any(p => p.PaneId == reviewed.PaneId && p.TerminalId != reviewed.TerminalId);
        if (panes.Any(p => p.TerminalId == reviewed.TerminalId)) return new(false, replacement);
        var affected = reviewed.AffectedProcesses;
        // A display-ID lookup miss alone is insufficient. All original process incarnations
        // must be positively gone, and the original terminal absent from the same daemon.
        var absent = affected is { Count: > 0 } && affected.All(p => processes.IsSameProcessAlive(p) == false);
        return new(absent, replacement);
    }
}
