using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner;

/// <summary>
/// Herdr-lane <see cref="ISessionChild"/> (CARD-0160). Creates/ensures a workspace, allocates a
/// quad-tab pane, starts claude via <c>agent.start</c>, and persists ids in <see cref="HerdrPaneSidecar"/>.
/// Input passthrough: Enter → <c>pane.send_keys</c>; everything else → <c>pane.send_text</c>.
/// P3: never calls <c>tab.close</c> — herdr auto-removes empty tabs.
/// </summary>
internal sealed class HerdrPaneChild : ISessionChild
{
    private readonly HerdrClient _client;
    private readonly SessionRunnerSettings _settings;
    private readonly ILogger _logger;
    private readonly Func<IReadOnlyList<HerdrPaneAllocator.LivePane>> _liveAntiphonPanes;

    private Guid _sessionId;
    private string? _paneId;
    private HerdrPaneSidecar? _sidecar;
    private bool _exited;

    public HerdrPaneChild(
        HerdrClient client,
        SessionRunnerSettings settings,
        ILogger logger,
        Func<IReadOnlyList<HerdrPaneAllocator.LivePane>> liveAntiphonPanes)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
        _liveAntiphonPanes = liveAntiphonPanes;
    }

    public event Action<ChildExit>? Exited;

    /// <summary>Re-bind an already-running pane after runner restart (adoption). Does not agent.start.</summary>
    public Task AttachExistingAsync(HerdrPaneSidecar sidecar, CancellationToken ct)
    {
        _sessionId = sidecar.SessionId;
        _paneId = sidecar.PaneId;
        _sidecar = sidecar;
        return Task.CompletedTask;
    }

    public async Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct)
    {
        if (request.Herdr is null)
            throw new ArgumentException("Herdr launch requires HerdrLaunchOptions.", nameof(request));

        _sessionId = request.SessionId;
        var opts = request.Herdr;

        if (!string.Equals(Path.GetFileNameWithoutExtension(request.Exe), "claude", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Exe, "claude", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Herdr lane: request.Exe={Exe} is not the stock claude launcher; herdr's agent manifest resolves the command (TUI-profile exe pinning does not carry into this lane).",
                request.Exe);
        }

        if (request.Cols != 120 || request.Rows != 30 || request.MemoryLimitMb != 0)
        {
            _logger.LogWarning(
                "Herdr lane ignores Cols/Rows/MemoryLimitMb (herdr owns layout; Job-object cap is pty-host machinery).");
        }

        await _client.ConnectAndValidateAsync(ct);

        var workspaceId = await EnsureWorkspaceAsync(opts, ct);
        var (tabId, paneId) = await AllocatePaneAsync(workspaceId, opts, request, ct);
        _paneId = paneId;

        await _client.PaneRenameAsync(paneId, opts.PaneTitle, ct);
        await _client.PaneReportMetadataAsync(
            paneId,
            new Dictionary<string, string?> { ["antiphon-session"] = request.SessionId.ToString("D") },
            title: opts.PaneTitle,
            ct);

        // agent.start name must match [a-z][a-z0-9_-]{0,31}
        var agentName = SanitizeAgentName(opts.PaneTitle);
        await _client.AgentStartAsync(
            agentName,
            HerdrAgentKinds.Claude,
            paneId,
            request.Args,
            timeoutMs: null,
            ct);

        var launchedAt = DateTime.UtcNow;
        int? childPid = null;
        int? shellPid = null;
        try
        {
            var proc = await _client.PaneProcessInfoAsync(paneId, ct);
            shellPid = proc.ShellPid;
            childPid = proc.ForegroundProcesses?
                .Select(p => (int?)p.Pid)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(ex, "Herdr pane.process_info failed after agent.start for session {SessionId}", request.SessionId);
        }

        _sidecar = new HerdrPaneSidecar
        {
            SessionId = request.SessionId,
            WorkspaceKey = opts.WorkspaceKey,
            WorkspaceId = workspaceId,
            TabId = tabId,
            PaneId = paneId,
            ChildPid = childPid,
            ShellPid = shellPid,
            LaunchedAtUtc = launchedAt,
            Cwd = request.Cwd,
            UpdatedAtUtc = launchedAt,
        };
        _sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, request.SessionId));

        return new ChildStarted(childPid, HostPid: null, launchedAt);
    }

    public async Task WriteAsync(string input, CancellationToken ct)
    {
        if (_paneId is null)
            throw new InvalidOperationException("HerdrPaneChild has not been launched.");

        if (input is "\r" or "\n")
            await _client.PaneSendKeysAsync(_paneId, ["enter"], ct);
        else
            await _client.PaneSendTextAsync(_paneId, input, ct);
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken ct)
    {
        _logger.LogDebug("HerdrPaneChild.ResizeAsync({Cols},{Rows}) is a no-op — herdr owns layout.", cols, rows);
        return Task.CompletedTask;
    }

    public async Task<bool> KillAsync(CancellationToken ct)
    {
        if (_paneId is null)
            return false;

        try
        {
            var proc = await _client.PaneProcessInfoAsync(_paneId, ct);
            var unexpected = proc.ForegroundProcesses?
                .Where(p => _sidecar?.ChildPid is not int child || p.Pid != child)
                .Where(p => _sidecar?.ShellPid is not int shell || p.Pid != shell)
                .ToList();
            if (unexpected is { Count: > 0 })
            {
                _logger.LogWarning(
                    "Herdr pane {PaneId} has unexpected foreground process(es) {Pids} — leaving pane open.",
                    _paneId, string.Join(",", unexpected.Select(p => p.Pid)));
                RaiseExited("HerdrPaneClosed");
                return false;
            }

            await _client.PaneCloseAsync(_paneId, ct);
            // P3: herdr auto-removes empty tabs — do not TabCloseAsync.
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(ex, "Herdr pane.close failed for {PaneId}", _paneId);
            RaiseExited("HerdrPaneClosed");
            return false;
        }

        HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, _sessionId);
        RaiseExited("HerdrPaneClosed");
        return true;
    }

    public async Task<ChildScreen?> ReadScreenAsync(CancellationToken ct)
    {
        if (_paneId is null)
            return null;

        var read = await _client.PaneReadAsync(_paneId, source: "visible", stripAnsi: true, lines: null, ct);
        return new ChildScreen(read.Text, read.Revision);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<string> EnsureWorkspaceAsync(HerdrLaunchOptions opts, CancellationToken ct)
    {
        var listed = await _client.WorkspaceListAsync(ct);
        var match = listed.FirstOrDefault(w =>
            w.Tokens is not null
            && w.Tokens.TryGetValue("antiphon-ws", out var key)
            && string.Equals(key, opts.WorkspaceKey, StringComparison.Ordinal));
        match ??= listed.FirstOrDefault(w =>
            string.Equals(w.Label, opts.WorkspaceLabel, StringComparison.Ordinal)
            && (opts.WorkspaceCwd is null
                || string.Equals(w.Tokens?.GetValueOrDefault("cwd"), opts.WorkspaceCwd, StringComparison.OrdinalIgnoreCase)));

        string workspaceId;
        if (match is not null)
        {
            workspaceId = match.WorkspaceId;
        }
        else
        {
            var created = await _client.WorkspaceCreateAsync(opts.WorkspaceCwd, opts.WorkspaceLabel, ct);
            workspaceId = created.WorkspaceId;
        }

        // Re-report every launch to refresh any TTL (best-effort identity; sidecar is authoritative).
        await _client.WorkspaceReportMetadataAsync(
            workspaceId,
            new Dictionary<string, string?> { ["antiphon-ws"] = opts.WorkspaceKey },
            ct);
        return workspaceId;
    }

    private async Task<(string TabId, string PaneId)> AllocatePaneAsync(
        string workspaceId,
        HerdrLaunchOptions opts,
        RunnerLaunchRequest request,
        CancellationToken ct)
    {
        var live = _liveAntiphonPanes()
            .Where(p =>
            {
                // Only panes still present in herdr count.
                return true;
            })
            .ToList();

        // Verify against pane.list — drop sidecars whose panes are gone.
        IReadOnlyList<HerdrPaneInfo> paneList;
        try
        {
            paneList = await _client.PaneListAsync(workspaceId, ct);
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(ex, "pane.list failed for workspace {WorkspaceId}; allocating as empty", workspaceId);
            paneList = [];
        }

        var liveIds = paneList.Select(p => p.PaneId).ToHashSet(StringComparer.Ordinal);
        var verified = live.Where(p => liveIds.Contains(p.PaneId)).ToList();

        var decision = HerdrPaneAllocator.Allocate(verified);
        switch (decision)
        {
            case HerdrPaneAllocator.CreateTab:
            {
                var created = await _client.TabCreateAsync(
                    workspaceId, request.Cwd, request.Env, opts.PaneTitle, ct);
                return (created.TabId, created.InitialPaneId);
            }
            case HerdrPaneAllocator.Split split:
            {
                var pane = await _client.PaneSplitAsync(
                    split.TargetPaneId, split.Direction, split.Ratio, request.Cwd, request.Env, ct);
                return (pane.TabId, pane.PaneId);
            }
            default:
                throw new InvalidOperationException($"Unknown allocator decision {decision.GetType().Name}");
        }
    }

    private void RaiseExited(string reason)
    {
        if (_exited) return;
        _exited = true;
        Exited?.Invoke(new ChildExit(ExitCode: null, reason));
    }

    /// <summary>Herdr agent names: <c>[a-z][a-z0-9_-]{0,31}</c>.</summary>
    internal static string SanitizeAgentName(string title)
    {
        var chars = title.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '-')
            .ToArray();
        var s = new string(chars).Trim('-');
        if (s.Length == 0 || !char.IsAsciiLetter(s[0]))
            s = "a" + s;
        if (s.Length > 32)
            s = s[..32];
        return s;
    }
}
