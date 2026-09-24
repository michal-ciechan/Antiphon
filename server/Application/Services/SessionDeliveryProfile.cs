using System.Collections.Concurrent;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Per-session delivery ceilings (CARD-0161). Wraps <see cref="PtyDeliveryProfile"/> and keys the
/// herdr arm on the immutable <see cref="AgentSession.SessionBackend"/> snapshot. Process-wide
/// pty resolution is untouched for PtyHost sessions.
/// </summary>
public sealed class SessionDeliveryProfile
{
    private static readonly TimeSpan CapabilityProbeTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CapabilityProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly PtyDeliveryProfile _ptyProfile;
    private readonly DelegationSettings _settings;
    private readonly ISessionRunnerClient _runner;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionDeliveryProfile> _logger;

    private readonly ConcurrentDictionary<Guid, (SessionBackend Backend, string? RunnerId, AgentKind Kind)> _snapshotCache = new();
    private readonly object _capabilityGate = new();
    private IReadOnlyList<string>? _sessionBackends;
    private DateTimeOffset _probedAt = DateTimeOffset.MinValue;
    private Task? _probe;

    public SessionDeliveryProfile(
        PtyDeliveryProfile ptyProfile,
        IOptions<DelegationSettings> settings,
        ISessionRunnerClient runner,
        TimeProvider time,
        ILogger<SessionDeliveryProfile> logger)
    {
        _ptyProfile = ptyProfile;
        _settings = settings.Value;
        _runner = runner;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Resolve ceilings for one delivery. PtyHost → process-wide pty profile. Herdr → herdr set
    /// only when the runner's live capabilities still advertise "herdr"; otherwise the inbox
    /// conservative set (loss-proof: over-spill / over-warn, never over-type).
    /// </summary>
    public async Task<PtyDeliveryCeilings> ForSessionAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var snapshot = await ResolveSnapshotAsync(db, sessionId, ct);
        if (!string.IsNullOrWhiteSpace(snapshot.RunnerId))
        {
            // CARD-0490 / CARD-0649. Phone-home never borrows local ModernConPty evidence: the
            // desktop's 43 KB ceiling is not a measurement of this pty, so the single-write
            // stays the inbox 1 KB. The join-safe narrowing follows the SESSION's kind. Applying
            // Grok's brief-inline 0 to every runner, including Claude, was why a Claude brief
            // fitted on the desktop was then re-spilled here as a marker-less pointer.
            return _settings.CeilingsFor(
                    Antiphon.Agents.Pty.PtyBackend.InboxConhost,
                    "phone-home delivery uses the inbox single-write ceiling; local ModernConPty is not evidence for this pty")
                .ForAgentKind(snapshot.Kind);
        }

        var backend = snapshot.Backend;
        if (backend != SessionBackend.Herdr)
            return _ptyProfile.Ceilings;

        await EnsureCapabilitiesProbedAsync(ct);
        IReadOnlyList<string>? advertised;
        lock (_capabilityGate)
            advertised = _sessionBackends;

        if (advertised is null)
        {
            // No answer = no evidence → conservative inbox set (plan §3).
            return _settings.CeilingsFor(
                Antiphon.Agents.Pty.PtyBackend.InboxConhost,
                "herdr session but runner capabilities probe returned no answer — conservative inbox set");
        }

        if (advertised.Contains(SessionBackends.Herdr, StringComparer.OrdinalIgnoreCase))
            return _settings.HerdrCeilings("AgentSession.SessionBackend=Herdr; runner advertises herdr");

        var listed = advertised.Count == 0 ? "none" : string.Join(", ", advertised);
        var reason =
            $"herdr session snapshot but runner SessionBackends=[{listed}] — downgrading to inbox "
            + "conservative set (CARD-0161)";
        _logger.LogWarning("Delivery ceilings downgraded: {Reason}", reason);
        return _settings.CeilingsFor(Antiphon.Agents.Pty.PtyBackend.InboxConhost, reason);
    }

    private async Task<(SessionBackend Backend, string? RunnerId, AgentKind Kind)> ResolveSnapshotAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        if (_snapshotCache.TryGetValue(sessionId, out var cached))
            return cached;

        var row = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.SessionBackend, s.RunnerId, s.AgentKind })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return (SessionBackend.PtyHost, null, AgentKind.ClaudeCode); // unknown id → today's pty behaviour

        var snapshot = (row.SessionBackend, row.RunnerId, row.AgentKind);
        _snapshotCache[sessionId] = snapshot;
        return snapshot;
    }

    private async Task EnsureCapabilitiesProbedAsync(CancellationToken ct)
    {
        Task? probe;
        lock (_capabilityGate)
        {
            var stale = _time.GetUtcNow() - _probedAt >= CapabilityProbeTtl;
            if (stale && (_probe is null || _probe.IsCompleted))
            {
                _probedAt = _time.GetUtcNow();
                _probe = ProbeCapabilitiesAsync();
            }

            probe = _probe;
        }

        if (probe is not null)
            await probe.WaitAsync(ct);
    }

    private async Task ProbeCapabilitiesAsync()
    {
        IReadOnlyList<string>? result = null;
        using var deadline = new CancellationTokenSource(CapabilityProbeTimeout);
        try
        {
            var caps = await _runner.GetCapabilitiesAsync(deadline.Token);
            result = caps?.SessionBackends;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SessionBackends capability probe failed");
        }

        lock (_capabilityGate)
            _sessionBackends = result;
    }
}
