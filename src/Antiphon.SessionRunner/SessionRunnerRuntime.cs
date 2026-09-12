using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// Session registry and orchestration. Since the pty-host split, the runner does NOT own ConPTY
/// processes: each session's child lives in a detached per-session Antiphon.PtyHost process, and
/// the runner talks to it over a named pipe. The runner keeps all interpretation (screen render,
/// transcripts, events) so it can be restarted freely without killing a single session.
/// </summary>
public sealed class SessionRunnerRuntime : IAsyncDisposable
{
    // The enum is the runner's actual dispatch surface. /capabilities derives its list from this
    // rather than a separately-maintained contract list, so a new switch arm cannot be omitted
    // from the advertised answer (CARD-0112 S1).
    internal enum TranscriptTailerKind { Claude, Grok, Codex }

    public static IReadOnlyList<string> SupportedTranscriptFormats { get; } =
        Enum.GetValues<TranscriptTailerKind>().Select(FormatFor).ToArray();

    private readonly ConcurrentDictionary<Guid, RunnerSession> _sessions = new();
    private int _startCoreSessionRegistrations;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _launchLocks = new();
    private readonly SessionRunnerEventHub _events = new();
    // One transcript, one session (CARD-0006 rule C1). Process-wide because the runner process is
    // the only thing that knows which sessions are live.
    private readonly TranscriptClaimRegistry _transcriptClaims = new();
    private readonly SessionRunnerSettings _settings;
    private readonly ShadowCopyStore _shadowStore;
    private readonly PtyHostLauncher _launcher;
    private readonly HerdrClient? _herdrClient;
    private readonly IProcessLivenessProbe _processLiveness;
    private readonly ILogger<SessionRunnerRuntime> _logger;
    private readonly RunnerStartupDiagnostics _startup;
    private readonly HerdrPlacementCoordinator _placement = new();
    private readonly HerdrNamedTabResolver _namedTabs = new(HerdrNamedTabResolver.HostLabelComparer);
    private readonly Lazy<RunnerCustodyLedger> _custody;
    public string? VerificationCustodyBackend => OperatingSystem.IsWindows()
        && PtyBackendPolicy.Resolve(_settings.PtyBackend).Backend == PtyBackend.ModernConPty ? "windows-job-v1" : null;
    public Guid RunnerStoreId => _custody.Value.Store.StoreId;
    private bool HasCustodyLedger => _custody.IsValueCreated
        || Directory.Exists(Path.Combine(_settings.SessionLogPath, "verification-custody"))
        || File.Exists(Path.Combine(_settings.SessionLogPath, "verification-custody.identity.json"));

    /// <summary>
    /// CARD-0162: fired when the set of live herdr panes changes (launch / adopt / exit) so the
    /// event pump can recycle its subscription.
    /// </summary>
    public event Action? PaneSetChanged;

    /// <summary>
    /// CARD-0497 G-10: how many sessions <see cref="StartCoreAsync"/> inserted into
    /// <c>_sessions</c>, including ones the launch catch later removed. Distinguishes
    /// "policy refused before TryAdd" from "registered then torn down".
    /// </summary>
    internal int StartCoreSessionRegistrations => _startCoreSessionRegistrations;

    public SessionRunnerRuntime(
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerRuntime> logger,
        HerdrClient? herdrClient = null,
        IProcessLivenessProbe? processLiveness = null,
        RunnerStartupDiagnostics? startupDiagnostics = null)
    {
        _settings = settings.Value;
        _custody = new(() => new RunnerCustodyLedger(Path.Combine(_settings.SessionLogPath, "verification-custody")));
        _logger = logger;
        _startup = startupDiagnostics ?? new RunnerStartupDiagnostics(logger);
        _herdrClient = herdrClient;
        _processLiveness = processLiveness ?? new SystemProcessLivenessProbe();
        _shadowStore = new ShadowCopyStore(_settings.PtyHostBinDir);
        _launcher = new PtyHostLauncher(_shadowStore, _settings.ResolvedPtyHostSourceDir);
        _transcriptClaims.ClaimDisplaced += OnTranscriptClaimDisplaced;
    }

    private void OnTranscriptClaimDisplaced(string path, Guid previousOwner, Guid newOwner)
    {
        if (_sessions.TryGetValue(previousOwner, out var session))
        {
            session.OnTranscriptClaimRevoked(path, newOwner);
            return;
        }

        _logger.LogWarning(
            "claim restored from the sidecar of session {Prev} on {Path} was displaced by its namesake {New}; the previous owner is not a live session",
            previousOwner, path, newOwner);
    }

    internal void NotifyPaneSetChanged() => PaneSetChanged?.Invoke();

    /// <summary>
    /// CARD-0162: live herdr sessions with a known pane id (pump subscription + verification).
    /// </summary>
    internal IReadOnlyList<LiveHerdrPane> LiveHerdrPanes()
    {
        var result = new List<LiveHerdrPane>();
        foreach (var (id, session) in _sessions)
        {
            if (session.HasExited) continue;
            if (session.HerdrPaneId is not { } paneId) continue;
            result.Add(new LiveHerdrPane(id, paneId, session));
        }

        return result;
    }

    internal readonly record struct LiveHerdrPane(Guid SessionId, string PaneId, RunnerSession Session);

    /// <summary>
    /// CARD-0382: refuse unsafe Grok rules before a <see cref="RunnerSession"/> is registered,
    /// a host starts, or Herdr is contacted. Herdr expands whole-argument <c>$env:NAME</c>
    /// tokens with <see cref="HerdrLaunchScript.TryResolveEnvToken"/> first; PtyHost does not.
    /// Identity is the request's Grok transcript/kind, not the executable basename.
    /// </summary>
    private static void EnsureGrokRulesArgvSafe(RunnerLaunchRequest request, bool useHerdr)
    {
        var isGrok = string.Equals(request.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Herdr?.AgentKind, HerdrAgentKinds.Grok, StringComparison.OrdinalIgnoreCase);
        var args = request.Args ?? Array.Empty<string>();
        IReadOnlyList<string> effective = args;
        IReadOnlyList<string?>? tokenNames = null;
        if (useHerdr && args.Count > 0)
        {
            var env = request.Env ?? new Dictionary<string, string>();
            var expanded = new string[args.Count];
            var names = new string?[args.Count];
            for (var i = 0; i < args.Count; i++)
            {
                var original = args[i] ?? "";
                if (HerdrLaunchScript.TryResolveEnvToken(original, env, out var resolved))
                {
                    expanded[i] = resolved;
                    HerdrLaunchScript.TryReadEnvTokenName(original, out var name);
                    names[i] = name;
                }
                else
                {
                    expanded[i] = original;
                }
            }

            effective = expanded;
            tokenNames = names;
        }

        var violation = GrokRulesArgvPolicy.ValidateArgv(
            effective, OperatingSystem.IsWindows(), isGrok, tokenNames);
        if (violation is null)
            return;

        throw new GrokRulesLaunchException(
            $"Session {request.SessionId:D}: {GrokRulesArgvPolicy.Format(violation)}");
    }

    public async Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
    {
        var gate = _launchLocks.GetOrAdd(request.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (request.VerificationBinding is null)
            {
                if (HasCustodyLedger) _custody.Value.RequireUntrackedSession(request.SessionId);
                return await StartCoreAsync(request, ct);
            }
            using var custodyLease = _custody.Value.AcquireSession(request.SessionId);
            if (!_custody.Value.PrepareStart(request, _settings.PtyBackend))
            {
                if (_sessions.TryGetValue(request.SessionId, out var existing)
                    && existing.VerificationBinding == request.VerificationBinding)
                    return existing.ToDto();
                throw new VerificationCustodyException("verification_custody_attempt_already_recorded");
            }
            return await StartCoreAsync(request, ct);
        }
        finally { gate.Release(); }
    }

    public async Task<VerificationCustodyStatus> ReadCustodyAsync(VerificationExecutionBinding binding,
        bool seal, CancellationToken ct)
    {
        var gate = _launchLocks.GetOrAdd(binding.Generation.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            using var custodyLease = _custody.Value.AcquireSession(binding.Generation.SessionId);
            if (seal) _custody.Value.Seal(binding);
            else _custody.Value.Store.RequireBinding(binding);
            if (_custody.Value.ReadFinal(binding) is { } final) return final;
            if (_sessions.TryGetValue(binding.Generation.SessionId, out var session)
                && session.VerificationBinding == binding)
                return await session.CollectCustodyAsync(ct);
            if (_custody.Value.ReadProducer(binding) is { } producer)
                return _custody.Value.Accept(producer);
            return _custody.Value.ReadUnavailable(binding);
        }
        finally { gate.Release(); }
    }

    public VerificationExecutionBinding ResolveCustodyBinding(Guid sessionId, Guid executionId, DateTime acceptedStartedAt) =>
        _custody.Value.Resolve(sessionId, executionId, acceptedStartedAt);

