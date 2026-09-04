using System.Collections.Concurrent;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0334 S2: once-a-minute idle-boundary relaunch when a live standing agent's bundles or
/// instruction files have drifted from the repo. Notify-lane delivery is S3; this sweep only
/// resolves that lane so it does not kill. Singleton so the in-memory per-agent attempt stamp
/// survives the hosted service's per-tick scope.
///
/// <para>Kill uses <see cref="SessionTerminationSource.PolicyRefresh"/>, never
/// <c>StopAsync</c> (that suspends supervision). A failed <c>StartAsync</c> leaves supervision
/// untouched — the session is Stopped and the supervisor's next tick schedules its normal
/// ladder restart, which also carries the new bundles.</para>
/// </summary>
public sealed class PolicyRefreshService
{
    internal static readonly TimeSpan KillConfirmTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, DateTime> _attempts = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentSessionRuntime _runtime;
    private readonly ISessionRunnerClient _runner;
    private readonly PolicyRefreshSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<PolicyRefreshService> _logger;

    public PolicyRefreshService(
        IServiceScopeFactory scopeFactory,
        AgentSessionRuntime runtime,
        ISessionRunnerClient runner,
        IOptions<SupervisionSettings> settings,
        TimeProvider time,
        ILogger<PolicyRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _runtime = runtime;
        _runner = runner;
        _settings = settings.Value.PolicyRefresh ?? new PolicyRefreshSettings();
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Scan standing agents with a live PtyHost session; relaunch at most one idle drifted
    /// AlwaysOn ClaudeCode per agent this pass. Returns how many relaunches
    /// <c>StartAsync</c> accepted. Other agents may ride along on a shared database — callers
    /// must not assert on this number as a global count.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        try
        {
            await _runner.ListAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Policy-refresh sweep skipped: session runner unreachable");
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogTrace(ex, "Policy-refresh runner probe failed non-transport; continuing");
        }

        List<Agent> agents;
        Dictionary<Guid, AgentSession> sessions;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            agents = await db.Agents.AsNoTracking()
                .Where(a => a.PersistentSessionId != null && !a.IsPoolDelegate)
                .ToListAsync(ct);
            if (agents.Count == 0)
                return 0;

            var sessionIds = new List<Guid>(agents.Count);
            foreach (var agent in agents)
            {
                if (Guid.TryParse(agent.PersistentSessionId, out var id))
                    sessionIds.Add(id);
            }

            sessions = await db.AgentSessions.AsNoTracking()
                .Where(s => sessionIds.Contains(s.Id) && s.Status == SessionStatus.Running)
                .ToDictionaryAsync(s => s.Id, ct);
        }

