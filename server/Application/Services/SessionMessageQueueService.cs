using System.Collections.Concurrent;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// Manages messages queued to a live agent session. "Send now" delivers immediately; "wait until idle"
/// holds the message until the agent reaches a turn-end (<c>stop_reason: end_turn</c>), then delivers the
/// oldest pending message — one per turn. When a turn ends with an empty queue the session is considered
/// completely finished and a <c>SessionFinished</c> signal is broadcast (badge + notification).
///
/// Singleton: it owns per-session flush locks and is invoked from the (singleton) <see cref="AgentSessionRuntime"/>
/// transcript observer. DB access is via a scope per operation, mirroring the runtime's own pattern.
/// </summary>
public sealed class SessionMessageQueueService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentSessionRuntime _runtime;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly DeliveryVerificationSettings _verification;
    private readonly ILogger<SessionMessageQueueService> _logger;

    public SessionMessageQueueService(
        IServiceScopeFactory scopeFactory,
        AgentSessionRuntime runtime,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<SessionMessageQueueService> logger,
        IOptions<SupervisionSettings>? supervisionSettings = null)
    {
        _scopeFactory = scopeFactory;
        _runtime = runtime;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _verification = (supervisionSettings?.Value ?? new SupervisionSettings()).DeliveryVerification;
        _logger = logger;
    }

    /// <summary>Queue a message ("wait until idle") or deliver it immediately ("send now").</summary>
    public async Task<SessionQueueDto> EnqueueAsync(
        Guid sessionId, string body, MessageSendMode mode, CancellationToken ct)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(body), "Message must not be empty.");

        await EnsureSessionExistsAsync(sessionId, ct);

        if (mode == MessageSendMode.Now)
        {
            if (!_runtime.ListLiveSessions().Contains(sessionId))
                throw new ConflictException($"Agent session '{sessionId}' is not live; cannot send now.");

            var verdict = await DeliverAsync(sessionId, trimmed, ct);
            if (verdict != DeliveryVerdict.Delivered)
            {
                await HandleDeliveryFailureAsync(sessionId, null, verdict, ct);
                throw new ConflictException(
                    "Message delivery could not be verified — the terminal did not accept it "
                    + $"({Describe(verdict)}). See the agent's incidents.");
            }
            return await GetQueueAsync(sessionId, ct);
        }

        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = UtcNow();

            var nextSequence = (await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == sessionId)
                .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;

            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = trimmed,
                Status = QueuedMessageStatus.Pending,
                Sequence = nextSequence,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);

            // If the agent is already idle (waiting at the prompt), there is no upcoming turn-end to
            // flush on — deliver right away so the message isn't stranded.
            if (_runtime.ListLiveSessions().Contains(sessionId) && !await IsWorkingAsync(db, sessionId, ct))
                await DeliverNextLockedAsync(db, sessionId, ct);
        }
        finally
        {
            sem.Release();
        }

        var dto = await GetQueueAsync(sessionId, ct);
        await PublishQueueChangedAsync(dto, ct);
        return dto;
    }

    /// <summary>Pending messages for the session, plus whether the agent is currently working.</summary>
    public async Task<SessionQueueDto> GetQueueAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await BuildQueueDtoAsync(db, sessionId, ct);
    }

    /// <summary>Remove a pending message before it is delivered.</summary>
    public async Task<SessionQueueDto> CancelAsync(Guid sessionId, Guid messageId, CancellationToken ct)
    {
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.AgentSessionId == sessionId, ct)
                ?? throw new NotFoundException(nameof(SessionQueuedMessage), messageId);

            if (message.Status == QueuedMessageStatus.Pending)
            {
                message.Status = QueuedMessageStatus.Canceled;
                message.CanceledAt = UtcNow();
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            sem.Release();
        }

        var dto = await GetQueueAsync(sessionId, ct);
        await PublishQueueChangedAsync(dto, ct);
        return dto;
    }

    /// <summary>Promote a specific queued message: deliver it immediately and remove it from the queue.</summary>
    public async Task<SessionQueueDto> SendNowAsync(Guid sessionId, Guid messageId, CancellationToken ct)
    {
        if (!_runtime.ListLiveSessions().Contains(sessionId))
            throw new ConflictException($"Agent session '{sessionId}' is not live; cannot send now.");

        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.AgentSessionId == sessionId, ct)
                ?? throw new NotFoundException(nameof(SessionQueuedMessage), messageId);

            if (message.Status != QueuedMessageStatus.Pending)
                throw new ConflictException("Message is no longer pending.");

            message.Status = QueuedMessageStatus.Sent;
            message.SentAt = UtcNow();
            await db.SaveChangesAsync(ct);
            var verdict = await DeliverAsync(sessionId, message.Body, ct);
            if (verdict != DeliveryVerdict.Delivered)
            {
                await HandleDeliveryFailureAsync(sessionId, message.Id, verdict, ct);
                throw new ConflictException(
                    "Message delivery could not be verified — the terminal did not accept it "
                    + $"({Describe(verdict)}). The message has been returned to the queue.");
            }
        }
        finally
        {
            sem.Release();
        }

        var dto = await GetQueueAsync(sessionId, ct);
        await PublishQueueChangedAsync(dto, ct);
        return dto;
    }

    /// <summary>
    /// Called when a session reaches a turn-end (idle). Delivers the next queued message if any; otherwise
    /// the agent has completely finished, so broadcast <c>SessionFinished</c>.
    /// </summary>
    public async Task OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        FlushResult flush;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            flush = await DeliverNextLockedAsync(db, sessionId, ct);
        }
        finally
        {
            sem.Release();
        }

        if (flush == FlushResult.Nothing)
        {
            await PublishFinishedAsync(sessionId, ct);
        }
        else
        {
            // Delivered (queue shrank) or Failed (message reverted to Pending) — either way the
            // queue view changed. A failed flush is NOT "finished".
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        }
    }

    /// <summary>
    /// Stranded-queue watchdog (called periodically by the session-health hosted service): delivers
    /// pending messages that have been sitting on an IDLE, live, always-on session longer than
    /// <see cref="DeliveryVerificationSettings.StrandedAgeSeconds"/>. This is the redelivery half of
    /// delivery verification — after a verification failure kills a wedged session and the
    /// supervisor resumes it (same session id), nothing else would flush the reverted message until
    /// the next turn end, which an idle session never produces.
    /// </summary>
    public async Task<int> FlushStrandedQueuesAsync(CancellationToken ct)
    {
        var cutoff = UtcNow() - TimeSpan.FromSeconds(_verification.StrandedAgeSeconds);

        List<Guid> candidates;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pendingSessionIds = await db.SessionQueuedMessages
                .AsNoTracking()
                .Where(m => m.Status == QueuedMessageStatus.Pending && m.CreatedAt <= cutoff)
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct);
            if (pendingSessionIds.Count == 0)
                return 0;

            // Only always-on agents' sessions: their composer is guaranteed fresh after a
            // verification-failure restart, so re-typing cannot double up. Other sessions keep
            // their pending messages visible for a human to resend.
            var keys = pendingSessionIds.Select(id => id.ToString("D")).ToList();
            var alwaysOnKeys = await db.Agents
                .AsNoTracking()
                .Where(a => a.AlwaysOn && a.PersistentSessionId != null && keys.Contains(a.PersistentSessionId))
                .Select(a => a.PersistentSessionId!)
                .ToListAsync(ct);
            candidates = alwaysOnKeys.Select(Guid.Parse).ToList();
        }

        if (candidates.Count == 0)
            return 0;

        var live = _runtime.ListLiveSessions();
        var flushed = 0;
        foreach (var sessionId in candidates.Where(live.Contains))
        {
            ct.ThrowIfCancellationRequested();
            var result = FlushResult.Nothing;
            var sem = GetLock(sessionId);
            await sem.WaitAsync(ct);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await IsWorkingAsync(db, sessionId, ct))
                    result = await DeliverNextLockedAsync(db, sessionId, ct);
            }
            finally
            {
                sem.Release();
            }

            if (result == FlushResult.Delivered)
            {
                flushed++;
                _logger.LogInformation(
                    "Stranded-queue watchdog delivered a pending message to idle session {SessionId}", sessionId);
                await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
            }
        }

        return flushed;
    }

    private enum FlushResult { Nothing, Delivered, Failed }

    // Claims and delivers the oldest pending message (caller holds the per-session lock).
    private async Task<FlushResult> DeliverNextLockedAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var next = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .OrderBy(m => m.Sequence)
            .FirstOrDefaultAsync(ct);
        if (next is null)
            return FlushResult.Nothing;

        next.Status = QueuedMessageStatus.Sent;
        next.SentAt = UtcNow();
        await db.SaveChangesAsync(ct);
        var verdict = await DeliverAsync(sessionId, next.Body, ct);
        if (verdict == DeliveryVerdict.Delivered)
            return FlushResult.Delivered;

        await HandleDeliveryFailureAsync(sessionId, next.Id, verdict, ct);
        return FlushResult.Failed;
    }

    private enum DeliveryVerdict { Delivered, NoComposerEvidence, NoSubmitOutput }

    private static string Describe(DeliveryVerdict verdict) => verdict switch
    {
        DeliveryVerdict.NoComposerEvidence => "the typed message never appeared in the composer",
        DeliveryVerdict.NoSubmitOutput => "the submitting Enter produced no output",
        _ => "delivered",
    };

    // Inject text into the session terminal and submit it, reusing the runtime's input path (which also
    // kicks off manual-turn tracking). The body and the submitting carriage return are sent as two
    // separate writes with a short pause between — NOT concatenated. Claude Code's TUI treats text and a
    // trailing CR arriving in a single write as a bracketed paste and folds the CR into a literal newline,
    // so the message lands in the composer but never submits. A delayed, separate CR is the same path
    // RunnerTerminalSession.SendLineAsync uses for prompts, and it submits reliably.
    //
    // For Claude sessions the gap between the two writes is also the VERIFICATION window: the rendered
    // screen must show evidence of the typed body (ComposerDeliveryEvidence — the contract pinned by
    // ClaudeComposerRenderCanaryTests) before the Enter is sent, and the output sequence must advance
    // after it. A wedged terminal leaves neither fingerprint, and crucially the Enter is withheld so the
    // message is never lost into a dead composer.
    private async Task<DeliveryVerdict> DeliverAsync(Guid sessionId, string body, CancellationToken ct)
    {
        var trimmed = body.TrimEnd();

        var verify = _verification.Enabled && await IsClaudeCodeSessionAsync(sessionId, ct);
        AgentSessionLiveSnapshot before = default!;
        if (verify && !_runtime.TryGetLiveSnapshot(sessionId, out before))
        {
            // The screen is unobservable (snapshot endpoint down, adopted-but-resyncing, …).
            // That is an observability failure, not a terminal failure — deliver blind rather
            // than wrongly declare the session wedged (the echo-probe lesson).
            _logger.LogDebug(
                "Delivery to session {SessionId} is unverifiable (no live snapshot); sending blind", sessionId);
            verify = false;
        }

        await _runtime.SendInputAsync(sessionId, trimmed, ct);

        if (verify && !await WaitForComposerEvidenceAsync(sessionId, before.RenderedScreen, trimmed, ct))
        {
            _logger.LogWarning(
                "Delivery verification failed for session {SessionId}: body ({Length} chars) produced no "
                + "composer evidence within {Timeout}s — submit Enter withheld",
                sessionId, trimmed.Length, _verification.EvidenceTimeoutSeconds);
            return DeliveryVerdict.NoComposerEvidence;
        }

        long? sequenceBeforeSubmit = null;
        if (verify && _runtime.TryGetLiveMetadata(sessionId, out var meta))
            sequenceBeforeSubmit = meta.LastSequence;

        await Task.Delay(TimeSpan.FromMilliseconds(20), _timeProvider, ct);
        await _runtime.SendInputAsync(sessionId, "\r", ct);

        if (sequenceBeforeSubmit is { } baseline
            && !await WaitForSequenceAdvanceAsync(sessionId, baseline, ct))
        {
            _logger.LogWarning(
                "Delivery verification failed for session {SessionId}: submit Enter produced no output "
                + "within {Timeout}s",
                sessionId, _verification.PostSubmitAdvanceTimeoutSeconds);
            return DeliveryVerdict.NoSubmitOutput;
        }

        return DeliveryVerdict.Delivered;
    }

    private async Task<bool> WaitForComposerEvidenceAsync(
        Guid sessionId, string screenBefore, string body, CancellationToken ct)
    {
        var deadline = UtcNow() + TimeSpan.FromSeconds(_verification.EvidenceTimeoutSeconds);
        while (true)
        {
            if (_runtime.TryGetLiveSnapshot(sessionId, out var after)
                && ComposerDeliveryEvidence.IsVisible(screenBefore, after.RenderedScreen, body))
            {
                return true;
            }

            if (UtcNow() >= deadline)
                return false;

            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
    }

    private async Task<bool> WaitForSequenceAdvanceAsync(Guid sessionId, long baseline, CancellationToken ct)
    {
        var deadline = UtcNow() + TimeSpan.FromSeconds(_verification.PostSubmitAdvanceTimeoutSeconds);
        while (true)
        {
            if (_runtime.TryGetLiveMetadata(sessionId, out var meta) && meta.LastSequence > baseline)
                return true;

            if (UtcNow() >= deadline)
                return false;

            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
    }

    private async Task<bool> IsClaudeCodeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        // The composer rendering contract is Claude-specific; Codex/Raw sessions deliver blind.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.AgentKind == AgentKind.ClaudeCode, ct);
    }

    // Verification failed: return the message to the queue (never silently lose it), record an
    // incident against the owning agent (which also raises an alert), and for always-on agents kill
    // the wedged session — the supervisor's ladder restarts it (resuming the SAME session row, so
    // the reverted message redelivers via the stranded-queue watchdog), and the kill guarantees a
    // fresh composer so redelivery cannot double-type.
    private async Task HandleDeliveryFailureAsync(
        Guid sessionId, Guid? messageId, DeliveryVerdict verdict, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            if (messageId is { } id)
            {
                var message = await db.SessionQueuedMessages
                    .FirstOrDefaultAsync(m => m.Id == id && m.AgentSessionId == sessionId, ct);
                if (message is not null && message.Status == QueuedMessageStatus.Sent)
                {
                    message.Status = QueuedMessageStatus.Pending;
                    message.SentAt = null;
                }
            }

            if (agent is not null)
            {
                var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                await supervisor.RecordIncidentAsync(
                    agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed, AlertSeverity.Error,
                    $"Message delivery could not be verified: {Describe(verdict)}; the terminal looks wedged."
                    + (agent.AlwaysOn
                        ? " Restarting the session; the message stays queued and redelivers after the restart."
                        : " The message has been returned to the queue."),
                    ct: ct);
            }

            await db.SaveChangesAsync(ct);

            if (agent is not null)
            {
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
                if (agent.AlwaysOn)
                {
                    var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
                    await sessions.KillAsync(sessionId, ct);
                }
            }

            _logger.LogWarning(
                "Delivery to session {SessionId} failed verification ({Verdict}); agent={AgentName}, alwaysOn={AlwaysOn}",
                sessionId, verdict, agent?.Name ?? "<none>", agent?.AlwaysOn ?? false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to handle delivery verification failure for session {SessionId}", sessionId);
        }
    }

    private static async Task<bool> IsWorkingAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        // Mirror the client's isWorking(): the agent is working while activity outranks the last turn-end.
        var lastEnd = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;
        var lastActivity = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind != TranscriptKinds.TurnEnd
                && t.Kind != TranscriptKinds.TurnTitle)
            .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;
        return lastActivity > lastEnd;
    }

    private static async Task<SessionQueueDto> BuildQueueDtoAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var messages = await db.SessionQueuedMessages
            .AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .OrderBy(m => m.Sequence)
            .Select(m => new QueuedMessageDto(m.Id, m.Sequence, m.Body, m.Status.ToString(), m.CreatedAt))
            .ToListAsync(ct);
        var working = await IsWorkingAsync(db, sessionId, ct);
        return new SessionQueueDto(sessionId, messages, working);
    }

    private async Task EnsureSessionExistsAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.AgentSessions.AnyAsync(s => s.Id == sessionId, ct))
            throw new NotFoundException(nameof(AgentSession), sessionId);
    }

    private async Task PublishQueueChangedAsync(SessionQueueDto dto, CancellationToken ct)
    {
        try
        {
            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(dto.SessionId), "SessionQueueChanged", dto, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish queue change for session {SessionId}", dto.SessionId);
        }
    }

    private async Task PublishFinishedAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions
                .AsNoTracking()
                .Include(s => s.Card)
                .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

            Guid? cardId = session?.CardId;
            Guid? boardId = session?.Card?.BoardId;
            var label = session?.Card?.Identifier;
            Guid? agentId = null;
            if (label is null)
            {
                var agent = await db.Agents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.PersistentSessionId == sessionId.ToString("D"), ct);
                label = agent?.Name ?? "Agent";
                agentId = agent?.Id;
            }

            var payload = new { sessionId, cardId, boardId, agentId, label };
            // Session-scoped (for the open terminal's badge) and global (for the app-wide toast).
            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(sessionId), "SessionFinished", payload, ct);
            await _eventBus.PublishToAllAsync("SessionFinished", payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish finished signal for session {SessionId}", sessionId);
        }
    }

    private SemaphoreSlim GetLock(Guid sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