    private async Task<RunnerSessionDto> StartCoreAsync(RunnerLaunchRequest request, CancellationToken ct)
    {
        if (request.SessionId == Guid.Empty)
            throw new ArgumentException("SessionId must not be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Exe))
            throw new ArgumentException("Exe must not be empty.", nameof(request));
        if (request.Cols <= 0 || request.Rows <= 0)
            throw new ArgumentException("Terminal size must be positive.", nameof(request));
        if (request.MemoryLimitMb < 0)
            throw new ArgumentException("MemoryLimitMb must not be negative.", nameof(request));
        if (request.TranscriptFormat is { } format
            && !TryResolveTranscriptTailer(format, out _))
        {
            throw new UnsupportedTranscriptFormatException(format, SupportedTranscriptFormats);
        }

        // CARD-0160: Backend validation. Null = pty-host (pre-herdr meaning). Unknown → throw.
        var backend = request.Backend;
        if (backend is not null
            && !string.Equals(backend, SessionBackends.PtyHost, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(backend, SessionBackends.Herdr, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unsupported session backend '{backend}'. Supported: {SessionBackends.PtyHost}, {SessionBackends.Herdr}.",
                nameof(request));
        }

        var useHerdr = string.Equals(backend, SessionBackends.Herdr, StringComparison.OrdinalIgnoreCase);
        EnsureGrokRulesArgvSafe(request, useHerdr);
        if (!useHerdr && string.Equals(request.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase))
            HerdrGrokResumeGuard.Require(request.SessionId, request, HerdrAgentKinds.Grok, _logger);
        if (useHerdr && request.Herdr is null)
            throw new ArgumentException("Herdr launch requires HerdrLaunchOptions.", nameof(request));
        if (useHerdr && _herdrClient is null)
            throw new HerdrBackendUnavailableException(
                "Herdr backend is not available in this runner process (HerdrClient was not registered).");
        if (useHerdr && !HerdrAgentKinds.IsSupported(request.Herdr!.AgentKind))
        {
            throw new ArgumentException(
                $"Unsupported herdr agent kind '{request.Herdr.AgentKind}'. Supported: {string.Join(", ", HerdrAgentKinds.Supported)}.",
                nameof(request));
        }

        if (request.GrokRulesPayload is { } rules)
        {
            _settings.GrokRules.Validate();
            GrokRulesTransport.Encode(rules,
                string.Equals(request.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase),
                _settings.GrokRules.MaxFileBytes);
            var effective = useHerdr
                ? request.Args.Select(arg => HerdrLaunchScript.TryResolveEnvToken(arg, request.Env, out var value) ? value : arg).ToArray()
                : request.Args;
            if (GrokRulesTransport.HasExplicitRules(effective))
                throw new GrokRulesTransportException("grok_rules_source_conflict", "Move the append to SystemPromptAppend.");
            if (_sessions.TryGetValue(request.SessionId, out var running) && !running.HasExited)
                throw new InvalidOperationException($"Session '{request.SessionId}' is already running.");
            var store = new GrokRulesFileStore(_settings.SessionLogPath, _settings.GrokRules);
            var bootstrap = GrokRulesTransport.Bootstrap(store.PathFor(request.SessionId));
            var finalArgs = request.Args.ToList();
            var terminator = finalArgs.IndexOf("--");
            finalArgs.InsertRange(terminator < 0 ? finalArgs.Count : terminator, ["--rules", bootstrap]);
            request = request with { Args = finalArgs };
            EnsureGrokRulesArgvSafe(request, useHerdr);
            var budget = request.CommandLineBudgetChars ?? 30000;
            var actualArgs = useHerdr
                ? finalArgs.Select(arg => HerdrLaunchScript.TryResolveEnvToken(arg, request.Env, out var value) ? value : arg)
                : finalArgs;
            var actualLength = request.Exe.Length + actualArgs.Sum(arg => arg.Length + 3);
            if (budget <= 0 || actualLength > budget)
                throw new GrokRulesTransportException("grok_rules_argv_unsafe", "command_line_budget");
            request = request with
            {
                InstalledGrokRulesReceipt = await store.WriteAsync(request.SessionId, rules, ct),
            };
        }

        // CARD-0497: rewrite a recognized npm Codex shim to node.exe + codex.js and apply the
        // launcher-aware budget before a session is registered, a host starts, or Herdr is contacted.
        request = CodexWindowsLaunchPolicy.Apply(request, useHerdr);

        var session = new RunnerSession(request.SessionId, _settings, _events, _logger, _transcriptClaims, _processLiveness);
        session.BindAcceptedGeneration(request.AcceptedStartedAt);
        if (request.VerificationBinding is { } binding)
            session.SetCustody(_custody.Value, binding);
        session.GrokRulesReceipt = request.InstalledGrokRulesReceipt;
        if (!_sessions.TryAdd(request.SessionId, session))
        {
            // A session id can be relaunched once its process has exited (claude --resume reuses
            // the original id); only a live session blocks the id.
            if (_sessions.TryGetValue(request.SessionId, out var existing)
                && existing.HasExited)
            {
                // CARD-0050: the pipe name derives from the session id, so a relaunch races the
                // PREVIOUS host's teardown — its Shutdown ack is fire-and-forget (HandleExited),
                // and until that host exits it still owns a pipe server instance with the exact
                // name the new host will claim. Under load the new client's connect reached the
                // dying host, which correctly answered "alreadyLaunched: Session is Exited" and
                // failed the relaunch. The child is already exited here, so forcing the old host
                // out forfeits nothing a pty-host exists to protect.
                try { await existing.EnsureExitedHostGoneAsync(TimeSpan.FromSeconds(5), ct); }
                catch { await session.DisposeAsync(); throw; }
                if (!_sessions.TryUpdate(request.SessionId, session, existing))
                {
                    await session.DisposeAsync();
                    throw new InvalidOperationException($"Session '{request.SessionId}' changed during relaunch.");
                }
                await existing.DisposeAsync();
            }
            else
            {
                await session.DisposeAsync();
                throw new InvalidOperationException($"Session '{request.SessionId}' is already running.");
            }
        }

        Interlocked.Increment(ref _startCoreSessionRegistrations);

        try
        {
            if (useHerdr)
            {
                if (request.InstalledGrokRulesReceipt is { } receipt)
                    new HerdrPaneSidecar
                    {
                        SessionId = request.SessionId, GrokRulesReceipt = receipt, LaunchPending = true,
                        WorkspaceKey = request.Herdr!.WorkspaceKey, WorkspaceId = "", TabId = "", PaneId = "",
                        UpdatedAtUtc = DateTime.UtcNow,
                        AcceptedStartedAt = request.AcceptedStartedAt,
                    }.SaveAtomic(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, request.SessionId));
                await session.StartHerdrAsync(
                    request,
                    _herdrClient!,
                    () => CollectLiveAntiphonPanes(request.Herdr!.WorkspaceKey),
                    () => NotifyPaneSetChanged(),
                    _placement,
                    LookupBinding,
                    ct);
                NotifyPaneSetChanged();
            }
            else
            {
                if (request.InstalledGrokRulesReceipt is { } receipt)
                    new PtyHostManifest
                    {
                        SessionId = request.SessionId, GrokRulesReceipt = receipt, LaunchPending = true,
                        PipeName = PtyHostProtocol.PipeNameFor(request.SessionId), HostPid = 0,
                        HostStartTimeUtc = DateTime.MinValue, CreatedAtUtc = DateTime.UtcNow,
                        AcceptedStartedAt = request.AcceptedStartedAt,
                    }.SaveAtomic(PtyHostManifest.PathFor(_settings.PtyHostManifestDir, request.SessionId));
                await session.StartAsync(request, _launcher, ct);
            }

            return session.ToDto();
        }
        catch (Exception ex)
        {
            _sessions.TryRemove(request.SessionId, out _);
            if (request.VerificationBinding is { } failedBinding
                && ex is VerificationCustodyException { Code: "verification_custody_unsupported_backend" })
            {
                try { _custody.Value.RecordUnsupported(failedBinding); }
                catch (Exception storageError)
                {
                    _logger.LogWarning(storageError, "Unsupported custody outcome could not be persisted for {ExecutionId}", failedBinding.ExecutionId);
                }
            }
            // Kill then dispose — DisposeAsync is detach-not-kill (pty-host split). Same shape
            // as AgentSessionService.KillAndDisposeAsync (CARD-0056 D1, CARD-0086).
            session.TearDownFailedLaunch();
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>CARD-0213: read-only pane snapshot. Nothing is written, typed, or renamed.</summary>
    public async Task<HerdrPaneInspectDto> InspectHerdrPaneAsync(string paneId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        EnsureHerdrClient();
        await _herdrClient!.ConnectAndValidateAsync(ct);

        var child = new HerdrPaneChild(
            _herdrClient, _settings, _logger, () => Array.Empty<HerdrPaneAllocator.LivePane>(), _processLiveness);
        var dto = await child.InspectAsync(paneId, ct);
        var bound = FindBoundPane(paneId, exceptSessionId: null);
        return dto with { BoundToSessionId = bound?.SessionId, BoundOrigin = bound?.Origin };
    }

    /// <summary>CARD-0213: bind a standing session to an operator pane Antiphon did not launch.</summary>
    public async Task<RunnerSessionDto> AttachHerdrAsync(HerdrAttachRequest request, CancellationToken ct)
    {
        var gate = _launchLocks.GetOrAdd(request.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (HasCustodyLedger) _custody.Value.RequireUntrackedSession(request.SessionId);
            return await AttachHerdrCoreAsync(request, ct);
        }
        finally { gate.Release(); }
    }

    private async Task<RunnerSessionDto> AttachHerdrCoreAsync(HerdrAttachRequest request, CancellationToken ct)
    {
        if (request.SessionId == Guid.Empty)
            throw new ArgumentException("SessionId must not be empty.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PaneId);
        EnsureHerdrClient();
        if (!HerdrAgentKinds.IsSupported(request.ExpectedKind))
        {
            throw new ArgumentException(
                $"Unsupported herdr agent kind '{request.ExpectedKind}'. Supported: {string.Join(", ", HerdrAgentKinds.Supported)}.",
                nameof(request));
        }

        await _herdrClient!.ConnectAndValidateAsync(ct);

        var bound = FindBoundPane(request.PaneId, exceptSessionId: request.SessionId);
        if (bound is { } holder)
        {
            throw new HerdrLaunchException(
                $"pane {request.PaneId} is bound to session {holder.SessionId:D} ({holder.Origin})",
                HerdrProblemTypes.PaneBound);
        }

        var session = new RunnerSession(request.SessionId, _settings, _events, _logger, _transcriptClaims, _processLiveness);
        session.BindAcceptedGeneration(request.AcceptedStartedAt);
        if (!_sessions.TryAdd(request.SessionId, session))
        {
            if (_sessions.TryGetValue(request.SessionId, out var existing)
                && existing.HasExited)
            {
                try { await existing.EnsureExitedHostGoneAsync(TimeSpan.FromSeconds(5), ct); }
                catch { await session.DisposeAsync(); throw; }
                if (!_sessions.TryUpdate(request.SessionId, session, existing))
                {
                    await session.DisposeAsync();
                    throw new InvalidOperationException($"Session '{request.SessionId}' changed during attach.");
                }
                await existing.DisposeAsync();
            }
            else
            {
                await session.DisposeAsync();
                throw new InvalidOperationException($"Session '{request.SessionId}' is already running.");
            }
        }

        try
        {
            await session.AttachHerdrAsync(
                request,
                _herdrClient,
                () => CollectLiveAntiphonPanes(request.WorkspaceKey),
                () => NotifyPaneSetChanged(),
                _placement,
                LookupBinding,
                ct);
            NotifyPaneSetChanged();
            return session.ToDto();
        }
        catch
        {
            _sessions.TryRemove(request.SessionId, out _);
            session.TearDownFailedLaunch();
            await session.DisposeAsync();
            throw;
        }
    }

    private void EnsureHerdrClient()
    {
        if (_herdrClient is null)
        {
            throw new HerdrBackendUnavailableException(
                "Herdr backend is not available in this runner process (HerdrClient was not registered).");
        }
    }

    private readonly record struct BoundPane(Guid SessionId, string Origin, bool Live);

    private PaneBinding? LookupBinding(string paneId, Guid? exceptSessionId) =>
        FindBoundPane(paneId, exceptSessionId) is { } bound
            ? new PaneBinding(bound.SessionId, bound.Origin, bound.Live)
            : null;

    /// <summary>
    /// A live session, on-disk sidecar, pending named claim, or another id's last-pane pointing at
    /// <paramref name="paneId"/>. Same-id last-pane is allowed (the operator is reclaiming their own pane).
    /// </summary>
    private BoundPane? FindBoundPane(string paneId, Guid? exceptSessionId)
    {
        if (_placement.FindPaneClaim(paneId, exceptSessionId) is { } claim)
            return new BoundPane(claim.SessionId, HerdrPaneOrigins.Launched, Live: true);

        foreach (var (id, session) in _sessions)
        {
            if (exceptSessionId is Guid skip && id == skip)
                continue;
            if (session.HasExited)
                continue;
            if (string.Equals(session.HerdrPaneId, paneId, StringComparison.Ordinal))
                return new BoundPane(id, session.HerdrOrigin ?? HerdrPaneOrigins.Launched, Live: true);
            if (session.PendingSidecar is { } pending
                && string.Equals(pending.PaneId, paneId, StringComparison.Ordinal))
            {
                return new BoundPane(id, pending.Origin ?? HerdrPaneOrigins.Launched, Live: true);
            }
        }

        foreach (var sidecar in HerdrPaneSidecar.LoadAll(_settings.SessionLogPath))
        {
            if (exceptSessionId is Guid skip && sidecar.SessionId == skip)
                continue;
            if (!string.Equals(sidecar.PaneId, paneId, StringComparison.Ordinal))
                continue;
            if (_sessions.TryGetValue(sidecar.SessionId, out var live) && live.HasExited)
                continue;
            return new BoundPane(sidecar.SessionId, sidecar.Origin ?? HerdrPaneOrigins.Launched, Live: true);
        }

        foreach (var last in HerdrLastPane.LoadAll(_settings.SessionLogPath))
        {
            if (exceptSessionId is Guid skip && last.SessionId == skip)
                continue;
            if (!string.Equals(last.PaneId, paneId, StringComparison.Ordinal))
                continue;
            return new BoundPane(last.SessionId, last.Origin, Live: false);
        }

        return null;
    }

    /// <summary>Live Antiphon herdr panes for the allocator (sidecar + still in this runner).</summary>
    private IReadOnlyList<HerdrPaneAllocator.LivePane> CollectLiveAntiphonPanes(string workspaceKey)
    {
        var reservedTabs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sidecar in HerdrPaneSidecar.LoadAll(_settings.SessionLogPath))
        {
            if (!string.Equals(sidecar.WorkspaceKey, workspaceKey, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(sidecar.TabLabel))
                continue;
            if (string.Equals(sidecar.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_sessions.TryGetValue(sidecar.SessionId, out var live) || live.HasExited)
                continue;
            reservedTabs.Add(sidecar.TabId);
        }

        var result = new List<HerdrPaneAllocator.LivePane>();
        foreach (var sidecar in HerdrPaneSidecar.LoadAll(_settings.SessionLogPath))
        {
            if (!string.Equals(sidecar.WorkspaceKey, workspaceKey, StringComparison.Ordinal))
                continue;
            if (string.Equals(sidecar.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_sessions.TryGetValue(sidecar.SessionId, out var session) || session.HasExited)
                continue;
            if (reservedTabs.Contains(sidecar.TabId) || _placement.IsNamedTabReserved(sidecar.TabId))
                continue;
            // TabNumber unknown from sidecar alone — use 0 and let allocator order by TabId as tiebreak.
            // Callers that have tab.get can refine; for gap refill within one tab, number equality is fine.
            result.Add(new HerdrPaneAllocator.LivePane(
                sidecar.SessionId, sidecar.TabId, sidecar.PaneId, TabNumber: 0));
        }

        return result;
    }

    /// <summary>
    /// CARD-0384: read-only named-tab placement classification. Never creates furniture,
    /// refreshes tokens, writes files, or leaves a claim.
    /// </summary>
    public async Task<HerdrPlacementCheckResult> CheckHerdrPlacementAsync(
        HerdrPlacementCheckRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Herdr);
        EnsureHerdrClient();
        await _herdrClient!.ConnectAndValidateAsync(ct);

        var listed = await _herdrClient.WorkspaceListAsync(ct);
        var workspaceId = HerdrPaneChild.FindExistingWorkspaceId(listed, request.Herdr);
        if (workspaceId is null || string.IsNullOrWhiteSpace(request.Herdr.TabLabel))
            return new HerdrPlacementCheckResult(HerdrNamedTabResolver.ActionCreate);

        var tabs = await _herdrClient.TabListAsync(workspaceId, ct);
        var matches = _namedTabs.MatchTabs(tabs, workspaceId, request.Herdr.TabLabel);
        if (matches.Count == 0)
            return new HerdrPlacementCheckResult(HerdrNamedTabResolver.ActionCreate, workspaceId);

        var panes = await _herdrClient.PaneListAsync(workspaceId, ct);
        var pick = _namedTabs.PickUniqueSinglePaneTab(tabs, panes, workspaceId, request.Herdr.TabLabel);
        if (pick is null)
            return new HerdrPlacementCheckResult(HerdrNamedTabResolver.ActionCreate, workspaceId);

        var pane = await _herdrClient.PaneGetAsync(pick.Pane.PaneId, ct);
        var proc = await _herdrClient.PaneProcessInfoAsync(pick.Pane.PaneId, ct);
        var expectedKind = string.IsNullOrEmpty(request.Herdr.AgentKind)
            ? HerdrAgentKinds.Claude
            : request.Herdr.AgentKind;
        var shellName = proc.ShellPid is int shell ? _processLiveness.TryGetProcessName(shell) : null;
        var occupant = _namedTabs.Classify(pane, proc, expectedKind, request.SessionId, shellName);
        if (occupant.Kind == NamedOccupantKind.Occupied
            || FindBoundPane(pick.Pane.PaneId, request.SessionId) is { Live: true })
        {
            throw new HerdrLaunchException(
                occupant.OccupiedDetail
                ?? $"pane {pick.Pane.PaneId} is occupied; not stolen",
                HerdrProblemTypes.PaneOccupied);
        }

        if (occupant.Kind == NamedOccupantKind.Adopt)
        {
            return new HerdrPlacementCheckResult(
                HerdrNamedTabResolver.ActionAdopt, workspaceId, pick.Tab.TabId, pick.Pane.PaneId);
        }

        return new HerdrPlacementCheckResult(
            HerdrNamedTabResolver.ActionRelaunch, workspaceId, pick.Tab.TabId, pick.Pane.PaneId);
    }

    internal static bool TryResolveTranscriptTailer(string format, out TranscriptTailerKind tailer)
    {
        foreach (var candidate in Enum.GetValues<TranscriptTailerKind>())
        {
            if (string.Equals(FormatFor(candidate), format, StringComparison.OrdinalIgnoreCase))
            {
                tailer = candidate;
                return true;
            }
        }

        tailer = default;
        return false;
    }

    private static string FormatFor(TranscriptTailerKind tailer) => tailer switch
    {
        TranscriptTailerKind.Claude => TranscriptFormats.Claude,
        TranscriptTailerKind.Grok => TranscriptFormats.Grok,
        TranscriptTailerKind.Codex => TranscriptFormats.Codex,
        _ => throw new ArgumentOutOfRangeException(nameof(tailer), tailer, null),
    };

    public IReadOnlyList<RunnerSessionDto> List() =>
        _sessions.Values.Select(session => session.ToDto()).OrderBy(session => session.StartedAt).ToList();

    public RunnerSessionDto Get(Guid sessionId) => GetSession(sessionId).ToDto();

    /// <summary>
    /// CARD-0161: for herdr sessions, refresh LastSequence + AgentStatus via one pane.get before
    /// answering. CARD-0186 S3: stamps <see cref="RunnerSessionDto.HerdrVerifiedAtUtc"/> after a
    /// passing liveness verify. List() stays cheap (no herdr calls) and reports the last stamp.
    /// </summary>
    public async Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
    {
        var session = GetSession(sessionId);
        if (_herdrClient is not null)
            await session.TryStampHerdrVerifiedAsync(_herdrClient, ct);
        await session.RefreshHerdrSurfaceAsync(ct);
        return session.ToDto();
    }

    public RunnerBufferDto GetBuffer(Guid sessionId) => GetSession(sessionId).GetBuffer();

    public RunnerSnapshotDto GetSnapshot(Guid sessionId) => GetSession(sessionId).GetSnapshot();

    public RunnerTranscriptDto GetTranscript(Guid sessionId) => GetSession(sessionId).GetTranscript();

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        string.IsNullOrEmpty(input)
            ? Task.CompletedTask
            : GetSession(sessionId).WriteAsync(input, ct);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return GetSession(sessionId).ClearLiveBufferAsync(ct);
    }

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        if (cols <= 0 || rows <= 0)
            throw new ArgumentException("Terminal size must be positive.");

        return GetSession(sessionId).ResizeAsync(cols, rows, ct);
    }

    /// <param name="exitReasonOverride">When set, replaces the host's KilledByRequest exit reason
    /// so the server can distinguish WHY the runner killed the session (e.g. the CPU spin
    /// watchdog's <c>CpuSpinKilled</c>). A natural exit that wins the race keeps its own reason.</param>
    public async Task<RunnerSessionDto> KillAsync(
        Guid sessionId, TimeSpan timeout, CancellationToken ct, string? exitReasonOverride = null)
    {
        var session = GetSession(sessionId);
        await session.KillAsync(timeout, ct, exitReasonOverride);
        return session.ToDto();
    }

    public async Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, TimeSpan timeout, CancellationToken ct)
    {
        var gate = _launchLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null);

            if (!SessionGeneration.Equal(session.AcceptedStartedAt, expectedAcceptedStartedAt))
            {
                return new RunnerKillGenerationResult(
                    sessionId, false, KillGenerationOutcomes.Mismatch, session.AcceptedStartedAt);
            }

            if (session.HasExited)
            {
                return new RunnerKillGenerationResult(
                    sessionId, false, KillGenerationOutcomes.AlreadyExited, session.AcceptedStartedAt);
            }

            await session.KillAsync(timeout, ct);
            return new RunnerKillGenerationResult(
                sessionId, true, KillGenerationOutcomes.Killed, session.AcceptedStartedAt);
        }
        finally { gate.Release(); }
    }

