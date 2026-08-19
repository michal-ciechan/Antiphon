using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0082 S3: once-a-minute idle+full scan that enqueues a verified <c>/compact</c> through
/// the existing session queue. Eligibility is every Running Claude Code session (claimed or not);
/// per-agent overrides apply only when an Agent's PersistentSessionId claims the session.
///
/// Singleton: the in-memory per-session attempt stamp has to survive the hosted service's
/// per-tick scope, or two ticks a few seconds apart would double-fire inside the delivery window.
/// A server restart losing the stamp can at worst re-attempt a compact whose preconditions still
/// hold — wasteful once, not harmful. The durable cooldown is transcript rows.
/// </summary>
public sealed class ContextCompactionService
{
    /// <summary>
    /// The trigger body. Instructions do two jobs: a better summary, and a body long enough that
    /// CARD-0055's confirmation is a real text match (raw typed record AND the wrapper), never
    /// the weak arm. A bare 8-char <c>/compact</c> would fall through.
    /// </summary>
    public const string CompactTriggerBody =
        "/compact Focus the summary on: current task state, key decisions and their reasons, "
        + "file paths touched, and anything you committed or still owe.";

    public const string CompactCommandName = "/compact";

    /// <summary>
    /// Covers the seconds-wide window between enqueue and the <c>/compact</c> landing as a
    /// transcript row (which is what the durable cooldown reads). Longer than one sweep period,
    /// shorter than the 24 h transcript cooldown.
    /// </summary>
    internal static readonly TimeSpan AttemptStampTtl = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, DateTime> _attempts = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionMessageQueueService _queue;
    private readonly AgentSessionRuntime _runtime;
    private readonly ContextCompactionSettings _settings;
    private readonly ContextWindowSettings _window;
    private readonly TimeProvider _time;
    private readonly ILogger<ContextCompactionService> _logger;

    public ContextCompactionService(
        IServiceScopeFactory scopeFactory,
        SessionMessageQueueService queue,
        AgentSessionRuntime runtime,
        IOptions<ContextCompactionSettings> settings,
        IOptions<ContextWindowSettings> window,
        TimeProvider time,
        ILogger<ContextCompactionService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _runtime = runtime;
        _settings = settings.Value;
        _window = window.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Scan running Claude sessions; enqueue at most one compact per eligible session. Returns
    /// how many sessions this pass actually enqueued for. Other sessions may ride along on a
    /// shared database — callers must not assert on this number as a global count.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        List<(Guid Id, string? EffectiveModelId)> sessions = [];
        Dictionary<Guid, Agent> owners = [];
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.AgentSessions.AsNoTracking()
                .Where(s => s.Status == SessionStatus.Running && s.AgentKind == AgentKind.ClaudeCode)
                .Select(s => new { s.Id, s.EffectiveModelId })
                .ToListAsync(ct);
            sessions = rows.Select(s => (s.Id, s.EffectiveModelId)).ToList();
            if (sessions.Count == 0)
                return 0;

            var idTexts = sessions.Select(s => s.Item1.ToString("D")).ToList();
            var claimed = await db.Agents.AsNoTracking()
                .Where(a => a.PersistentSessionId != null && idTexts.Contains(a.PersistentSessionId))
                .ToListAsync(ct);
            owners = new Dictionary<Guid, Agent>();
            foreach (var agent in claimed)
            {
                if (Guid.TryParse(agent.PersistentSessionId, out var sid) && !owners.ContainsKey(sid))
                    owners[sid] = agent;
            }
        }

