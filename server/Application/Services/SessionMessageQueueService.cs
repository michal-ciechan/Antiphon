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
    private readonly Settings.ChannelBridgeSettings _bridgeSettings;
    private readonly DelegationSettings _delegationSettings;
    private readonly PtyDeliveryProfile? _ptyProfile;
    private readonly ILogger<SessionMessageQueueService> _logger;

    public SessionMessageQueueService(
        IServiceScopeFactory scopeFactory,
        AgentSessionRuntime runtime,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<SessionMessageQueueService> logger,
        IOptions<SupervisionSettings>? supervisionSettings = null,
        IOptions<Settings.ChannelBridgeSettings>? bridgeSettings = null,
        IOptions<DelegationSettings>? delegationSettings = null,
        PtyDeliveryProfile? ptyProfile = null)
    {
        _ptyProfile = ptyProfile;
        _scopeFactory = scopeFactory;
        _runtime = runtime;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _verification = (supervisionSettings?.Value ?? new SupervisionSettings()).DeliveryVerification;
        _bridgeSettings = bridgeSettings?.Value ?? new Settings.ChannelBridgeSettings();
        _delegationSettings = delegationSettings?.Value ?? new DelegationSettings();
        _logger = logger;
    }

    /// <summary>
    /// The ceilings for the pty on the other end (CARD-0037). No profile — every test construction,
    /// which passes none — is the inbox conhost and the numbers that shipped with it: the raised
    /// paste-path ceilings are only ever reached by explicitly resolving the backend, never by
    /// defaulting into them.
    /// </summary>
    private PtyDeliveryCeilings Ceilings =>
        _ptyProfile?.Ceilings
        ?? _delegationSettings.CeilingsFor(PtyBackend.InboxConhost, "no pty profile — assuming the default backend");

    private string NowModeFileStem() =>
        $"now-{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMddHHmmss}";

    /// <summary>
    /// CARD-0025: if <paramref name="body"/> is over the single-write envelope, write it to
    /// <c>{cwd}/.antiphon/inbox/{fileStem}.md</c> and return the pointer to type. Under the
    /// ceiling, or if the file cannot be written (empty cwd, IO error), the original is
    /// returned so <see cref="DeliverAsync"/>'s tripwire still fires.
    /// </summary>
    private async Task<string> SpillQueueBodyAsync(
        Guid sessionId,
        string body,
        string fileStem,
        string? channelEnvelope,
        AppDbContext? db,
        CancellationToken ct)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(body) <= Ceilings.SingleWriteMaxBytes)
            return body;

        string? cwd = null;
        var kind = AgentKind.ClaudeCode;
        if (db is not null)
        {
            var session = await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.Cwd, s.AgentKind })
                .FirstOrDefaultAsync(ct);
            cwd = session?.Cwd;
            kind = session?.AgentKind ?? AgentKind.ClaudeCode;
        }
        else
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var scoped = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await scoped.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.Cwd, s.AgentKind })
                .FirstOrDefaultAsync(ct);
            cwd = session?.Cwd;
            kind = session?.AgentKind ?? AgentKind.ClaudeCode;
        }

        string? absolute = null;
        if (!string.IsNullOrWhiteSpace(cwd))
            absolute = TypedBodySpill.InboxAbsolutePath(cwd, fileStem);

        return TypedBodySpill.Fit(new TypedBodySpill.Request(
            Body: body,
            CeilingBytes: Ceilings.SingleWriteMaxBytes,
            AbsoluteSpillPath: absolute,
            RelativeSpillPath: TypedBodySpill.InboxRelativePath(fileStem),
            AgentKind: kind,
            EnvelopePrefix: channelEnvelope,
            Logger: _logger)).ToType;
    }

    /// <summary>
    /// Typing attempts a queued message gets before it parks for a human (CARD-0055). Floored at 1:
    /// a misconfigured 0 would park every message on creation, i.e. deliver nothing at all.
    /// </summary>
    private int MaxAttempts => Math.Max(1, _verification.MaxDeliveryAttempts);

    /// <summary>Queue a message ("wait until idle") or deliver it immediately ("send now").</summary>
    public async Task<SessionQueueDto> EnqueueAsync(
        Guid sessionId, string body, MessageSendMode mode, CancellationToken ct,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Ui, string? conversationKey = null,
        Guid? sourceTaskId = null, string? contentDigest = null, string? noteHeader = null)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(body), "Message must not be empty.");

        var kind = await RequireSessionKindAsync(sessionId, ct);
        if (TryGetForbiddenReason(kind, trimmed, out var forbiddenReason))
            throw new ValidationException(nameof(body), forbiddenReason);

        if (mode == MessageSendMode.Now)
        {
            if (!_runtime.ListLiveSessions().Contains(sessionId))
                throw new ConflictException($"Agent session '{sessionId}' is not live; cannot send now.");
            if (!await IsAcceptingInputAsync(sessionId, ct))
            {
                throw new ConflictException(
                    $"Agent session '{sessionId}' is still starting; its terminal is not ready for input yet.");
            }

            var nowBody = await SpillQueueBodyAsync(
                sessionId, trimmed, NowModeFileStem(),
                origin == QueuedMessageOrigin.Channel
                    ? TypedBodySpill.TryReadChannelEnvelope(trimmed)
                    : null,
                db: null, ct);
            // CARD-0137 S7: take the per-session lock the poll already holds, so a Mode.Now send
            // cannot interleave with a poll (or another Now) in one composer. DeliverAsync itself
            // must not take the lock — SendNowAsync and the turn-end flush already hold it.
            var nowLock = GetLock(sessionId);
            await nowLock.WaitAsync(ct);
            DeliveryOutcome outcome;
            try
            {
                outcome = await DeliverAsync(sessionId, nowBody, ct);
                if (outcome.Verdict == DeliveryVerdict.ForbiddenBody)
                {
                    await HandleForbiddenBodyAsync(sessionId, null, nowBody, outcome.RecordText, ct);
                    throw new ValidationException(
                        nameof(body),
                        outcome.RecordText ?? "This body is forbidden for this agent kind.");
                }
                if (outcome.Verdict == DeliveryVerdict.Truncated)
                {
                    await HandleTruncationAsync(sessionId, null, nowBody, outcome.RecordText, ct);
                    throw new ConflictException(
                        "Message delivery reached the transcript truncated "
                        + $"({Describe(outcome.Verdict)}). See the agent's incidents.");
                }
                if (outcome.Verdict != DeliveryVerdict.Delivered)
                {
                    await HandleDeliveryFailureAsync(sessionId, null, outcome.Verdict, ct);
                    throw new ConflictException(
                        "Message delivery could not be verified — the terminal did not accept it "
                        + $"({Describe(outcome.Verdict)}). See the agent's incidents.");
                }
            }
            finally
            {
                nowLock.Release();
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

            var row = new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = trimmed,
                Status = QueuedMessageStatus.Pending,
                Sequence = nextSequence,
                CreatedAt = now,
                Origin = origin,
                ConversationKey = conversationKey,
                SourceTaskId = sourceTaskId,
                ContentDigest = contentDigest,
                NoteHeader = noteHeader,
            };
            db.SessionQueuedMessages.Add(row);
            await db.SaveChangesAsync(ct);

            // If the agent is already idle (waiting at the prompt), there is no upcoming turn-end to
            // flush on — deliver right away so the message isn't stranded. But NEVER into a session
            // still Starting: the write lands during TUI boot (the runner's write path now waits
            // out the pty cold start), Claude takes the prompt and starts working, and the launch's
            // ready probe — which waits for an IDLE composer — times out and KILLS a healthy,
            // already-working delegate (live miss 2026-08-09, session 429445c3, died mid-task at
            // 2m41s). A Starting session's messages stay Pending; the launch path flushes the
            // queue itself the moment boot completes (FlushSessionAsync).
            var working = await IsWorkingAsync(db, sessionId, ct);
            if (_runtime.ListLiveSessions().Contains(sessionId)
                && await IsAcceptingInputAsync(sessionId, ct)
                && !working)
            {
                await DeliverNextLockedAsync(db, sessionId, ct);
            }
            else if (origin == QueuedMessageOrigin.Supervision && working)
            {
                // Cancel-not-strand (CARD-0082): a Pending /compact flushed at the *next turn end*
                // would compact a session that just became active. Drop it; a later sweep re-derives.
                row.Status = QueuedMessageStatus.Canceled;
                row.CanceledAt = UtcNow();
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Canceled Supervision compact {MessageId} on session {SessionId}: session is working",
                    row.Id, sessionId);
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

    /// <summary>Pending messages for the session, plus whether the agent is currently working.</summary>
    public async Task<SessionQueueDto> GetQueueAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await BuildQueueDtoAsync(db, sessionId, Math.Max(1, _verification.MaxDeliveryAttempts), ct);
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

    /// <summary>
    /// Prepend <paramref name="prefix"/> to a still-Pending, never-typed message and re-fit it
    /// to <paramref name="ceiling"/>. CARD-0074: the check-note sweep uses this so a task that
    /// settled while the note sat in the queue is marked, not silently delivered as live.
    ///
    /// <para>Same lock, same scoped context, same re-check-under-the-lock shape as
    /// <see cref="CancelAsync"/>. A read-modify-write from the sweep's own scope races
    /// <c>FlushAsync</c>: the flush reads <c>head.Body</c> into memory and stamps Sent in a later
    /// SaveChanges, and an amend landing in that gap makes the stored body disagree with what
    /// was typed — exactly the disagreement CARD-0055/CARD-0024 exist to detect, which can then
    /// trigger the always-on kill on a false <c>NoTranscriptRecord</c>.</para>
    ///
    /// <para>Returns false when the row is gone, no longer Pending, already typed
    /// (<c>DeliveryAttempts != 0</c>), or already carries the prefix. The caller decides what
    /// to say; this only applies it safely.</para>
    /// </summary>
    public async Task<bool> AmendPendingBodyAsync(
        Guid sessionId, Guid messageId, string prefix, int ceiling, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        var applied = false;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.AgentSessionId == sessionId, ct);
            if (message is null)
                return false;

            if (message.Status != QueuedMessageStatus.Pending || message.DeliveryAttempts != 0)
                return false;

            var intro = prefix.TrimEnd();
            if (message.Body.StartsWith(intro, StringComparison.Ordinal))
                return false;

            message.Body = PrependWithinCeiling(intro, message.Body, ceiling);
            await db.SaveChangesAsync(ct);
            applied = true;
        }
        finally
        {
            sem.Release();
        }

        if (applied)
        {
            var dto = await GetQueueAsync(sessionId, ct);
            await PublishQueueChangedAsync(dto, ct);
        }

        return applied;
    }

    /// <summary>
    /// Cancels a still-Pending message only if it has never been typed. The superseded-check sweep
    /// uses the same lock and re-check-under-lock shape as <see cref="CancelAsync"/> so it cannot
    /// race a flush that has already captured the body for delivery.
    /// </summary>
    public async Task<bool> CancelPendingIfUntypedAsync(Guid sessionId, Guid messageId, CancellationToken ct)
    {
        var canceled = false;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.AgentSessionId == sessionId, ct);
            if (message is null || message.Status != QueuedMessageStatus.Pending || message.DeliveryAttempts != 0)
                return false;

            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = UtcNow();
            await db.SaveChangesAsync(ct);
            canceled = true;
        }
        finally
        {
            sem.Release();
        }

        if (canceled)
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        return canceled;
    }

    /// <summary>
    /// Keep the prefix whole and trim the original body's tail when the pair exceeds the
    /// ceiling. The banner is worth more to the reader than the last block of a snapshot
    /// they have just been told is historical.
    /// </summary>
    internal static string PrependWithinCeiling(string prefix, string body, int ceiling)
    {
        var intro = prefix.TrimEnd() + "\n\n";
        if (ceiling < 1)
            ceiling = 1;
        if (intro.Length >= ceiling)
            return intro[..ceiling];
        var room = ceiling - intro.Length;
        return body.Length <= room ? intro + body : intro + body[..room];
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

            // Same late-confirm as the automatic paths: a previously attempted message whose body
            // is already in the transcript went in, and re-typing it here would put it in twice.
            // Truncated is also "handled" — the body submitted, so re-typing would double-send.
            if ((await LateConfirmAttemptedMessagesAsync(db, sessionId, [message], ct)).Handled > 0)
            {
                var confirmed = await GetQueueAsync(sessionId, ct);
                await PublishQueueChangedAsync(confirmed, ct);
                return confirmed;
            }

            var baseline = await CaptureTranscriptBaselineAsync(db, sessionId, ct);
            var sendNowBody = await SpillQueueBodyAsync(
                sessionId, message.Body, message.Id.ToString("D"),
                message.Origin == QueuedMessageOrigin.Channel
                    ? TypedBodySpill.TryReadChannelEnvelope(message.Body)
                    : null,
                db, ct);
            if (sendNowBody != message.Body)
                message.Body = sendNowBody;
            message.Status = QueuedMessageStatus.Sent;
            message.SentAt = UtcNow();
            message.DeliveryAttempts++;
            message.LastDeliveryStartedAt = UtcNow();
            message.LastDeliveryBaselineSequence = baseline.Observable ? baseline.MaxSequence : null;
            await db.SaveChangesAsync(ct);
            var outcome = await DeliverAsync(sessionId, sendNowBody, ct, baseline);
            if (outcome.Verdict == DeliveryVerdict.ForbiddenBody)
            {
                await HandleForbiddenBodyAsync(sessionId, [message.Id], sendNowBody, outcome.RecordText, ct);
                throw new ValidationException(
                    "body",
                    outcome.RecordText ?? "This body is forbidden for this agent kind.");
            }
            if (outcome.Verdict == DeliveryVerdict.Truncated)
            {
                await HandleTruncationAsync(sessionId, [message.Id], sendNowBody, outcome.RecordText, ct);
                throw new ConflictException(
                    "Message delivery reached the transcript truncated "
                    + $"({Describe(outcome.Verdict)}). The message has been parked in the queue.");
            }
            if (outcome.Verdict != DeliveryVerdict.Delivered)
            {
                await HandleDeliveryFailureAsync(sessionId, [message.Id], outcome.Verdict, ct);
                throw new ConflictException(
                    "Message delivery could not be verified — the terminal did not accept it "
                    + $"({Describe(outcome.Verdict)}). The message has been returned to the queue.");
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
            // Cancel-not-strand: a Supervision /compact that sat through a turn must not fire now.
            await CancelPendingSupervisionLockedAsync(db, sessionId, "turn-end", ct);
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
            // Parked messages (CARD-0055: at MaxDeliveryAttempts) are excluded here and in the
            // delegation query below. This watchdog is the automatic retry path, and parking means
            // exactly "no automatic retry" — a session whose only pending message is parked must not
            // even be woken up for it.
            var maxAttempts = MaxAttempts;
            var pendingSessionIds = await db.SessionQueuedMessages
                .AsNoTracking()
                .Where(m => m.Status == QueuedMessageStatus.Pending
                    && m.CreatedAt <= cutoff
                    && m.DeliveryAttempts < maxAttempts)
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct);
            if (pendingSessionIds.Count == 0)
                return 0;

            // Always-on agents' sessions: their composer is guaranteed fresh after a
            // verification-failure restart, so re-typing cannot double up. Other sessions keep
            // their pending messages visible for a human to resend — EXCEPT delegation briefs,
            // which no human is watching: a delegate whose boot brief stranded sits at an idle
            // prompt forever (live miss 2026-08-09, CARD-0003). A stranded-then-reverted brief is
            // safe to re-type: a transport failure never reached the terminal, and a verification
            // failure withheld the submitting Enter.
            var keys = pendingSessionIds.Select(id => id.ToString("D")).ToList();
            var alwaysOnKeys = await db.Agents
                .AsNoTracking()
                .Where(a => a.AlwaysOn && a.PersistentSessionId != null && keys.Contains(a.PersistentSessionId))
                .Select(a => a.PersistentSessionId!)
                .ToListAsync(ct);
            var delegationSessionIds = await db.SessionQueuedMessages
                .AsNoTracking()
                .Where(m => m.Status == QueuedMessageStatus.Pending
                    && m.CreatedAt <= cutoff
                    && m.DeliveryAttempts < maxAttempts
                    && m.Origin == QueuedMessageOrigin.Delegation)
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct);
            // Supervision is the same shape as Delegation for the watchdog: no human is watching
            // an auto-compact, and unclaimed sessions are not AlwaysOn, so without this arm a
            // failed-under-cap compact on an unclaimed session would sit Pending forever.
            var supervisionSessionIds = await db.SessionQueuedMessages
                .AsNoTracking()
                .Where(m => m.Status == QueuedMessageStatus.Pending
                    && m.CreatedAt <= cutoff
                    && m.DeliveryAttempts < maxAttempts
                    && m.Origin == QueuedMessageOrigin.Supervision)
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct);
            candidates = alwaysOnKeys.Select(Guid.Parse)
                .Union(delegationSessionIds)
                .Union(supervisionSessionIds)
                .ToList();
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
                // Same Starting-session guard as the enqueue path: redelivering into a booting
                // TUI would re-create the ready-probe kill this watchdog exists to recover from.
                if (await IsAcceptingInputAsync(sessionId, ct) && !await IsWorkingAsync(db, sessionId, ct))
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
            }

            // A late-confirm is not a delivery — nothing was typed — but the queue still changed.
            if (result is FlushResult.Delivered or FlushResult.LateConfirmed)
                await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        }

        return flushed;
    }

    /// <summary>
    /// Deliver pending messages to a session that just finished booting. The launch paths call
    /// this right after their boot typing completes, because the enqueue path deliberately
    /// refuses to type into a Starting session — without this nudge a fresh delegate's brief
    /// would wait for the stranded-queue watchdog's next sweep.
    /// </summary>
    public async Task FlushSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        FlushResult result;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Boot is not the moment to compact — cancel-not-strand, a later sweep re-derives.
            await CancelPendingSupervisionLockedAsync(db, sessionId, "boot-flush", ct);
            result = !await IsWorkingAsync(db, sessionId, ct)
                ? await DeliverNextLockedAsync(db, sessionId, ct)
                : FlushResult.Nothing;
        }
        finally
        {
            sem.Release();
        }

        if (result != FlushResult.Nothing)
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
    }

    /// <summary>
    /// NARROW flush for a manual compaction boundary (CARD-0041): deliver the next queued message
    /// if the session is idle, and nothing else. A manual boundary IS a turn end for the working
    /// rule — without a flush here, messages queued before the compaction sit until the stranded
    /// watchdog's next sweep, which only serves always-on sessions (the CARD-0029 delegation brief
    /// is the live case), and a session that never takes another turn never flushes at all.
    ///
    /// Deliberately NOT <see cref="OnTurnEndAsync"/>: an empty queue must NOT publish
    /// <c>SessionFinished</c> (every idle /compact would fire a spurious "Agent finished" toast —
    /// the SessionFinishedDuplicateTests domain), and the channel/review/task dispatchers must NOT
    /// run (task settlement would be attempted against the STALE pre-compaction report, the exact
    /// mis-settle CARD-0029 warns about). Compaction is not a report.
    /// </summary>
    public async Task FlushIfIdleAsync(Guid sessionId, CancellationToken ct)
    {
        var result = FlushResult.Nothing;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Same Starting-session guard as the enqueue path: a boundary arriving while the TUI
            // still boots must not put text in front of the launch's ready probe.
            if (await IsAcceptingInputAsync(sessionId, ct) && !await IsWorkingAsync(db, sessionId, ct))
            {
                // A pending auto-compact after a *manual* compact is redundant; drop it.
                await CancelPendingSupervisionLockedAsync(db, sessionId, "idle-flush", ct);
                result = await DeliverNextLockedAsync(db, sessionId, ct);
            }
        }
        finally
        {
            sem.Release();
        }

        if (result != FlushResult.Nothing)
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
    }

    private async Task CancelPendingSupervisionLockedAsync(
        AppDbContext db, Guid sessionId, string reason, CancellationToken ct)
    {
        var pending = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending
                && m.Origin == QueuedMessageOrigin.Supervision)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return;

        var now = UtcNow();
        foreach (var message in pending)
        {
            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = now;
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Canceled {Count} Supervision compact(s) on session {SessionId} ({Reason})",
            pending.Count, sessionId, reason);
    }

    // The DB session status is the ready gate: Starting means the launch's ready probe has not
    // yet seen an idle composer, so nothing may type into the terminal (see EnqueueAsync).
    private async Task<bool> IsAcceptingInputAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
    }

    private enum FlushResult { Nothing, Delivered, Failed, LateConfirmed }

    // Claims and delivers the oldest pending message (caller holds the per-session lock). With
    // batching enabled, a CONTIGUOUS head run of Channel-origin messages from the SAME conversation
    // coalesces into one delivery under the batch markers (OpenClaw's 'collect' model): a run of 1
    // is literally today's behaviour; UI/System messages and conversation changes break the run, so
    // cross-origin FIFO order is preserved and operator messages keep 1:1 turns.
    private async Task<FlushResult> DeliverNextLockedAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var pending = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return FlushResult.Nothing;

        // THE anti-duplicate keystone (CARD-0055 D3): nothing may re-type a message that has been
        // typed before without first asking the transcript whether it actually went in. A delivery
        // fails verification for two very different reasons — the body never reached Claude, or it
        // did and the matcher was blind (ingestion stall, a fork, a text transform) — and only the
        // transcript can tell them apart. Automatic retry is safe BECAUSE the retry looks first.
        var late = await LateConfirmAttemptedMessagesAsync(db, sessionId, pending, ct);
        if (late.Handled > 0)
            pending = pending.Where(m => m.Status == QueuedMessageStatus.Pending).ToList();

        // Parked messages (at the attempts cap) stay Pending and visible, but no automatic path
        // types them again — that is what "parks for a human" means. They are still late-confirmed
        // above, so a park resolves itself if the body turns out to have landed complete. A
        // truncated park stays parked: identity-without-completeness is not Sent.
        var deliverable = pending.Where(m => m.DeliveryAttempts < MaxAttempts).ToList();
        if (deliverable.Count == 0)
            return late.Handled > 0 ? FlushResult.LateConfirmed : FlushResult.Nothing;

        pending = deliverable;

        // CARD-0132 S1.3: the per-session lock already protects this read and cancellation from a
        // concurrent flush. A check whose subject settled never consumes the caller's next turn;
        // a missing completion note retains the CARD-0074 bannered fallback instead.
        var changedSuperseded = false;
        foreach (var message in pending.Where(m => m.Origin == QueuedMessageOrigin.Check).ToList())
        {
            if (!AgentTaskCheckService.TryParseCheckConversationKey(message.ConversationKey, out var taskId))
                continue;
            var supersession = await AgentTaskCheckService.EvaluateAsync(db, taskId, ct);
            if (supersession is not { Settled: true } settled)
                continue;

            var task = await db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId)
                .Select(t => new { t.ParentSessionId, t.RootTaskId }).FirstOrDefaultAsync(ct);
            if (task?.ParentSessionId is Guid parentSession
                && await AgentTaskCheckService.HasCompletionNoteAsync(db, parentSession, task.RootTaskId, ct))
            {
                message.Status = QueuedMessageStatus.Canceled;
                message.CanceledAt = UtcNow();
                changedSuperseded = true;
                continue;
            }

            var capturedAt = AgentTaskCheckService.TryReadCapturedAt(message.Body, out var parsed)
                ? parsed : message.CreatedAt;
            var banner = AgentTaskCheckService.SupersededBanner(
                settled.Status, settled.SettledAt, capturedAt);
            if (!message.Body.StartsWith(AgentTaskCheckService.SupersededMarker, StringComparison.Ordinal))
            {
                message.Body = PrependWithinCeiling(banner, message.Body, Ceilings.ReplyInlineMaxChars);
                changedSuperseded = true;
            }
        }
        if (changedSuperseded)
            await db.SaveChangesAsync(ct);
        pending = pending.Where(m => m.Status == QueuedMessageStatus.Pending).ToList();
        if (pending.Count == 0)
            return changedSuperseded ? FlushResult.LateConfirmed : FlushResult.Nothing;

        // CARD-0132 S3b: a status poll only replaces a completion report when this particular
        // queued row recorded the exact report the parent session read. Unlike the Check-origin
        // supersession loop above, this never cancels a row: the short pointer is still delivered.
        var changedShrunk = false;
        if (_delegationSettings.ShrinkPolledCompletionNotes)
        {
            foreach (var message in pending.Where(m =>
                         m.Origin == QueuedMessageOrigin.Delegation
                         && m.DeliveryAttempts == 0
                         && m.SourceTaskId is not null
                         && m.ContentDigest is not null).ToList())
            {
                var contentDigest = message.ContentDigest;
                var noteHeader = message.NoteHeader;
                if (contentDigest is null || noteHeader is null)
                    continue;
                var task = await db.AgentTasks.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == message.SourceTaskId, ct);
                if (task is null
                    || !AgentTaskService.IsSettled(task.Status)
                    || task.LastPolledResultHash is null
                    || task.LastPolledResultAt is null
                    || !string.Equals(contentDigest, task.LastPolledResultHash, StringComparison.Ordinal))
                    continue;

                var reportChars = message.Body.Length;
                message.Body = DelegationReportFormatter.BuildPolledNoteBody(
                    noteHeader, task, reportChars, task.LastPolledResultAt.Value);
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = task.Id,
                    Type = AgentTaskEventType.NoteShrunk,
                    Detail = $"Polled {task.LastPolledResultAt.Value:O}; digest {contentDigest[..Math.Min(8, contentDigest.Length)]}; withheld {reportChars:N0} chars.",
                    At = UtcNow(),
                });
                _logger.LogInformation(
                    "Polled completion note on task {ShortId} {Outcome} for session {SessionId}",
                    DelegationReportFormatter.Short(task.Id), "shrunk", sessionId);
                changedShrunk = true;
            }
        }
        if (changedShrunk)
            await db.SaveChangesAsync(ct);

        var head = pending[0];
        var run = new List<SessionQueuedMessage> { head };

        // Delegation batches for the same reason Channel does — five delegates finishing together
        // should reach the orchestrator as ONE note, not five turns — but with a size cap. Task
        // reports run to thousands of characters where chat messages run to tens, so an uncapped
        // run could push 100k into a TUI in a single paste. The overflow simply rides the next
        // turn-end, which the queue already does naturally.
        var batches = _bridgeSettings.BatchingEnabled
            && head.ConversationKey is not null
            && head.Origin is QueuedMessageOrigin.Channel or QueuedMessageOrigin.Delegation;

        if (batches)
        {
            var budget = head.Origin == QueuedMessageOrigin.Delegation
                ? Math.Max(head.Body.Length, Ceilings.ReplyInlineMaxChars)
                : int.MaxValue;
            var used = head.Body.Length;

            foreach (var m in pending.Skip(1))
            {
                if (m.Origin != head.Origin || m.ConversationKey != head.ConversationKey)
                    break;
                if (used + m.Body.Length > budget)
                    break;
                used += m.Body.Length;
                run.Add(m);
            }
        }

        var composed = run.Count == 1
            ? head.Body
            : ChannelPromptFormat.FormatBatch(
                run.Take(run.Count - 1).Select(m => m.Body).ToList(), run[^1].Body);

        // CARD-0025: spill an oversize composed body BEFORE the Sent stamp, and persist the
        // POINTER as each row's Body in that same SaveChanges. Confirmation, late-confirm,
        // PromptsMatch and CARD-0024 completeness all compare the stored Body against the
        // transcript; leaving the original while typing a pointer would fire Truncated on
        // every successful spill and leave Channel replies unroutable. Under-ceiling batches
        // keep their original per-row bodies so each still matches by containment.
        var channelEnvelope = head.Origin == QueuedMessageOrigin.Channel
            ? TypedBodySpill.TryReadChannelEnvelope(run[^1].Body)
            : null;
        var body = await SpillQueueBodyAsync(
            sessionId, composed, head.Id.ToString("D"), channelEnvelope, db, ct);
        var spilled = !ReferenceEquals(body, composed) && body != composed;

        // Stamped BEFORE a byte is typed, and deliberately NOT undone by the revert on failure: the
        // attempt happened, and the baseline is what the next attempt's late-confirm reads. A crash
        // between here and the write costs one attempt, which is the safe direction to be wrong in.
        var now = UtcNow();
        var baseline = await CaptureTranscriptBaselineAsync(db, sessionId, ct);
        foreach (var m in run)
        {
            m.Status = QueuedMessageStatus.Sent;
            m.SentAt = now;
            m.DeliveryAttempts++;
            m.LastDeliveryStartedAt = now;
            m.LastDeliveryBaselineSequence = baseline.Observable ? baseline.MaxSequence : null;
            if (spilled)
                m.Body = body;
        }
        await db.SaveChangesAsync(ct);

        DeliveryOutcome outcome;
        try
        {
            outcome = await DeliverAsync(sessionId, body, ct, baseline);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — but the run is already marked Sent, and a Sent-but-never-delivered
            // message is invisible to every retry path (live miss 2026-08-09: four delegated
            // tasks' briefs stranded this way). Revert before propagating.
            await RevertRunAsync(db, run);
            throw;
        }
        catch (Exception ex)
        {
            // Transport failure: the runner 500'd, was unreachable, or timed out (an HttpClient
            // timeout is an OperationCanceledException with OUR token not cancelled — treat it as
            // transport, never as shutdown). The terminal never saw the write, so reverting to
            // Pending cannot double-type; redelivery comes via the stranded-queue watchdog or the
            // next turn-end flush, and the incident makes the failure visible on the agent.
            _logger.LogWarning(
                ex,
                "Delivery to session {SessionId} threw before the terminal accepted it; reverting {Count} message(s) to Pending",
                sessionId, run.Count);
            await RevertRunAsync(db, run);
            await RecordTransportFailureAsync(sessionId, ex, ct);
            return FlushResult.Failed;
        }

        if (outcome.Verdict == DeliveryVerdict.Delivered)
            return FlushResult.Delivered;

        var ids = run.Select(m => m.Id).ToList();
        if (outcome.Verdict == DeliveryVerdict.ForbiddenBody)
        {
            await HandleForbiddenBodyAsync(sessionId, ids, body, outcome.RecordText, ct);
            return FlushResult.Failed;
        }
        if (outcome.Verdict == DeliveryVerdict.Truncated)
        {
            await HandleTruncationAsync(sessionId, ids, body, outcome.RecordText, ct);
            return FlushResult.Failed;
        }

        await HandleDeliveryFailureAsync(sessionId, ids, outcome.Verdict, ct);
        return FlushResult.Failed;
    }

    /// <summary>
    /// CARD-0055 D3's late-confirm: for every Pending message that has already been typed at least
    /// once, re-run the prompt matcher over the <c>UserPrompt</c> rows that arrived after that
    /// attempt's stored baseline. A COMPLETE match means the body DID reach Claude — the first
    /// attempt's verification was simply blind (ingestion stall, a mid-session fork, a slow tailer)
    /// — so the message is marked Sent with ZERO writes to the terminal and never typed again.
    /// Identity without completeness is CARD-0024 truncation: park, do not mark Sent (a splice
    /// that lands after the confirm window must not be promoted on the next flush).
    ///
    /// This is what makes the automatic retry above safe. The re-pressed Enter inside a delivery
    /// cannot double-submit (empty composer, per-session lock); the place a duplicate to a human
    /// could originate is a REDELIVERY that re-types, and this runs before every one of them.
    ///
    /// Two deliberate restrictions:
    /// <list type="bullet">
    /// <item>Text match only. The weak arm — "any UserPrompt past the baseline counts" — is fine
    /// inside a 30-second confirm window but not here, where the window is however long the message
    /// has sat Pending: some prompt will always have arrived. A short body that cannot be identified
    /// by text is therefore redelivered rather than assumed delivered. Duplicating an auto-continue
    /// is cheap; silently dropping a human's "yes please" is not.</item>
    /// <item>A message with no stored baseline (the session had no observable transcript at attempt
    /// time) is never late-confirmed — there is no floor, so a match would prove nothing.</item>
    /// </list>
    /// </summary>
    private async Task<LateConfirmCounts> LateConfirmAttemptedMessagesAsync(
        AppDbContext db, Guid sessionId, IReadOnlyList<SessionQueuedMessage> pending, CancellationToken ct)
    {
        if (!_verification.TranscriptConfirmEnabled)
            return LateConfirmCounts.Empty;

        var confirmed = 0;
        var truncated = 0;
        foreach (var m in pending)
        {
            if (m.DeliveryAttempts == 0 || m.LastDeliveryBaselineSequence is not { } floor)
                continue;
            if (!PromptSubmissionMatch.RequiresTextMatch(m.Body))
                continue;

            var match = await TryFindConfirmingRecordAsync(db, sessionId, m.Body, floor, ct);
            if (!match.Identity)
                continue;

            if (!match.Complete)
            {
                // Park the tracked entity in THIS context so the rest of the flush (the
                // deliverable filter) sees the cap. HandleTruncationAsync uses its own scope
                // for the incident and the durable park; the two writes are the same values.
                if (m.Status == QueuedMessageStatus.Sent)
                {
                    m.Status = QueuedMessageStatus.Pending;
                    m.SentAt = null;
                }
                m.DeliveryAttempts = Math.Max(m.DeliveryAttempts, MaxAttempts);
                await HandleTruncationAsync(sessionId, [m.Id], m.Body, match.Text, ct);
                truncated++;
                continue;
            }

            m.Status = QueuedMessageStatus.Sent;
            m.SentAt = UtcNow();
            confirmed++;
            _logger.LogInformation(
                "Message {MessageId} on session {SessionId} late-confirmed: its body became a UserPrompt "
                + "record past sequence {Baseline} after attempt {Attempt}, so it is marked Sent and the "
                + "redelivery is skipped",
                m.Id, sessionId, floor, m.DeliveryAttempts);
        }

        if (confirmed > 0)
            await db.SaveChangesAsync(ct);

        return new LateConfirmCounts(confirmed, truncated);
    }

    private static async Task RevertRunAsync(AppDbContext db, IReadOnlyList<SessionQueuedMessage> run)
    {
        foreach (var m in run)
        {
            m.Status = QueuedMessageStatus.Pending;
            m.SentAt = null;
        }
        // Not the caller's token: when the revert is racing shutdown, completing it is the point.
        await db.SaveChangesAsync(CancellationToken.None);
    }

    // The transport-failure sibling of HandleDeliveryFailureAsync: records the incident (visible on
    // the agent card + alert feed) but never kills the session — the terminal is not wedged, the
    // path to it failed, and a kill issued over that same path would fail too.
    private async Task RecordTransportFailureAsync(Guid sessionId, Exception failure, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);
            if (agent is null)
                return;

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                agent.Id, sessionId, AgentIncidentKind.DeliveryTransportFailed, AlertSeverity.Error,
                $"Message delivery failed in transport before the terminal accepted it: {failure.Message} "
                + "The message has been returned to the queue for redelivery.",
                ct: ct);
            await db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to record delivery transport failure for session {SessionId}", sessionId);
        }
    }

    // Sibling of RecordTransportFailureAsync: surfaces an oversize delivery on the agent card and
    // the alert feed. Best-effort by design — an unowned session (no agent row) still gets the log
    // line above, and failing to record must never abort the delivery it is only annotating.
    private async Task RecordOversizeAsync(
        Guid sessionId, int length, PtyDeliveryCeilings ceilings, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);
            if (agent is null)
                return;

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                agent.Id, sessionId, AgentIncidentKind.OversizedTerminalDelivery, AlertSeverity.Warning,
                $"A {length:N0}-byte message was written into this terminal, past the "
                + $"{ceilings.SingleWriteMaxBytes:N0} bytes measured to arrive whole on {ceilings.Backend}. "
                + (ceilings.IsPastePath
                    ? "Beyond that envelope nothing has been measured, and a paste the composer "
                      + "abandons leaves NOTHING rather than a fragment."
                    : "The receiving TUI keeps ONE read chunk per event-loop turn and discards the "
                      + "rest, so part of this — the head, the middle, or all but one chunk — may be "
                      + "missing.")
                + " There is no visible sign either way. Treat what the agent read as unverified.",
                ct: ct);
            await db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to record oversized delivery for session {SessionId}", sessionId);
        }
    }

    private enum DeliveryVerdict
    {
        Delivered,
        NoComposerEvidence,
        NoSubmitOutput,
        NoTranscriptRecord,
        Truncated,
        ForbiddenBody,
        LocalCommandNotAccepted,
    }

    /// <summary>
    /// Result of <see cref="TypeLocalCommandAsync"/> — the shared core of the poll transport and
    /// the local-command arm of <see cref="DeliverAsync"/>. Callers own Esc-before/after,
    /// Navigation, buffer capture, incidents, and kill.
    /// </summary>
    private enum LocalCommandTypeResult
    {
        /// <summary>Composer never showed the command; Enter was withheld.</summary>
        NotAccepted,
        /// <summary>Enter went out but the output sequence did not advance.</summary>
        NotAdvanced,
        Sent,
    }

    private readonly record struct DeliveryOutcome(DeliveryVerdict Verdict, string? RecordText = null)
    {
        public static DeliveryOutcome Delivered { get; } = new(DeliveryVerdict.Delivered);
        public static DeliveryOutcome Of(DeliveryVerdict verdict, string? recordText = null) =>
            new(verdict, recordText);
    }

    private readonly record struct TranscriptConfirm(bool Identity, bool Complete, string? Text)
    {
        public static TranscriptConfirm None { get; } = new(false, false, null);

        public static TranscriptConfirm Classify(string? body, string? recordText)
        {
            if (!PromptSubmissionMatch.IsConfirmedBy(body, recordText))
                return None;
            return new(true, PromptSubmissionMatch.IsCompleteIn(body, recordText), recordText);
        }
    }

    private readonly record struct LateConfirmCounts(int Confirmed, int Truncated)
    {
        public static LateConfirmCounts Empty { get; } = new(0, 0);
        public int Handled => Confirmed + Truncated;
    }

    private static string Describe(DeliveryVerdict verdict) => verdict switch
    {
        DeliveryVerdict.NoComposerEvidence => "the typed message never appeared in the composer",
        DeliveryVerdict.NoSubmitOutput => "the submitting Enter produced no output",
        DeliveryVerdict.NoTranscriptRecord => "the submitted prompt never became a transcript record",
        DeliveryVerdict.Truncated => "the submitted prompt reached the transcript truncated",
        DeliveryVerdict.ForbiddenBody => "the body is forbidden for this agent kind",
        DeliveryVerdict.LocalCommandNotAccepted => "the local TUI command was not accepted by the composer",
        _ => "delivered",
    };

    /// <summary>
    /// What the session's transcript looked like the instant before we typed. <see cref="Observable"/>
    /// is the CARD-0055 observability gate: with no stored entry at all the transcript is either not
    /// bound yet (a fresh session's launch note is queued before its JSONL exists — CARD-0006) or
    /// binding failed, and there is no ground truth to confirm against. Degrade to the legacy
    /// screen-only verdict there; never fail a delivery for want of a signal.
    ///
    /// <see cref="MaxSequence"/> is the confirmation floor. Stored sequences are ARRIVAL-ordered and
    /// rebased past the session max (the 2026-08-08 backfill bullet), so anything ingested after
    /// this moment sits strictly above it — backfill reordering can neither fake nor hide a match.
    /// </summary>
    private readonly record struct TranscriptBaseline(bool Observable, long MaxSequence);

    private async Task<TranscriptBaseline> CaptureTranscriptBaselineAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await CaptureTranscriptBaselineAsync(db, sessionId, ct);
    }

    private static async Task<TranscriptBaseline> CaptureTranscriptBaselineAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var max = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence, ct);
        return new TranscriptBaseline(max is not null, max ?? 0);
    }

    // Inject text into the session terminal and submit it, reusing the runtime's input path (which also
    // kicks off manual-turn tracking). The body and the submitting carriage return are sent as two
    // separate writes with a short pause between — NOT concatenated. Claude Code's TUI treats text and a
    // trailing CR arriving in a single write as a bracketed paste and folds the CR into a literal newline,
    // so the message lands in the composer but never submits. A delayed, separate CR is the same path
    // RunnerTerminalSession.SendLineAsync uses for prompts, and it submits reliably.
    //
    // For Claude sessions the gap between the two writes is also the VERIFICATION window: the rendered
    // screen must show evidence of the typed body (ComposerDeliveryEvidence — the contract pinned by
    // ClaudeComposerRenderCanaryTests) before the Enter is sent. A wedged terminal leaves no
    // fingerprint, and crucially the Enter is withheld so the message is never lost into a dead
    // composer.
    //
    // What happens AFTER the Enter is CARD-0055's subject. "The output sequence advanced" used to be
    // the delivery verdict, and it is satisfied by any redraw — a spinner, a status line, the composer
    // re-rendering the text it is STILL HOLDING. Measured consequences (session cefed08a): one note
    // marked Sent at 15:16:20Z did not reach Claude until 17:00:09Z, when the NEXT delivery's Enter
    // pushed it in; the next note's own Enter submitted that STALE body, a new UserPrompt record duly
    // appeared with the wrong text, and its own body died with the composer — never in the transcript
    // at all. So a record ARRIVING is not confirmation either: the record's TEXT must be ours.
    //
    // <paramref name="stampedBaseline"/> is the floor the caller already captured and persisted on
    // the message rows before typing; passing it through keeps the stored baseline and the one this
    // confirm loop reads identical. Callers with nothing to persist (Now-mode) pass none.
    private async Task<DeliveryOutcome> DeliverAsync(
        Guid sessionId, string body, CancellationToken ct, TranscriptBaseline? stampedBaseline = null)
    {
        // Line endings are normalized to LF before anything touches the PTY. Measured against real
        // Claude (probe runs 2026-07-31): a \n in written input is ALWAYS a literal newline in the
        // composer, while a \r MID-body acts as Enter and SUBMITS the fragment before it — and
        // current conhost builds strip the bracketed-paste markers from written input, so the wrap
        // alone cannot protect a CR-carrying body. CRLF bodies (Windows/Telegram sources) would
        // fragment exactly like the 2026-07-29 live miss. Shared with every other typing path via
        // PtyInputEncoding (SendLineAsync callers were the 2026-08-08 miss).
        var trimmed = Antiphon.Agents.Pty.PtyInputEncoding.NormalizeBody(body);

        // CARD-0137 S3 / L0: refuse Forbidden bodies before a byte is typed. Codex `/usage` is the
        // founding case — typing it opens a picker whose highlighted option redeems the account's
        // one usage-limit reset, and CARD-0055's confirm loop would re-press Enter into that picker.
        // Matching is the first whitespace-delimited token so `/usage --json` is refused too.
        // Belt-and-braces for rows already in the queue when the EnqueueAsync pre-check lands, and
        // for any future caller that reaches DeliverAsync without going through EnqueueAsync.
        var kind = await TryGetSessionKindAsync(sessionId, ct);
        if (kind is { } refusedKind && TryGetForbiddenReason(refusedKind, trimmed, out var forbiddenReason))
        {
            _logger.LogError(
                "Refusing forbidden body '{Token}' for {Kind} session {SessionId}: {Reason}",
                FirstCommandToken(trimmed), refusedKind, sessionId, forbiddenReason);
            return DeliveryOutcome.Of(DeliveryVerdict.ForbiddenBody, forbiddenReason);
        }

        // Size gate. Above the measured-safe ceiling the pty drops whole 1024-byte chunks of the
        // body and reports success — and because a surviving head or tail is enough for it, the
        // composer-evidence check below certifies the splice as Delivered. That is exactly how a
        // 5 203-char brief and a 5 368-char report reached their readers as coherent-looking
        // fragments on 2026-08-10.
        //
        // The gate is NOT a guarantee that a body under it arrives whole: on 2026-08-11 four
        // bodies of 1 366-2 320 chars arrived as their final 1024-byte chunk alone, passing
        // straight through here without a word. We still deliver —
        // refusing would strand the message with no path forward — but never silently: the caller
        // paths that produce multi-KB bodies (delegation briefs and reports) now spill to a file
        // instead, so anything still arriving here is a case we have not yet given a file path to.
        // Measured in UTF-8 BYTES, because that is the unit loss is measured in (CARD-0027). This
        // used to compare string.Length against PtyInlineSafeChars (4 000 CHARACTERS), which left
        // everything from ~1 KB to 4 KB typed, clipped and silent — the window that swallowed four
        // briefs on 2026-08-11 without raising a thing.
        //
        // WHERE the threshold sits is the pty's business, not ours (CARD-0037): on the inbox conhost
        // it is one 1 024-byte read chunk, on the shipped modern pseudoconsole it is the 86 400-byte
        // single write measured whole. The tripwire is not removed on the modern backend, only
        // moved — anything past the measured envelope is past all evidence, and a delivery nobody
        // has ever watched arrive is exactly what this exists to name. CARD-0025 spills at the
        // call sites, so this arm is the backstop for a write failure (or a future typer). A
        // successful spill must not reach here.
        var ceilings = Ceilings;
        var bodyBytes = System.Text.Encoding.UTF8.GetByteCount(trimmed);
        if (bodyBytes > ceilings.SingleWriteMaxBytes)
        {
            _logger.LogError(
                "Delivering an OVERSIZED body to session {SessionId}: {Bytes:N0} UTF-8 bytes is past "
                + "the {Limit:N0}-byte single write measured whole on {Backend}. Beyond it we have no "
                + "evidence the body survives, and the recipient cannot tell. Give this path a spill file.",
                sessionId, bodyBytes, ceilings.SingleWriteMaxBytes, ceilings.Backend);
            await RecordOversizeAsync(sessionId, bodyBytes, ceilings, ct);
        }

        // CARD-0137 S4 / L1: a declared local command that writes no UserPrompt must not enter
        // CARD-0055's confirm loop (there is no row coming; the timeout would kill). ONE Enter —
        // a re-press lands on a picker's highlighted option (CARD-0141). WritesUserPrompt:true
        // (Claude /compact) and undeclared bodies keep today's path, byte for byte.
        if (kind is { } localKind
            && TryGetLocalCommandFact(localKind, trimmed, out var localFact)
            && !localFact.WritesUserPrompt)
        {
            var typed = await TypeLocalCommandAsync(sessionId, trimmed, ct);
            if (typed == LocalCommandTypeResult.Sent)
                return DeliveryOutcome.Delivered;
            return DeliveryOutcome.Of(DeliveryVerdict.LocalCommandNotAccepted);
        }

        var verify = _verification.Enabled && await IsVerifiedDeliverySessionAsync(sessionId, ct);
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

        // The confirmation floor, captured BEFORE a byte is written: everything ingested from here
        // on sits above it. Also the observability gate — a session with no transcript rows at all
        // has no ground truth to confirm against, so it keeps the legacy screen-only verdict.
        var baseline = verify && _verification.TranscriptConfirmEnabled
            ? stampedBaseline ?? await CaptureTranscriptBaselineAsync(sessionId, ct)
            : default;
        var confirmTranscript = baseline.Observable;
        if (verify && _verification.TranscriptConfirmEnabled && !confirmTranscript)
        {
            _logger.LogDebug(
                "Delivery to session {SessionId} cannot be transcript-confirmed (no transcript entries "
                + "yet — unbound or pre-first-turn); falling back to the screen-only verdict", sessionId);
        }

        // Multi-line bodies MUST travel as one bracketed paste (\e[200~..\e[201~): ConPTY chunks
        // large writes at arbitrary boundaries, and without the markers the TUI's paste heuristic
        // fragments the body at line breaks — live miss 2026-07-29, where a 2.4 KB calendar message
        // reached the agent as only its final fragment. The markers delimit the paste regardless of
        // read chunking; the submitting CR below stays a separate, unbracketed write.
        // CARD-0137 S6: proactive detector. Match measured overlay fragments against the
        // pre-send snapshot. A generic "looks like a modal" match is refused (CARD-0047).
        // At most one Esc per delivery — if this arm fires, S5 must not send another.
        var overlayDismissed = false;
        if (verify && kind is { } overlayKind)
        {
            var overlay = ProviderContractCatalog.For(overlayKind).TerminalOverlay;
            if (overlay.DetectFragments.Count > 0
                && overlay.DetectFragments.Any(f =>
                    ComposerDeliveryEvidence.FragmentIsVisible(before.RenderedScreen, f)))
            {
                overlayDismissed = await TryDismissOverlayAsync(sessionId, overlayKind, ct);
                if (overlayDismissed && _runtime.TryGetLiveSnapshot(sessionId, out var afterDismiss))
                    before = afterDismiss;
            }
        }

        var payload = Antiphon.Agents.Pty.PtyInputEncoding.WrapIfMultiline(trimmed);
        await _runtime.SendInputAsync(sessionId, payload, ct);

        if (verify && !await WaitForComposerEvidenceAsync(sessionId, before.RenderedScreen, trimmed, ct))
        {
            // CARD-0137 S5: reactive overlay recovery. One-shot, idle-gated Esc-and-retype.
            // The Esc is gated on working == false AFTER a fresh CatchUpTranscriptAsync pull —
            // a session parked on a tool-permission modal is mid-turn, so working is true and
            // no Esc is sent. Re-typing is legal here: Enter was withheld, so nothing submitted.
            var recovered = false;
            if (!overlayDismissed
                && kind is { } recoverKind
                && await TryDismissOverlayAsync(sessionId, recoverKind, ct))
            {
                if (_runtime.TryGetLiveSnapshot(sessionId, out var recoveredSnap))
                    before = recoveredSnap;
                await _runtime.SendInputAsync(sessionId, payload, ct);
                recovered = await WaitForComposerEvidenceAsync(
                    sessionId, before.RenderedScreen, trimmed, ct);
                if (recovered)
                {
                    _logger.LogInformation(
                        "Overlay recovery restored composer evidence for session {SessionId} after one Esc",
                        sessionId);
                }
            }

            if (!recovered)
            {
                _logger.LogWarning(
                    "Delivery verification failed for session {SessionId}: body ({Length} chars) produced no "
                    + "composer evidence within {Timeout}s — submit Enter withheld",
                    sessionId, trimmed.Length, _verification.EvidenceTimeoutSeconds);
                return DeliveryOutcome.Of(DeliveryVerdict.NoComposerEvidence);
            }
        }

        long? sequenceBeforeSubmit = null;
        if (verify && _runtime.TryGetLiveMetadata(sessionId, out var meta))
            sequenceBeforeSubmit = meta.LastSequence;

        await Task.Delay(TimeSpan.FromMilliseconds(20), _timeProvider, ct);
        await _runtime.SendInputAsync(sessionId, "\r", ct);

        if (confirmTranscript)
            return await WaitForTranscriptConfirmAsync(sessionId, trimmed, baseline, sequenceBeforeSubmit, ct);

        if (sequenceBeforeSubmit is { } advanceFrom
            && !await WaitForSequenceAdvanceAsync(sessionId, advanceFrom, ct))
        {
            _logger.LogWarning(
                "Delivery verification failed for session {SessionId}: submit Enter produced no output "
                + "within {Timeout}s",
                sessionId, _verification.PostSubmitAdvanceTimeoutSeconds);
            return DeliveryOutcome.Of(DeliveryVerdict.NoSubmitOutput);
        }

        return DeliveryOutcome.Delivered;
    }

    /// <summary>
    /// CARD-0055's confirm loop, and the only thing that may now produce <c>Delivered</c> on a
    /// transcript-observable Claude session: poll for a <c>UserPrompt</c> row past
    /// <paramref name="baseline"/> whose text carries our FULL body, re-pressing Enter every
    /// <c>ReEnterIntervalSeconds</c> until <c>SubmitAttempts</c> is spent.
    ///
    /// Identity without completeness is <c>Truncated</c> and stops the loop immediately: the
    /// UserPrompt is written once, waiting will not grow it, and another Enter would submit
    /// whatever is now in the composer — not repair the splice.
    ///
    /// The retry is ENTER-ONLY and this is not negotiable. If the first Enter really did submit,
    /// the composer is empty and a re-press is a no-op (the documented <c>VerifiedSubmitOptions</c>
    /// contract the boot path has relied on since 2026-08-08); the per-session queue lock guarantees
    /// no OTHER body can be standing in the composer for a re-press to submit. Re-TYPING the body
    /// would be the one move that can double-send to a human, so nothing here does it — and the
    /// redelivery path that could (slice 3) late-confirms before it types.
    ///
    /// Both measured shapes resolve here: a swallowed Enter gets a re-press that submits the body
    /// still held in the composer, and a stale-body submit produces a record whose text FAILS the
    /// match, so the re-press submits ours and the next record matches.
    /// </summary>
    private async Task<DeliveryOutcome> WaitForTranscriptConfirmAsync(
        Guid sessionId, string body, TranscriptBaseline baseline, long? sequenceBeforeSubmit, CancellationToken ct)
    {
        var strong = PromptSubmissionMatch.RequiresTextMatch(body);
        var deadline = UtcNow() + TimeSpan.FromSeconds(_verification.TranscriptConfirmTimeoutSeconds);
        var reEnterAfter = TimeSpan.FromSeconds(Math.Max(0, _verification.ReEnterIntervalSeconds));
        var lastEnter = UtcNow();
        var entersSent = 1; // the caller's submitting Enter
        var sawSequenceAdvance = false;

        while (true)
        {
            var match = await TryFindConfirmingRecordAsync(sessionId, body, baseline.MaxSequence, ct);
            if (match.Identity)
            {
                if (!match.Complete)
                {
                    _logger.LogWarning(
                        "Delivery to session {SessionId} reached a UserPrompt record past sequence "
                        + "{Baseline} after {Enters} Enter(s) but the body is truncated "
                        + "(sent {Sent} normalized chars, recorded {Recorded})",
                        sessionId, baseline.MaxSequence, entersSent,
                        PromptSubmissionMatch.Normalize(body).Length,
                        PromptSubmissionMatch.Normalize(match.Text ?? "").Length);
                    return DeliveryOutcome.Of(DeliveryVerdict.Truncated, match.Text);
                }

                _logger.LogDebug(
                    "Delivery to session {SessionId} confirmed by a UserPrompt record past sequence "
                    + "{Baseline} after {Enters} Enter(s) ({Strength} match)",
                    sessionId, baseline.MaxSequence, entersSent, strong ? "text" : "weak");
                return DeliveryOutcome.Delivered;
            }

            // Kept only as a wedge signal for the log: a terminal that redrew but produced no record
            // is a different failure from one that did nothing at all. It can no longer say Delivered.
            if (!sawSequenceAdvance
                && sequenceBeforeSubmit is { } from
                && _runtime.TryGetLiveMetadata(sessionId, out var meta)
                && meta.LastSequence > from)
            {
                sawSequenceAdvance = true;
            }

            if (UtcNow() >= deadline)
            {
                _logger.LogWarning(
                    "Delivery verification failed for session {SessionId}: the body ({Length} chars) never "
                    + "became a UserPrompt record past sequence {Baseline} within {Timeout}s after {Enters} "
                    + "Enter(s); screen output {Advanced} in that window",
                    sessionId, body.Length, baseline.MaxSequence,
                    _verification.TranscriptConfirmTimeoutSeconds, entersSent,
                    sawSequenceAdvance ? "DID advance (the terminal redrew but nothing was submitted)" : "never advanced");
                return DeliveryOutcome.Of(DeliveryVerdict.NoTranscriptRecord);
            }

            if (entersSent < _verification.SubmitAttempts && UtcNow() - lastEnter >= reEnterAfter)
            {
                _logger.LogInformation(
                    "No transcript record yet for the delivery to session {SessionId}; pressing Enter again "
                    + "(attempt {Attempt} of {Max}). This never re-types the body — if the first Enter did "
                    + "submit, the composer is empty and this is a no-op",
                    sessionId, entersSent + 1, _verification.SubmitAttempts);
                await _runtime.SendInputAsync(sessionId, "\r", ct);
                entersSent++;
                lastEnter = UtcNow();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
    }

    /// <summary>
    /// Keep looking for the confirming record for a short window after the verdict, and return the
    /// ids that turned out to have landed.
    ///
    /// <para>This is the SAME evidence <see cref="LateConfirmAttemptedMessagesAsync"/> requires —
    /// a text match against a real stored baseline — just consulted before the kill instead of on
    /// the next flush. It can only ever turn a failure into a success, never the reverse, and the
    /// text-match restriction means a body too short to identify (an auto-continue) is never
    /// grace-confirmed: those take the ordinary failure path exactly as before.</para>
    /// </summary>
    private async Task<(HashSet<Guid> Confirmed, HashSet<Guid> Truncated)> GraceConfirmAsync(
        Guid sessionId, IReadOnlyList<Guid> messageIds, CancellationToken ct)
    {
        var confirmed = new HashSet<Guid>();
        var truncated = new HashSet<Guid>();
        var grace = TimeSpan.FromSeconds(Math.Max(0, _verification.PostFailureConfirmGraceSeconds));
        if (!_verification.TranscriptConfirmEnabled || grace <= TimeSpan.Zero)
            return (confirmed, truncated);

        var deadline = UtcNow() + grace;
        while (true)
        {
            // PULL the runner's own transcript before every check. This is the whole point: the
            // live event stream is not a reliable clock, and on the measured failure the records
            // sat unstored for 45s and only appeared when the session ended — i.e. the kill was
            // what produced the evidence that the kill was wrong. Waiting longer does not fix that
            // (90s was tried and still lost by 1.2s); asking the runner does.
            await _runtime.CatchUpTranscriptAsync(sessionId, ct);

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var outstanding = (await db.SessionQueuedMessages
                        .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                        .ToListAsync(ct))
                    .Where(m => !confirmed.Contains(m.Id) && !truncated.Contains(m.Id))
                    .ToList();

                foreach (var message in outstanding)
                {
                    var late = await LateConfirmAttemptedMessagesAsync(db, sessionId, [message], ct);
                    if (late.Confirmed > 0)
                        confirmed.Add(message.Id);
                    else if (late.Truncated > 0)
                        truncated.Add(message.Id);
                }
            }

            if (confirmed.Count + truncated.Count == messageIds.Count)
                return (confirmed, truncated);

            if (UtcNow() >= deadline)
            {
                // Said out loud: the next thing that happens may be a kill, and if the record turns
                // up seconds later then the window — not the delivery — is what was wrong.
                _logger.LogWarning(
                    "Post-failure grace of {Grace}s expired for session {SessionId} with {Confirmed} of "
                    + "{Total} message(s) confirmed ({Truncated} truncated); proceeding to the failure path",
                    grace.TotalSeconds, sessionId, confirmed.Count, messageIds.Count, truncated.Count);
                return (confirmed, truncated);
            }

            // Deliberately slower than PollIntervalMs: each iteration fetches a whole transcript
            // over HTTP, and the thing being waited on is a runner round trip, not a DB commit.
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(1000, _verification.PollIntervalMs)), _timeProvider, ct);
        }
    }

    // A fresh scope per poll: this runs outside any caller's DbContext and must see rows the
    // transcript ingestion path is committing from its own scope, concurrently.
    private async Task<TranscriptConfirm> TryFindConfirmingRecordAsync(
        Guid sessionId, string body, long baselineSequence, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TryFindConfirmingRecordAsync(db, sessionId, body, baselineSequence, ct);
    }

    private static async Task<TranscriptConfirm> TryFindConfirmingRecordAsync(
        AppDbContext db, Guid sessionId, string body, long baselineSequence, CancellationToken ct)
    {
        var texts = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt)
                && t.Sequence > baselineSequence)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text)
            .ToListAsync(ct);

        foreach (var text in texts)
        {
            var match = TranscriptConfirm.Classify(body, text);
            if (match.Identity)
                return match;
        }

        return TranscriptConfirm.None;
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

    private async Task<bool> WaitForSequenceAdvanceAsync(
        Guid sessionId, long baseline, CancellationToken ct, int? timeoutSeconds = null)
    {
        var seconds = timeoutSeconds ?? _verification.PostSubmitAdvanceTimeoutSeconds;
        var deadline = UtcNow() + TimeSpan.FromSeconds(Math.Max(1, seconds));
        while (true)
        {
            if (_runtime.TryGetLiveMetadata(sessionId, out var meta) && meta.LastSequence > baseline)
                return true;

            if (UtcNow() >= deadline)
                return false;

            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
    }

    private async Task<bool> IsVerifiedDeliverySessionAsync(Guid sessionId, CancellationToken ct)
    {
        // Claude, Grok and Codex all echo the composer's content on the rendered screen (Grok
        // measured 1.0.5, CARD-0080 S1: typed and pasted bodies render, no placeholder collapse at
        // 4.4 KB; Codex measured 0.147.0, CARD-0099 S1: a typed body renders and Enter on an empty
        // composer submits nothing) and all three have a structured transcript for CARD-0055's
        // confirm to poll — Grok's rows come from its ACP updates.jsonl (CARD-0080 S2), Codex's
        // from its rollout JSONL (CARD-0099 S1). OpenCode/Raw sessions deliver blind.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var kind = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct);
        return kind is { } k
            && ProviderContractCatalog.For(k).DeliveryVerification.State
                == AgentTuiCapabilityState.Supported;
    }

    /// <summary>
    /// CARD-0024: identity matched but the stored UserPrompt does not contain the full body.
    /// The submit happened — re-typing would double-send, killing would abort a live turn —
    /// so park immediately, raise <see cref="AgentIncidentKind.TruncatedTerminalDelivery"/>,
    /// and leave the session alone. Deduped on the message id so a later late-confirm of the
    /// same splice does not raise a second row.
    /// </summary>
    private async Task HandleTruncationAsync(
        Guid sessionId,
        IReadOnlyList<Guid>? messageIds,
        string body,
        string? recordText,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            if (messageIds is { Count: > 0 })
            {
                var messages = await db.SessionQueuedMessages
                    .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                    .ToListAsync(ct);
                foreach (var message in messages)
                {
                    if (message.Status == QueuedMessageStatus.Sent)
                    {
                        message.Status = QueuedMessageStatus.Pending;
                        message.SentAt = null;
                    }

                    message.DeliveryAttempts = Math.Max(message.DeliveryAttempts, MaxAttempts);
                }
            }

            var sentLen = PromptSubmissionMatch.Normalize(body).Length;
            var recordedLen = PromptSubmissionMatch.Normalize(recordText ?? string.Empty).Length;

            if (agent is not null)
            {
                var keys = messageIds is { Count: > 0 }
                    ? messageIds.Select(id => id.ToString("D")).ToList()
                    : [$"now:{sessionId:D}"];
                var failureReason = string.Join(",", keys.OrderBy(k => k, StringComparer.Ordinal));
                var already = await db.AgentIncidents
                    .Where(i => i.AgentId == agent.Id
                        && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery
                        && i.FailureReason != null)
                    .Select(i => i.FailureReason!)
                    .ToListAsync(ct);
                var covered = keys.All(key =>
                    already.Any(reason => reason.Contains(key, StringComparison.Ordinal)));

                if (!covered)
                {
                    var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == agent.Id, ct);
                    var severity = channelBound ? AlertSeverity.Critical : AlertSeverity.Warning;
                    var detail =
                        $"A {sentLen:N0}-character message reached this terminal as {recordedLen:N0} "
                        + "characters in the UserPrompt record (normalized). The submit happened — the "
                        + "body is a splice. The message is PARKED; it will not be re-typed (that would "
                        + "send a second copy). The session was not restarted."
                        + (channelBound
                            ? " This agent is channel-bound: someone is waiting on a reply."
                            : string.Empty);

                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.TruncatedTerminalDelivery, severity,
                        detail, failureReason: failureReason, ct: ct);
                }
            }

            await db.SaveChangesAsync(ct);

            if (agent is not null)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);

            _logger.LogWarning(
                "Delivery to session {SessionId} was truncated (sent {Sent} normalized chars, "
                + "recorded {Recorded}); agent={AgentName}, parked={Parked}, killed=false",
                sessionId, sentLen, recordedLen, agent?.Name ?? "<none>",
                messageIds is { Count: > 0 });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to handle truncated delivery for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// CARD-0137 S3 / L0: a body whose first token is in this kind's
    /// <see cref="LocalCommandContract.Forbidden"/> map. Nothing was typed — retrying is
    /// pointless — so park immediately, raise <see cref="AgentIncidentKind.ForbiddenTerminalBody"/>
    /// at Error (never Critical), and never kill.
    /// </summary>
    private async Task HandleForbiddenBodyAsync(
        Guid sessionId,
        IReadOnlyList<Guid>? messageIds,
        string body,
        string? reason,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            if (messageIds is { Count: > 0 })
            {
                var messages = await db.SessionQueuedMessages
                    .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                    .ToListAsync(ct);
                foreach (var message in messages)
                {
                    if (message.Status == QueuedMessageStatus.Sent)
                    {
                        message.Status = QueuedMessageStatus.Pending;
                        message.SentAt = null;
                    }

                    message.DeliveryAttempts = Math.Max(message.DeliveryAttempts, MaxAttempts);
                }
            }

            var token = FirstCommandToken(body);
            var detailReason = string.IsNullOrWhiteSpace(reason)
                ? "this body is forbidden for this agent kind"
                : reason;

            if (agent is not null)
            {
                var keys = messageIds is { Count: > 0 }
                    ? messageIds.Select(id => id.ToString("D")).ToList()
                    : [$"now:{sessionId:D}"];
                var failureReason = string.Join(",", keys.OrderBy(k => k, StringComparer.Ordinal));
                var already = await db.AgentIncidents
                    .Where(i => i.AgentId == agent.Id
                        && i.Kind == AgentIncidentKind.ForbiddenTerminalBody
                        && i.FailureReason != null)
                    .Select(i => i.FailureReason!)
                    .ToListAsync(ct);
                var covered = keys.All(key =>
                    already.Any(existing => existing.Contains(key, StringComparison.Ordinal)));

                if (!covered)
                {
                    var parked = messageIds is { Count: > 0 };
                    var detail =
                        $"Refused to type '{token}' into this terminal: {detailReason}. "
                        + "Nothing was written. The session was not restarted."
                        + (parked
                            ? " The message is PARKED; retrying a body we refuse to type is pointless."
                            : string.Empty);

                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.ForbiddenTerminalBody, AlertSeverity.Error,
                        detail, failureReason: failureReason, ct: ct);
                }
            }

            await db.SaveChangesAsync(ct);

            if (agent is not null)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);

            _logger.LogWarning(
                "Delivery to session {SessionId} refused forbidden body '{Token}'; agent={AgentName}, "
                + "parked={Parked}, killed=false, reason={Reason}",
                sessionId, token, agent?.Name ?? "<none>",
                messageIds is { Count: > 0 }, detailReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to handle forbidden body for session {SessionId}", sessionId);
        }
    }

    // Verification failed: return the message to the queue (never silently lose it), record an
    // incident against the owning agent (which also raises an alert), and for always-on agents kill
    // the wedged session — the supervisor's ladder restarts it (resuming the SAME session row, so
    // the reverted message redelivers via the stranded-queue watchdog), and the kill guarantees a
    // fresh composer so redelivery cannot double-type.
    //
    // CARD-0055 adds two brakes to that kill. A session that is now WORKING is evidence the submit
    // may have succeeded with the matcher blind — killing it would abort a live turn to settle a
    // bookkeeping doubt, so it is left alone and the next turn-end flush late-confirms the message.
    // The guard covers every verdict, not just NoTranscriptRecord, and costs no wedge recovery: a
    // session that reads working already blocks every QUEUED delivery, so the only deliveries that
    // can reach one are human-initiated (Now-mode / send-now), where killing is plainly wrong.
    // And a message that has hit MaxDeliveryAttempts PARKS: still Pending and visible in the queue
    // UI, but no automatic path types it again, and the incident escalates to Critical when the
    // agent is channel-bound, because a parked channel reply is a human waiting on a dead line.
    //
    // CARD-0103 adds a THIRD brake, narrower than both: a NoComposerEvidence verdict on a session
    // that has never produced a transcript row, inside PreFirstTurnNoEvidenceGraceMinutes of the
    // message being enqueued, refunds the attempt, withholds the kill and reports ONE Warning. That
    // is not a wedged session, it is a Claude TUI that is painted but not yet draining stdin — a
    // state measured at 48-200 seconds under load, i.e. wide enough to swallow the whole 3-attempt
    // budget in ~2.5 minutes and park a brief in a session that was healthy the entire time. The
    // refund only ever applies to that triple condition; "started working and then stalled" keeps
    // CARD-0055's behaviour exactly, because that session HAS a baseline.
    private async Task HandleDeliveryFailureAsync(
        Guid sessionId, IReadOnlyList<Guid>? messageIds, DeliveryVerdict verdict, CancellationToken ct)
    {
        try
        {
            // Last look for the evidence before anything destructive happens. A NoTranscriptRecord
            // verdict says our ingestion had not caught up inside the confirm window, which is NOT
            // the same claim as "the submit failed" — and the difference is a session's life. See
            // DeliveryVerificationSettings.PostFailureConfirmGraceSeconds for the measured miss.
            // A truncated classification during grace is handled (park + incident), not confirmed:
            // it must not fall through as NoTranscriptRecord and kill the session.
            HashSet<Guid> lateConfirmed = [];
            HashSet<Guid> lateTruncated = [];
            if (verdict == DeliveryVerdict.NoTranscriptRecord && messageIds is { Count: > 0 })
                (lateConfirmed, lateTruncated) = await GraceConfirmAsync(sessionId, messageIds, ct);
            if (messageIds is { Count: > 0 }
                && lateConfirmed.Count + lateTruncated.Count == messageIds.Count)
            {
                if (lateTruncated.Count == 0)
                {
                    _logger.LogInformation(
                        "Delivery to session {SessionId} verified late: all {Count} message(s) reached the "
                        + "transcript within the post-failure grace window. No incident, and the session is "
                        + "NOT restarted — it took the message correctly, our ingestion was just behind",
                        sessionId, messageIds.Count);
                }
                else
                {
                    _logger.LogWarning(
                        "Delivery to session {SessionId} classified {Truncated} message(s) as truncated "
                        + "during the post-failure grace window ({Confirmed} confirmed). The session is "
                        + "NOT restarted — the submit happened",
                        sessionId, lateTruncated.Count, lateConfirmed.Count);
                }
                await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            // Revert the whole failed batch (null = Now-mode, nothing persisted to revert), minus
            // anything the grace window just proved landed. The attempt metadata deliberately
            // survives the revert — it is the retry brake.
            var parked = 0;
            var canceledSupervision = 0;
            var allSupervision = false;
            var refunded = 0;
            var preFirstTurn = false;
            DateTime? refundOldestCreatedAt = null;
            if (messageIds is { Count: > 0 })
            {
                var messages = await db.SessionQueuedMessages
                    .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                    .ToListAsync(ct);
                messages = messages
                    .Where(m => !lateConfirmed.Contains(m.Id) && !lateTruncated.Contains(m.Id))
                    .ToList();
                var reverting = messages.Where(m => m.Status == QueuedMessageStatus.Sent).ToList();
                foreach (var message in reverting)
                {
                    message.Status = QueuedMessageStatus.Pending;
                    message.SentAt = null;
                }

                // CARD-0103: the attempt is REFUNDED on the same revert, for one shape only. The
                // stamp-before-type crash-safety is untouched — a crash still leaves the attempt
                // charged, because only an OBSERVED verdict can reach here — and the counter stays
                // the honest thing every DeliveryAttempts < MaxAttempts predicate reads.
                //
                // Why it is safe on this verdict and no other: NoComposerEvidence means the body
                // never rendered, so the submitting Enter was withheld and nothing can have been
                // submitted. Why it is right: a session with zero transcript rows at type time has
                // not taken a single turn yet, and a Claude TUI that is painted but not yet draining
                // stdin was measured deaf for 48-200 seconds (2026-08-20) — long enough to spend all
                // three attempts on a session that is perfectly healthy and merely still waking.
                // A session that DID start working and then stalled has a non-null baseline and is
                // deliberately left to CARD-0055's original design.
                if (verdict == DeliveryVerdict.NoComposerEvidence)
                {
                    var grace = TimeSpan.FromMinutes(
                        Math.Max(0, _verification.PreFirstTurnNoEvidenceGraceMinutes));
                    var now = UtcNow();
                    foreach (var message in reverting)
                    {
                        if (message.LastDeliveryBaselineSequence is not null
                            || message.DeliveryAttempts <= 0
                            || now - message.CreatedAt >= grace)
                            continue;

                        message.DeliveryAttempts--;
                        refunded++;
                        refundOldestCreatedAt = refundOldestCreatedAt is { } oldest && oldest < message.CreatedAt
                            ? oldest
                            : message.CreatedAt;
                    }

                    // All-or-nothing for the kill and the incident: a mixed run (one pre-first-turn
                    // row batched with one that is past its grace) is not the shape this covers, and
                    // the destructive default is the safer place to land.
                    preFirstTurn = reverting.Count > 0 && refunded == reverting.Count;
                }

                allSupervision = messages.Count > 0
                    && messages.All(m => m.Origin == QueuedMessageOrigin.Supervision);
                foreach (var message in messages.Where(m =>
                             m.Origin == QueuedMessageOrigin.Supervision
                             && m.DeliveryAttempts >= MaxAttempts))
                {
                    // Cancel-not-park (CARD-0082): parking exists for human-owed content. An
                    // auto-compact that spent its attempts is dropped; a later sweep re-derives.
                    message.Status = QueuedMessageStatus.Canceled;
                    message.CanceledAt = UtcNow();
                    canceledSupervision++;
                }

                parked = messages.Count(m =>
                    m.Origin != QueuedMessageOrigin.Supervision && m.DeliveryAttempts >= MaxAttempts);
            }

            // Asked before the kill decision AND before the incident text is written, so both tell
            // the same story. Never kill over a Supervision compact — the session may be the
            // operator's own live conversation (CARD-0056 re-adoption).
            var working = await IsWorkingAsync(db, sessionId, ct);
            // CARD-0103 withholds the always-on kill for the refunded shape too: the fresh composer
            // a kill buys is worthless against a TUI that has not started reading, and killing and
            // relaunching straight back into the same race is CARD-0047's restart loop by another
            // route. The message stays Pending and the 60s stranded sweep retries it.
            var kill = agent is { AlwaysOn: true } && !working && !allSupervision && !preFirstTurn
                && verdict is not (DeliveryVerdict.ForbiddenBody or DeliveryVerdict.LocalCommandNotAccepted);

            if (allSupervision && canceledSupervision > 0)
            {
                var compactMessage =
                    $"Idle auto-compact delivery could not be verified ({Describe(verdict)}) after "
                    + $"{MaxAttempts} attempt(s) and was canceled rather than parked.";
                if (agent is not null)
                {
                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.AutoCompactFailed, AlertSeverity.Warning,
                        compactMessage, failureReason: "DeliveryFailed", ct: ct);
                }
                else
                {
                    _logger.LogWarning(
                        "AUTO-COMPACT FAILED on unclaimed session {SessionId}: {Message}",
                        sessionId, compactMessage);
                    var alerts = scope.ServiceProvider.GetService<IAlertService>();
                    if (alerts is not null)
                    {
                        await alerts.RaiseAsync(
                            new AlertRaise(
                                AlertSeverity.Warning,
                                Source: "supervisor",
                                Title: $"{AgentIncidentKind.AutoCompactFailed}: idle auto-compact",
                                Detail: compactMessage,
                                DedupKey: ContextCompactionService.AutoCompactFailedDedupKey(sessionId),
                                AgentId: null,
                                SessionId: sessionId),
                            ct);
                    }
                }
            }
            else if (preFirstTurn && agent is not null)
            {
                // One Warning, not one Error per attempt. Today's signature was six sessions x 3
                // attempts of Error spam describing a race none of them caused; the fault is real
                // but it is ONE fault per message, and it self-heals on the next sweep.
                var since = refundOldestCreatedAt ?? UtcNow();
                var alreadyReported = await db.AgentIncidents.AnyAsync(
                    i => i.SessionId == sessionId
                        && i.Kind == AgentIncidentKind.DeliveryVerificationFailed
                        && i.CreatedAt >= since,
                    ct);
                if (!alreadyReported)
                {
                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed,
                        AlertSeverity.Warning,
                        $"Message delivery could not be verified: {Describe(verdict)}. The session has "
                        + "produced no transcript activity at all, so it is still becoming "
                        + "input-responsive rather than wedged (CARD-0103): the attempt was refunded "
                        + "instead of charged, the session was NOT restarted, and the stranded-queue "
                        + $"watchdog retries within ~{Math.Max(1, _verification.StrandedAgeSeconds)}s. "
                        + $"Past {Math.Max(0, _verification.PreFirstTurnNoEvidenceGraceMinutes)} minutes "
                        + "from enqueue, attempts charge normally and the message parks.",
                        ct: ct);
                }
            }
            else if (agent is not null)
            {
                var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == agent.Id, ct);
                var severity = parked > 0 && channelBound ? AlertSeverity.Critical : AlertSeverity.Error;
                var fate = parked > 0
                    ? $" It has now failed {MaxAttempts} delivery attempts and is PARKED in the queue"
                      + " for a human — nothing will retry it automatically."
                      + (channelBound ? " This agent is channel-bound: someone is waiting on a reply." : string.Empty)
                    : working
                        ? " The session is mid-turn, so the submit may have succeeded unseen. The message"
                          + " stays queued and is re-checked against the transcript before any redelivery."
                        : " The message has been returned to the queue.";
                var restart = kill
                    ? " Restarting the session; a fresh composer is what makes redelivery safe."
                    : agent.AlwaysOn && working
                        ? " The session was NOT restarted — killing it would abort a live turn."
                        : string.Empty;
                var detail = fate + restart;

                var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                await supervisor.RecordIncidentAsync(
                    agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed, severity,
                    $"Message delivery could not be verified: {Describe(verdict)}." + detail,
                    ct: ct);
            }

            await db.SaveChangesAsync(ct);

            if (agent is not null)
            {
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
                if (kill)
                {
                    var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
                    await sessions.KillAsync(sessionId, ct);
                }
            }

            _logger.LogWarning(
                "Delivery to session {SessionId} failed verification ({Verdict}); agent={AgentName}, "
                + "alwaysOn={AlwaysOn}, working={Working}, killed={Killed}, parked={Parked}, "
                + "canceledSupervision={CanceledSupervision}, refundedAttempts={Refunded}",
                sessionId, verdict, agent?.Name ?? "<none>", agent?.AlwaysOn ?? false, working, kill,
                parked, canceledSupervision, refunded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to handle delivery verification failure for session {SessionId}", sessionId);
        }
    }

    // Internal so the agent list/detail can surface the SAME working signal on agent cards —
    // "Working" on a card must mean mid-turn right now, not merely "session started".
    internal static async Task<bool> IsWorkingAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        // Mirror the client's isWorking(): the agent is working while activity outranks the last
        // turn-end. An interrupt marker ("[Request interrupted...") counts as a turn END, not
        // activity — an aborted turn writes NO TurnEnd, and counting the marker as activity left
        // the session permanently "working" and stranded every WhenIdle delivery (2026-07-29).
        // A SessionRestartBoundary is a turn end for the same reason: the relaunch proved the old
        // turn's process is gone (2026-08-08). A MANUAL compaction boundary is one too: /compact
        // runs only between turns, and no TurnEnd is ever coming for it (2026-08-11, CARD-0041 —
        // a compacted session read "working" for two days because TWO post-compaction records
        // escaped the exclusions below: the RAW typed "/compact …" prompt, which Claude records in
        // addition to the <command-name> wrapper, and the synthetic continuation prompt. Both are
        // outranked once the boundary itself is the turn's end; the continuation is excluded from
        // activity as well, because it lands AFTER the boundary). An AUTO boundary stays
        // housekeeping — it fires mid-turn, so counting it as an end would read a working session
        // as idle. Predicates inlined for EF translation, like the interrupt prefix.
        var end = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.TurnEnd
                    || t.Kind == TranscriptKinds.SessionRestartBoundary
                    || (t.Kind == TranscriptKinds.CompactBoundary
                        && t.Text != null
                        && t.Text.Contains(TranscriptKinds.ManualCompactMarker))
                    || (t.Kind == TranscriptKinds.UserPrompt
                        && t.Text != null
                        && t.Text.StartsWith(TranscriptKinds.InterruptedPromptPrefix))))
            .GroupBy(t => t.AgentSessionId)
            .Select(g => new { Seq = g.Max(t => t.Sequence), Ts = g.Max(t => t.Timestamp) })
            .FirstOrDefaultAsync(ct);
        var activity = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind != TranscriptKinds.TurnEnd
                && t.Kind != TranscriptKinds.TurnTitle
                && t.Kind != TranscriptKinds.SessionRestartBoundary
                // queued_command carries its composer enqueue timestamp, which can be older than
                // preceding file-order records. It confirms delivery only; treating it as activity
                // could make the timestamp override report a busy session idle.
                && t.Kind != TranscriptKinds.QueuedUserPrompt
                // Local slash-command records (/model, /status …) are housekeeping with NO
                // TurnEnd — counting them as activity stranded WhenIdle deliveries (2026-07-31).
                && !(t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && (t.Text.StartsWith(TranscriptKinds.LocalCommandPrefix)
                        || t.Text.StartsWith(TranscriptKinds.LocalCommandStdoutPrefix)))
                // Compaction is idle-time housekeeping, not work: counting the boundary as
                // activity would flip an idle session to permanently "working" (no TurnEnd ever
                // follows), stranding every WhenIdle message — including the recovery note. The
                // blanket exclusion stays: manual boundaries are ranked as ENDS above, and
                // auto/trigger-less ones are neither activity nor an end.
                && t.Kind != TranscriptKinds.CompactBoundary
                // The synthetic "This session is being continued from a previous conversation…"
                // record compaction writes: nobody typed it and no TurnEnd follows (CARD-0041).
                && !(t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && t.Text.StartsWith(TranscriptKinds.CompactionContinuationPromptPrefix))
                && !(t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && t.Text.StartsWith(TranscriptKinds.InterruptedPromptPrefix)))
            .GroupBy(t => t.AgentSessionId)
            .Select(g => new { Seq = g.Max(t => t.Sequence), Ts = g.Max(t => t.Timestamp) })
            .FirstOrDefaultAsync(ct);

        if ((activity?.Seq ?? 0) <= (end?.Seq ?? 0))
            return false;

        // Sequence says working — but stored sequences are ARRIVAL-ordered: a catch-up sync that
        // backfills entries missed during a stream gap rebases them past the session's max, so
        // stale pre-gap activity can leapfrog an already-persisted TurnEnd. That exact shape left
        // Antiphon-Opus badged "Working" forever after a server restart (2026-08-08): 8 backfilled
        // tool records landed ABOVE the turn's end. Record timestamps come from the transcript
        // itself and survive reordering — when they PROVE all activity predates the last end, the
        // session is idle. Equal timestamps stay with the sequence verdict (same-line record pairs
        // share one timestamp), and missing ones (TurnTitle-only in practice, excluded anyway)
        // never override.
        if (activity?.Ts is DateTime activityTs && end?.Ts is DateTime endTs && activityTs < endTs)
            return false;

        return true;
    }

    /// <param name="maxAttempts">
    /// The parking threshold, passed in rather than read here so the flag is decided by the SAME
    /// setting the attention projection reads (CARD-0035 slice 4). Parking is not a status: a parked
    /// message is Pending like any other, and a queue that could not say so showed CARD-0055's
    /// parked messages as ordinary pending ones — visible, and silently never going anywhere.
    /// </param>
    private static async Task<SessionQueueDto> BuildQueueDtoAsync(
        AppDbContext db, Guid sessionId, int maxAttempts, CancellationToken ct)
    {
        var messages = await db.SessionQueuedMessages
            .AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .OrderBy(m => m.Sequence)
            .Select(m => new QueuedMessageDto(
                m.Id,
                m.Sequence,
                m.Body,
                m.Status.ToString(),
                m.CreatedAt,
                m.DeliveryAttempts,
                m.Origin.ToString(),
                m.DeliveryAttempts >= maxAttempts))
            .ToListAsync(ct);
        var working = await IsWorkingAsync(db, sessionId, ct);
        return new SessionQueueDto(sessionId, messages, working);
    }

    private async Task<AgentKind> RequireSessionKindAsync(Guid sessionId, CancellationToken ct)
    {
        var kind = await TryGetSessionKindAsync(sessionId, ct);
        if (kind is null)
            throw new NotFoundException(nameof(AgentSession), sessionId);
        return kind.Value;
    }

    private async Task<AgentKind?> TryGetSessionKindAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// First whitespace-delimited token of <paramref name="body"/>, used to match
    /// <see cref="LocalCommandContract.Forbidden"/> and <see cref="LocalCommandContract.Commands"/>.
    /// <c>/usage --json</c> matches <c>/usage</c>; <c>/compact Focus the summary…</c> matches
    /// <c>/compact</c>.
    /// </summary>
    private static string FirstCommandToken(string body)
    {
        var trimmed = body.AsSpan().Trim();
        if (trimmed.IsEmpty)
            return "";
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            end++;
        return trimmed[..end].ToString();
    }

    private static bool TryGetForbiddenReason(AgentKind kind, string body, out string reason)
    {
        reason = "";
        var token = FirstCommandToken(body);
        if (token.Length == 0)
            return false;
        return ProviderContractCatalog.For(kind).LocalCommands.Forbidden.TryGetValue(token, out reason!);
    }

    /// <summary>
    /// CARD-0137: send the kind's measured dismiss key at most once, only when a fresh transcript
    /// pull says the session is idle. Returns false (and types nothing) when the contract is not
    /// Supported, recovery is disabled, or the session is working — the permission-dialog guard.
    /// </summary>
    private async Task<bool> TryDismissOverlayAsync(Guid sessionId, AgentKind kind, CancellationToken ct)
    {
        var overlay = ProviderContractCatalog.For(kind).TerminalOverlay;
        if (!_verification.OverlayRecoveryEnabled
            || overlay.State != AgentTuiCapabilityState.Supported
            || string.IsNullOrEmpty(overlay.DismissKey))
        {
            return false;
        }

        await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await IsWorkingAsync(db, sessionId, ct))
            {
                _logger.LogInformation(
                    "Overlay dismiss withheld for session {SessionId}: session is working after transcript pull",
                    sessionId);
                return false;
            }
        }

        await _runtime.SendInputAsync(sessionId, overlay.DismissKey, ct, trackManualTurn: false);
        var settle = TimeSpan.FromMilliseconds(Math.Max(0, _verification.OverlaySettleMs));
        if (settle > TimeSpan.Zero)
            await Task.Delay(settle, _timeProvider, ct);
        return true;
    }

    private static bool TryGetLocalCommandFact(AgentKind kind, string body, out LocalCommandFact fact)
    {
        fact = null!;
        var token = FirstCommandToken(body);
        if (token.Length == 0)
            return false;
        return ProviderContractCatalog.For(kind).LocalCommands.Commands.TryGetValue(token, out fact!);
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
            _logger.LogInformation(
                "Broadcasting SessionFinished for session {SessionId} ({Label})", sessionId, label);
            // Broadcast to all clients only — connections joined to the session group are part of
            // Clients.All, so an additional group-scoped publish would deliver the event twice to
            // anyone with that session's terminal open (duplicate toasts). Handlers that care about
            // a specific session filter by payload.sessionId.
            await _eventBus.PublishToAllAsync("SessionFinished", payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish finished signal for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// CARD-0143: type a local TUI command (e.g. Codex <c>/status</c>) into a live idle session
    /// without going through prompt delivery. Holds the per-session lock throughout so a poll
    /// cannot race a real message. Deliberately does NOT transcript-confirm, re-press Enter,
    /// call <c>HandleDeliveryFailureAsync</c>, or create a <c>SessionQueuedMessage</c> row —
    /// a local command writes no <c>UserPrompt</c>, so the normal path would time out and kill
    /// every always-on agent twice an hour.
    /// </summary>
    public async Task<LocalCommandPollResult> TryPollLocalCommandAsync(
        Guid sessionId, LocalCommandPoll poll, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poll);
        if (string.IsNullOrWhiteSpace(poll.Command))
            throw new ArgumentException("Local-command poll requires a command body.", nameof(poll));

        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!_runtime.ListLiveSessions().Contains(sessionId))
                return new LocalCommandPollResult.Skipped("not live");
            if (!await IsAcceptingInputAsync(sessionId, ct))
                return new LocalCommandPollResult.Skipped("not Running");
            if (await IsWorkingAsync(db, sessionId, ct))
                return new LocalCommandPollResult.Skipped("working");
            if (await db.SessionQueuedMessages.AsNoTracking()
                .AnyAsync(m => m.AgentSessionId == sessionId
                    && m.Status == QueuedMessageStatus.Pending, ct))
            {
                return new LocalCommandPollResult.Skipped("pending messages");
            }

            var forbidden = ProviderContractCatalog.For(poll.Kind).LocalCommands.Forbidden;
            foreach (var (body, reason) in forbidden)
            {
                if (string.Equals(body, poll.Command, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Forbidden local-command poll body '{poll.Command}' for {poll.Kind}: {reason}");
                }
            }

            var settle = TimeSpan.FromMilliseconds(Math.Max(0, poll.OverlaySettleMs));
            if (poll.OpensOverlay)
            {
                await _runtime.SendInputAsync(sessionId, "\u001b", ct, trackManualTurn: false);
                if (settle > TimeSpan.Zero)
                    await Task.Delay(settle, _timeProvider, ct);
            }

            var typed = await TypeLocalCommandAsync(sessionId, poll.Command, ct, poll.PanelTimeoutSeconds);
            if (typed == LocalCommandTypeResult.NotAccepted)
                return new LocalCommandPollResult.NotAccepted();
            if (typed != LocalCommandTypeResult.Sent)
            {
                if (poll.OpensOverlay)
                    await _runtime.SendInputAsync(sessionId, "\u001b", ct, trackManualTurn: false);
                return new LocalCommandPollResult.PanelNotRendered();
            }

            foreach (var key in poll.Navigation)
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                await _runtime.SendInputAsync(sessionId, key, ct, trackManualTurn: false);
                if (settle > TimeSpan.Zero)
                    await Task.Delay(settle, _timeProvider, ct);
            }

            var snapshot = _runtime.GetBufferSnapshot(sessionId);

            if (poll.OpensOverlay)
                await _runtime.SendInputAsync(sessionId, "\u001b", ct, trackManualTurn: false);

            return new LocalCommandPollResult.Sent(snapshot.Buffer);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Type a local TUI command and prove the composer took it. No transcript confirm (a local
    /// command may write no UserPrompt row), ONE Enter (CARD-0141: a re-press lands on a picker's
    /// highlighted option), no incidents, no kill. Callers own Esc-before/after, Navigation,
    /// buffer capture, and everything else.
    /// </summary>
    private async Task<LocalCommandTypeResult> TypeLocalCommandAsync(
        Guid sessionId, string command, CancellationToken ct, int? sequenceTimeoutSeconds = null)
    {
        if (!_runtime.TryGetLiveSnapshot(sessionId, out var before))
            return LocalCommandTypeResult.NotAccepted;

        await _runtime.SendInputAsync(sessionId, command, ct, trackManualTurn: false);

        if (!await WaitForComposerEvidenceAsync(sessionId, before.RenderedScreen, command, ct))
            return LocalCommandTypeResult.NotAccepted;

        long sequenceBefore = 0;
        if (_runtime.TryGetLiveMetadata(sessionId, out var metaBefore))
            sequenceBefore = metaBefore.LastSequence;

        await Task.Delay(TimeSpan.FromMilliseconds(20), _timeProvider, ct);
        await _runtime.SendInputAsync(sessionId, "\r", ct, trackManualTurn: false);

        if (!await WaitForSequenceAdvanceAsync(sessionId, sequenceBefore, ct, sequenceTimeoutSeconds))
            return LocalCommandTypeResult.NotAdvanced;

        return LocalCommandTypeResult.Sent;
    }

    internal SemaphoreSlim GetLock(Guid sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