    /// <summary>Explicit expiry by an artifact owner; never called by kill, retirement, or worktree cleanup.</summary>
    public async Task ExpireRulesArtifactAsync(Guid sessionId, CancellationToken ct)
    {
        var gate = _launchLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var session = GetSession(sessionId);
            if (!session.HasExited) throw new InvalidOperationException("A live session's rules cannot expire.");
            new GrokRulesFileStore(_settings.SessionLogPath, _settings.GrokRules).Expire(sessionId);
            session.GrokRulesReceipt = null;
        }
        finally { gate.Release(); }
    }

    public ChannelReader<RunnerServerSentEvent> Subscribe(CancellationToken ct) => _events.Subscribe(ct);

    /// <summary>Transcript ownership, rule C1 (see <see cref="TranscriptClaimRegistry"/>). Test surface.</summary>
    internal TranscriptClaimRegistry TranscriptClaims => _transcriptClaims;

    /// <summary>
    /// Kills every live session (and thereby its host, via the exit-&gt;Shutdown ack). The
    /// scorched-earth path behind <c>restart-session-runner.ps1 -KillSessions</c> and
    /// <c>POST /sessions/kill-all</c> - the ONLY sanctioned way to take hosts down in bulk.
    /// </summary>
    public async Task<IReadOnlyList<RunnerSessionDto>> KillAllAsync(TimeSpan timeout, CancellationToken ct)
    {
        var killed = new List<RunnerSessionDto>();
        foreach (var (sessionId, session) in _sessions)
        {
            if (session.HasExited && session.VerificationBinding is null)
                continue;
            try
            {
                await session.KillAsync(timeout, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Kill-all failed for session {SessionId}", sessionId);
            }

            killed.Add(session.ToDto());
        }

        return killed;
    }

    /// <summary>
    /// Best-effort disk hygiene for pty-host state: prunes shadow-copy version dirs no live host
    /// runs from (oldest first) and host logs past the retention window. Never throws.
    /// </summary>
    public void CleanupPtyHostState()
    {
        try
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(_settings.PtyHostBinDir))
                referenced.Add(_launcher.CurrentShadowDir);

            foreach (var process in System.Diagnostics.Process.GetProcessesByName("Antiphon.PtyHost"))
            {
                try
                {
                    if (Path.GetDirectoryName(process.MainModule?.FileName) is { } dir)
                        referenced.Add(dir);
                }
                catch
                {
                    // Access denied / exited mid-scan - a locked dir survives deletion anyway.
                }
                finally
                {
                    process.Dispose();
                }
            }

            var deleted = _shadowStore.CleanupUnreferenced(referenced);
            if (deleted > 0)
                _logger.LogInformation("Pruned {Count} unreferenced pty-host shadow-copy dir(s)", deleted);

            var cutoff = DateTime.UtcNow.AddDays(-14);
            if (Directory.Exists(_settings.PtyHostLogDir))
            {
                foreach (var log in Directory.EnumerateFiles(_settings.PtyHostLogDir, "*.log"))
                {
                    if (File.GetLastWriteTimeUtc(log) < cutoff)
                        TryDeleteFile(log);
                }
            }

            // Transcript sidecars, same window — but only for sessions this runner no longer knows
            // about, since a live session's sidecar is how the NEXT restart re-tails it.
            var sidecarDir = TranscriptSidecar.DirectoryFor(_settings.SessionLogPath);
            if (Directory.Exists(sidecarDir))
            {
                foreach (var sidecar in Directory.EnumerateFiles(sidecarDir, "*.json"))
                {
                    if (File.GetLastWriteTimeUtc(sidecar) >= cutoff)
                        continue;
                    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(sidecar), "N", out var id)
                        && _sessions.ContainsKey(id))
                    {
                        continue;
                    }

                    TryDeleteFile(sidecar);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "pty-host state cleanup pass failed");
        }
    }

    /// <summary>
    /// Startup adoption sweep: reconnects to pty-hosts that survived a runner restart. MUST run
    /// to completion before the HTTP API starts listening - the server's reconciler treats "the
    /// runner doesn't know this session" as fatal, so the runner may never serve a half-adopted
    /// session list. For each manifest on disk:
    /// live host  -> reconnect, rebuild interpretation from the ansi log, resume streaming;
    /// exited host-> collect the recorded exit, publish the missed SessionExited, ack Shutdown;
    /// dead host  -> register the session as Exited with whatever fate the manifest recorded.
    /// </summary>
    public async Task<int> AdoptOrphanedHostsAsync(IProcessLivenessProbe probe, CancellationToken ct)
    {
        var hasCustodyLedger = HasCustodyLedger;
        if (hasCustodyLedger)
        {
            _custody.Value.ValidateRecovery();
            _custody.Value.RecoverManifests(_settings.PtyHostManifestDir, probe);
        }
        using var sweep = _startup.Begin("adoption-sweep");
        // Rebuild transcript claims BEFORE any session is adopted. This sweep already has to
        // complete before the HTTP API starts listening, so restoring here means a freshly launched
        // session can never race the restore and discover a file a surviving session still owns.
        using (var claims = _startup.Begin("claims"))
        {
            var restoredClaims = RestoreTranscriptClaims();
            claims.Complete(restoredClaims);
        }

        // CARD-0160: herdr adoption arm AFTER claims. Sidecar present + pane/pid/read evidence →
        // re-adopt; restored-but-empty or unknown pane → Exited(HerdrRestartPresumedDead);
        // herdr unreachable + OS-alive → Pending (S3); unreachable + OS-dead → ChildGone.
        using (var herdr = _startup.Begin("herdr"))
        {
            if (_herdrClient is not null)
            {
                var retentionDays = Math.Max(0, _herdrClient.Settings.LastPaneRetentionDays);
                if (retentionDays > 0)
                    HerdrLastPane.DeleteOlderThan(_settings.SessionLogPath, TimeSpan.FromDays(retentionDays));
                await AdoptHerdrSessionsAsync(probe, ct);
            }
            // DTO construction can consult a failing transcript tailer; this count needs only the backend.
            herdr.Complete(_sessions.Values.Count(s => s.Backend == SessionBackends.Herdr));
        }

        using var pty = _startup.Begin("pty-manifests");
        var manifestDir = _settings.PtyHostManifestDir;
        if (!Directory.Exists(manifestDir))
        {
            pty.Complete(0); sweep.Complete(0);
            return 0;
        }

        var adopted = 0;
        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var manifest = PtyHostManifest.TryLoad(file);
            if (manifest is null || manifest.SessionId == Guid.Empty)
            {
                TryDeleteFile(file);
                continue;
            }

            if (_sessions.TryGetValue(manifest.SessionId, out var existingPty))
            {
                if (manifest.AcceptedStartedAt is { } orphanGeneration
                    && !SessionGeneration.Equal(existingPty.AcceptedStartedAt, orphanGeneration)
                    && (manifest.ExitReason is not null || manifest.ExitCode is not null || manifest.ExitedAtUtc is not null))
                {
                    _ = RunnerSession.CreateAdoptedExited(manifest, _settings, _events, _logger);
                }

                continue;
            }

            var tracked = hasCustodyLedger ? _custody.Value.Store.ReadReservations()
                .Where(b => b.Generation.SessionId == manifest.SessionId).ToArray() : [];
            if (tracked.Length != 0)
            {
                if (manifest.VerificationBinding is not { } binding || !tracked.Contains(binding)
                    || manifest.VerificationHost is not { } identity)
                {
                    _logger.LogWarning("Custody identity missing for manifest {SessionId}; retained without adoption", manifest.SessionId);
                    continue;
                }
                _custody.Value.RequireHost(binding, identity);
                if (manifest.HostPid != identity.HostPid || manifest.HostStartTimeUtc != identity.HostStartTimeUtc
                    || manifest.Cwd != binding.Creation.WorktreePath)
                    throw new VerificationCustodyException("verification_custody_identity_mismatch");
                if (manifest.AnsiLogPath is { } ansi) _custody.Value.Store.RequireOutsideSnapshot(ansi, binding);
            }

            using var hostAttempt = _startup.Begin("pty-host-adoption", manifest.SessionId);

            if (manifest.HostPid > 0 && probe.IsAlive(manifest.HostPid, manifest.HostStartTimeUtc))
            {
                var session = new RunnerSession(manifest.SessionId, _settings, _events, _logger, _transcriptClaims);
                session.BindAcceptedGeneration(manifest.AcceptedStartedAt);
                if (manifest.VerificationBinding is { } binding)
                    session.SetCustody(_custody.Value, binding);
                try
                {
                    var running = await session.AdoptAsync(manifest, ct);
                    _sessions.TryAdd(manifest.SessionId, session);
                    adopted++;
                    hostAttempt.Complete(1, running ? "running" : "exited");
                    _logger.LogInformation(
                        "Adopted pty-host for session {SessionId} (host pid {HostPid}, {State})",
                        manifest.SessionId, manifest.HostPid, running ? "running" : "exited while runner was down");
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Live pty-host for session {SessionId} (pid {HostPid}) could not be adopted; treating as dead",
                        manifest.SessionId, manifest.HostPid);
                    await session.DisposeAsync();
                    if (manifest.VerificationBinding is null) KillPidBestEffort(manifest.HostPid);
                }
            }

            // Dead (or unadoptable) host: the ConPTY died with it, so the child is gone too.
            // Register the session as Exited with the fate the manifest recorded so the server
            // sees a real exit instead of an unknown session.
            var exitedSession = RunnerSession.CreateAdoptedExited(manifest, _settings, _events, _logger);
            if (manifest.VerificationBinding is { } exitedBinding)
                exitedSession.SetCustody(_custody.Value, exitedBinding);
            _sessions.TryAdd(manifest.SessionId, exitedSession);
            hostAttempt.Complete(0, "terminal");
            TryDeleteFile(file);
            _logger.LogWarning(
                "pty-host for session {SessionId} (pid {HostPid}) is gone; registered as Exited ({Reason})",
                manifest.SessionId, manifest.HostPid, exitedSession.ToDto().ExitReason);
        }

        pty.Complete(adopted); sweep.Complete(adopted);
        return adopted;
    }

    /// <summary>
    /// CARD-0160 §6A + CARD-0186 S2 OS-pid axis: adopt or mark-exited each
    /// <see cref="HerdrPaneSidecar"/>. Evidence order: socket, pane.get, ChildPid listed,
    /// then OS liveness before any PresumedDead/PaneClosed verdict. P7: Claude dies with
    /// herdr, so R2 (pane restored, pid not listed, OS-dead) is the live restart shape;
    /// R3/R5 orphan (OS-alive) is defensive. R6 (unreachable + OS-alive) is Pending.
    /// </summary>
    private async Task AdoptHerdrSessionsAsync(IProcessLivenessProbe probe, CancellationToken ct)
    {
        foreach (var sidecar in HerdrPaneSidecar.LoadAll(_settings.SessionLogPath))
        {
            ct.ThrowIfCancellationRequested();
            if (_sessions.TryGetValue(sidecar.SessionId, out var existingHerdr)
                && SessionGeneration.Equal(existingHerdr.AcceptedStartedAt, sidecar.AcceptedStartedAt))
                continue;
            if (_sessions.ContainsKey(sidecar.SessionId))
            {
                if (sidecar.AcceptedStartedAt is { } sidecarGeneration
                    && !SessionGeneration.Equal(_sessions[sidecar.SessionId].AcceptedStartedAt, sidecarGeneration))
                {
                    _ = RunnerSession.CreateAdoptedHerdrExited(
                        sidecar, _settings, _events, _logger, HerdrExitReasons.RestartPresumedDead);
                }

                continue;
            }

            using var attempt = _startup.Begin("herdr-adoption", sidecar.SessionId);
            var verdict = await EvaluateHerdrBarAsync(sidecar, probe, ct);
            switch (verdict)
            {
                case HerdrBarVerdict.Adopt:
                    var session = new RunnerSession(
                        sidecar.SessionId, _settings, _events, _logger, _transcriptClaims, _processLiveness);
                    session.BindAcceptedGeneration(sidecar.AcceptedStartedAt);
                    await session.AdoptHerdrAsync(sidecar, _herdrClient!, () => NotifyPaneSetChanged(), _placement, LookupBinding, ct);
                    _sessions.TryAdd(sidecar.SessionId, session);
                    NotifyPaneSetChanged();
                    _logger.LogInformation(
                        "Adopted herdr pane {PaneId} for session {SessionId} (child pid {Pid})",
                        sidecar.PaneId, sidecar.SessionId, sidecar.ChildPid);
                    break;
                case HerdrBarVerdict.RestartPresumedDead:
                    RegisterHerdrExited(sidecar, HerdrExitReasons.RestartPresumedDead);
                    break;
                case HerdrBarVerdict.ChildGone:
                    RegisterHerdrExited(sidecar, HerdrExitReasons.ChildGone);
                    break;
                case HerdrBarVerdict.Unreachable:
                    var pending = RunnerSession.CreatePendingHerdr(
                        sidecar, _settings, _events, _logger, _processLiveness);
                    _sessions.TryAdd(sidecar.SessionId, pending);
                    _logger.LogWarning(
                        "Herdr unreachable while adopting session {SessionId}; registered Pending ({Reason}); sidecar retained",
                        sidecar.SessionId, HerdrPendingReasons.Unreachable);
                    break;
            }
            attempt.Complete(1, verdict.ToString());
        }
    }

    private enum HerdrBarVerdict { Adopt, RestartPresumedDead, ChildGone, Unreachable }

    /// <summary>
    /// CARD-0186 §5 bar. Socket, pane.get, ChildPid listed, then OS before any death verdict.
    /// Orphan kills (R3/R5) run as a side effect of RestartPresumedDead.
    /// </summary>
    private async Task<HerdrBarVerdict> EvaluateHerdrBarAsync(
        HerdrPaneSidecar sidecar, IProcessLivenessProbe probe, CancellationToken ct)
    {
        try
        {
            await _herdrClient!.ConnectAndValidateAsync(ct);
            try
            {
                _ = await _herdrClient.PaneGetAsync(sidecar.PaneId, ct);
            }
            catch (HerdrApiException)
            {
                // R4/R5: unknown pane. OS-alive → orphan kill (R5); then RestartPresumedDead.
                TryKillOrphanedChild(sidecar, probe);
                return HerdrBarVerdict.RestartPresumedDead;
            }

            var proc = await _herdrClient.PaneProcessInfoAsync(sidecar.PaneId, ct);
            var childPresent = sidecar.ChildPid is int child
                && proc.ForegroundProcesses?.Any(p => p.Pid == child) == true;
            if (!childPresent)
            {
                // R2/R3: restored-but-empty (P7) or orphan. OS-alive → kill by pid (R3).
                TryKillOrphanedChild(sidecar, probe);
                return HerdrBarVerdict.RestartPresumedDead;
            }

            // R1: pid present — also require pane.read to answer.
            _ = await _herdrClient.PaneReadAsync(sidecar.PaneId, "visible", stripAnsi: true, lines: 1, ct);
            return HerdrBarVerdict.Adopt;
        }
        catch (HerdrBackendUnavailableException)
        {
            // R7: unreachable + OS-dead → ChildGone (needs no socket). R6: OS-alive → Pending.
            return IsOsAlive(sidecar, probe) ? HerdrBarVerdict.Unreachable : HerdrBarVerdict.ChildGone;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Herdr adoption bar failed for session {SessionId}; treating as RestartPresumedDead",
                sidecar.SessionId);
            TryKillOrphanedChild(sidecar, probe);
            return HerdrBarVerdict.RestartPresumedDead;
        }
    }

    private async Task RetryPendingHerdrAsync(
        RunnerSession session, IProcessLivenessProbe probe, CancellationToken ct)
    {
        var sidecar = session.PendingSidecar;
        if (sidecar is null || _herdrClient is null)
            return;

        var verdict = await EvaluateHerdrBarAsync(sidecar, probe, ct);
        switch (verdict)
        {
            case HerdrBarVerdict.Adopt:
                await session.AdoptHerdrAsync(sidecar, _herdrClient, () => NotifyPaneSetChanged(), _placement, LookupBinding, ct);
                NotifyPaneSetChanged();
                _logger.LogInformation(
                    "Pending herdr session {SessionId} adopted after herdr returned (pane {PaneId})",
                    sidecar.SessionId, sidecar.PaneId);
                break;
            case HerdrBarVerdict.RestartPresumedDead:
                session.CompletePendingAsExited(HerdrExitReasons.RestartPresumedDead);
                HerdrPaneSidecar.Retire(_settings.SessionLogPath, sidecar.SessionId, HerdrExitReasons.RestartPresumedDead);
                _logger.LogWarning(
                    "Pending herdr session {SessionId} exited ({Reason}) once herdr answered",
                    sidecar.SessionId, HerdrExitReasons.RestartPresumedDead);
                break;
            case HerdrBarVerdict.ChildGone:
                session.CompletePendingAsExited(HerdrExitReasons.ChildGone);
                HerdrPaneSidecar.Retire(_settings.SessionLogPath, sidecar.SessionId, HerdrExitReasons.ChildGone);
                _logger.LogWarning(
                    "Pending herdr session {SessionId} exited ({Reason}); child is OS-dead",
                    sidecar.SessionId, HerdrExitReasons.ChildGone);
                break;
            case HerdrBarVerdict.Unreachable:
                break;
        }
    }

    /// <summary>
    /// CARD-0186 R3/R5: kill our named child by pid only on positive identity (pid + start time).
    /// P7 never hit this live (Claude dies with herdr); the arm is defensive.
    /// </summary>
    private bool TryKillOrphanedChild(HerdrPaneSidecar sidecar, IProcessLivenessProbe probe)
    {
        if (sidecar.ChildPid is not int pid)
            return false;
        if (!probe.IsAlive(pid, sidecar.LaunchedAtUtc))
            return false;
        if (string.Equals(sidecar.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "HerdrOrphanNotOurs: pane {PaneId} session {SessionId} pid {Pid} was OS-alive but the sidecar is attached-origin; dropping sidecar, not killing",
                sidecar.PaneId, sidecar.SessionId, pid);
            return false;
        }

        KillPidBestEffort(pid);
        _logger.LogWarning(
            "HerdrOrphanedChildKilled: pane {PaneId} session {SessionId} pid {Pid} was OS-alive but not listed in the pane; killed by pid",
            sidecar.PaneId, sidecar.SessionId, pid);
        return true;
    }

    private static bool IsOsAlive(HerdrPaneSidecar sidecar, IProcessLivenessProbe probe) =>
        sidecar.ChildPid is int pid && probe.IsAlive(pid, sidecar.LaunchedAtUtc);

    private void RegisterHerdrExited(HerdrPaneSidecar sidecar, string reason)
    {
        var exited = RunnerSession.CreateAdoptedHerdrExited(sidecar, _settings, _events, _logger, reason);
        _sessions.TryAdd(sidecar.SessionId, exited);
        HerdrPaneSidecar.Retire(_settings.SessionLogPath, sidecar.SessionId, reason);
        _logger.LogWarning(
            "Herdr pane {PaneId} for session {SessionId} registered as Exited ({Reason})",
            sidecar.PaneId, sidecar.SessionId, reason);
    }

    /// <summary>
    /// Re-asserts every transcript claim recorded in a sidecar. A claim that outlives its session
    /// is deliberate: a previous session's transcript must never become adoptable by a new one, and
    /// a relaunch of the SAME session id (which is what <c>--resume</c> does) re-claims it as the
    /// same owner. Sidecars are pruned on the 14-day cleanup pass, so this cannot grow without bound.
    /// </summary>
    private int RestoreTranscriptClaims()
    {
        var restored = 0;
        var heuristic = 0;
        var exact = 0;
        var suspect = 0;
        foreach (var sidecar in TranscriptSidecar.LoadAll(_settings.SessionLogPath))
        {
            if (sidecar.TranscriptPath is not { } path
                || !_transcriptClaims.TryClaim(path, sidecar.SessionId).Claimed)
                continue;

            restored++;
            var strength = TranscriptClaimRegistry.IsNamesake(path, sidecar.SessionId)
                ? ClaimStrength.Exact
                : ClaimStrength.Heuristic;
            if (strength == ClaimStrength.Exact)
                exact++;
            else
                heuristic++;

            if (strength == ClaimStrength.Heuristic
                && TranscriptClaimRegistry.TryReadNamesake(path) is { } namesake
                && namesake != sidecar.SessionId)
            {
                suspect++;
                var alsoHere = File.Exists(TranscriptSidecar.PathFor(_settings.SessionLogPath, namesake));
                _logger.LogWarning(
                    "Sidecar for session {Prev} claims {Path}, a file named for session {Namesake}{Also}. Restored as a heuristic claim; the namesake's own bind will displace it.",
                    sidecar.SessionId, path, namesake,
                    alsoHere ? ", which also has a sidecar here" : "");
            }
        }

        if (restored > 0)
            _logger.LogInformation(
                "Restored {Count} transcript claim(s) from sidecars ({Heuristic} heuristic, {Exact} exact, {Suspect} on another session's file)",
                restored, heuristic, exact, suspect);
        return restored;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort; a re-read next startup lands in the same branch.
        }
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

    /// <summary>
    /// Marks every "Running" session whose OS process is gone as Exited (reason ProcessVanished)
    /// and publishes the missed SessionExited event. This is the liveness backstop for exits the
    /// normal observer never saw — a session once sat "Running" on a dead PID for a week, keeping
    /// its agent badged Working in the UI with no process behind it. Returns the ids it marked.
    /// Pending herdr sessions are not Running; use
    /// <see cref="SweepVanishedSessionsAsync"/> so the sweep can re-run the adoption bar.
    /// </summary>
    public IReadOnlyList<Guid> SweepVanishedSessions(IProcessLivenessProbe probe)
    {
        var marked = new List<Guid>();
        foreach (var (sessionId, session) in _sessions)
        {
            if (session.MarkVanishedIfDead(probe))
            {
                _logger.LogWarning(
                    "Liveness sweep marked session {SessionId} as Exited: its process vanished without an exit event",
                    sessionId);
                marked.Add(sessionId);
            }
        }

        return marked;
    }

    /// <summary>
    /// CARD-0186 S3: the OS-pid vanish sweep plus the herdr pending arm. For each pending session,
    /// re-run the §5 bar (R1 adopt in place + SessionAdopted; R2–R5 / R7 publish SessionExited).
    /// Running herdr sessions keep the existing OS-pid check from S2.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> SweepVanishedSessionsAsync(
        IProcessLivenessProbe probe, CancellationToken ct)
    {
        var marked = SweepVanishedSessions(probe).ToList();
        if (_herdrClient is null)
            return marked;

        foreach (var (sessionId, session) in _sessions)
        {
            if (!session.IsPendingHerdr)
                continue;

            var before = session.ToDto().Status;
            await RetryPendingHerdrAsync(session, probe, ct);
            if (session.HasExited && before != "Exited")
                marked.Add(sessionId);
        }

        return marked;
    }

    /// <summary>
    /// Detaches from every host WITHOUT killing anything - sessions keep running in their
    /// detached hosts and are re-adopted by the next runner via <see cref="AdoptOrphanedHostsAsync"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var (sessionId, session) in _sessions)
        {
            _sessions.TryRemove(sessionId, out _);
            await session.DisposeAsync();
        }
    }

    private RunnerSession GetSession(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session)
            ? session
            : throw new KeyNotFoundException($"Session '{sessionId}' was not found.");

    /// <summary>Per-session state. Internal so <see cref="HerdrEventPumpService"/> can verify/apply status.</summary>
    internal sealed class RunnerSession : IAsyncDisposable
    {
        internal GrokRulesReceipt? GrokRulesReceipt { get; set; }
        internal VerificationExecutionBinding? VerificationBinding { get; private set; }
        private RunnerCustodyLedger? _custodyLedger;
        private readonly SemaphoreSlim _custodyReadGate = new(1, 1);
        private readonly CancellationTokenSource _custodyLifetime = new();
        private readonly TaskCompletionSource _launchFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _custodyShutdown;

        internal void SetCustody(RunnerCustodyLedger ledger, VerificationExecutionBinding binding)
        {
            ledger.Store.RequireBinding(binding);
            VerificationBinding = binding;
            _custodyLedger = ledger;
        }
        private readonly Guid _sessionId;
        private readonly SessionRunnerSettings _settings;
        private readonly SessionRunnerEventHub _events;
        private readonly ILogger _logger;
        private readonly object _gate = new();
        private readonly StringBuilder _liveBuffer = new();
        // Completes true once the pty-host pipe is connected and the child launched; false once
        // the session is dead (failed start, exit, vanish, dispose). Input that arrives during
        // the cold-start window waits on this instead of failing (live miss 2026-08-09: the boot
        // prompt landed ~1s before the host process existed and was lost as an unhandled 500).
        private readonly TaskCompletionSource<bool> _clientReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TranscriptClaimRegistry? _transcriptClaims;
        private readonly IProcessLivenessProbe _processLiveness;
        // Everything typed into this session, normalized and bounded — the evidence rule C4 needs
        // to prove a candidate transcript is ours (CARD-0006).
        private readonly SessionInputLog _inputLog = new();

        private PtyHostClient? _client;
        private ISessionChild? _herdrChild;
        private string? _herdrAgentStatus;
        private DateTime? _herdrAgentStatusSinceUtc;
        private Action? _onHerdrPaneSetChanged;
        private int _hostPid;
        private int? _childPid;
        private DateTime _startedAt;
        private TerminalScreen? _screen;
        private string? _ansiLogPath;
        private ITranscriptTailer? _tailer;
        private TranscriptSidecar? _sidecar;
        private long _lastSequence;
        private string _status = "Starting";
        private int? _exitCode;
        private string _exitReason = PtyExitReason.Unknown.ToString();
        private string? _exitReasonOverride;
        private bool _adopted;
        private string _backend = SessionBackends.PtyHost;
        public string Backend => _backend;
        private string? _pendingReason;
        private HerdrPaneSidecar? _pendingSidecar;
        private DateTime? _herdrVerifiedAtUtc;
        private string? _herdrOrigin;
        private DateTime? _acceptedStartedAt;

        internal DateTime? AcceptedStartedAt => _acceptedStartedAt;

        internal void BindAcceptedGeneration(DateTime? value) =>
            _acceptedStartedAt = value is { } v ? SessionGeneration.Normalize(v) : null;

        private RunnerSessionExitedEvent ExitEnvelope(int? exitCode, string reason, long lastSequence) =>
            new(_sessionId, exitCode, reason, lastSequence, AcceptedStartedAt: _acceptedStartedAt);

        public RunnerSession(
            Guid sessionId,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger,
            TranscriptClaimRegistry? transcriptClaims = null,
            IProcessLivenessProbe? processLiveness = null)
        {
            _sessionId = sessionId;
            _settings = settings;
            _events = events;
            _logger = logger;
            _transcriptClaims = transcriptClaims;
            _processLiveness = processLiveness ?? new SystemProcessLivenessProbe();
        }

        public DateTime StartedAt => _startedAt;

        public void OnTranscriptClaimRevoked(string path, Guid newOwner) =>
            _tailer?.NotifyClaimRevoked(path, newOwner);

        /// <summary>CARD-0162: pane id when this session is on the herdr lane.</summary>
        internal string? HerdrPaneId => (_herdrChild as HerdrPaneChild)?.PaneId;

        /// <summary>CARD-0213: <see cref="HerdrPaneOrigins"/> once known.</summary>
        internal string? HerdrOrigin => _herdrOrigin;

        /// <summary>CARD-0186 S3: adoption is waiting on herdr.</summary>
        internal bool IsPendingHerdr => _pendingReason is not null;

        /// <summary>CARD-0186 S3: sidecar retained while pending (herdr might come back).</summary>
        internal HerdrPaneSidecar? PendingSidecar => _pendingSidecar;

        /// <summary>
        /// CARD-0161 / CARD-0164: fold pane.get revision AND the runner content-delta counter into
        /// LastSequence; capture AgentStatus. Herdr's own revision is sticky on 0.8.2 — the
        /// content counter is the real advance signal.
        /// </summary>
        public async Task RefreshHerdrSurfaceAsync(CancellationToken ct)
        {
            if (_herdrChild is not HerdrPaneChild herdr)
                return;

            try
            {
                var (revision, contentSequence, status) = await herdr.RefreshStatusAsync(ct);
                lock (_gate)
                {
                    _lastSequence = Math.Max(_lastSequence, Math.Max(revision, contentSequence));
                }

                if (status is not null)
                    ApplyHerdrAgentStatus(status, DateTime.UtcNow, publishEvent: false);
            }
            catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
            {
                _logger.LogDebug(ex, "Herdr pane.get refresh failed for session {SessionId}", _sessionId);
            }
        }

        /// <summary>
        /// CARD-0162: update the herdr status cache. <paramref name="publishEvent"/> is true for
        /// pump-driven changes (SSE to server); GET refresh updates the cache silently.
        /// </summary>
        internal void ApplyHerdrAgentStatus(string status, DateTime observedAtUtc, bool publishEvent = true)
        {
            string? previous;
            lock (_gate)
            {
                previous = _herdrAgentStatus;
                if (string.Equals(previous, status, StringComparison.Ordinal))
                {
                    // Same value — since does not move (hysteresis).
                    return;
                }

                _herdrAgentStatus = status;
                _herdrAgentStatusSinceUtc = observedAtUtc;
            }

            if (publishEvent)
            {
                _events.Publish(
                    SessionRunnerEventNames.SessionAgentStatus,
                    new RunnerAgentStatusEvent(_sessionId, status, previous, observedAtUtc));
            }
        }

        /// <summary>
        /// CARD-0162 / CARD-0186 S2: §6A evidence bar as a runtime check. Events are triggers only.
        /// OS-pid axis is consulted before any PaneClosed verdict so a herdr restart that left our
        /// child alive is the orphan case (kill by pid, RestartPresumedDead), not a clean close.
        /// Unreachable → no verdict (true). R13: healthy pane + replayed pane_closed → bar passes.
        /// </summary>
        internal async Task<bool> VerifyHerdrLivenessAsync(HerdrClient client, CancellationToken ct)
        {
            if (_herdrChild is not HerdrPaneChild herdr || herdr.PaneId is null || herdr.Sidecar is null)
                return true;

            var sidecar = herdr.Sidecar;
            try
            {
                try
                {
                    _ = await client.PaneGetAsync(sidecar.PaneId, ct);
                }
                catch (HerdrApiException)
                {
                    CloseHerdrAfterBarFailed(herdr, sidecar);
                    return false;
                }

                if (sidecar.ChildPid is int childPid)
                {
                    var proc = await client.PaneProcessInfoAsync(sidecar.PaneId, ct);
                    var childPresent = proc.ForegroundProcesses?.Any(p => p.Pid == childPid) == true;
                    if (!childPresent)
                    {
                        CloseHerdrAfterBarFailed(herdr, sidecar);
                        return false;
                    }
                }

                // ChildPid null: pane existence alone (weaker bar, stated honestly).
                return true;
            }
            catch (HerdrBackendUnavailableException)
            {
                // Unreachable is never evidence of death.
                return true;
            }
        }

        private void CloseHerdrAfterBarFailed(HerdrPaneChild herdr, HerdrPaneSidecar sidecar)
        {
            if (string.Equals(sidecar.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "HerdrOrphanNotOurs: pane {PaneId} session {SessionId} pid {Pid} was OS-alive but the sidecar is attached-origin; dropping sidecar, not killing",
                    sidecar.PaneId, sidecar.SessionId, sidecar.ChildPid);
                herdr.RaiseVerifiedClosed(HerdrExitReasons.RestartPresumedDead);
                return;
            }

            if (sidecar.ChildPid is int pid && _processLiveness.IsAlive(pid, sidecar.LaunchedAtUtc))
            {
                KillPidBestEffort(pid);
                _logger.LogWarning(
                    "HerdrOrphanedChildKilled: pane {PaneId} session {SessionId} pid {Pid} was OS-alive but not listed in the pane; killed by pid",
                    sidecar.PaneId, sidecar.SessionId, pid);
                herdr.RaiseVerifiedClosed(HerdrExitReasons.RestartPresumedDead);
                return;
            }

            herdr.RaiseVerifiedClosed(HerdrExitReasons.PaneClosed);
        }

        /// <summary>CARD-0160 herdr lane — shares transcript/input-log machinery with the pty path.</summary>
        public async Task StartHerdrAsync(
            RunnerLaunchRequest request,
            HerdrClient herdrClient,
            Func<IReadOnlyList<HerdrPaneAllocator.LivePane>> liveAntiphonPanes,
            Action onPaneSetChanged,
            HerdrPlacementCoordinator placement,
            Func<string, Guid?, PaneBinding?> findBound,
            CancellationToken ct)
        {
            Directory.CreateDirectory(_settings.SessionLogPath);
            _ansiLogPath = Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(request.Cols > 0 ? request.Cols : 120, request.Rows > 0 ? request.Rows : 30);
            _onHerdrPaneSetChanged = onPaneSetChanged;

            try
            {
                _backend = SessionBackends.Herdr;
                _herdrChild = new HerdrPaneChild(
                    herdrClient, _settings, _logger, liveAntiphonPanes, _processLiveness,
                    placement,
                    findBound);
                _herdrChild.Exited += exit =>
                {
                    lock (_gate)
                    {
                        if (_status == "Exited") return;
                        _status = "Exited";
                        _exitCode = exit.ExitCode;
                        _exitReason = exit.Reason;
                    }

                    _clientReady.TrySetResult(false);
                    _exited.TrySetResult();
                    _events.Publish(
                        SessionRunnerEventNames.SessionExited,
                        ExitEnvelope(exit.ExitCode, exit.Reason, 0));
                    _onHerdrPaneSetChanged?.Invoke();
                };

                var started = await _herdrChild.LaunchAsync(request, ct);
                _childPid = started.ChildPid;
                _startedAt = started.ChildStartUtc;
                _herdrOrigin = (_herdrChild as HerdrPaneChild)?.Sidecar?.Origin ?? HerdrPaneOrigins.Launched;
                lock (_gate)
                    _status = "Running";
                _clientReady.TrySetResult(true);

                _events.Publish(
                    SessionRunnerEventNames.SessionStarted,
                    new RunnerSessionStartedEvent(_sessionId, _childPid, _startedAt, _acceptedStartedAt));

                // Same transcript binding as pty — format selected from request.TranscriptFormat
                // (CARD-0187: herdr no longer forces Claude).
                StartTailerFor(request, started.ChildStartUtc);
            }
            catch
            {
                _clientReady.TrySetResult(false);
                if (_herdrChild is not null)
                {
                    try { await _herdrChild.KillAsync(CancellationToken.None); }
                    catch { /* tear-down must not replace the launch exception */ }
                    await _herdrChild.DisposeAsync();
                    _herdrChild = null;
                }

                throw;
            }
        }

        /// <summary>
        /// CARD-0213: bind a pane Antiphon did not launch. Mirrors <see cref="AdoptHerdrAsync"/>
        /// with <c>_adopted = false</c> and a <see cref="SessionRunnerEventNames.SessionStarted"/>
        /// publish, not SessionAdopted.
        /// </summary>
        public async Task AttachHerdrAsync(
            HerdrAttachRequest request,
            HerdrClient herdrClient,
            Func<IReadOnlyList<HerdrPaneAllocator.LivePane>> liveAntiphonPanes,
            Action onPaneSetChanged,
            HerdrPlacementCoordinator placement,
            Func<string, Guid?, PaneBinding?> findBound,
            CancellationToken ct)
        {
            Directory.CreateDirectory(_settings.SessionLogPath);
            _ansiLogPath = Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(120, 30);
            _onHerdrPaneSetChanged = onPaneSetChanged;

            try
            {
                _adopted = false;
                _backend = SessionBackends.Herdr;
                _herdrOrigin = HerdrPaneOrigins.Attached;
                _herdrChild = new HerdrPaneChild(
                    herdrClient, _settings, _logger, liveAntiphonPanes, _processLiveness,
                    placement,
                    findBound);
                _herdrChild.Exited += exit =>
                {
                    lock (_gate)
                    {
                        if (_status == "Exited") return;
                        _status = "Exited";
                        _exitCode = exit.ExitCode;
                        _exitReason = exit.Reason;
                    }

                    _clientReady.TrySetResult(false);
                    _exited.TrySetResult();
                    _events.Publish(
                        SessionRunnerEventNames.SessionExited,
                        ExitEnvelope(exit.ExitCode, exit.Reason, 0));
                    _onHerdrPaneSetChanged?.Invoke();
                };

                var attached = await ((HerdrPaneChild)_herdrChild).AttachAsync(request, ct);
                _childPid = attached.Started.ChildPid;
                _startedAt = attached.Started.ChildStartUtc;
                lock (_gate)
                    _status = "Running";
                _clientReady.TrySetResult(true);

                _events.Publish(
                    SessionRunnerEventNames.SessionStarted,
                    new RunnerSessionStartedEvent(_sessionId, _childPid, _startedAt, _acceptedStartedAt));

                StartAttachedTailer(request, attached);
            }
            catch
            {
                _clientReady.TrySetResult(false);
                if (_herdrChild is not null)
                {
                    try { await _herdrChild.KillAsync(CancellationToken.None); }
                    catch { /* tear-down must not replace the attach exception */ }
                    await _herdrChild.DisposeAsync();
                    _herdrChild = null;
                }

                throw;
            }
        }

        /// <summary>
        /// CARD-0213: Grok uses the GUID-located path (offset 0); Claude exact-or-discovery;
        /// Codex discovery. Conversation predates us so resumeLaunch is true (C3 waived).
        /// </summary>
        private void StartAttachedTailer(HerdrAttachRequest request, HerdrAttachResult attached)
        {
            var cwd = attached.Sidecar.Cwd ?? "";
            var childStartUtc = attached.Started.ChildStartUtc;
            var format = string.IsNullOrWhiteSpace(request.TranscriptFormat)
                ? TranscriptFormats.Claude
                : request.TranscriptFormat;

            if (string.Equals(format, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase)
                && attached.GrokUpdatesPath is { } grokPath)
            {
                SaveSidecar(new TranscriptSidecar
                {
                    SessionId = _sessionId,
                    Cwd = cwd,
                    ChildStartUtc = childStartUtc,
                    ResumeLaunch = true,
                    TranscriptPath = grokPath,
                    How = TranscriptBindMethods.Deterministic,
                    Format = TranscriptFormats.Grok,
                });
                _tailer = new GrokTranscriptTailer(
                    _sessionId, grokPath, _events, _logger, inputLog: _inputLog);
                _tailer.Start();
                return;
            }

            if (string.Equals(format, TranscriptFormats.Codex, StringComparison.OrdinalIgnoreCase))
            {
                SaveSidecar(new TranscriptSidecar
                {
                    SessionId = _sessionId,
                    Cwd = cwd,
                    ChildStartUtc = childStartUtc,
                    ResumeLaunch = true,
                    TranscriptPath = null,
                    How = null,
                    Format = TranscriptFormats.Codex,
                });
                _tailer = new CodexTranscriptTailer(
                    _sessionId, cwd, _events, _logger,
                    claims: _transcriptClaims,
                    inputLog: _inputLog,
                    firstInputUtc: null,
                    childStartUtc: childStartUtc,
                    resumeLaunch: true,
                    sessionsRoot: CodexTranscriptTailer.ResolveSessionsRoot(null),
                    onBound: RecordTranscriptBinding,
                    onUnbound: RecordTranscriptUnbinding);
                _tailer.Start();
                return;
            }

            string? knownPath = null;
            if (!string.IsNullOrEmpty(cwd))
            {
                try
                {
                    var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                    var root = string.IsNullOrWhiteSpace(configDir)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                        : configDir;
                    var candidate = Path.Combine(
                        root,
                        "projects",
                        Uri.EscapeDataString(Path.GetFullPath(cwd)),
                        $"{_sessionId:D}.jsonl");
                    if (File.Exists(candidate))
                        knownPath = candidate;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Could not resolve Claude exact transcript path for attached session {SessionId}", _sessionId);
                }
            }

            SaveSidecar(new TranscriptSidecar
            {
                SessionId = _sessionId,
                Cwd = cwd,
                ChildStartUtc = childStartUtc,
                ResumeLaunch = true,
                TranscriptPath = knownPath,
                How = knownPath is null ? null : TranscriptBindMethods.Exact,
                Format = TranscriptFormats.Claude,
            });
            _tailer = new TranscriptTailer(
                _sessionId, cwd, _events, _logger,
                claims: _transcriptClaims,
                inputLog: _inputLog,
                firstInputUtc: null,
                childStartUtc: childStartUtc,
                resumeLaunch: true,
                knownTranscriptPath: knownPath,
                onBound: RecordTranscriptBinding,
                onUnbound: RecordTranscriptUnbinding,
                knownSessions: new SidecarKnownSessionProbe(_settings.SessionLogPath));
            _tailer.Start();
        }

        /// <summary>
        /// CARD-0187: same tailer selection the pty lane uses, now shared with herdr so a Grok/Codex
        /// herdr launch does not force <see cref="TranscriptFormats.Claude"/>.
        /// </summary>
        private void StartTailerFor(RunnerLaunchRequest request, DateTime childStartUtc)
        {
            if (!request.TranscriptEnabled)
                return;

            var transcriptTailer = request.TranscriptFormat is { } requestedFormat
                ? TryResolveTranscriptTailer(requestedFormat, out var resolvedTailer)
                    ? resolvedTailer
                    : throw new UnsupportedTranscriptFormatException(requestedFormat, SupportedTranscriptFormats)
                : TranscriptTailerKind.Claude;
            if (transcriptTailer == TranscriptTailerKind.Grok)
            {
                // Grok's transcript path is DETERMINISTIC (we pass --session-id and grok
                // honours it — measured 1.0.5, CARD-0080 S1; CARD-0187 K1: the dir exists on
                // herdr before the first prompt), so the sidecar records the bound path up
                // front and none of the Claude discovery/claim machinery runs.
                var updatesPath = GrokTranscriptTailer.ResolveUpdatesPath(
                    request.Env, request.Cwd, _sessionId);
                SaveSidecar(new TranscriptSidecar
                {
                    SessionId = _sessionId,
                    Cwd = request.Cwd,
                    ChildStartUtc = childStartUtc,
                    ResumeLaunch = IsResumeLaunch(request.Args),
                    TranscriptPath = updatesPath,
                    How = TranscriptBindMethods.Deterministic,
                    Format = TranscriptFormats.Grok,
                });

                _tailer = new GrokTranscriptTailer(
                    _sessionId, updatesPath, _events, _logger, inputLog: _inputLog);
                _tailer.Start();
            }
            else if (transcriptTailer == TranscriptTailerKind.Codex)
            {
                SaveSidecar(new TranscriptSidecar
                {
                    SessionId = _sessionId,
                    Cwd = request.Cwd,
                    ChildStartUtc = childStartUtc,
                    ResumeLaunch = IsCodexResumeLaunch(request.Args),
                    TranscriptPath = null,
                    How = null,
                    Format = TranscriptFormats.Codex,
                });

                _tailer = new CodexTranscriptTailer(
                    _sessionId, request.Cwd, _events, _logger,
                    claims: _transcriptClaims,
                    inputLog: _inputLog,
                    firstInputUtc: null,
                    childStartUtc: childStartUtc,
                    resumeLaunch: IsCodexResumeLaunch(request.Args),
                    sessionsRoot: CodexTranscriptTailer.ResolveSessionsRoot(request.Env),
                    onBound: RecordTranscriptBinding,
                    onUnbound: RecordTranscriptUnbinding);
                _tailer.Start();
            }
            else
            {
                var agentName = FindArgValue(request.Args, "--name");
                var resumeLaunch = IsResumeLaunch(request.Args);
                SaveSidecar(new TranscriptSidecar
                {
                    SessionId = _sessionId,
                    Cwd = request.Cwd,
                    AgentName = agentName,
                    ChildStartUtc = childStartUtc,
                    ResumeLaunch = resumeLaunch,
                    TranscriptPath = null,
                    How = null,
                });

                _tailer = new TranscriptTailer(
                    _sessionId, request.Cwd, _events, _logger,
                    claims: _transcriptClaims,
                    inputLog: _inputLog,
                    firstInputUtc: null,
                    childStartUtc: childStartUtc,
                    agentName: agentName,
                    resumeLaunch: resumeLaunch,
                    onBound: RecordTranscriptBinding,
                    onUnbound: RecordTranscriptUnbinding,
                    knownSessions: new SidecarKnownSessionProbe(_settings.SessionLogPath));
                _tailer.Start();
            }
        }

        public async Task StartAsync(RunnerLaunchRequest request, PtyHostLauncher launcher, CancellationToken ct)
        {
            Directory.CreateDirectory(_settings.SessionLogPath);
            Directory.CreateDirectory(_settings.PtyHostLogDir);
            _ansiLogPath = Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(request.Cols, request.Rows);

            try
            {
                _hostPid = await launcher.LaunchDetachedAsync(
                    _sessionId,
                    _settings.PtyHostManifestDir,
                    hostLogFile: Path.Combine(_settings.PtyHostLogDir, $"{_sessionId:N}.log"),
                    launchTimeout: TimeSpan.FromSeconds(_settings.PtyHostLaunchTimeoutSec),
                    lingerTtl: TimeSpan.FromHours(_settings.PtyHostLingerHours),
                    ringCapChars: Math.Max(1, _settings.ReplayBufferMaxChars),
                    // CARD-0045: state the backend on the host's command line instead of relying on it
                    // inheriting our environment block. Production is unchanged — the daemon exports the
                    // same SessionRunner:PtyBackend value into ANTIPHON_PTY_BACKEND at startup, so the
                    // host now hears the same answer twice. What it BUYS is the host-mediated tests: a
                    // caller that builds its own runtime (DirectSessionRunnerClient) could not reach
                    // PtyAgentRunner's per-instance override at all, three processes down, and so ran on
                    // whatever the test process had inherited.
                    ptyBackend: _settings.PtyBackend,
                    custodyStoreRoot: _custodyLedger?.Store.Root,
                    ct: ct);

                _client = await PtyHostClient.ConnectAsync(
                    PtyHostProtocol.PipeNameFor(_sessionId),
                    TimeSpan.FromSeconds(_settings.PtyHostConnectTimeoutSec),
                    ct);
                _client.OnOutput += HandleOutput;
                _client.OnExited += HandleExited;
                _client.OnDisconnected += HandleDisconnected;

                if (VerificationBinding is { } binding)
                {
                    using var hostProcess = System.Diagnostics.Process.GetProcessById(_hostPid);
                    if (_client.Hello.SessionId != _sessionId || _client.Hello.HostInstanceId is not { } hostInstance
                        || _client.Hello.Features?.Contains("verificationCustodyV1") != true)
                        throw new VerificationCustodyException("verification_custody_unsupported_backend");
                    _custodyLedger!.RecordConnectedHost(binding,
                        new(hostInstance, _hostPid, hostProcess.StartTime.ToUniversalTime()));
                }

                var launched = await _client.LaunchAsync(
                    new LaunchMessage(
                        request.Exe,
                        request.Args,
                        request.Env,
                        request.Cwd,
                        request.Cols,
                        request.Rows,
                        request.MemoryLimitMb,
                        request.TranscriptEnabled,
                        _ansiLogPath,
                        GrokRulesReceipt, VerificationBinding, _custodyLedger?.Store.StoreId,
                        request.AcceptedStartedAt),
                    ct);

                _childPid = launched.ChildPid;
                _startedAt = launched.ChildStartTimeUtc;
                lock (_gate)
                {
                    _status = "Running";
                }
                _clientReady.TrySetResult(true);

                _events.Publish(
                    SessionRunnerEventNames.SessionStarted,
                    new RunnerSessionStartedEvent(_sessionId, _childPid, _startedAt, _acceptedStartedAt));

                if (await _client.AttachAsync(0, ct) is { } resync)
                {
                    // Impossible on a fresh host (nothing can have left the ring yet) — but if it
                    // ever happens, the ansi log still has everything; log and continue live.
                    _logger.LogWarning(
                        "Fresh session {SessionId} answered attach with resync ({First}..{Last})",
                        _sessionId, resync.FirstAvailableSeq, resync.LastSeq);
                    await _client.AttachAsync(resync.LastSeq, ct);
                }

                StartTailerFor(request, _startedAt);
            }
            catch
            {
                // Never leave an orphaned empty host behind a failed start.
                _clientReady.TrySetResult(false);
                TearDownFailedLaunch();
                throw;
            }
            finally { _launchFinished.TrySetResult(); }
        }

        /// <summary>
        /// CARD-0086: kill the host a failed <see cref="StartAsync"/> spawned. DisposeAsync is
        /// detach-not-kill (pty-host split); this is the runner analogue of
        /// <c>AgentSessionService.KillAndDisposeAsync</c>. Never throws — a kill failure must
        /// not replace the launch exception. Double-kill is harmless.
        /// </summary>
        internal void TearDownFailedLaunch()
        {
            if (_hostPid <= 0)
                return;

            try
            {
                KillHostIfStillOurs();
            }
            catch
            {
                // Already gone / pid reuse / access denied.
            }
        }

        /// <summary>
        /// Re-attach to a host that survived a runner restart. Rebuilds runner-side interpretation
        /// (screen, live buffer) from the ansi log tail, resumes live streaming at the host's
        /// sequence, and - if the child exited while the runner was down - publishes the missed
        /// SessionExited and acks Shutdown. Returns true if the session is still running.
        /// </summary>
        public async Task<bool> AdoptAsync(PtyHostManifest manifest, CancellationToken ct)
        {
            try { return await AdoptCoreAsync(manifest, ct); }
            finally { _launchFinished.TrySetResult(); }
        }

        private async Task<bool> AdoptCoreAsync(PtyHostManifest manifest, CancellationToken ct)
        {
            if (manifest.GrokRulesReceipt is { } receipt
                && await new GrokRulesFileStore(_settings.SessionLogPath, _settings.GrokRules)
                    .VerifyAsync(_sessionId, receipt, ct))
                GrokRulesReceipt = receipt;
            _hostPid = manifest.HostPid;
            _childPid = manifest.ChildPid;
            _adopted = true;
            _startedAt = manifest.ChildStartTimeUtc ?? manifest.CreatedAtUtc;
            _ansiLogPath = manifest.AnsiLogPath
                ?? Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(
                manifest.Cols > 0 ? manifest.Cols : 120,
                manifest.Rows > 0 ? manifest.Rows : 30);

            _client = await PtyHostClient.ConnectAsync(manifest.PipeName, TimeSpan.FromSeconds(5), ct);
            if (VerificationBinding is { } binding)
            {
                var identity = manifest.VerificationHost
                    ?? throw new VerificationCustodyException("verification_custody_identity_mismatch");
                _custodyLedger!.RequireHost(binding, identity);
                if (_client.Hello.SessionId != binding.Generation.SessionId
                    || _client.Hello.HostInstanceId != identity.HostInstanceId
                    || _client.Hello.Features?.Contains("verificationCustodyV1") != true)
                    throw new VerificationCustodyException("verification_custody_identity_mismatch");
            }
            var runnerVersion = RunnerBuildIdentity.Resolve().InformationalVersion;
            if (!string.Equals(_client.Hello.HostVersion, runnerVersion, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Adopted pty-host for session {SessionId} was built as {HostVersion}, while this session runner is {RunnerVersion}",
                    _sessionId, _client.Hello.HostVersion, runnerVersion);
            }
            _client.OnOutput += HandleOutput;
            _client.OnExited += HandleExited;
            _client.OnDisconnected += HandleDisconnected;

            var status = await _client.GetStatusAsync(ct);
            _childPid = status.ChildPid ?? _childPid;
            RebuildInterpretationFromAnsiLog(status.LastSeq);

            if (status.Status == PtyHostStatus.Exited)
            {
                // HandleExited publishes the missed event and acks Shutdown.
                HandleExited(new ExitedMessage(status.ExitCode, status.ExitReason ?? "Unknown", status.LastSeq));
                return false;
            }

            lock (_gate)
            {
                _status = "Running";
            }

            var attachAt = status.LastSeq;
            for (var attempt = 0; ; attempt++)
            {
                if (await _client.AttachAsync(attachAt, ct) is not { } resync)
                    break;

                // Output flooded past the ring between Status and Attach; the ansi log has it all.
                if (attempt >= 3)
                    throw new InvalidOperationException(
                        $"Session {_sessionId}: attach kept resyncing (ring {resync.FirstAvailableSeq}..{resync.LastSeq}).");
                RebuildInterpretationFromAnsiLog(resync.LastSeq);
                attachAt = resync.LastSeq;
            }

            _clientReady.TrySetResult(true);
            _events.Publish(
                SessionRunnerEventNames.SessionAdopted,
                new RunnerSessionAdoptedEvent(_sessionId, _childPid, _startedAt, status.LastSeq, _acceptedStartedAt));

            if (manifest.TranscriptEnabled
                && (VerificationBinding is null || _custodyLedger!.ReadFinal(VerificationBinding) is null))
            {
                // Re-tail the file we already knew about instead of re-running discovery: after a
                // restart the input log is empty, so nothing could prove ownership of a candidate,
                // and the heuristic that used to fill that gap is what bound an agent to the
                // operator's own conversation (CARD-0006).
                var sidecar = TranscriptSidecar.TryLoad(
                    TranscriptSidecar.PathFor(_settings.SessionLogPath, _sessionId));
                var cwd = manifest.Cwd ?? sidecar?.Cwd ?? "";
                RestoreTailerFromSidecar(sidecar, cwd, manifest.ChildStartTimeUtc ?? sidecar?.ChildStartUtc);
            }

            return true;
        }

        /// <summary>Persists the transcript binding so the next runner re-tails it without guessing.</summary>
        private void RecordTranscriptBinding(string transcriptPath, string how)
        {
            var current = _sidecar ?? new TranscriptSidecar { SessionId = _sessionId, ChildStartUtc = _startedAt };
            var persistedHow = how == TranscriptBindMethods.Sidecar
                && current.TranscriptPath is { } existing
                && string.Equals(existing, transcriptPath, StringComparison.OrdinalIgnoreCase)
                && current.How is not null
                    ? current.How
                    : how;
            SaveSidecar(current with { TranscriptPath = transcriptPath, How = persistedHow });
        }

        private void RecordTranscriptUnbinding()
        {
            var current = _sidecar ?? new TranscriptSidecar { SessionId = _sessionId, ChildStartUtc = _startedAt };
            SaveSidecar(current with { TranscriptPath = null, How = null });
        }

        private void SaveSidecar(TranscriptSidecar sidecar)
        {
            _sidecar = sidecar with { UpdatedAtUtc = DateTime.UtcNow };
            try
            {
                _sidecar.SaveAtomic(TranscriptSidecar.PathFor(_settings.SessionLogPath, _sessionId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: losing the sidecar costs this session its no-guess restart path, not
                // its transcript. It is never a reason to fail a launch.
                _logger.LogWarning(ex, "Could not write the transcript sidecar for session {SessionId}", _sessionId);
            }
        }

        private static string? FindArgValue(IReadOnlyList<string> args, string name)
        {
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i] == name && i + 1 < args.Count)
                    return args[i + 1];
                if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                    return args[i][(name.Length + 1)..];
            }

            return null;
        }

        // --resume/--continue replay a conversation whose records legitimately predate this launch,
        // which is exactly what rule C3 would otherwise reject.
        /// <summary>
        /// Codex's own resume vocabulary: <c>codex resume</c> / <c>codex fork</c> (subcommands, not
        /// flags — <c>codex --help</c>, 0.147.0). Deliberately separate from
        /// <see cref="IsResumeLaunch"/>: Codex's <c>-c</c> is <c>--config</c>, not <c>--continue</c>,
        /// so reusing the Claude predicate would waive C3 on every configured launch.
        /// </summary>
        private static bool IsCodexResumeLaunch(IReadOnlyList<string> args) =>
            args.Any(a => a is "resume" or "fork");

        private static bool IsResumeLaunch(IReadOnlyList<string> args) =>
            args.Any(a =>
                a is "--resume" or "-r" or "--continue" or "-c"
                || a.StartsWith("--resume=", StringComparison.Ordinal));

        /// <summary>
        /// Registers a session whose host is gone: the fate is whatever the manifest recorded
        /// (a real exit while the runner was down, or ProcessVanished when the host died cold).
        /// Publishes the missed SessionExited so late subscribers reconcile off the registry.
        /// </summary>
        public static RunnerSession CreateAdoptedExited(
            PtyHostManifest manifest,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger)
        {
            var session = new RunnerSession(manifest.SessionId, settings, events, logger)
            {
                _hostPid = manifest.HostPid,
                _childPid = manifest.ChildPid,
                _startedAt = manifest.ChildStartTimeUtc ?? manifest.CreatedAtUtc,
                _ansiLogPath = manifest.AnsiLogPath,
                _adopted = true,
                _status = "Exited",
                _exitCode = manifest.ExitCode ?? -1,
                _exitReason = manifest.ExitReason ?? "ProcessVanished",
                _acceptedStartedAt = manifest.AcceptedStartedAt is { } accepted
                    ? SessionGeneration.Normalize(accepted) : null,
            };
            session._clientReady.TrySetResult(false);
            session._exited.TrySetResult();
            session._launchFinished.TrySetResult();

            events.Publish(
                SessionRunnerEventNames.SessionExited,
                session.ExitEnvelope(session._exitCode, session._exitReason, 0));
            return session;
        }

        public static RunnerSession CreateAdoptedHerdrExited(
            HerdrPaneSidecar sidecar,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger,
            string reason)
        {
            var session = new RunnerSession(sidecar.SessionId, settings, events, logger)
            {
                _childPid = sidecar.ChildPid,
                _startedAt = sidecar.LaunchedAtUtc,
                _adopted = true,
                _status = "Exited",
                _exitCode = null,
                _exitReason = reason,
                _backend = SessionBackends.Herdr,
                _herdrOrigin = sidecar.Origin ?? HerdrPaneOrigins.Launched,
                _acceptedStartedAt = sidecar.AcceptedStartedAt is { } accepted
                    ? SessionGeneration.Normalize(accepted) : null,
            };
            session._clientReady.TrySetResult(false);
            session._exited.TrySetResult();
            events.Publish(
                SessionRunnerEventNames.SessionExited,
                session.ExitEnvelope(null, reason, 0));
            return session;
        }

        /// <summary>
        /// CARD-0186 S3 R6: herdr unreachable at runner restart and the sidecar's child is still
        /// OS-alive. Listed as Starting + Adopted with Pending=HerdrUnreachable; sidecar retained.
        /// </summary>
        public static RunnerSession CreatePendingHerdr(
            HerdrPaneSidecar sidecar,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger,
            IProcessLivenessProbe processLiveness)
        {
            return new RunnerSession(sidecar.SessionId, settings, events, logger, processLiveness: processLiveness)
            {
                _childPid = sidecar.ChildPid,
                _startedAt = sidecar.LaunchedAtUtc,
                _adopted = true,
                _status = "Starting",
                _backend = SessionBackends.Herdr,
                _pendingReason = HerdrPendingReasons.Unreachable,
                _pendingSidecar = sidecar,
                _herdrOrigin = sidecar.Origin ?? HerdrPaneOrigins.Launched,
                _acceptedStartedAt = sidecar.AcceptedStartedAt is { } accepted
                    ? SessionGeneration.Normalize(accepted) : null,
            };
        }

        /// <summary>
        /// CARD-0186 S3: a pending session reached a death verdict (R2–R5 / R7). Sidecar deletion
        /// is the caller's job so the bar's orphan-kill side effects stay in one place.
        /// </summary>
        internal void CompletePendingAsExited(string reason, int? exitCode = null)
        {
            lock (_gate)
            {
                if (_status == "Exited")
                    return;
                _status = "Exited";
                _exitCode = exitCode;
                _exitReason = reason;
                _pendingReason = null;
            }

            _clientReady.TrySetResult(false);
            _exited.TrySetResult();
            _events.Publish(
                SessionRunnerEventNames.SessionExited,
                ExitEnvelope(exitCode, reason, 0));
        }

        /// <summary>
        /// CARD-0186 S3: stamp <see cref="RunnerSessionDto.HerdrVerifiedAtUtc"/> only on a
        /// positive bar pass. Unreachable / pending / not-herdr never stamp — "I could not ask"
        /// is not evidence of liveness.
        /// </summary>
        internal async Task<bool> TryStampHerdrVerifiedAsync(HerdrClient client, CancellationToken ct)
        {
            if (_pendingReason is not null)
                return false;
            if (_herdrChild is not HerdrPaneChild herdr || herdr.PaneId is null || herdr.Sidecar is null)
                return false;

            var sidecar = herdr.Sidecar;
            try
            {
                try
                {
                    _ = await client.PaneGetAsync(sidecar.PaneId, ct);
                }
                catch (HerdrApiException)
                {
                    CloseHerdrAfterBarFailed(herdr, sidecar);
                    return false;
                }

                if (sidecar.ChildPid is int childPid)
                {
                    var proc = await client.PaneProcessInfoAsync(sidecar.PaneId, ct);
                    var childPresent = proc.ForegroundProcesses?.Any(p => p.Pid == childPid) == true;
                    if (!childPresent)
                    {
                        CloseHerdrAfterBarFailed(herdr, sidecar);
                        return false;
                    }
                }

                lock (_gate)
                    _herdrVerifiedAtUtc = DateTime.UtcNow;
                return true;
            }
            catch (HerdrBackendUnavailableException)
            {
                return false;
            }
        }

        /// <summary>Re-attach a surviving herdr pane after a runner restart (CARD-0160 §6A positive arm).</summary>
        public async Task AdoptHerdrAsync(
            HerdrPaneSidecar sidecar,
            HerdrClient client,
            Action onPaneSetChanged,
            HerdrPlacementCoordinator placement,
            Func<string, Guid?, PaneBinding?> findBound,
            CancellationToken ct)
        {
            if (sidecar.GrokRulesReceipt is { } receipt
                && await new GrokRulesFileStore(_settings.SessionLogPath, _settings.GrokRules)
                    .VerifyAsync(_sessionId, receipt, ct))
                GrokRulesReceipt = receipt;
            _adopted = true;
            _backend = SessionBackends.Herdr;
            if (_acceptedStartedAt is null)
                BindAcceptedGeneration(sidecar.AcceptedStartedAt);
            _pendingReason = null;
            _pendingSidecar = null;
            _childPid = sidecar.ChildPid;
            _startedAt = sidecar.LaunchedAtUtc;
            _herdrOrigin = sidecar.Origin ?? HerdrPaneOrigins.Launched;
            _onHerdrPaneSetChanged = onPaneSetChanged;
            _herdrChild = new HerdrPaneChild(
                client,
                _settings,
                _logger,
                liveAntiphonPanes: () => Array.Empty<HerdrPaneAllocator.LivePane>(),
                _processLiveness,
                placement,
                findBound);
            // Re-bind the existing pane without re-launching: reconstruct the child's identity fields.
            await ((HerdrPaneChild)_herdrChild).AttachExistingAsync(sidecar, ct);
            _herdrChild.Exited += exit =>
            {
                lock (_gate)
                {
                    if (_status == "Exited") return;
                    _status = "Exited";
                    _exitCode = exit.ExitCode;
                    _exitReason = exit.Reason;
                }

                _clientReady.TrySetResult(false);
                _exited.TrySetResult();
                _events.Publish(
                    SessionRunnerEventNames.SessionExited,
                    ExitEnvelope(exit.ExitCode, exit.Reason, 0));
                _onHerdrPaneSetChanged?.Invoke();
            };
            lock (_gate)
                _status = "Running";
            _clientReady.TrySetResult(true);
            _events.Publish(
                SessionRunnerEventNames.SessionAdopted,
                new RunnerSessionAdoptedEvent(_sessionId, _childPid, _startedAt, LastSequence: 0, _acceptedStartedAt));

            // Re-tail via existing TranscriptSidecar if present — format from the sidecar, not Claude.
            var transcript = TranscriptSidecar.TryLoad(TranscriptSidecar.PathFor(_settings.SessionLogPath, _sessionId));
            if (transcript is not null)
            {
                RestoreTailerFromSidecar(
                    transcript,
                    transcript.Cwd ?? sidecar.Cwd ?? "",
                    transcript.ChildStartUtc ?? sidecar.LaunchedAtUtc);
            }
        }

        /// <summary>
        /// CARD-0187: the pty adopt switch, extracted so herdr re-adopt restores Grok/Codex too.
        /// No sidecar / no path ⇒ Claude discovery under sidecar rules (same as pty).
        /// </summary>
        private void RestoreTailerFromSidecar(TranscriptSidecar? sidecar, string cwd, DateTime? childStartUtc)
        {
            if (string.Equals(sidecar?.Format, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase))
            {
                _sidecar = sidecar;
                var updatesPath = sidecar!.TranscriptPath
                    ?? GrokTranscriptTailer.ResolveUpdatesPath(null, cwd, _sessionId);
                _tailer = new GrokTranscriptTailer(
                    _sessionId, updatesPath, _events, _logger, inputLog: _inputLog);
                _tailer.Start();
                return;
            }

            if (string.Equals(sidecar?.Format, TranscriptFormats.Codex, StringComparison.OrdinalIgnoreCase))
            {
                _sidecar = sidecar;
                _tailer = new CodexTranscriptTailer(
                    _sessionId, cwd, _events, _logger,
                    claims: _transcriptClaims,
                    inputLog: _inputLog,
                    firstInputUtc: sidecar!.FirstInputAtUtc,
                    childStartUtc: childStartUtc ?? sidecar!.ChildStartUtc,
                    resumeLaunch: sidecar!.ResumeLaunch,
                    knownTranscriptPath: sidecar.TranscriptPath,
                    onBound: RecordTranscriptBinding,
                    onUnbound: RecordTranscriptUnbinding);
                _tailer.Start();
                return;
            }

            _sidecar = sidecar ?? new TranscriptSidecar
            {
                SessionId = _sessionId,
                Cwd = cwd,
                ChildStartUtc = childStartUtc,
            };

            _tailer = new TranscriptTailer(
                _sessionId, cwd, _events, _logger,
                claims: _transcriptClaims,
                inputLog: _inputLog,
                firstInputUtc: sidecar?.FirstInputAtUtc,
                childStartUtc: childStartUtc ?? sidecar?.ChildStartUtc,
                agentName: sidecar?.AgentName,
                resumeLaunch: sidecar?.ResumeLaunch ?? false,
                knownTranscriptPath: sidecar?.TranscriptPath,
                onBound: RecordTranscriptBinding,
                onUnbound: RecordTranscriptUnbinding,
                knownSessions: new SidecarKnownSessionProbe(_settings.SessionLogPath));
            _tailer.Start();
        }

        private void RebuildInterpretationFromAnsiLog(long lastSeq)
        {
            // ReadAnsiLog already bounds itself to ReplayBufferMaxChars.
            var replay = ReadAnsiLog();

            lock (_gate)
            {
                _liveBuffer.Clear();
                _liveBuffer.Append(replay);
                _screen?.Feed(replay);
                _lastSequence = Math.Max(_lastSequence, lastSeq);
            }
        }

        /// <summary>
        /// Evict from the front so the runner's mirror of the session stays bounded — the pty-host
        /// bounds its own ring the same way and to the same cap (see <c>HostSession</c>). Without
        /// this the mirror grew for the life of the session, and every snapshot payload built from
        /// it grew with it. Trimming only once the buffer is over twice the cap amortises the
        /// memmove to roughly one per <c>cap</c> chars appended instead of one per chunk.
        /// Caller must hold <see cref="_gate"/>.
        /// </summary>
        private void TrimLiveBuffer()
        {
            var cap = Math.Max(1, _settings.ReplayBufferMaxChars);
            if (_liveBuffer.Length > cap * 2L)
                _liveBuffer.Remove(0, _liveBuffer.Length - cap);
        }

        public bool HasExited
        {
            get
            {
                lock (_gate)
                    return _status == "Exited";
            }
        }

        public RunnerSessionDto ToDto()
        {
            lock (_gate)
            {
                var boundPath = _tailer?.BoundTranscriptPath;
                return new RunnerSessionDto(
                    _sessionId,
                    _childPid,
                    _startedAt,
                    _status,
                    _exitCode,
                    _exitReason,
                    _lastSequence,
                    _hostPid > 0 ? _hostPid : null,
                    _adopted,
                    _herdrAgentStatus,
                    _herdrAgentStatusSinceUtc,
                    TranscriptBound: _tailer is null ? null : boundPath is not null,
                    TranscriptBindHow: boundPath is not null ? _tailer!.BindHow : null,
                    TranscriptUnboundReason: boundPath is null ? _tailer?.UnboundReason : null,
                    Backend: _backend,
                    Pending: _pendingReason,
                    HerdrVerifiedAtUtc: _herdrVerifiedAtUtc,
                    HerdrOrigin: _herdrOrigin,
                        GrokRulesReceipt: GrokRulesReceipt,
                        VerificationBinding: VerificationBinding,
                        AcceptedStartedAt: _acceptedStartedAt);
            }
        }

        public RunnerBufferDto GetBuffer()
        {
            long lastSequence;
            lock (_gate)
                lastSequence = _lastSequence;
            return new RunnerBufferDto(_sessionId, ReadAnsiLog(), lastSequence);
        }

        public RunnerSnapshotDto GetSnapshot()
        {
            if (_pendingReason is not null)
                throw new HerdrBackendUnavailableException(
                    "Herdr is unreachable; session is pending adoption.");

            // Herdr has no push stream in S2 — serve an on-demand pane.read when asked.
            if (_herdrChild is { } herdr)
            {
                try
                {
                    var screen = herdr.ReadScreenAsync(CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (screen is not null)
                    {
                        lock (_gate)
                        {
                            // CARD-0164: fold content-delta counter alongside herdr revision.
                            _lastSequence = Math.Max(
                                _lastSequence, Math.Max(screen.Revision, screen.ContentSequence));
                            return new RunnerSnapshotDto(
                                _sessionId,
                                screen.Text,
                                screen.Text,
                                _lastSequence,
                                _startedAt);
                        }
                    }
                }
                catch (HerdrBackendUnavailableException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Herdr pane.read failed for snapshot of {SessionId}", _sessionId);
                }
            }

            lock (_gate)
            {
                return new RunnerSnapshotDto(
                    _sessionId,
                    _liveBuffer.ToString(),
                    _screen?.GetScreenText() ?? "",
                    _lastSequence,
                    _startedAt);
            }
        }

        public RunnerTranscriptDto GetTranscript() =>
            _tailer?.Snapshot() ?? new RunnerTranscriptDto(_sessionId, Array.Empty<RunnerTranscriptEvent>(), 0);

        public async Task WriteAsync(string input, CancellationToken ct)
        {
            if (VerificationBinding is { } binding
                && _custodyLedger!.Store.ReadRecord<CustodyStamp>(binding, "runner-seal.json") is not null)
                throw new VerificationCustodyException("verification_custody_sealed");
            // Recorded BEFORE the write: Claude cannot persist a prompt we have not sent yet, so
            // the input log is always ahead of the transcript record that rule C4 matches it to.
            _inputLog.Append(input);
            if (!_inputLog.IsEmpty && _sidecar?.FirstInputAtUtc is null)
            {
                var current = _sidecar ?? new TranscriptSidecar { SessionId = _sessionId, ChildStartUtc = _startedAt };
                SaveSidecar(current with { FirstInputAtUtc = DateTime.UtcNow });
            }
            if (_pendingReason is not null)
                throw new HerdrBackendUnavailableException(
                    "Herdr is unreachable; session is pending adoption.");
            if (_herdrChild is { } herdr)
            {
                // Wait for LaunchAsync to finish (same _clientReady gate the pty path uses).
                if (!await _clientReady.Task.WaitAsync(ct))
                    throw new InvalidOperationException("Herdr session ended before it was ready for input.");
                await herdr.WriteAsync(input, ct);
                return;
            }

            var client = await AwaitClientAsync(ct);
            await client.InputAsync(input, ct);
        }

        public async Task ClearLiveBufferAsync(CancellationToken ct)
        {
            lock (_gate)
                _liveBuffer.Clear();
            if (_client is { } client)
                await client.ClearLiveBufferAsync(ct);
        }

        public async Task ResizeAsync(int cols, int rows, CancellationToken ct)
        {
            if (_pendingReason is not null)
                throw new HerdrBackendUnavailableException(
                    "Herdr is unreachable; session is pending adoption.");
            if (_herdrChild is { } herdr)
            {
                await herdr.ResizeAsync(cols, rows, ct);
                return;
            }

            var client = await AwaitClientAsync(ct);
            await client.ResizeAsync(cols, rows, ct);
        }

        public async Task KillAsync(TimeSpan timeout, CancellationToken ct, string? exitReasonOverride = null)
        {
            if (HasExited && VerificationBinding is null)
                return;

            if (exitReasonOverride is not null)
            {
                lock (_gate)
                    _exitReasonOverride = exitReasonOverride;
            }

            if (_pendingReason is not null)
            {
                KillPendingHerdr();
                return;
            }

            if (_herdrChild is { } herdr)
            {
                await herdr.KillAsync(ct);
                await Task.WhenAny(_exited.Task, Task.Delay(timeout + TimeSpan.FromSeconds(2), ct));
                return;
            }

            if (_client is not { } client)
                return;

            await client.KillAsync(timeout, ct);
            // Parity with the old in-proc KillAsync: wait for the exit (with a grace margin for
            // the pipe round-trip); the liveness sweep is the backstop if it never arrives.
            await Task.WhenAny(_exited.Task, Task.Delay(timeout + TimeSpan.FromSeconds(2), ct));
        }

        /// <summary>
        /// CARD-0186 S3: operator kill of a pending session. Delete the sidecar, kill the child
        /// by pid if OS-alive (positive identity), Exited(PaneLeftOpen) — the pane, if it still
        /// exists when herdr comes back, is not ours to close blind.
        /// </summary>
        private void KillPendingHerdr()
        {
            var sidecar = _pendingSidecar;
            var attached = string.Equals(sidecar?.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase);
            if (!attached
                && sidecar?.ChildPid is int pid
                && _processLiveness.IsAlive(pid, sidecar.LaunchedAtUtc))
            {
                KillPidBestEffort(pid);
            }

            HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, _sessionId);
            CompletePendingAsExited(
                attached ? HerdrExitReasons.Detached : HerdrExitReasons.PaneLeftOpen,
                attached ? 0 : null);
        }

        /// <summary>
        /// Liveness backstop: if this session claims Running but its OS process is gone, transition
        /// to Exited and publish the SessionExited event the normal observer missed. Idempotent and
        /// race-safe: re-checks the status under the gate before transitioning.
        /// </summary>
        public bool MarkVanishedIfDead(IProcessLivenessProbe probe)
        {
            int? pid;
            DateTime startedAt;
            lock (_gate)
            {
                if (_status != "Running")
                    return false;
                pid = _childPid;
                startedAt = _startedAt;
            }

            if (pid is int livePid && probe.IsAlive(livePid, startedAt))
                return false;

            long lastSequence;
            int? exitCode;
            lock (_gate)
            {
                if (_status != "Running")
                    return false; // a real exit event won the race — keep its verdict
                _status = "Exited";
                _exitCode ??= -1;
                _exitReason = "ProcessVanished";
                exitCode = _exitCode;
                lastSequence = _lastSequence;
            }

            _events.Publish(
                SessionRunnerEventNames.SessionExited,
                ExitEnvelope(exitCode, "ProcessVanished", lastSequence));
            _clientReady.TrySetResult(false);
            _exited.TrySetResult();
            _tailer?.NotifyChildExited();

            if (_herdrChild is not null)
            {
                // CARD-0224: the pane is still standing — retire to last-pane so the next
                // launch of this id can target it. KillAsync's pane.close / PaneLeftOpen
                // paths still plain-delete.
                HerdrPaneSidecar.Retire(_settings.SessionLogPath, _sessionId, "ProcessVanished");
                _onHerdrPaneSetChanged?.Invoke();
                return true;
            }

            // The session is declared dead; the host (if any survives) has no further purpose.
            _ = Task.Run(ShutdownHostAsync);
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            _custodyLifetime.Cancel();
            _launchFinished.TrySetResult();
            if (_custodyShutdown is not null) await _custodyShutdown;
            _clientReady.TrySetResult(false);
            // Dispose detaches from the host — it must NOT kill it: surviving the runner's own
            // teardown is the entire point of the pty-host split.
            if (_client is { } client)
            {
                _client = null;
                client.OnOutput -= HandleOutput;
                client.OnExited -= HandleExited;
                client.OnDisconnected -= HandleDisconnected;
                await client.DisposeAsync();
            }

            if (_tailer is not null)
                await _tailer.DisposeAsync();
        }

        private void HandleOutput(long seq, string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
                return;

            lock (_gate)
            {
                _lastSequence = Math.Max(_lastSequence, seq);
                _liveBuffer.Append(chunk);
                TrimLiveBuffer();
                _screen?.Feed(chunk);
            }

            _events.Publish(
                SessionRunnerEventNames.SessionOutput,
                new RunnerOutputEvent(_sessionId, seq, chunk));
        }

        private void HandleExited(ExitedMessage exited)
        {
            bool transitioned;
            string exitReason;
            lock (_gate)
            {
                transitioned = _status != "Exited";
                // The override only applies when OUR kill is what ended the child (the host says
                // KilledByRequest) — a natural exit that races the kill keeps its real reason.
                exitReason = _exitReasonOverride is { } requested
                    && exited.ExitReason == PtyExitReason.KilledByRequest.ToString()
                        ? requested
                        : exited.ExitReason;
                if (transitioned)
                {
                    _status = "Exited";
                    _exitCode = exited.ExitCode;
                    _exitReason = exitReason;
                    _lastSequence = Math.Max(_lastSequence, exited.LastSeq);
                }
            }

            if (transitioned)
            {
                _events.Publish(
                    SessionRunnerEventNames.SessionExited,
                    ExitEnvelope(exited.ExitCode, exitReason, exited.LastSeq));
            }

            _clientReady.TrySetResult(false);
            _exited.TrySetResult();
            // A dead child writes no more transcript: stop hunting for one (and say so if input was
            // delivered and nothing ever bound).
            _tailer?.NotifyChildExited();
            // Fate is recorded — ack so the host deletes its manifest and exits. Run outside the
            // client's read loop (this handler IS the read loop).
            _ = Task.Run(ShutdownHostAsync);
        }

        private void HandleDisconnected(Exception? failure)
        {
            if (HasExited)
                return;

            // The host outlives us by design; a dropped pipe on a running session means the runner
            // is shutting down (adoption reconnects on next start) or the host died (the liveness
            // sweep will mark the vanished child). Nothing to do here but record it.
            _logger.LogWarning(
                failure,
                "pty-host pipe for running session {SessionId} disconnected (host pid {HostPid})",
                _sessionId, _hostPid);
        }

        /// <summary>
        /// Relaunch prerequisite (CARD-0050): waits until this EXITED session's pty-host process is
        /// really gone, so a new host for the same session id cannot lose the pipe-name race to it.
        /// Ack-first (the normal path — the host deletes its manifest and exits), bounded wait,
        /// then a verified kill: with the child already exited the host protects nothing, and a
        /// lingering one only exists to reject the relaunch. Never throws.
        /// </summary>
        public async Task EnsureExitedHostGoneAsync(TimeSpan bound, CancellationToken ct)
        {
            if (VerificationBinding is { } binding && _custodyLedger!.ReadFinal(binding) is null)
                throw new VerificationCustodyException("verification_custody_session_fenced");
            if (!HasExited || _hostPid <= 0)
                return;

            await ShutdownHostAsync();
            if (VerificationBinding is not null && _custodyShutdown is not null)
                await _custodyShutdown.WaitAsync(ct);

            var deadline = DateTime.UtcNow + bound;
            var killed = false;
            while (DateTime.UtcNow < deadline)
            {
                if (!HostProcessStillAlive())
                    return;
                if (!killed && DateTime.UtcNow + TimeSpan.FromSeconds(2) >= deadline)
                {
                    if (VerificationBinding is not null)
                        throw new VerificationCustodyException("verification_custody_host_still_present");
                    // The ack did not take (host wedged, or the ack raced its own pipe teardown) —
                    // escalate once, then keep waiting for the exit inside the same bound.
                    KillHostIfStillOurs();
                    killed = true;
                }

                try
                {
                    await Task.Delay(50, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            _logger.LogWarning(
                "Exited session {SessionId}'s pty-host (pid {HostPid}) survived shutdown + kill within "
                + "{Bound}; the relaunch may race its pipe",
                _sessionId, _hostPid, bound);
        }

        private bool HostProcessStillAlive()
        {
            try
            {
                using var host = System.Diagnostics.Process.GetProcessById(_hostPid);
                // Pid reuse by an unrelated process counts as "gone" — never wait on a stranger.
                return !host.HasExited && host.ProcessName.Contains("PtyHost", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void KillHostIfStillOurs()
        {
            try
            {
                using var host = System.Diagnostics.Process.GetProcessById(_hostPid);
                if (!host.HasExited && host.ProcessName.Contains("PtyHost", StringComparison.OrdinalIgnoreCase))
                    host.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }
        }

        private async Task ShutdownHostAsync()
        {
            if (VerificationBinding is not null)
            {
                lock (_gate)
                    _custodyShutdown ??= Task.Run(ObserveCustodyThenShutdownAsync);
                return;
            }
            var client = _client;
            if (client is null)
                return;

            try
            {
                await client.ShutdownAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Shutdown ack to pty-host for session {SessionId} failed (host likely already gone)",
                    _sessionId);
            }
        }

        internal async Task<VerificationCustodyStatus> CollectCustodyAsync(CancellationToken ct)
        {
            var binding = VerificationBinding
                ?? throw new VerificationCustodyException("verification_custody_binding_required");
            await _launchFinished.Task.WaitAsync(ct);
            await _custodyReadGate.WaitAsync(ct);
            try
            {
                if (_custodyLedger!.ReadFinal(binding) is { } final) return final;
                var observed = _custodyLedger.ReadProducer(binding);
                if (observed is null)
                {
                    if (_client is not { } client) return _custodyLedger.ReadUnavailable(binding);
                    var seal = HasExited || _custodyLedger.Store.ReadRecord<CustodyStamp>(binding, "runner-seal.json") is not null;
                    observed = await client.GetCustodyAsync(binding, seal, ct);
                }
                if (observed.Receipt is not null && _tailer is { } tailer)
                {
                    await tailer.DisposeAsync();
                    _tailer = null;
                }
                return _custodyLedger.Accept(observed);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                return new(binding, VerificationCustodyState.Unknown,
                    ct.IsCancellationRequested ? "observation_canceled" : "custody_observer_unavailable");
            }
            finally { _custodyReadGate.Release(); }
        }

        private async Task ObserveCustodyThenShutdownAsync()
        {
            var ct = _custodyLifetime.Token;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var custody = await CollectCustodyAsync(ct);
                    if (custody.Receipt is not null && _client is { } client)
                    {
                        await client.ShutdownAsync(ct);
                        return;
                    }
                    if (custody.Reason == "custody_observer_unavailable") return;
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Custody remains unresolved for {ExecutionId}", VerificationBinding!.ExecutionId);
                    return;
                }
            }
        }

        /// <summary>
        /// The connected client, waiting out the cold-start window when necessary. On a cold
        /// launch the pty-host pipe takes ~a second to appear AFTER the session is registered, so
        /// input that raced the launch used to die as "Session has no live pty-host connection"
        /// and the boot prompt was silently lost (CARD-0018). Bounded by the same budget the
        /// launch itself gets; a session that dies first fails fast with the reason.
        /// </summary>
        private async Task<PtyHostClient> AwaitClientAsync(CancellationToken ct)
        {
            if (_client is { } live)
                return live;

            var timeout = TimeSpan.FromSeconds(
                _settings.PtyHostLaunchTimeoutSec + _settings.PtyHostConnectTimeoutSec + 5);
            var completed = await Task.WhenAny(_clientReady.Task, Task.Delay(timeout, ct));
            ct.ThrowIfCancellationRequested();

            if (completed != _clientReady.Task)
            {
                throw new InvalidOperationException(
                    $"Session has no live pty-host connection after waiting {timeout.TotalSeconds:0}s for the host to start.");
            }

            if (!await _clientReady.Task || _client is not { } client)
                throw new InvalidOperationException("Session ended before its pty-host connection was ready.");

            return client;
        }

        /// <summary>
        /// The tail of the ANSI log, bounded to <see cref="SessionRunnerSettings.ReplayBufferMaxChars"/>.
        /// Never reads the whole file: these logs reach tens of MB, and both callers (replay-on-adopt
        /// and the /buffer endpoint the server polls every 50ms) only ever wanted the tail. Reading
        /// the lot each time churned the LOH hard enough to strand multi-GB ArrayPool buckets for the
        /// life of the process.
        /// </summary>
        private string ReadAnsiLog()
        {
            if (_ansiLogPath is null || !File.Exists(_ansiLogPath))
                return "";

            var cap = Math.Max(1, _settings.ReplayBufferMaxChars);

            // The host appends concurrently; open shared so reads never fail or block it.
            using var stream = new FileStream(
                _ansiLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // UTF-8 is at most 4 bytes/char, so this window always yields at least `cap` chars.
            var window = cap * 4L;
            if (stream.Length > window)
            {
                stream.Seek(-window, SeekOrigin.End);

                // That lands mid-file and possibly mid-character. Continuation bytes are 10xxxxxx:
                // skip them so the decoder starts on a real character boundary.
                for (var i = 0; i < 3; i++)
                {
                    var b = stream.ReadByte();
                    if (b < 0)
                        break;
                    if ((b & 0xC0) != 0x80)
                    {
                        stream.Seek(-1, SeekOrigin.Current);
                        break;
                    }
                }
            }

            // The host writes UTF-8 with no BOM (File.AppendAllText), and we may be mid-file, so
            // don't let a stray byte triple be mistaken for one.
            using var reader = new StreamReader(
                stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false);
            var text = reader.ReadToEnd();

            return text.Length > cap ? text[^cap..] : text;
        }
    }
}

public sealed record RunnerServerSentEvent(string EventName, string Json);

public sealed class SessionRunnerEventHub
{
    private readonly object _gate = new();
    private readonly List<Channel<RunnerServerSentEvent>> _subscribers = [];

    public ChannelReader<RunnerServerSentEvent> Subscribe(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<RunnerServerSentEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_gate)
            _subscribers.Add(channel);

        ct.Register(() =>
        {
            lock (_gate)
                _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }

    public void Publish<T>(string eventName, T payload)
    {
        var evt = new RunnerServerSentEvent(eventName, System.Text.Json.JsonSerializer.Serialize(payload));
        Channel<RunnerServerSentEvent>[] subscribers;
        lock (_gate)
            subscribers = [.. _subscribers];

        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(evt);
    }
}
