using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner;

/// <summary>
/// Herdr-lane <see cref="ISessionChild"/> (CARD-0160 / CARD-0187). Creates/ensures a workspace,
/// allocates a quad-tab pane, types a launch script (never <c>agent.start</c>), polls
/// <c>pane.get.agent</c> for the expected kind, and persists ids in <see cref="HerdrPaneSidecar"/>.
/// Input passthrough: Enter → <c>pane.send_keys</c>; everything else → <c>pane.send_text</c>.
/// P3: never calls <c>tab.close</c> — herdr auto-removes empty tabs.
/// </summary>
internal sealed class HerdrPaneChild : ISessionChild
{
    private readonly HerdrClient _client;
    private readonly SessionRunnerSettings _settings;
    private readonly ILogger _logger;
    private readonly Func<IReadOnlyList<HerdrPaneAllocator.LivePane>> _liveAntiphonPanes;
    private readonly IProcessLivenessProbe _processLiveness;

    private Guid _sessionId;
    private string? _paneId;
    private HerdrPaneSidecar? _sidecar;
    private bool _exited;

    // CARD-0164: herdr's pane.revision measurably stays flat across real turns (0/3), so the
    // runner owns a content-delta counter — bump whenever stripped visible pane.read text differs
    // from the last observation. Folded into LastSequence alongside revision (Math.Max); nothing
    // may *require* revision to move. First observation establishes the baseline without a bump.
    private readonly object _contentGate = new();
    private long _contentSequence;
    private string? _lastVisibleText;