        var now = UtcNow();
        var fired = 0;
        foreach (var (sessionId, effectiveModelId) in sessions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                owners.TryGetValue(sessionId, out var owner);
                if (await ProcessSessionAsync(sessionId, effectiveModelId, owner, now, ct))
                    fired++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-compact sweep failed for session {SessionId}", sessionId);
            }
        }

        return fired;
    }

    private async Task<bool> ProcessSessionAsync(
        Guid sessionId,
        string? effectiveModelId,
        Agent? owner,
        DateTime now,
        CancellationToken ct)
    {
        var resolved = ContextCompaction.Resolve(_settings, owner);

        await ObserveTimedOutCompactsAsync(sessionId, owner, now, ct);

        if (!resolved.Enabled)
            return false;
        if (RecentlyAttempted(sessionId, now))
            return false;
        if (!_runtime.ListLiveSessions().Contains(sessionId))
            return false;

        if (!await IsEligibleFromStoreAsync(sessionId, effectiveModelId, resolved, now, pullFirst: false, ct))
            return false;

        // Pull before acting: no destructive-ish action on "the transcript doesn't show activity"
        // without a pull. Recompute idle-for, fullness, and working from stored rows after.
        await _runtime.CatchUpTranscriptAsync(sessionId, ct);

        if (!await IsEligibleFromStoreAsync(sessionId, effectiveModelId, resolved, UtcNow(), pullFirst: true, ct))
            return false;

        Stamp(sessionId, UtcNow());
        await _queue.EnqueueAsync(
            sessionId, CompactTriggerBody, MessageSendMode.WhenIdle, ct,
            origin: QueuedMessageOrigin.Supervision);

        _logger.LogInformation(
            "Auto-compact enqueued for session {SessionId} (owner={Owner}, idleMinutes={IdleMinutes}, contextPercent={ContextPercent})",
            sessionId, owner?.Name ?? "<unclaimed>", resolved.IdleMinutes, resolved.ContextPercent);
        return true;
    }

    /// <summary>
    /// Cheap stored-row eligibility. <paramref name="pullFirst"/> is documentation — the caller
    /// decides whether CatchUp already ran; this always reads what is in the DB now.
    /// </summary>
    private async Task<bool> IsEligibleFromStoreAsync(
        Guid sessionId,
        string? effectiveModelId,
        ResolvedContextCompaction resolved,
        DateTime now,
        bool pullFirst,
        CancellationToken ct)
    {
        _ = pullFirst;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stillRunning = await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
        if (!stillRunning)
            return false;

        if (await HasOpenSupervisionCompactAsync(db, sessionId, now, ct))
            return false;

        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .Select(t => new
            {
                t.Sequence,
                t.Kind,
                t.Text,
                t.InputTokens,
                t.OutputTokens,
                t.CacheReadTokens,
                t.CacheCreationTokens,
                t.Model,
                t.IsApiError,
                t.Timestamp,
                t.CreatedAt,
            })
            .ToListAsync(ct);

        // Zero transcript rows: unknown on both idle and fullness. Bind-failed sessions skip.
        if (rows.Count == 0)
            return false;

        if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
            return false;

        var newest = rows.Max(r => r.Timestamp ?? r.CreatedAt);
        if (now - newest < TimeSpan.FromMinutes(resolved.IdleMinutes))
            return false;

        var cooldownFrom = now - TimeSpan.FromHours(_settings.CooldownHours);
        if (rows.Any(r => IsCooldownRow(r.Kind, r.Text, r.Timestamp ?? r.CreatedAt, cooldownFrom)))
            return false;

        var contextRows = rows
            .Select(r => new TranscriptContextRow(
                r.Sequence, r.Kind, r.Text,
                r.InputTokens, r.OutputTokens, r.CacheReadTokens, r.CacheCreationTokens,
                r.Model, r.IsApiError))
            .ToList();
        var usage = SessionContextUsage.Compute(contextRows, effectiveModelId, _window, _logger);
        if (usage.Fullness is not double fullness)
            return false;
        if (fullness * 100.0 < resolved.ContextPercent)
            return false;

        return true;
    }

    private async Task ObserveTimedOutCompactsAsync(
        Guid sessionId, Agent? owner, DateTime now, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = now - TimeSpan.FromMinutes(_settings.BoundaryTimeoutMinutes);

        var timedOut = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && m.Origin == QueuedMessageOrigin.Supervision
                && m.Status == QueuedMessageStatus.Sent
                && m.SentAt != null
                && m.SentAt < cutoff)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);
        if (timedOut.Count == 0)
            return;

        var alreadyRaised = await HasAutoCompactFailedSinceAsync(db, sessionId, timedOut[0].SentAt!.Value, ct);
        if (alreadyRaised)
            return;

        var floor = timedOut.Min(m => m.LastDeliveryBaselineSequence ?? 0);
        var boundaryLanded = await db.TranscriptEntries.AsNoTracking()
            .AnyAsync(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.CompactBoundary
                && t.Text != null
                && t.Text.Contains(TranscriptKinds.ManualCompactMarker)
                && t.Sequence > floor, ct);
        if (boundaryLanded)
            return;

        var message =
            $"Idle auto-compact on session {sessionId:D} was submitted at {timedOut[0].SentAt:u} "
            + $"but no (manual) CompactBoundary appeared within {_settings.BoundaryTimeoutMinutes} minutes.";
        await RaiseAutoCompactFailedAsync(scope, db, sessionId, owner, message, "BoundaryTimeout", ct);
    }

    private async Task<bool> HasOpenSupervisionCompactAsync(
        AppDbContext db, Guid sessionId, DateTime now, CancellationToken ct)
    {
        // A Pending row is in flight (or waiting for the idle path). A Sent row still inside
        // the boundary-timeout window may yet land a CompactBoundary. Older Sent rows have
        // either succeeded (cooldown from the boundary) or timed out (incident already raised).
        var sentCutoff = now - TimeSpan.FromMinutes(_settings.BoundaryTimeoutMinutes);
        return await db.SessionQueuedMessages.AsNoTracking()
            .AnyAsync(m => m.AgentSessionId == sessionId
                && m.Origin == QueuedMessageOrigin.Supervision
                && (m.Status == QueuedMessageStatus.Pending
                    || (m.Status == QueuedMessageStatus.Sent
                        && m.SentAt != null
                        && m.SentAt >= sentCutoff)), ct);
    }

    private static async Task<bool> HasAutoCompactFailedSinceAsync(
        AppDbContext db, Guid sessionId, DateTime sinceUtc, CancellationToken ct)
    {
        if (await db.AgentIncidents.AsNoTracking()
            .AnyAsync(i => i.SessionId == sessionId
                && i.Kind == AgentIncidentKind.AutoCompactFailed
                && i.CreatedAt >= sinceUtc, ct))
            return true;

        var key = AutoCompactFailedDedupKey(sessionId);
        return await db.Alerts.AsNoTracking()
            .AnyAsync(a => a.SessionId == sessionId && a.DedupKey == key && a.CreatedAt >= sinceUtc, ct);
    }

    private async Task RaiseAutoCompactFailedAsync(
        IServiceScope scope,
        AppDbContext db,
        Guid sessionId,
        Agent? owner,
        string message,
        string failureReason,
        CancellationToken ct)
    {
        if (owner is not null)
        {
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                owner.Id,
                sessionId,
                AgentIncidentKind.AutoCompactFailed,
                AlertSeverity.Warning,
                message,
                failureReason: failureReason,
                ct: ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Unclaimed: no Agent row to hang an incident on (AgentIncident.AgentId is required).
        // Raise a standalone Warning alert with the session id so it is still visible in the
        // alerts feed, and log — the same "notify nobody, do not swallow" shape as an unowned
        // ChannelReplyLost (which also has no caller).
        _logger.LogWarning(
            "AUTO-COMPACT FAILED on unclaimed session {SessionId}: {Message} ({Reason})",
            sessionId, message, failureReason);

        var alerts = scope.ServiceProvider.GetService<IAlertService>();
        if (alerts is null)
            return;
        await alerts.RaiseAsync(
            new AlertRaise(
                AlertSeverity.Warning,
                Source: "supervisor",
                Title: $"{AgentIncidentKind.AutoCompactFailed}: idle auto-compact",
                Detail: message,
                DedupKey: AutoCompactFailedDedupKey(sessionId),
                AgentId: null,
                SessionId: sessionId),
            ct);
    }

    internal static string AutoCompactFailedDedupKey(Guid sessionId) =>
        $"supervisor:{AgentIncidentKind.AutoCompactFailed}:{sessionId:D}";

    internal static bool IsCooldownRow(string kind, string? text, DateTime at, DateTime cooldownFrom)
    {
        if (at < cooldownFrom)
            return false;
        if (kind == TranscriptKinds.CompactBoundary)
            return true;
        return IsCompactSubmission(kind, text);
    }

    internal static bool IsCompactSubmission(string kind, string? text)
    {
        if (kind != TranscriptKinds.UserPrompt || text is null)
            return false;
        var name = TranscriptKinds.TryReadLocalCommandName(kind, text);
        if (string.Equals(name, CompactCommandName, StringComparison.OrdinalIgnoreCase))
            return true;

        // The raw typed line Claude records IN ADDITION to the wrapper. Matching a '/' prefix
        // alone stays rejected (a real prompt may begin with a slash); this is the command
        // followed by end-of-string or whitespace, the same shape IsRawLocalCommandEcho uses.
        var trimmed = text.TrimStart();
        if (trimmed.Length < CompactCommandName.Length)
            return false;
        if (!trimmed.StartsWith(CompactCommandName, StringComparison.OrdinalIgnoreCase))
            return false;
        return trimmed.Length == CompactCommandName.Length
            || char.IsWhiteSpace(trimmed[CompactCommandName.Length]);
    }

    private bool RecentlyAttempted(Guid sessionId, DateTime now) =>
        _attempts.TryGetValue(sessionId, out var at) && now - at < AttemptStampTtl;

    private void Stamp(Guid sessionId, DateTime now) => _attempts[sessionId] = now;

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
