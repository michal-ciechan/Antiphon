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

    /// <summary>CARD-0213: read-only pane snapshot. Nothing written, typed, or renamed.</summary>
    public async Task<HerdrPaneInspectDto> InspectAsync(string paneId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        var inspected = await InspectForegroundAsync(paneId, ct);
        var foreground = inspected.NonShell
            .Select(p => new HerdrForegroundProcessDto(
                p.Pid,
                p.Name,
                p.Argv,
                p.Cwd,
                _processLiveness.TryGetStartTimeUtc(p.Pid)))
            .ToList();
        var shellName = inspected.Process.ShellPid is int shell
            ? _processLiveness.TryGetProcessName(shell)
            : null;
        return new HerdrPaneInspectDto(
            inspected.Pane.PaneId,
            inspected.Pane.WorkspaceId,
            inspected.Pane.TabId,
            inspected.Pane.Label,
            inspected.Pane.Title,
            inspected.Pane.Agent,
            inspected.Pane.AgentStatus,
            inspected.Process.ShellPid,
            shellName,
            foreground,
            inspected.NativeSessionId,
            inspected.NativeSessionSource,
            BoundToSessionId: null,
            BoundOrigin: null);
    }

    /// <summary>
    /// CARD-0213: bind this child to an operator pane. Re-runs every inspect check. Writes an
    /// attached-origin sidecar; never types, and never renames unless the request carries
    /// <see cref="HerdrAttachRequest.PaneTitle"/> / <see cref="HerdrAttachRequest.AgentSlug"/>.
    /// </summary>
    public async Task<HerdrAttachResult> AttachAsync(HerdrAttachRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PaneId);
        if (request.SessionId == Guid.Empty)
            throw new ArgumentException("SessionId must not be empty.", nameof(request));

        var expectedKind = string.IsNullOrEmpty(request.ExpectedKind)
            ? HerdrAgentKinds.Claude
            : request.ExpectedKind;
        var inspected = await InspectForegroundAsync(request.PaneId, ct);
        var pane = inspected.Pane;

        if (string.IsNullOrEmpty(pane.Agent))
        {
            throw new HerdrLaunchException(
                $"pane {request.PaneId} has no detected agent",
                HerdrProblemTypes.PaneUnoccupied);
        }

        if (!string.Equals(pane.Agent, expectedKind, StringComparison.Ordinal))
        {
            throw new HerdrLaunchException(
                $"pane {request.PaneId} is '{pane.Agent}' where '{expectedKind}' was expected",
                HerdrProblemTypes.KindMismatch);
        }

        if (inspected.Occupant is null
            || inspected.NonShell.Count != 1
            || !HerdrAgentKinds.IsFamilyMember(expectedKind, inspected.Occupant.Name))
        {
            var listed = inspected.NonShell.Count == 0
                ? "none"
                : string.Join(", ", inspected.NonShell.Select(p => $"{p.Name} pid {p.Pid}"));
            throw new HerdrLaunchException(
                $"pane {request.PaneId} foreground is not a single {expectedKind} process ({listed})",
                HerdrProblemTypes.PaneForeign);
        }

        var occupant = inspected.Occupant;
        if (occupant.Pid != request.ExpectedChildPid)
        {
            throw new HerdrLaunchException(
                $"pane {request.PaneId} pid {occupant.Pid} != expected {request.ExpectedChildPid}",
                HerdrProblemTypes.PaneChanged);
        }

        if (request.ExpectedNativeSessionId is Guid expectedNative
            && inspected.NativeSessionId != expectedNative)
        {
            throw new HerdrLaunchException(
                $"pane {request.PaneId} native session id {inspected.NativeSessionId?.ToString("D") ?? "none"} != expected {expectedNative:D}",
                HerdrProblemTypes.PaneChanged);
        }

        var isGrok = string.Equals(expectedKind, HerdrAgentKinds.Grok, StringComparison.Ordinal)
            || string.Equals(request.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase);
        if (isGrok && inspected.NativeSessionId is null)
        {
            throw new HerdrLaunchException(
                $"pane {request.PaneId} grok has no --session-id in argv and agent_session did not name one; relaunch with --session-id",
                HerdrProblemTypes.NativeIdUnknown);
        }

        string? grokUpdatesPath = null;
        string? grokEncodedCwd = null;
        if (isGrok)
        {
            var grokHome = GrokTranscriptTailer.ResolveGrokHome();
            var located = GrokTranscriptTailer.TryLocateSessionDirectory(grokHome, inspected.NativeSessionId!.Value);
            if (located is null)
            {
                throw new HerdrLaunchException(
                    $"no grok session directory for {inspected.NativeSessionId:D} under {Path.Combine(grokHome, "sessions")}",
                    HerdrProblemTypes.TranscriptNotFound);
            }

            grokUpdatesPath = Path.Combine(located, "updates.jsonl");
            grokEncodedCwd = GrokTranscriptTailer.EncodedCwdOf(located);
            if (occupant.Cwd is { } processCwd && grokEncodedCwd is not null)
            {
                string decoded;
                try { decoded = Uri.UnescapeDataString(grokEncodedCwd); }
                catch (UriFormatException) { decoded = grokEncodedCwd; }

                string processFull;
                try { processFull = Path.GetFullPath(processCwd); }
                catch (Exception) { processFull = processCwd; }

                if (!string.Equals(decoded, processFull, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Grok session directory cwd encoding {Encoded} decodes to {Decoded} which differs from process cwd {Cwd} for session {SessionId}",
                        grokEncodedCwd, decoded, processFull, request.SessionId);
                }
            }
        }

        _sessionId = request.SessionId;
        _paneId = request.PaneId;

        try
        {
            await _client.PaneReportMetadataAsync(
                request.PaneId,
                new Dictionary<string, string?> { ["antiphon-session"] = request.SessionId.ToString("D") },
                title: request.PaneTitle,
                ct);
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(ex, "pane.report_metadata failed during attach of {PaneId}; sidecar is authoritative", request.PaneId);
        }

        if (!string.IsNullOrWhiteSpace(request.PaneTitle))
            await _client.PaneRenameAsync(request.PaneId, request.PaneTitle, ct);
        if (!string.IsNullOrWhiteSpace(request.AgentSlug))
            await TryApplyAgentNameAsync(request.PaneId, request.AgentSlug, ct);

        var startUtc = _processLiveness.TryGetStartTimeUtc(occupant.Pid) ?? DateTime.UtcNow;
        _sidecar = new HerdrPaneSidecar
        {
            SessionId = request.SessionId,
            WorkspaceKey = string.IsNullOrWhiteSpace(request.WorkspaceKey) ? "none" : request.WorkspaceKey,
            WorkspaceId = pane.WorkspaceId,
            TabId = pane.TabId,
            PaneId = request.PaneId,
            ChildPid = occupant.Pid,
            ShellPid = inspected.Process.ShellPid,
            LaunchedAtUtc = startUtc,
            Cwd = occupant.Cwd,
            AgentKind = expectedKind,
            Origin = HerdrPaneOrigins.Attached,
            UpdatedAtUtc = startUtc,
        };
        _sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, request.SessionId));

        return new HerdrAttachResult(
            new ChildStarted(occupant.Pid, HostPid: null, startUtc),
            _sidecar,
            grokUpdatesPath,
            grokEncodedCwd);
    }

    /// <summary>
    /// CARD-0162: raise Exited(<paramref name="reason"/>) once after verification fails. Retires
    /// the sidecar to a last-pane record (CARD-0224) so the next launch of this id can target
    /// the standing pane. Idempotent against MarkVanishedIfDead and repeated close events.
    /// </summary>
    public void RaiseVerifiedClosed(string reason = HerdrExitReasons.PaneClosed)
    {
        if (_exited) return;
        HerdrPaneSidecar.Retire(_settings.SessionLogPath, _sessionId, reason);
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
        var target = await ResolveTargetPaneAsync(workspaceId, opts, request, expectedKind, ct);

        if (target.Kind == TargetPaneKind.Adopt)
            return await AdoptInPlaceAsync(request, opts, expectedKind, target, ct);

        string tabId;
        string paneId;
        string sidecarWorkspaceId;
        if (target.Kind == TargetPaneKind.Relaunch)
        {
            tabId = target.TabId;
            paneId = target.PaneId;
            sidecarWorkspaceId = target.WorkspaceId;
        }
        else
        {
            (tabId, paneId) = await AllocatePaneAsync(workspaceId, opts, request, ct);
            sidecarWorkspaceId = workspaceId;
        }

        return await CompleteTypedLaunchAsync(
            request, opts, expectedKind, sidecarWorkspaceId, tabId, paneId, ct);
    }

    private enum TargetPaneKind { Allocate, Relaunch, Adopt }

    private sealed record TargetPane(
        TargetPaneKind Kind,
        string WorkspaceId,
        string TabId,
        string PaneId,
        Guid? LastPaneSessionId = null,
        HerdrPaneProcess? Occupant = null,
        int? ShellPid = null);

    /// <summary>
    /// CARD-0224: decide whether this launch reuses a standing last-pane, adopts a live occupant,
    /// or falls through to the allocator. Throws <see cref="HerdrLaunchException"/> with
    /// <see cref="HerdrLaunchException.CodePaneOccupied"/> rather than stealing a foreign pane.
    /// </summary>
    private async Task<TargetPane> ResolveTargetPaneAsync(
        string workspaceId,
        HerdrLaunchOptions opts,
        RunnerLaunchRequest request,
        string expectedKind,
        CancellationToken ct)
    {
        var candidate = HerdrLastPane.TryLoad(_settings.SessionLogPath, request.SessionId);
        if (candidate is null && opts.ReusePaneOfSessionId is Guid prev)
            candidate = HerdrLastPane.TryLoad(_settings.SessionLogPath, prev);

        if (candidate is null)
            return new TargetPane(TargetPaneKind.Allocate, workspaceId, TabId: "", PaneId: "");

        if (!string.Equals(candidate.WorkspaceKey, opts.WorkspaceKey, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Last-pane workspace {OldKey} != {NewKey} for session {SessionId}; allocating a new pane",
                candidate.WorkspaceKey, opts.WorkspaceKey, request.SessionId);
            HerdrLastPane.TryDelete(_settings.SessionLogPath, candidate.SessionId);
            return new TargetPane(TargetPaneKind.Allocate, workspaceId, TabId: "", PaneId: "");
        }

        HerdrPaneInfo pane;
        HerdrPaneProcessInfo proc;
        List<HerdrPaneProcess> nonShell;
        try
        {
            (pane, proc, nonShell) = await ReadForegroundAsync(candidate.PaneId, ct);
        }
        catch (HerdrApiException ex) when (IsPaneNotFound(ex))
        {
            _logger.LogInformation(
                "Last-pane {PaneId} is unknown to herdr for session {SessionId}; allocating a new pane",
                candidate.PaneId, request.SessionId);
            HerdrLastPane.TryDelete(_settings.SessionLogPath, candidate.SessionId);
            return new TargetPane(TargetPaneKind.Allocate, workspaceId, TabId: "", PaneId: "");
        }

        if (nonShell.Count == 0)
        {
            // 4a: empty (or only the shell pid).
            if (string.Equals(candidate.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Last-pane {PaneId} was attached-origin; not typing into a pane we did not create",
                    candidate.PaneId);
                HerdrLastPane.TryDelete(_settings.SessionLogPath, candidate.SessionId);
                return new TargetPane(TargetPaneKind.Allocate, workspaceId, TabId: "", PaneId: "");
            }

            await RequirePowerShellShellAsync(candidate.PaneId, ct);
            return new TargetPane(
                TargetPaneKind.Relaunch,
                candidate.WorkspaceId,
                candidate.TabId,
                candidate.PaneId,
                candidate.SessionId,
                ShellPid: proc.ShellPid);
        }

        if (string.Equals(pane.Agent, expectedKind, StringComparison.Ordinal)
            && nonShell.Count == 1
            && TryReadNativeSessionId(nonShell[0].Argv, out var native)
            && native == request.SessionId)
        {
            return new TargetPane(
                TargetPaneKind.Adopt,
                candidate.WorkspaceId,
                candidate.TabId,
                candidate.PaneId,
                candidate.SessionId,
                Occupant: nonShell[0],
                ShellPid: proc.ShellPid);
        }

        // 4c: foreign / unidentifiable / wrong kind / more than one process — refuse, keep the record.
        var occupant = nonShell[0];
        var nativeText = TryReadNativeSessionId(occupant.Argv, out var foreignId)
            ? foreignId.ToString("D")
            : "no --session-id";
        throw new HerdrLaunchException(
            $"pane {candidate.PaneId} is occupied by {occupant.Name} pid {occupant.Pid} ({nativeText}); not stolen — run attach (CARD-0213) or free the pane",
            HerdrLaunchException.CodePaneOccupied);
    }

    private static bool IsPaneNotFound(HerdrApiException ex) =>
        string.Equals(ex.Code, "pane_not_found", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.Code, "not_found", StringComparison.OrdinalIgnoreCase);

    private sealed record InspectedForeground(
        HerdrPaneInfo Pane,
        HerdrPaneProcessInfo Process,
        IReadOnlyList<HerdrPaneProcess> NonShell,
        HerdrPaneProcess? Occupant,
        Guid? NativeSessionId,
        string? NativeSessionSource);

    private async Task<(HerdrPaneInfo Pane, HerdrPaneProcessInfo Process, List<HerdrPaneProcess> NonShell)> ReadForegroundAsync(
        string paneId, CancellationToken ct)
    {
        var pane = await _client.PaneGetAsync(paneId, ct);
        var proc = await _client.PaneProcessInfoAsync(paneId, ct);
        var nonShell = (proc.ForegroundProcesses ?? [])
            .Where(p => proc.ShellPid is not int shell || p.Pid != shell)
            .ToList();
        return (pane, proc, nonShell);
    }

    private async Task<InspectedForeground> InspectForegroundAsync(string paneId, CancellationToken ct)
    {
        HerdrPaneInfo pane;
        HerdrPaneProcessInfo proc;
        List<HerdrPaneProcess> nonShell;
        try
        {
            (pane, proc, nonShell) = await ReadForegroundAsync(paneId, ct);
        }
        catch (HerdrApiException ex) when (IsPaneNotFound(ex))
        {
            throw new HerdrLaunchException(
                $"pane {paneId} is unknown to herdr",
                HerdrProblemTypes.PaneNotFound);
        }

        var occupant = nonShell.Count == 1 ? nonShell[0] : null;
        Guid? nativeId = TryResolveNativeSessionId(occupant?.Argv, pane.AgentSession, out var parsed, out var source)
            ? parsed
            : null;
        return new InspectedForeground(pane, proc, nonShell, occupant, nativeId, source);
    }

    /// <summary>
    /// CARD-0213: argv first (CARD-0224 flags), then herdr <c>agent_session</c> when it is not
    /// our own report. Source <c>antiphon</c> is treated as absent — that is our stamp, not
    /// independent evidence.
    /// </summary>
    internal static bool TryResolveNativeSessionId(
        IReadOnlyList<string>? argv,
        HerdrAgentSessionInfo? agentSession,
        out Guid sessionId,
        out string? source)
    {
        if (TryReadNativeSessionId(argv, out sessionId))
        {
            source = HerdrNativeSessionSources.Argv;
            return true;
        }

        if (agentSession is not null
            && !string.Equals(agentSession.Source, HerdrSources.Antiphon, StringComparison.OrdinalIgnoreCase)
            && TryParseAgentSessionValue(agentSession, out sessionId))
        {
            source = HerdrNativeSessionSources.AgentSession;
            return true;
        }

        sessionId = default;
        source = null;
        return false;
    }

    private static bool TryParseAgentSessionValue(HerdrAgentSessionInfo info, out Guid id)
    {
        if (Guid.TryParse(info.Value, out id))
            return true;

        foreach (var part in info.Value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = Path.GetFileNameWithoutExtension(part);
            if (Guid.TryParse(token, out id) || Guid.TryParse(part, out id))
                return true;
        }

        id = default;
        return false;
    }

    /// <summary>
    /// CARD-0224 4b: argv names our session via <c>--session-id</c> / <c>--resume</c> / <c>-s</c> /
    /// <c>-r</c> / <c>--resume=</c>. Codex carries none of these, so 4b never proves identity for it.
    /// </summary>
    internal static bool TryReadNativeSessionId(IReadOnlyList<string>? argv, out Guid sessionId)
    {
        sessionId = default;
        if (argv is null || argv.Count == 0)
            return false;

        for (var i = 0; i < argv.Count; i++)
        {
            var arg = argv[i];
            if (arg.StartsWith("--resume=", StringComparison.OrdinalIgnoreCase))
                return Guid.TryParse(arg.AsSpan("--resume=".Length), out sessionId);
            if (IsSessionIdFlag(arg) && i + 1 < argv.Count)
                return Guid.TryParse(argv[i + 1], out sessionId);
        }

        return false;
    }

    private static bool IsSessionIdFlag(string arg) =>
        arg.Equals("--session-id", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--resume", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("-s", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("-r", StringComparison.OrdinalIgnoreCase);

    private async Task<ChildStarted> CompleteTypedLaunchAsync(
        RunnerLaunchRequest request,
        HerdrLaunchOptions opts,
        string expectedKind,
        string workspaceId,
        string tabId,
        string paneId,
        CancellationToken ct)
    {
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

        WriteSidecar(
            request, opts, expectedKind, workspaceId, tabId, paneId,
            childPid, shellPid, launchedAt, HerdrPaneOrigins.Launched);
        HerdrLastPane.TryDelete(_settings.SessionLogPath, request.SessionId);
        if (opts.ReusePaneOfSessionId is Guid prev && prev != request.SessionId)
            HerdrLastPane.TryDelete(_settings.SessionLogPath, prev);

        TryDeleteLaunchScript(scriptPath);

        return new ChildStarted(childPid, HostPid: null, launchedAt);
    }

    private async Task<ChildStarted> AdoptInPlaceAsync(
        RunnerLaunchRequest request,
        HerdrLaunchOptions opts,
        string expectedKind,
        TargetPane target,
        CancellationToken ct)
    {
        var occupant = target.Occupant
            ?? throw new InvalidOperationException("AdoptInPlace requires an occupant.");

        _paneId = target.PaneId;

        await _client.PaneRenameAsync(target.PaneId, opts.PaneTitle, ct);
        await _client.PaneReportMetadataAsync(
            target.PaneId,
            new Dictionary<string, string?> { ["antiphon-session"] = request.SessionId.ToString("D") },
            title: opts.PaneTitle,
            ct);

        await TryApplyAgentNameAsync(target.PaneId, opts.AgentSlug, ct);

        var startUtc = _processLiveness.TryGetStartTimeUtc(occupant.Pid) ?? DateTime.UtcNow;
        WriteSidecar(
            request, opts, expectedKind, target.WorkspaceId, target.TabId, target.PaneId,
            occupant.Pid, target.ShellPid, startUtc, HerdrPaneOrigins.Launched);
        HerdrLastPane.TryDelete(_settings.SessionLogPath, request.SessionId);
        if (target.LastPaneSessionId is Guid last && last != request.SessionId)
            HerdrLastPane.TryDelete(_settings.SessionLogPath, last);

        _logger.LogInformation(
            "Adopted live {Kind} pid {Pid} in pane {PaneId} for session {SessionId} (operator relaunch; nothing typed)",
            expectedKind, occupant.Pid, target.PaneId, request.SessionId);

        return new ChildStarted(occupant.Pid, HostPid: null, startUtc);
    }

    private void WriteSidecar(
        RunnerLaunchRequest request,
        HerdrLaunchOptions opts,
        string expectedKind,
        string workspaceId,
        string tabId,
        string paneId,
        int? childPid,
        int? shellPid,
        DateTime launchedAt,
        string origin)
    {
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
            Origin = origin,
            UpdatedAtUtc = launchedAt,
        };
        _sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, request.SessionId));
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

        if (string.Equals(_sidecar?.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase))
            return await DetachAsync(ct);

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

    /// <summary>
    /// CARD-0213: drop the sidecar and clear metadata; never <c>pane.close</c>, never pid-kill.
    /// Attached exits write no last-pane record (<see cref="HerdrPaneSidecar.Retire"/> already skips).
    /// </summary>
    private async Task<bool> DetachAsync(CancellationToken ct)
    {
        try
        {
            await _client.PaneReportMetadataAsync(
                new HerdrPaneReportMetadataParams(
                    _paneId!,
                    HerdrSources.Antiphon,
                    Tokens: new Dictionary<string, string?> { ["antiphon-session"] = null },
                    ClearStateLabels: true),
                ct);
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
        {
            _logger.LogWarning(ex, "pane.report_metadata (detach) failed for {PaneId}", _paneId);
        }

        HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, _sessionId);
        RaiseExited(HerdrExitReasons.Detached, exitCode: 0);
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
                "pane shell is missing; set herdr default_shell or use PtyHost",
                HerdrLaunchException.CodePaneShell);
        }

        var name = _processLiveness.TryGetProcessName(shellPid);
        if (!IsPowerShellProcessName(name))
        {
            throw new HerdrLaunchException(
                $"pane shell '{name ?? $"pid {shellPid}"}' is not PowerShell; set herdr default_shell or use PtyHost",
                HerdrLaunchException.CodePaneShell);
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

    private void RaiseExited(string reason, int? exitCode = null)
    {
        if (_exited) return;
        _exited = true;
        Exited?.Invoke(new ChildExit(exitCode, reason));
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

/// <summary>CARD-0213: result of <see cref="HerdrPaneChild.AttachAsync"/>.</summary>
internal sealed record HerdrAttachResult(
    ChildStarted Started,
    HerdrPaneSidecar Sidecar,
    string? GrokUpdatesPath,
    string? GrokEncodedCwd);