    public HerdrPaneChild(
        HerdrClient client,
        SessionRunnerSettings settings,
        ILogger logger,
        Func<IReadOnlyList<HerdrPaneAllocator.LivePane>> liveAntiphonPanes,
        IProcessLivenessProbe processLiveness)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
        _liveAntiphonPanes = liveAntiphonPanes;
        _processLiveness = processLiveness;
    }

    public event Action<ChildExit>? Exited;

    /// <summary>Pane id once launched/attached; null before launch.</summary>
    public string? PaneId => _paneId;

    /// <summary>Sidecar (workspace/tab/pane ids + ChildPid); null before launch.</summary>
    public HerdrPaneSidecar? Sidecar => _sidecar;

    /// <summary>Re-bind an already-running pane after runner restart (adoption). Does not agent.start.</summary>
    public Task AttachExistingAsync(HerdrPaneSidecar sidecar, CancellationToken ct)
    {
        _sessionId = sidecar.SessionId;
        _paneId = sidecar.PaneId;
        _sidecar = sidecar;
        return Task.CompletedTask;
    }

    /// <summary>
    /// CARD-0162: raise Exited(<paramref name="reason"/>) once after verification fails. Deletes
    /// the sidecar. Idempotent against MarkVanishedIfDead and repeated close events.
    /// </summary>
    public void RaiseVerifiedClosed(string reason = HerdrExitReasons.PaneClosed)
    {
        if (_exited) return;
        HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, _sessionId);
        RaiseExited(reason);
    }

    public async Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct)
    {
        if (request.Herdr is null)
            throw new ArgumentException("Herdr launch requires HerdrLaunchOptions.", nameof(request));

        _sessionId = request.SessionId;
        var opts = request.Herdr;
        var expectedKind = string.IsNullOrEmpty(opts.AgentKind)
            ? HerdrAgentKinds.Claude
            : opts.AgentKind;

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

        var shellPid = await RequirePowerShellShellAsync(paneId, ct);

        var scriptPath = HerdrLaunchScript.PathFor(_settings.SessionLogPath, request.SessionId);
        HerdrLaunchScript.Write(scriptPath, request.Exe, request.Args);

        var typed = HerdrLaunchScript.TypedCommand(scriptPath);
        await _client.PaneSendTextAsync(paneId, typed, ct);
        await _client.PaneSendKeysAsync(paneId, ["enter"], ct);

        await WaitForExpectedAgentAsync(paneId, expectedKind, ct);

        await TryApplyAgentNameAsync(paneId, opts.AgentSlug, ct);

        var launchedAt = DateTime.UtcNow;
        int? childPid = null;
        try
        {
            var proc = await _client.PaneProcessInfoAsync(paneId, ct);
            shellPid = proc.ShellPid ?? shellPid;
            childPid = proc.ForegroundProcesses?
                .Select(p => (int?)p.Pid)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(ex, "Herdr pane.process_info failed after launch for session {SessionId}", request.SessionId);
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
            AgentKind = expectedKind,
            UpdatedAtUtc = launchedAt,
        };
        _sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, request.SessionId));

        TryDeleteLaunchScript(scriptPath);

        return new ChildStarted(childPid, HostPid: null, launchedAt);
    }

    /// <summary>
    /// CARD-0161 / CARD-0164: <c>pane.get</c> for revision + agent_status, plus one
    /// <c>pane.read</c> (same params as <see cref="ReadScreenAsync"/>) so the content-delta
    /// counter can advance when herdr's own revision is sticky. Used by the single-session GET.
    /// </summary>
    public async Task<(long Revision, long ContentSequence, string? AgentStatus)> RefreshStatusAsync(
        CancellationToken ct)
    {
        if (_paneId is null)
            throw new InvalidOperationException("HerdrPaneChild has not been launched.");

        var pane = await _client.PaneGetAsync(_paneId, ct);
        var read = await _client.PaneReadAsync(_paneId, source: "visible", stripAnsi: true, lines: null, ct);
        var contentSequence = ObserveVisibleText(read.Text);
        return (pane.Revision, contentSequence, pane.AgentStatus);
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
            var unexpected = _sidecar is null
                ? null
                : proc.ForegroundProcesses?
                    .Where(p => _sidecar.ChildPid is not int child || p.Pid != child)
                    .Where(p => _sidecar.ShellPid is not int shell || p.Pid != shell)
                    .ToList();
            if (unexpected is { Count: > 0 })
            {
                var foreign = string.Join(",", unexpected.Select(p => p.Pid));
                // P8: herdr itself succeeds and kills whatever is in the pane. The refusal is
                // ours. Kill our named child by pid (positive identity) and leave the pane open.
                if (_sidecar?.ChildPid is int child
                    && _processLiveness.IsAlive(child, _sidecar.LaunchedAtUtc))
                {
                    KillPidBestEffort(child);
                }

                _logger.LogWarning(
                    "Herdr pane {PaneId} has unexpected foreground process(es) {Pids} — killed our child by pid and leaving pane open.",
                    _paneId, foreign);
                HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, _sessionId);
                RaiseExited(HerdrExitReasons.PaneLeftOpen);
                return true;
            }

            await _client.PaneCloseAsync(_paneId, ct);
            // P3: herdr auto-removes empty tabs — do not TabCloseAsync.
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(ex, "Herdr pane.close failed for {PaneId}", _paneId);
            RaiseExited(HerdrExitReasons.PaneClosed);
            return false;
        }

        HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, _sessionId);
        RaiseExited(HerdrExitReasons.PaneClosed);
        return true;
    }

    private static void KillPidBestEffort(int pid)
    {
        try
        {
            System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }

    public async Task<ChildScreen?> ReadScreenAsync(CancellationToken ct)
    {
        if (_paneId is null)
            return null;

        // Identical pane.read params to RefreshStatusAsync — path interleaving must not fabricate
        // a content delta (CARD-0164 decision 10).
        var read = await _client.PaneReadAsync(_paneId, source: "visible", stripAnsi: true, lines: null, ct);
        var contentSequence = ObserveVisibleText(read.Text);
        return new ChildScreen(read.Text, read.Revision, contentSequence);
    }

    /// <summary>
    /// CARD-0164: ordinal full-string compare against the last-seen visible text. First sighting
    /// sets the baseline without bumping (no idle false-advance). Differ → increment.
    /// </summary>
    private long ObserveVisibleText(string text)
    {
        lock (_contentGate)
        {
            if (_lastVisibleText is null)
            {
                _lastVisibleText = text;
                return _contentSequence;
            }

            if (!string.Equals(_lastVisibleText, text, StringComparison.Ordinal))
            {
                _lastVisibleText = text;
                _contentSequence++;
            }

            return _contentSequence;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<int?> RequirePowerShellShellAsync(string paneId, CancellationToken ct)
    {
        HerdrPaneProcessInfo proc;
        try
        {
            proc = await _client.PaneProcessInfoAsync(paneId, ct);
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            throw new HerdrLaunchException(
                $"pane shell could not be read ({ex.Message}); set herdr default_shell or use PtyHost");
        }

        if (proc.ShellPid is not int shellPid || shellPid <= 0)
        {
            throw new HerdrLaunchException(
                "pane shell is missing; set herdr default_shell or use PtyHost");
        }

        var name = _processLiveness.TryGetProcessName(shellPid);
        if (!IsPowerShellProcessName(name))
        {
            throw new HerdrLaunchException(
                $"pane shell '{name ?? $"pid {shellPid}"}' is not PowerShell; set herdr default_shell or use PtyHost");
        }

        return shellPid;
    }

    internal static bool IsPowerShellProcessName(string? name) =>
        string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "pwsh", StringComparison.OrdinalIgnoreCase);

    private async Task WaitForExpectedAgentAsync(string paneId, string expectedKind, CancellationToken ct)
    {
        var timeoutMs = Math.Max(1, _client.Settings.LaunchDetectTimeoutMs);
        var poll = TimeSpan.FromMilliseconds(250);
        var started = DateTime.UtcNow;
        string? detected = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var pane = await _client.PaneGetAsync(paneId, ct);
            detected = pane.Agent;
            if (string.Equals(detected, expectedKind, StringComparison.Ordinal))
                return;
            if (!string.IsNullOrEmpty(detected))
            {
                throw new HerdrLaunchException(
                    $"herdr detected '{detected}' where '{expectedKind}' was expected");
            }

            var elapsed = DateTime.UtcNow - started;
            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                throw new HerdrLaunchException(
                    $"herdr did not detect agent kind '{expectedKind}' within {timeoutMs}ms (last observed: none)");
            }

            var remaining = TimeSpan.FromMilliseconds(timeoutMs) - elapsed;
            var delay = remaining < poll ? remaining : poll;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }
    }

    private void TryDeleteLaunchScript(string scriptPath)
    {
        try
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete herdr launch script {Path}", scriptPath);
        }
    }

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

    /// <summary>
    /// CARD-0211: apply <paramref name="agentSlug"/> as the herdr agent name after detection.
    /// Never throws except on cancellation — a name is identity for the operator's convenience,
    /// and the launch has already succeeded by the time this runs.
    /// </summary>
    private async Task TryApplyAgentNameAsync(string paneId, string? agentSlug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentSlug))
        {
            _logger.LogDebug("herdr agent.list/rename skipped for pane {PaneId}: no AgentSlug", paneId);
            return;
        }

        var desired = SanitizeAgentName(agentSlug);
        IReadOnlyList<HerdrAgentInfo> live;
        try
        {
            live = await _client.AgentListAsync(ct);
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(
                ex,
                "herdr agent.list failed; not renaming pane {PaneId} — cannot prove '{Desired}' is free",
                paneId, desired);
            return;
        }

        var others = live.Where(a => a.PaneId != paneId && a.Name is not null).ToList();
        var heldNames = others.Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        var holder = others.FirstOrDefault(a => string.Equals(a.Name, desired, StringComparison.Ordinal));

        var name = desired;
        if (heldNames.Contains(name))
        {
            string? suffixed = null;
            for (var n = 2; n <= 9; n++)
            {
                var candidate = Suffix(desired, n);
                if (!heldNames.Contains(candidate))
                {
                    suffixed = candidate;
                    break;
                }
            }

            if (suffixed is null)
            {
                _logger.LogWarning(
                    "herdr agent name '{Desired}' is held by pane {HolderPaneId}; not renaming pane {PaneId} — no free suffix within 9 attempts",
                    desired, holder?.PaneId, paneId);
                return;
            }

            _logger.LogWarning(
                "herdr agent name '{Desired}' is held by pane {HolderPaneId}; renaming pane {PaneId} to '{Name}'",
                desired, holder?.PaneId, paneId, suffixed);
            name = suffixed;
        }

        try
        {
            await _client.AgentRenameAsync(paneId, name, ct);
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            var code = ex is HerdrApiException api ? api.Code : "unavailable";
            _logger.LogWarning(
                ex,
                "herdr agent.rename to '{Name}' refused ({Code}) for pane {PaneId}; agent stays unnamed",
                name, code, paneId);
            return;
        }

        if (string.Equals(name, desired, StringComparison.Ordinal))
        {
            _logger.LogInformation("herdr agent on pane {PaneId} named '{Name}'", paneId, name);
        }
        else
        {
            _logger.LogInformation(
                "herdr agent on pane {PaneId} named '{Name}' (from '{Desired}')",
                paneId, name, desired);
        }
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

    /// <summary>
    /// CARD-0211 D2: <c>UniqueSlugAsync</c> transposed to herdr's 32-char cap — trim the base
    /// to <c>32 - "-n".Length</c> before appending.
    /// </summary>
    internal static string Suffix(string desired, int n)
    {
        var suffix = $"-{n}";
        var budget = 32 - suffix.Length;
        var trimmed = desired.Length <= budget ? desired : desired[..budget];
        trimmed = trimmed.TrimEnd('-');
        if (trimmed.Length == 0 || !char.IsAsciiLetter(trimmed[0]))
            trimmed = "a" + trimmed;
        if (trimmed.Length > budget)
            trimmed = trimmed[..budget];
        return trimmed + suffix;
    }
}
