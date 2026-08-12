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

    /// <summary>Queue a message ("wait until idle") or deliver it immediately ("send now").</summary>
    public async Task<SessionQueueDto> EnqueueAsync(
        Guid sessionId, string body, MessageSendMode mode, CancellationToken ct,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Ui, string? conversationKey = null)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(body), "Message must not be empty.");

        await EnsureSessionExistsAsync(sessionId, ct);

        if (mode == MessageSendMode.Now)
        {
            if (!_runtime.ListLiveSessions().Contains(sessionId))
                throw new ConflictException($"Agent session '{sessionId}' is not live; cannot send now.");
            if (!await IsAcceptingInputAsync(sessionId, ct))
            {
                throw new ConflictException(
                    $"Agent session '{sessionId}' is still starting; its terminal is not ready for input yet.");
            }

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
                Origin = origin,
                ConversationKey = conversationKey,
            });
            await db.SaveChangesAsync(ct);

            // If the agent is already idle (waiting at the prompt), there is no upcoming turn-end to
            // flush on — deliver right away so the message isn't stranded. But NEVER into a session
            // still Starting: the write lands during TUI boot (the runner's write path now waits
            // out the pty cold start), Claude takes the prompt and starts working, and the launch's
            // ready probe — which waits for an IDLE composer — times out and KILLS a healthy,
            // already-working delegate (live miss 2026-08-09, session 429445c3, died mid-task at
            // 2m41s). A Starting session's messages stay Pending; the launch path flushes the
            // queue itself the moment boot completes (FlushSessionAsync).
            if (_runtime.ListLiveSessions().Contains(sessionId)
                && await IsAcceptingInputAsync(sessionId, ct)
                && !await IsWorkingAsync(db, sessionId, ct))
            {
                await DeliverNextLockedAsync(db, sessionId, ct);
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
                await HandleDeliveryFailureAsync(sessionId, [message.Id], verdict, ct);
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
                    && m.Origin == QueuedMessageOrigin.Delegation)
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct);
            candidates = alwaysOnKeys.Select(Guid.Parse).Union(delegationSessionIds).ToList();
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
                await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
            }
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

    // The DB session status is the ready gate: Starting means the launch's ready probe has not
    // yet seen an idle composer, so nothing may type into the terminal (see EnqueueAsync).
    private async Task<bool> IsAcceptingInputAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
    }

    private enum FlushResult { Nothing, Delivered, Failed }

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

        var body = run.Count == 1
            ? head.Body
            : ChannelPromptFormat.FormatBatch(
                run.Take(run.Count - 1).Select(m => m.Body).ToList(), run[^1].Body);

        var now = UtcNow();
        foreach (var m in run)
        {
            m.Status = QueuedMessageStatus.Sent;
            m.SentAt = now;
        }
        await db.SaveChangesAsync(ct);

        DeliveryVerdict verdict;
        try
        {
            verdict = await DeliverAsync(sessionId, body, ct);
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

        if (verdict == DeliveryVerdict.Delivered)
            return FlushResult.Delivered;

        await HandleDeliveryFailureAsync(sessionId, run.Select(m => m.Id).ToList(), verdict, ct);
        return FlushResult.Failed;
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
        // Line endings are normalized to LF before anything touches the PTY. Measured against real
        // Claude (probe runs 2026-07-31): a \n in written input is ALWAYS a literal newline in the
        // composer, while a \r MID-body acts as Enter and SUBMITS the fragment before it — and
        // current conhost builds strip the bracketed-paste markers from written input, so the wrap
        // alone cannot protect a CR-carrying body. CRLF bodies (Windows/Telegram sources) would
        // fragment exactly like the 2026-07-29 live miss. Shared with every other typing path via
        // PtyInputEncoding (SendLineAsync callers were the 2026-08-08 miss).
        var trimmed = Antiphon.Agents.Pty.PtyInputEncoding.NormalizeBody(body);

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
        // has ever watched arrive is exactly what this exists to name.
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

        // Multi-line bodies MUST travel as one bracketed paste (\e[200~..\e[201~): ConPTY chunks
        // large writes at arbitrary boundaries, and without the markers the TUI's paste heuristic
        // fragments the body at line breaks — live miss 2026-07-29, where a 2.4 KB calendar message
        // reached the agent as only its final fragment. The markers delimit the paste regardless of
        // read chunking; the submitting CR below stays a separate, unbracketed write.
        var payload = Antiphon.Agents.Pty.PtyInputEncoding.WrapIfMultiline(trimmed);
        await _runtime.SendInputAsync(sessionId, payload, ct);

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
        Guid sessionId, IReadOnlyList<Guid>? messageIds, DeliveryVerdict verdict, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            // Revert the whole failed batch (null = Now-mode, nothing persisted to revert).
            if (messageIds is { Count: > 0 })
            {
                var messages = await db.SessionQueuedMessages
                    .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                    .ToListAsync(ct);
                foreach (var message in messages.Where(m => m.Status == QueuedMessageStatus.Sent))
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

    // Internal so the agent list/detail can surface the SAME working signal on agent cards —
    // "Working" on a card must mean mid-turn right now, not merely "session started".
    internal static async Task<bool> IsWorkingAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        // Mirror the client's isWorking(): the agent is working while activity outranks the last
        // turn-end. An interrupt marker ("[Request interrupted...") counts as a turn END, not
        // activity — an aborted turn writes NO TurnEnd, and counting the marker as activity left
        // the session permanently "working" and stranded every WhenIdle delivery (2026-07-29).
        // A SessionRestartBoundary is a turn end for the same reason: the relaunch proved the old
        // turn's process is gone (2026-08-08).
        var end = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.TurnEnd
                    || t.Kind == TranscriptKinds.SessionRestartBoundary
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
                // Local slash-command records (/model, /status …) are housekeeping with NO
                // TurnEnd — counting them as activity stranded WhenIdle deliveries (2026-07-31).
                && !(t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && (t.Text.StartsWith(TranscriptKinds.LocalCommandPrefix)
                        || t.Text.StartsWith(TranscriptKinds.LocalCommandStdoutPrefix)))
                // Compaction is idle-time housekeeping, not work: counting the boundary as
                // activity would flip an idle session to permanently "working" (no TurnEnd ever
                // follows), stranding every WhenIdle message — including the recovery note.
                && t.Kind != TranscriptKinds.CompactBoundary
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

    private SemaphoreSlim GetLock(Guid sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
