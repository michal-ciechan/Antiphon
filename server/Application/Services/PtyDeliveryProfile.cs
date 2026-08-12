using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Decides which set of delivery ceilings is in force, from the pseudoconsole ACTUALLY serving the
/// ptys we type into (CARD-0037 step 3).
///
/// <para><b>Why this is not just a constant.</b> CARD-0030 measured the paste path: with the shipped
/// <c>conpty.dll</c> + <c>OpenConsole.exe</c> in front of the pty, real Claude took 86 400 bytes in
/// one write with zero loss, while the inbox conhost in the same run kept 25%. So the ceilings can
/// be raised — but only where that binary is. A machine without the redistributable falls back to
/// the inbox conhost (<see cref="PtyBackendPolicy"/>), and there every old ceiling is still
/// load-bearing. Raising them unconditionally would re-open the original bug on exactly the machines
/// least able to notice.</para>
///
/// <para><b>Two independent facts have to agree.</b> The flag is process-wide and inherited
/// (<see cref="PtyBackendPolicy"/>), but the session runner is a SEPARATE PROCESS with its own
/// config, and its pty-hosts inherit ITS environment, not ours. A server told <c>modern</c> in front
/// of a runner that was not would type 43 KB briefs into a pty that clips at 1 KB — the original
/// failure, restored, with the ceilings' own log lines claiming otherwise. So the modern ceilings
/// are used only when this process resolves modern AND the runner does not contradict it
/// (<c>GET /capabilities</c>). Anything else is the inbox set.</para>
///
/// <para><b>Unknown is not modern.</b> A client that cannot answer (the in-proc adapters, every test
/// fake) leaves the local decision standing — it is evidence of nothing, and the local flag is still
/// the operator's stated intent for this process's own ptys. A client that answers <c>InboxConhost</c>
/// is evidence, and it downgrades, loudly.</para>
/// </summary>
public sealed class PtyDeliveryProfile
{
    /// <summary>How long a runner verdict is trusted before it is re-probed.</summary>
    private static readonly TimeSpan ProbeTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Bound on one probe. The runner client's own timeout is the request timeout (100 s by
    /// default), and this probe runs on the startup path — a wedged runner must cost a few seconds
    /// and a fallback to the local decision, not a server that will not come up.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly DelegationSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PtyDeliveryProfile> _logger;
    private readonly TimeProvider _time;
    private readonly PtyBackendDecision _local;
    private readonly object _gate = new();

    private PtyDeliveryCeilings _ceilings;
    private DateTimeOffset _probedAt = DateTimeOffset.MinValue;
    private Task? _probe;
    private string? _lastLogged;

    public PtyDeliveryProfile(
        IServiceScopeFactory scopeFactory,
        ILogger<PtyDeliveryProfile> logger,
        IOptions<DelegationSettings>? settings = null,
        TimeProvider? timeProvider = null,
        string? backendOverride = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings?.Value ?? new DelegationSettings();
        _time = timeProvider ?? TimeProvider.System;
        _local = PtyBackendPolicy.Resolve(backendOverride);
        _ceilings = _settings.CeilingsFor(_local.Backend, _local.Reason);

        // Said once, at startup, because a "modern" request that fell back is invisible from
        // everywhere else and silently re-arms the 1 KB clipping the old ceilings exist for.
        if (_local.FellBack)
            _logger.LogWarning("PTY delivery ceilings: {Ceilings}", _ceilings);
        else
            _logger.LogInformation("PTY delivery ceilings: {Ceilings}", _ceilings);
    }

    /// <summary>This process's own resolution of the backend flag, before the runner is consulted.</summary>
    public PtyBackendDecision Local => _local;

    /// <summary>
    /// The ceilings in force. Cheap and non-blocking: returns the current verdict and kicks off a
    /// background re-probe when it is stale, so a delivery never waits on the runner's HTTP.
    /// </summary>
    public PtyDeliveryCeilings Ceilings
    {
        get
        {
            EnsureFresh();
            lock (_gate) return _ceilings;
        }
    }

    /// <summary>Probe now and return the resulting ceilings — for startup and for tests.</summary>
    public async Task<PtyDeliveryCeilings> RefreshAsync(CancellationToken ct)
    {
        await ProbeAsync(ct);
        lock (_gate) return _ceilings;
    }

    private void EnsureFresh()
    {
        // Only the modern arm needs corroboration: if this process is on the inbox conhost the
        // ceilings are already the conservative ones and the runner cannot raise them.
        if (_local.Backend != PtyBackend.ModernConPty)
            return;

        lock (_gate)
        {
            if (_probe is { IsCompleted: false })
                return;
            if (_time.GetUtcNow() - _probedAt < ProbeTtl)
                return;
            _probedAt = _time.GetUtcNow();
            _probe = Task.Run(() => ProbeAsync(CancellationToken.None));
        }
    }

    private async Task ProbeAsync(CancellationToken ct)
    {
        // Nothing to corroborate: the conservative set is already in force and no runner answer can
        // raise it — the server types into its own in-proc ptys too, and the ceilings are one set
        // for the deployment. Also keeps every test host and every runner-less server off the wire.
        if (_local.Backend != PtyBackend.ModernConPty)
            return;

        PtyBackend backend;
        string reason;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ProbeTimeout);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetService<ISessionRunnerClient>();
            var capabilities = client is null ? null : await client.GetCapabilitiesAsync(deadline.Token);

            if (capabilities is null)
            {
                // No evidence either way — keep this process's own decision.
                backend = _local.Backend;
                reason = _local.Reason + "; runner did not report its backend";
            }
            else if (IsBackend(capabilities.PtyBackend, PtyBackend.ModernConPty))
            {
                backend = _local.Backend;
                reason = _local.Reason + "; runner agrees";
            }
            else
            {
                backend = PtyBackend.InboxConhost;
                reason =
                    $"this process resolved {_local.Backend} but the session runner is serving ptys with "
                    + $"{capabilities.PtyBackend} ({capabilities.PtyBackendReason}) — the ceilings follow the "
                    + "pty that actually carries the body, so the conservative set stays in force";
            }
        }
        // Our own deadline expiring is "no answer", which is no evidence — the caller cancelling is
        // the only cancellation that means stop.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            backend = _local.Backend;
            reason = _local.Reason + "; runner backend probe failed: " + ex.Message;
            _logger.LogDebug(ex, "PTY backend probe against the session runner failed");
        }

        var resolved = _settings.CeilingsFor(backend, reason);
        lock (_gate)
        {
            _ceilings = resolved;
            _probedAt = _time.GetUtcNow();
        }

        // Log a CHANGE of verdict, not every probe: a five-minute heartbeat saying the same thing
        // is how a warning that matters gets ignored.
        if (_lastLogged == reason)
            return;
        _lastLogged = reason;
        if (backend != _local.Backend)
            _logger.LogWarning("PTY delivery ceilings downgraded: {Ceilings}", resolved);
        else
            _logger.LogInformation("PTY delivery ceilings confirmed: {Ceilings}", resolved);
    }

    private static bool IsBackend(string reported, PtyBackend expected) =>
        string.Equals(reported, expected.ToString(), StringComparison.OrdinalIgnoreCase);
}
