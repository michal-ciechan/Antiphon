using System.Collections.Concurrent;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// CARD-0334 S2/S3: once-a-minute idle-boundary relaunch or WhenIdle notify when a live
/// standing agent's bundles or instruction files have drifted from the repo. Singleton so
/// the in-memory per-agent attempt stamp survives the hosted service's per-tick scope.
///
/// <para>Kill uses <see cref="SessionTerminationSource.PolicyRefresh"/>, never
/// <c>StopAsync</c> (that suspends supervision). A failed <c>StartAsync</c> leaves supervision
/// untouched — the session is Stopped and the supervisor's next tick schedules its normal
/// ladder restart, which also carries the new bundles.</para>
/// </summary>
public sealed class PolicyRefreshService
{
    internal static readonly TimeSpan KillConfirmTimeout = TimeSpan.FromSeconds(30);

    public const string SessionWorkingCode = "session_working";
    public const string NotResumableCode = "not_resumable";

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
    /// <c>StartAsync</c> accepted. Notify-lane deliveries are not counted — callers must not
    /// assert on this number as a global count.
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
                var outcome = await ActAsync(agent, session, force: false, throwOnBlock: false, ct);
                if (outcome.Refreshed)
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

    /// <summary>
    /// CARD-0334 S3. Manual front door. Idle-gated like the sweep; <paramref name="force"/>
    /// skips only the idle-minutes floor and the cooldown. A working session is always
    /// 409 <see cref="SessionWorkingCode"/>; an agent that cannot be resumed or is out of
    /// population is 409 <see cref="NotResumableCode"/>.
    /// </summary>
    public async Task<PolicyRefreshOutcome> RefreshAgentAsync(
        Guid agentId, bool force, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        if (agent.IsPoolDelegate
            || !Guid.TryParse(agent.PersistentSessionId, out var sessionId))
        {
            throw new ConflictException(
                "This agent has no live session to refresh.", NotResumableCode);
        }

        var session = await db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct)
            ?? throw new ConflictException(
                "This agent has no live session to refresh.", NotResumableCode);