        var fired = 0;
        foreach (var agent in agents)
        {
            ct.ThrowIfCancellationRequested();
            if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId)
                || !sessions.TryGetValue(sessionId, out var session))
            {
                continue;
            }

            try
            {
                if (await ProcessAgentAsync(agent, session, ct))
                    fired++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Policy-refresh sweep failed for agent {AgentId} ({AgentName})",
                    agent.Id, agent.Name);
            }
        }

        return fired;
    }

    private async Task<bool> ProcessAgentAsync(Agent agent, AgentSession session, CancellationToken ct)
    {
        if (session.SessionBackend == SessionBackend.Herdr)
            return false;
        if (session.CardId is not null)
            return false;
        if (session.ComposedBundleStamp is null && session.InstructionFileStamp is null)
            return false;

        var mode = agent.PolicyRefreshMode ?? PolicyRefreshMode.Auto;
        if (mode == PolicyRefreshMode.Off)
            return false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attached = await AgentBundleAttachments.LoadAsync(db, agent.Id, _logger, ct);
        var composed = InstructionBundleComposer.Compose(
            attached,
            AgentReplyStyles.ComposedKey(agent.ReplyStyle),
            agent.SystemPromptAppend);
        var files = InstructionFileStamps.Compute(
            agent.WorkingDirectory,
            _settings.InstructionFiles ?? PolicyRefreshSettings.DefaultInstructionFiles);
        var drift = PolicyDrift.Of(
            session.ComposedBundleStamp,
            composed.StampLine,
            session.InstructionFileStamp,
            files.StampLine,
            mode);
        if (!drift.HasDrift)
            return false;

        var lane = ResolveLane(agent, session, mode);
        if (lane != Lane.Relaunch)
            return false;

        if (await IsTickBlockedAsync(scope, db, agent, session.Id, ct))
            return false;

        // Pull before acting: never kill on a stale working/idle read (gotchas #49/#50).
        await _runtime.CatchUpTranscriptAsync(session.Id, ct);

        if (await IsTickBlockedAsync(scope, db, agent, session.Id, ct))
            return false;

        var now = UtcNow();
        Stamp(agent.Id, now);

        var delta = PolicyRefreshDelta.Format(
            session.ComposedBundleStamp,
            composed.StampLine,
            session.InstructionFileStamp,
            files.StampLine);

        var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
        try
        {
            await sessions.KillAsync(session.Id, SessionTerminationSource.PolicyRefresh, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAsync(
                scope, db, agent.Id, session.Id,
                AgentIncidentKind.PolicyRefreshFailed, AlertSeverity.Warning,
                $"Policy refresh kill failed: {ex.Message}",
                raiseAlert: true, ct);
            return false;
        }

        if (!await WaitUntilStoppedAsync(session.Id, ct))
        {
            await RecordAsync(
                scope, db, agent.Id, session.Id,
                AgentIncidentKind.PolicyRefreshFailed, AlertSeverity.Warning,
                "Policy refresh kill did not reach Stopped within 30s; not starting.",
                raiseAlert: true, ct);
            return false;
        }

        var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
        AgentDetailDto started;
        try
        {
            started = await control.StartAsync(
                agent.Id,
                new StartAgentRequest(
                    Fresh: false,
                    IgnoreSubscriptionQuota: true,
                    PolicyRefreshDelta: delta),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAsync(
                scope, db, agent.Id, session.Id,
                AgentIncidentKind.PolicyRefreshFailed, AlertSeverity.Warning,
                $"Policy refresh start failed: {ex.Message}",
                raiseAlert: true, ct);
            return false;
        }

        var fresh = !string.Equals(
            started.PersistentSessionId, session.Id.ToString("D"), StringComparison.OrdinalIgnoreCase);
        var message = fresh
            ? $"Policy refreshed (fresh: true): {delta}"
            : $"Policy refreshed: {delta}";
        await RecordAsync(
            scope, db, agent.Id, session.Id,
            AgentIncidentKind.PolicyRefreshed, AlertSeverity.Info,
            message,
            raiseAlert: false, ct);

        _logger.LogInformation(
            "Policy-refresh relaunched agent {AgentName} ({AgentId}) session {SessionId} ({Mode}): {Delta}",
            agent.Name, agent.Id, session.Id, fresh ? "fresh" : "resume", delta);
        return true;
    }

    /// <summary>
    /// D4 lane. Notify is S3 — this sweep returns <see cref="Lane.Notify"/> so the caller
    /// does not kill. Tick-skips (suspended, held model, NextRestartAt) are not a lane.
    /// </summary>
    internal static Lane ResolveLane(Agent agent, AgentSession session, PolicyRefreshMode mode)
    {
        if (mode == PolicyRefreshMode.Off)
            return Lane.None;
        if (session.ComposedBundleStamp is null && session.InstructionFileStamp is null)
            return Lane.None;
        if (session.SessionBackend == SessionBackend.Herdr)
            return Lane.None;

        var kind = agent.Kind;
        var alwaysOn = agent.AlwaysOn;
        var relaunchEligible = kind == AgentKind.ClaudeCode && alwaysOn;
        return mode switch
        {
            PolicyRefreshMode.Relaunch when relaunchEligible => Lane.Relaunch,
            PolicyRefreshMode.Notify => Lane.Notify,
            PolicyRefreshMode.Auto when relaunchEligible => Lane.Relaunch,
            PolicyRefreshMode.Auto => Lane.Notify,
            PolicyRefreshMode.Relaunch => Lane.Notify,
            _ => Lane.None,
        };
    }

    internal enum Lane
    {
        None,
        Notify,
        Relaunch,
    }

    private async Task<bool> IsTickBlockedAsync(
        IServiceScope scope,
        AppDbContext db,
        Agent agent,
        Guid sessionId,
        CancellationToken ct)
    {
        if (!_runtime.ListLiveSessions().Contains(sessionId)
            && !_runtime.TryGetLiveMetadata(sessionId, out _))
        {
            // Kill already disposed the adapter, or the runtime never had it. Before kill this
            // means "not actually live" — skip. After kill the caller does not use this method.
            return true;
        }

        if (!IsTranscriptBound(sessionId))
            return true;

        var state = await db.AgentSupervisionStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentId == agent.Id, ct);
        if (state is { Suspended: true })
            return true;
        if (state?.NextRestartAt is not null)
            return true;

        var availability = scope.ServiceProvider.GetService<IModelAvailability>()
            ?? scope.ServiceProvider.GetService<ModelAvailability>();
        if (availability is not null)
        {
            var alias = ModelAlias.Normalize(agent.Kind, agent.ModelId)
                ?? ModelLevelAliases.For(agent.Kind, agent.ModelLevel);
            if (await availability.IsHeldAsync(agent.Kind, alias, ct))
                return true;
        }

        if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
            return true;

        if (await HasBlockingQueueRowAsync(db, sessionId, ct))
            return true;

        if (!await HasBeenIdleLongEnoughAsync(db, sessionId, ct))
            return true;

        if (RecentlyAttempted(agent.Id, UtcNow())
            || await HasRecentPolicyActionAsync(db, agent.Id, ct))
        {
            return true;
        }

        return false;
    }

    private bool IsTranscriptBound(Guid sessionId)
    {
        if (_runtime.TryGetLiveMetadata(sessionId, out var meta) && meta.TranscriptBound is bool bound)
            return bound;
        return false;
    }

    private static async Task<bool> HasBlockingQueueRowAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        return await db.SessionQueuedMessages.AsNoTracking()
            .AnyAsync(m => m.AgentSessionId == sessionId
                && m.Status != QueuedMessageStatus.Canceled
                && (m.Status == QueuedMessageStatus.Pending
                    || (m.Status == QueuedMessageStatus.Sent && m.DeliveryVerdict == null)
                    || (m.Origin == QueuedMessageOrigin.Channel && m.ChannelReplySettledAt == null)),
                ct);
    }

    private async Task<bool> HasBeenIdleLongEnoughAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var stamps = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .Select(t => new { t.Timestamp, t.CreatedAt })
            .ToListAsync(ct);
        if (stamps.Count == 0)
            return false;

        var newest = stamps.Max(t => t.Timestamp ?? t.CreatedAt);
        var idleFor = UtcNow() - newest;
        return idleFor >= TimeSpan.FromMinutes(Math.Max(1, _settings.IdleMinutes));
    }

    private async Task<bool> HasRecentPolicyActionAsync(AppDbContext db, Guid agentId, CancellationToken ct)
    {
        var since = UtcNow() - Cooldown();
        return await db.AgentIncidents.AsNoTracking()
            .AnyAsync(i => i.AgentId == agentId
                && (i.Kind == AgentIncidentKind.PolicyRefreshed
                    || i.Kind == AgentIncidentKind.PolicyRefreshFailed)
                && i.CreatedAt >= since, ct);
    }

    private async Task<bool> WaitUntilStoppedAsync(Guid sessionId, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < KillConfirmTimeout)
        {
            ct.ThrowIfCancellationRequested();
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var status = await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => (SessionStatus?)s.Status)
                .FirstOrDefaultAsync(ct);
            if (status == SessionStatus.Stopped)
                return true;
            if (status is SessionStatus.Failed or null)
                return false;
            await Task.Delay(50, ct);
        }

        return false;
    }

    private async Task RecordAsync(
        IServiceScope scope,
        AppDbContext db,
        Guid agentId,
        Guid sessionId,
        AgentIncidentKind kind,
        AlertSeverity severity,
        string message,
        bool raiseAlert,
        CancellationToken ct)
    {
        var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
        await supervisor.RecordIncidentAsync(
            agentId, sessionId, kind, severity, message, raiseAlert: raiseAlert, ct: ct);
        await db.SaveChangesAsync(ct);
    }

    private TimeSpan Cooldown() =>
        TimeSpan.FromMinutes(Math.Max(5, _settings.CooldownMinutes));

    private bool RecentlyAttempted(Guid agentId, DateTime now) =>
        _attempts.TryGetValue(agentId, out var at) && now - at < Cooldown();

    private void Stamp(Guid agentId, DateTime now) => _attempts[agentId] = now;

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