        return await ActAsync(agent, session, force, throwOnBlock: true, ct);
    }

    private async Task<PolicyRefreshOutcome> ActAsync(
        Agent agent,
        AgentSession session,
        bool force,
        bool throwOnBlock,
        CancellationToken ct)
    {
        if (session.SessionBackend == SessionBackend.Herdr
            || session.CardId is not null
            || (session.ComposedBundleStamp is null && session.InstructionFileStamp is null))
        {
            return Refuse(
                throwOnBlock,
                "This session cannot be policy-refreshed (no stamp, a card session, or a Herdr pane).");
        }

        var mode = agent.PolicyRefreshMode ?? PolicyRefreshMode.Auto;
        if (mode == PolicyRefreshMode.Off)
        {
            return Refuse(throwOnBlock, "Policy refresh is off for this agent.");
        }

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
            return PolicyRefreshOutcome.None;

        var lane = ResolveLane(agent, session, mode, IsTranscriptBound(session.Id));
        if (lane == Lane.None)
            return Refuse(throwOnBlock, "This agent cannot be policy-refreshed.");

        if (!await PrepareToActAsync(scope, db, agent, session.Id, force, throwOnBlock, ct))
            return PolicyRefreshOutcome.None;

        if (lane == Lane.Notify)
        {
            return await NotifyAsync(
                scope, db, agent, session, composed.StampLine, files.StampLine, ct);
        }

        return await RelaunchAsync(
            scope, db, agent, session, composed.StampLine, files.StampLine, ct);
    }

    private async Task<bool> PrepareToActAsync(
        IServiceScope scope,
        AppDbContext db,
        Agent agent,
        Guid sessionId,
        bool force,
        bool throwOnBlock,
        CancellationToken ct)
    {
        var block = await EvaluateGatesAsync(scope, db, agent, sessionId, force, ct);
        if (!ApplyBlock(block, throwOnBlock))
            return false;

        // Pull before acting: never kill or notify on a stale working/idle read (gotchas #49/#50).
        await _runtime.CatchUpTranscriptAsync(sessionId, ct);

        block = await EvaluateGatesAsync(scope, db, agent, sessionId, force, ct);
        return ApplyBlock(block, throwOnBlock);
    }

    private static bool ApplyBlock(GateBlock block, bool throwOnBlock)
    {
        if (block.Working || block.QueueBusy)
        {
            if (throwOnBlock)
            {
                throw new ConflictException(
                    "The session is working; wait until it is idle.", SessionWorkingCode);
            }

            return false;
        }

        if (block.NotLive || block.Suspended || block.HeldModel)
        {
            if (throwOnBlock)
            {
                throw new ConflictException(
                    block.NotLive
                        ? "This agent has no live session to refresh."
                        : block.Suspended
                            ? "Supervision is suspended for this agent."
                            : "This agent's model is held.",
                    NotResumableCode);
            }

            return false;
        }

        // Idle floor / cooldown: force already folded these off. Sweep and a non-force
        // POST skip the same way — not a 409.
        return !block.IdleTooRecent && !block.Cooldown;
    }

    private static PolicyRefreshOutcome Refuse(bool throwOnBlock, string message)
    {
        if (throwOnBlock)
            throw new ConflictException(message, NotResumableCode);
        return PolicyRefreshOutcome.None;
    }

    private async Task<PolicyRefreshOutcome> NotifyAsync(
        IServiceScope scope,
        AppDbContext db,
        Agent agent,
        AgentSession session,
        string currentBundles,
        string currentFiles,
        CancellationToken ct)
    {
        var stamp = ComposeNotifyStamp(currentBundles, currentFiles);
        var live = await db.AgentSessions.FirstAsync(s => s.Id == session.Id, ct);
        if (string.Equals(live.PolicyNotifiedStamp, stamp, StringComparison.Ordinal))
            return PolicyRefreshOutcome.None;

        var delta = PolicyRefreshDelta.FormatNotify(
            session.ComposedBundleStamp,
            currentBundles,
            session.InstructionFileStamp,
            currentFiles);
        var body = ChannelPreamble.PolicyDriftNotifyBody(delta);

        var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();
        await queue.EnqueueAsync(
            session.Id, body, MessageSendMode.WhenIdle, ct, origin: QueuedMessageOrigin.System);

        live.PolicyNotifiedStamp = stamp;
        await db.SaveChangesAsync(ct);

        Stamp(agent.Id, UtcNow());
        await RecordAsync(
            scope, db, agent.Id, session.Id,
            AgentIncidentKind.PolicyDriftNotified, AlertSeverity.Info,
            $"Policy drift notified: {delta}",
            raiseAlert: false, ct);

        _logger.LogInformation(
            "Policy-refresh notified agent {AgentName} ({AgentId}) session {SessionId}: {Delta}",
            agent.Name, agent.Id, session.Id, delta);
        return new PolicyRefreshOutcome(Refreshed: false, Notified: true);
    }

    private async Task<PolicyRefreshOutcome> RelaunchAsync(
        IServiceScope scope,
        AppDbContext db,
        Agent agent,
        AgentSession session,
        string currentBundles,
        string currentFiles,
        CancellationToken ct)
    {
        var now = UtcNow();
        Stamp(agent.Id, now);

        var delta = PolicyRefreshDelta.Format(
            session.ComposedBundleStamp,
            currentBundles,
            session.InstructionFileStamp,
            currentFiles);

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
            return PolicyRefreshOutcome.None;
        }

        if (!await WaitUntilStoppedAsync(session.Id, ct))
        {
            await RecordAsync(
                scope, db, agent.Id, session.Id,
                AgentIncidentKind.PolicyRefreshFailed, AlertSeverity.Warning,
                "Policy refresh kill did not reach Stopped within 30s; not starting.",
                raiseAlert: true, ct);
            return PolicyRefreshOutcome.None;
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
            return PolicyRefreshOutcome.None;
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
        return new PolicyRefreshOutcome(Refreshed: true, Notified: false);
    }

    /// <summary>
    /// D4 lane. Unbound transcript demotes Relaunch to Notify (a resume would fall back
    /// to fresh and lose the conversation). Tick-skips (suspended, held model, NextRestartAt)
    /// are not a lane.
    /// </summary>
    internal static Lane ResolveLane(
        Agent agent, AgentSession session, PolicyRefreshMode mode, bool transcriptBound)
    {
        if (mode == PolicyRefreshMode.Off)
            return Lane.None;
        if (session.ComposedBundleStamp is null && session.InstructionFileStamp is null)
            return Lane.None;
        if (session.SessionBackend == SessionBackend.Herdr)
            return Lane.None;

        var relaunchEligible = agent.Kind == AgentKind.ClaudeCode && agent.AlwaysOn && transcriptBound;
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

    internal static string ComposeNotifyStamp(string? bundles, string? files) =>
        $"{bundles ?? ""}\n{files ?? ""}";

    internal enum Lane
    {
        None,
        Notify,
        Relaunch,
    }

    private readonly record struct GateBlock(
        bool NotLive,
        bool Suspended,
        bool HeldModel,
        bool Working,
        bool QueueBusy,
        bool IdleTooRecent,
        bool Cooldown);

    private async Task<GateBlock> EvaluateGatesAsync(
        IServiceScope scope,
        AppDbContext db,
        Agent agent,
        Guid sessionId,
        bool force,
        CancellationToken ct)
    {
        var notLive = !_runtime.ListLiveSessions().Contains(sessionId)
            && !_runtime.TryGetLiveMetadata(sessionId, out _);

        var state = await db.AgentSupervisionStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentId == agent.Id, ct);
        var suspended = state is { Suspended: true } || state?.NextRestartAt is not null;

        var held = false;
        var availability = scope.ServiceProvider.GetService<IModelAvailability>()
            ?? scope.ServiceProvider.GetService<ModelAvailability>();
        if (availability is not null)
        {
            var alias = ModelAlias.Normalize(agent.Kind, agent.ModelId)
                ?? ModelLevelAliases.For(agent.Kind, agent.ModelLevel);
            held = await availability.IsHeldAsync(agent.Kind, alias, ct);
        }

        var working = await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct);
        var queueBusy = await HasBlockingQueueRowAsync(db, sessionId, ct);
        var idleTooRecent = !force && !await HasBeenIdleLongEnoughAsync(db, sessionId, ct);
        var cooldown = !force
            && (RecentlyAttempted(agent.Id, UtcNow())
                || await HasRecentPolicyActionAsync(db, agent.Id, ct));

        return new GateBlock(notLive, suspended, held, working, queueBusy, idleTooRecent, cooldown);
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
                    || i.Kind == AgentIncidentKind.PolicyRefreshFailed
                    || i.Kind == AgentIncidentKind.PolicyDriftNotified)
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

/// <summary>CARD-0334 S3. Result of one notify or relaunch attempt.</summary>
public readonly record struct PolicyRefreshOutcome(bool Refreshed, bool Notified)
{
    public static PolicyRefreshOutcome None { get; } = new(false, false);
}
