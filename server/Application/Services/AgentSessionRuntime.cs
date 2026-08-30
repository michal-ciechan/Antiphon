using System.Collections.Concurrent;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Backend-side session coordinator. It does not own PTY processes in the
/// production path; live process ownership lives in Antiphon.SessionRunner.
/// This service observes runner output, republishes SignalR deltas, tracks
/// manual xterm turns, and forwards input/resize/kill commands to the runner.
/// </summary>
public sealed class AgentSessionRuntime
{
    private static readonly TimeSpan ManualTurnPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentDictionary<Guid, long> _lastSequences = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _firstDeltas = new();
    private readonly ConcurrentDictionary<Guid, PendingTerminalInput> _pendingInputs = new();
    private readonly ConcurrentDictionary<Guid, byte> _manualTurns = new();
    private readonly ConcurrentDictionary<Guid, IAgentProtocolAdapter> _testAdapters = new();
    // CARD-0164 test seam: herdr AgentStatus on in-process adapters (production reads it from the runner).
    private readonly ConcurrentDictionary<Guid, string?> _testAgentStatuses = new();
    // CARD-0186 S3 test seam: runner Pending on in-process adapters.
    private readonly ConcurrentDictionary<Guid, string?> _testPending = new();
    private readonly ConcurrentDictionary<Guid, StringBuilder> _testBuffers = new();
    private readonly ISessionRunnerClient _runnerClient;
    private readonly IEventBus _eventBus;
    private readonly AgentSessionSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentSessionRuntime> _logger;
    private readonly AgentMentionRouter? _mentionRouter;

    public AgentSessionRuntime(
        ISessionRunnerClient runnerClient,
        IEventBus eventBus,
        IOptions<AgentSessionSettings> settings,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AgentSessionRuntime> logger,
        AgentMentionRouter? mentionRouter = null)
    {
        _runnerClient = runnerClient;
        _eventBus = eventBus;
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _mentionRouter = mentionRouter;
    }

    public AgentSessionRuntime(
        IEventBus eventBus,
        IOptions<AgentSessionSettings> settings,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AgentSessionRuntime> logger,
        AgentMentionRouter? mentionRouter = null)
        : this(
            new EmptySessionRunnerClient(),
            eventBus,
            settings,
            scopeFactory,
            timeProvider,
            logger,
            mentionRouter)
    {
    }

    public void Register(Guid sessionId, IAgentProtocolAdapter adapter)
    {
        if (!_testAdapters.TryAdd(sessionId, adapter))
            throw new ConflictException($"Agent session '{sessionId}' is already registered.");

        _testBuffers.TryAdd(sessionId, new StringBuilder());
        adapter.OnTextDelta += OnTestAdapterDelta;
        return;

        void OnTestAdapterDelta(string text)
        {
            _testBuffers.GetOrAdd(sessionId, _ => new StringBuilder()).Append(text);
            var sequence = _lastSequences.AddOrUpdate(sessionId, 1, (_, previous) => previous + 1);
            _ = ObserveOutputAsync(sessionId, sequence, text, CancellationToken.None);
        }
    }

    public async Task ObserveOutputAsync(Guid sessionId, long sequence, string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _lastSequences.AddOrUpdate(sessionId, sequence, (_, previous) => Math.Max(previous, sequence));
        _firstDeltas.GetOrAdd(sessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        var maxChunkChars = Math.Max(1, _settings.SignalRMaxChunkChars);
        for (var offset = 0; offset < text.Length; offset += maxChunkChars)
        {
            var chunk = text.Substring(offset, Math.Min(maxChunkChars, text.Length - offset));
            try
            {
                _mentionRouter?.ObserveDelta(sessionId, chunk);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to observe mention delta for session {SessionId}", sessionId);
            }

            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(sessionId),
                "AgentTextDelta",
                new { sessionId, sequence, text = chunk },
                ct);
        }

        await RecordActivityAsync(sessionId);
    }

    public async Task ObserveExitAsync(Guid sessionId, int? exitCode, AgentExitReason exitReason, CancellationToken ct)
    {
        await CloseSessionOnExitAsync(sessionId, exitCode, exitReason, ct);
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(sessionId),
            "SessionExited",
            new { sessionId, status = "Exited", exitCode, exitReason = exitReason.ToString() },
            ct);
    }

    // A runner exit must also land in the DATABASE, not just the SignalR group: the session row is
    // what the UI's liveSession/agent-status derive from. Before this, an exit that arrived via the
    // event pump left the session Running and its agent Working forever — a phantom with no process.
    // Idempotent: sessions already closed by another path (kill, launch-failure, reconciler) are
    // left untouched. Card-owned agents are skipped; their lifecycle belongs to the orchestrator.
    private async Task CloseSessionOnExitAsync(Guid sessionId, int? exitCode, AgentExitReason exitReason, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session is null)
                return;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var changed = false;
            // A CPU-spin watchdog kill reclaims an IDLE session whose process was busy-looping a
            // core after its turn completed — the non-zero exit code is just the kill's. It must
            // land as Stopped, not Failed, so the next message resumes the session by id.
            // CARD-0186: herdr pane-closed / presumed-dead / child-gone / pane-left-open are
            // never a clean stop — Stopped is operator intent and reconciliation's auto-kill key.
            var cleanStop = exitCode == 0 || exitReason == AgentExitReason.CpuSpinKilled;
            if (session.Status is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping)
            {
                session.Status = cleanStop ? SessionStatus.Stopped : SessionStatus.Failed;
                session.ExitCode = exitCode;
                if (session.Status == SessionStatus.Failed)
                    session.FailureReason = $"Process exited ({exitReason}, code {exitCode?.ToString() ?? "unknown"}).";
                // A prior KillAsync may already have persisted OperatorRequest/SystemRequest;
                // an exit event must not erase it (CARD-0256).
                if (session.TerminationSource == SessionTerminationSource.Unknown)
                    session.TerminationSource = SessionTerminationSource.ProcessExit;
                session.EndedAt ??= now;
                session.LastSeenAt = now;
                changed = true;
            }

            Guid? changedAgentId = null;
            var sessionIdText = sessionId.ToString("D");
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.PersistentSessionId == sessionIdText, ct);
            if (agent is not null && agent.Status == AgentStatus.Running && agent.CurrentCardId is null)
            {
                agent.Status = cleanStop ? AgentStatus.Stopped : AgentStatus.Failed;
                agent.UpdatedAt = now;
                changed = true;
                changedAgentId = agent.Id;
            }

            if (exitReason == AgentExitReason.HerdrPaneLeftOpen)
            {
                var ownerId = agent?.Id ?? changedAgentId;
                if (ownerId is Guid paneLeftOwner
                    && !await db.AgentIncidents.AnyAsync(
                        i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.HerdrPaneLeftOpen, ct))
                {
                    var supervisor = scope.ServiceProvider.GetService<AgentSupervisorService>();
                    if (supervisor is not null)
                    {
                        await supervisor.RecordIncidentAsync(
                            paneLeftOwner,
                            sessionId,
                            AgentIncidentKind.HerdrPaneLeftOpen,
                            AlertSeverity.Warning,
                            "Herdr kill left the pane open: a foreign process was in the foreground. Our child was killed by pid; tidy the pane by hand.",
                            failureReason: AgentExitReason.HerdrPaneLeftOpen.ToString(),
                            ct: ct);
                    }
                    else
                    {
                        db.AgentIncidents.Add(new AgentIncident
                        {
                            Id = Guid.NewGuid(),
                            AgentId = paneLeftOwner,
                            SessionId = sessionId,
                            Kind = AgentIncidentKind.HerdrPaneLeftOpen,
                            Severity = AlertSeverity.Warning,
                            Message = "Herdr kill left the pane open: a foreign process was in the foreground. Our child was killed by pid; tidy the pane by hand.",
                            FailureReason = AgentExitReason.HerdrPaneLeftOpen.ToString(),
                            CreatedAt = now,
                        });
                    }

                    changed = true;
                }
            }

            if (changed)
                await db.SaveChangesAsync(ct);
            if (changedAgentId is Guid agentId)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist exit of session {SessionId}", sessionId);
        }
    }

    /// <summary>Relays a structured transcript entry to the session's SignalR group and persists it (idempotently).</summary>
    public async Task ObserveTranscriptAsync(SessionRunnerTranscriptEvent entry, CancellationToken ct)
    {
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(entry.SessionId),
            "SessionTranscript",
            ToTranscriptPayload(entry),
            ct);

        // Decided BEFORE persisting: the check asks "has a row like this already landed?", and
        // persisting first would make every entry look like one we had already acted on.
        var actOnTurnBoundary = IsTurnBoundary(entry) && await IsUnseenTurnBoundaryAsync(entry, ct);

        var persisted = await PersistTranscriptAsync(entry.SessionId, new[] { entry });

        // A completed turn (the agent stopped and is waiting) is the trigger to flush the next queued
        // "wait until idle" message, or — when nothing is queued — to mark the session finished.
        // Interrupted turns (Esc / rejected tool call) end with the "[Request interrupted..." user
        // marker and NO TurnEnd — they are turn boundaries too, and the queue must flush or every
        // WhenIdle delivery strands until some later turn completes (live miss 2026-07-29).
        if (actOnTurnBoundary)
            await FlushQueueOnIdleAsync(entry.SessionId, ct);

        // Claude writes the turn's stop marker BEFORE its reply text: one API response becomes
        // several JSONL records (thinking first, then text) and EVERY one carries the response's
        // stop_reason, so the turn end we act on is the thinking record's. The TurnEnd-time
        // dispatch therefore sees no text and leaves the correlations pending — the text's own
        // arrival must re-trigger the channel reply dispatch (cheap no-op when nothing is pending).
        if (entry.Kind == TranscriptKinds.AssistantText)
            await DispatchChannelRepliesAsync(entry.SessionId, ct);

        // A compaction boundary triggers recovery (incident + workspace re-read note). Resolved
        // lazily like the queue service below: CompactionRecoveryService ctor-injects the queue,
        // which ctor-injects this runtime — direct injection here would close a constructor cycle.
        // Uses the STORED (session-monotonic) sequence, not the raw tailer one: the persisted
        // recovery watermark must stay comparable across tailer generations, and a boundary that
        // deduped away (replay) was already handled.
        if (entry.Kind == TranscriptKinds.CompactBoundary && persisted.LastStoredSeq is long boundarySeq)
        {
            await DispatchCompactionRecoveryAsync(entry.SessionId, boundarySeq, ct);

            // A MANUAL boundary is a turn end for the working rule (CARD-0041) and nothing else
            // will ever flush this session: compaction makes no API call, so no TurnEnd follows.
            // Recovery dispatch runs FIRST so its note is already queued when the flush looks.
            // Narrow on purpose — FlushIfIdleAsync, not the turn-end path: no "Agent finished",
            // no reply/task settlement against the stale pre-compaction report.
            if (TranscriptKinds.IsManualCompactBoundary(entry.Kind, entry.Text))
                await FlushQueueAfterManualCompactionAsync(entry.SessionId, ct);
        }
    }

    // Resolved lazily from a scope for the same constructor-cycle reason as FlushQueueOnIdleAsync.
    private async Task FlushQueueAfterManualCompactionAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetService<SessionMessageQueueService>();
            if (queue is not null)
                await queue.FlushIfIdleAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Failed to flush the message queue after a manual compaction for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// CARD-0162: herdr agent_status changed. The ONLY permitted side effect is a narrow
    /// <see cref="SessionMessageQueueService.FlushIfIdleAsync"/> when leaving <c>blocked</c> —
    /// never a delivery verdict, kill, or working-state override. The flush re-checks
    /// IsWorkingAsync and the S3 blocked gate before typing.
    /// </summary>
    public async Task ObserveAgentStatusAsync(SessionRunnerAgentStatusEvent evt, CancellationToken ct)
    {
        // The board query overlays runner status on demand. Publish on every status change so the
        // existing AgentChanged invalidation makes that overlay live; unclaimed sessions have no UI owner.
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agentId = await db.Agents.AsNoTracking()
                .Where(a => a.PersistentSessionId == evt.SessionId.ToString("D"))
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (agentId is Guid id)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(id), ct);
        }

        if (!string.Equals(evt.PreviousAgentStatus, "blocked", StringComparison.Ordinal))
            return;

        if (string.Equals(evt.AgentStatus, "blocked", StringComparison.Ordinal))
            return;

        _logger.LogInformation(
            "Herdr status left blocked ({Previous} → {Current}) for session {SessionId}; nudging FlushIfIdleAsync",
            evt.PreviousAgentStatus, evt.AgentStatus, evt.SessionId);
        // Narrow flush only — re-checks IsWorkingAsync + S3 blocked gate before typing.
        await FlushQueueAfterManualCompactionAsync(evt.SessionId, ct);
    }

    /// <summary>A transcript entry that means "the agent stopped and is waiting" (see the callers of
    /// <see cref="FlushQueueOnIdleAsync"/> for why the interrupt marker counts).</summary>
    private static bool IsTurnBoundary(SessionRunnerTranscriptEvent entry) =>
        (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == TranscriptKinds.StopReasons.EndTurn)
        // Grok's Esc interrupt is an EXPLICIT turn_completed with stop_reason "cancelled"
        // (measured 1.0.5, CARD-0080 S1) — the structured analog of Claude's "[Request
        // interrupted…" marker below, and like it, the queue must flush on it or every WhenIdle
        // delivery strands until some later turn completes. Claude's API never emits "cancelled"
        // as a stop_reason, so this arm cannot change Claude behaviour. CARD-0159: this stays
        // an idle boundary (the queue must flush) but is never a report boundary.
        || (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == TranscriptKinds.StopReasons.Cancelled)
        || TranscriptKinds.IsInterruptPrompt(entry.Kind, entry.Text);

    /// <summary>
    /// True when this turn boundary is one we have not already acted on. Two ways it can be a
    /// repeat, and both fired a duplicate queue flush and a duplicate "Agent finished" toast
    /// (live miss 2026-08-06: 13 toasts in one second on agent start, then 2 per turn after):
    ///
    /// 1. REPLAY. The runner's tailer always re-reads the transcript from offset 0 — on agent
    ///    start, on runner restart/adoption, and on the /clear fork-follow — so EVERY historic turn
    ///    end is re-published. Its transcript uuid is already stored: that is history, not news.
    /// 2. SPLIT API RESPONSE. Claude Code writes one API response as several JSONL records (a
    ///    "thinking" record, then a "text" record) and stamps every one with the response's
    ///    stop_reason, so a single turn yields several TurnEnd entries sharing one ApiCallId. Only
    ///    the first of them is the turn's end.
    ///
    /// Fails OPEN — an unknown session, a null uuid, or a failed query all count as "unseen".
    /// Missing a real turn end strands every WhenIdle delivery on that session (2026-07-29,
    /// 2026-07-31); a duplicate toast is merely noisy.
    /// </summary>
    private async Task<bool> IsUnseenTurnBoundaryAsync(SessionRunnerTranscriptEvent entry, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessionId = entry.SessionId;

            // (1) Same (uuid, kind) as a stored row — the same transcript line, seen again.
            // Mirrors the dedup key PersistTranscriptAsync uses, so "acted on" and "persisted"
            // can never disagree.
            var uuid = entry.Uuid;
            if (uuid is not null && await db.TranscriptEntries.AnyAsync(
                    t => t.AgentSessionId == sessionId && t.Uuid == uuid && t.Kind == entry.Kind, ct))
                return false;

            // (2) A different line of the SAME API response — its sibling already ended the turn.
            var apiCallId = entry.ApiCallId;
            if (entry.Kind == TranscriptKinds.TurnEnd
                && apiCallId is not null
                && await db.TranscriptEntries.AnyAsync(
                    t => t.AgentSessionId == sessionId
                        && t.Kind == TranscriptKinds.TurnEnd
                        && t.ApiCallId == apiCallId, ct))
                return false;

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Turn-boundary replay check failed for session {SessionId}; treating as live", entry.SessionId);
            return true;
        }
    }

    private async Task DispatchCompactionRecoveryAsync(Guid sessionId, long sequence, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var recovery = scope.ServiceProvider.GetService<CompactionRecoveryService>();
            if (recovery is not null)
                await recovery.OnCompactBoundaryAsync(sessionId, sequence, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Compaction recovery dispatch failed for session {SessionId}", sessionId);
        }
    }

    private async Task DispatchChannelRepliesAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var channelReplies = scope.ServiceProvider.GetService<ChannelReplyDispatcher>();
            if (channelReplies is not null)
                await channelReplies.OnTurnEndAsync(sessionId, ct);
            var reviewReplies = scope.ServiceProvider.GetService<ReviewReplyDispatcher>();
            if (reviewReplies is not null)
                await reviewReplies.OnTurnEndAsync(sessionId, ct);

            // Same reason as the channel dispatcher above: Claude can write the turn's stop marker
            // BEFORE its report text, so a delegate's task would stay Working forever if only the
            // TurnEnd triggered settlement. The text's own arrival re-triggers it (no-op otherwise).
            var taskReplies = scope.ServiceProvider.GetService<AgentTaskReplyService>();
            if (taskReplies is not null)
                await taskReplies.OnTurnEndAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Channel reply dispatch failed for session {SessionId}", sessionId);
        }
    }

    // Resolve the (singleton) queue service lazily from a scope to avoid a constructor cycle with this
    // runtime, then let it deliver the next queued message or emit the finished signal. The channel
    // reply dispatcher runs FIRST: it reads the just-finished turn's transcript to route the agent's
    // answer back down its external channel (Telegram etc.), and must see the transcript before the
    // queue injects the next prompt.
    private async Task FlushQueueOnIdleAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var channelReplies = scope.ServiceProvider.GetService<ChannelReplyDispatcher>();
            if (channelReplies is not null)
                await channelReplies.OnTurnEndAsync(sessionId, ct);

            // Review-thread replies land the same way channel replies do — before the queue
            // injects the next prompt, so extraction sees the finished turn intact.
            var reviewReplies = scope.ServiceProvider.GetService<ReviewReplyDispatcher>();
            if (reviewReplies is not null)
                await reviewReplies.OnTurnEndAsync(sessionId, ct);

            // A delegate's finished turn IS its report — settle the task and deliver the note to
            // its parent before the queue injects anything else into this session.
            var taskReplies = scope.ServiceProvider.GetService<AgentTaskReplyService>();
            if (taskReplies is not null)
                await taskReplies.OnTurnEndAsync(sessionId, ct);

            var queue = scope.ServiceProvider.GetService<SessionMessageQueueService>();
            if (queue is not null)
                await queue.OnTurnEndAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to flush message queue on idle for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// The fetch-and-persist half of <see cref="SyncTranscriptAsync"/>, with NO queue side effects —
    /// safe to call while holding the per-session queue lock, which <see cref="SyncTranscriptAsync"/>
    /// is not (its turn-boundary flush re-enters the queue and would deadlock).
    ///
    /// <para>This exists because the live event stream is not a reliable clock. On session
    /// <c>e809ce65</c> (2026-08-16) Claude wrote the confirming <c>UserPrompt</c> 0.9s after the
    /// submit and the server did not store it for 45 seconds — the records only landed when the
    /// session ENDED and this same snapshot fetch ran. A caller about to make an irreversible
    /// decision on "the transcript does not contain X" must PULL the runner's view first; waiting
    /// for the stream to catch up was measured at 90 seconds and still lost the race.</para>
    /// </summary>
    /// <returns>True when the pull stored at least one entry that was not already known.</returns>
    public async Task<bool> CatchUpTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            var snapshot = await _runnerClient.GetTranscriptAsync(sessionId, ct);
            return (await PersistTranscriptAsync(sessionId, snapshot.Entries)).LastStoredSeq is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not live in the runner, or the runner is unreachable: the caller falls back to
            // whatever the stream has already stored, which is exactly today's behaviour.
            _logger.LogDebug(ex, "Transcript catch-up skipped for session {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Best-effort catch-up: pull the full transcript snapshot from the live runner and upsert it, so the
    /// persisted history stays complete even if some live SessionTranscript events were missed during a
    /// stream disconnect. No-op (swallowed) when the session is not live in the runner.
    /// </summary>
    public async Task SyncTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        PersistResult persisted;
        try
        {
            var snapshot = await _runnerClient.GetTranscriptAsync(sessionId, ct);
            persisted = await PersistTranscriptAsync(sessionId, snapshot.Entries);
        }
        catch (Exception ex)
        {
            // Session is not live in the runner (or runner unavailable) — the DB still holds whatever streamed.
            _logger.LogDebug(ex, "Transcript sync skipped for session {SessionId}", sessionId);
            return;
        }

        // A boundary that arrives via SYNC must trigger the same turn-end actions a live one does.
        // The live path won't cover it: when the synced row lands first, the later live re-emission
        // (if any) dedups as "seen" and never flushes — a TurnEnd missed during a server restart
        // then strands every WhenIdle delivery until the next real turn (live miss 2026-08-08).
        // FlushQueueOnIdleAsync re-checks IsWorkingAsync itself, so a mid-turn sync stays a no-op.
        if (persisted.AddedTurnBoundary)
            await FlushQueueOnIdleAsync(sessionId, ct);
        else if (persisted.AddedAssistantText)
            await DispatchChannelRepliesAsync(sessionId, ct);

        // A MANUAL compaction boundary is a turn end too, and it can arrive ONLY via backfill —
        // same argument as the TurnEnd case above. It gets the NARROW flush, never the turn-end
        // path: a compaction is not a report to settle a task against (CARD-0041).
        if (persisted.AddedManualCompactBoundary)
            await FlushQueueAfterManualCompactionAsync(sessionId, ct);
    }

    // What one persist call actually changed — LastStoredSeq is the stored (session-monotonic)
    // sequence of the last NEWLY persisted entry, or null when everything deduped away or
    // persistence failed.
    private sealed record PersistResult(
        long? LastStoredSeq, bool AddedTurnBoundary, bool AddedAssistantText, bool AddedManualCompactBoundary)
    {
        public static PersistResult Empty { get; } = new(null, false, false, false);
    }

    private async Task<PersistResult> PersistTranscriptAsync(Guid sessionId, IReadOnlyList<SessionRunnerTranscriptEvent> entries)
    {
        if (entries.Count == 0)
            return PersistResult.Empty;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // FK safety: only persist for sessions the DB actually knows about (skips test/transient ids).
            if (!await db.AgentSessions.AnyAsync(s => s.Id == sessionId))
                return PersistResult.Empty;

            // Dedup by transcript line uuid, NOT by sequence: the runner tailer numbers entries per
            // tailer LIFETIME, restarting from 1 after a session relaunch or fork re-discovery. A
            // resumed session (same id, fresh tailer) therefore re-issues low sequences that collide
            // with last generation's rows — sequence-dedup silently dropped every new entry, which
            // killed reply routing after a relaunch (2026-07-23). Uuids come from the transcript
            // lines themselves, so they survive re-numbering, and history re-emitted on adoption
            // dedups cleanly. Entries without a uuid keep the old sequence-dedup.
            var incomingUuids = entries.Where(e => e.Uuid is not null).Select(e => e.Uuid!).ToHashSet();
            var seenUuids = (await db.TranscriptEntries
                    .Where(t => t.AgentSessionId == sessionId && t.Uuid != null && incomingUuids.Contains(t.Uuid!))
                    .Select(t => new { t.Uuid, t.Kind })
                    .ToListAsync())
                .Select(x => (x.Uuid!, x.Kind))
                .ToHashSet();
            var incomingSeqs = entries.Where(e => e.Uuid is null).Select(e => e.Sequence).ToHashSet();
            var seenSeqs = incomingSeqs.Count == 0
                ? []
                : (await db.TranscriptEntries
                        .Where(t => t.AgentSessionId == sessionId && incomingSeqs.Contains(t.Sequence))
                        .Select(t => t.Sequence)
                        .ToListAsync())
                    .ToHashSet();

            // Stored sequences stay strictly monotonic per SESSION: a new tailer generation's
            // restarted numbering is rebased past the session's current max, so "latest turn"
            // queries (reply dispatcher, queue idle checks, UI ordering) keep working across
            // relaunches without schema changes.
            var maxSeq = await db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var added = false;
            var addedTurnBoundary = false;
            var addedAssistantText = false;
            var addedManualCompactBoundary = false;
            foreach (var e in entries)
            {
                if (e.Uuid is not null)
                {
                    if (!seenUuids.Add((e.Uuid, e.Kind)))
                        continue; // already persisted, or a duplicate within this batch
                }
                else if (!seenSeqs.Add(e.Sequence))
                {
                    continue;
                }

                addedTurnBoundary |= IsTurnBoundary(e);
                addedAssistantText |= e.Kind == TranscriptKinds.AssistantText;
                // Tracked SEPARATELY from IsTurnBoundary on purpose: it must reach the narrow
                // flush only, never actOnTurnBoundary / the finished toast / settlement.
                addedManualCompactBoundary |= TranscriptKinds.IsManualCompactBoundary(e.Kind, e.Text);

                var storedSeq = e.Sequence > maxSeq ? e.Sequence : maxSeq + 1;
                maxSeq = storedSeq;

                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = storedSeq,
                    Kind = e.Kind,
                    Uuid = e.Uuid,
                    ParentUuid = e.ParentUuid,
                    Timestamp = e.Timestamp?.UtcDateTime,
                    Role = e.Role,
                    Text = e.Text,
                    ToolName = e.ToolName,
                    ToolInput = e.ToolInput,
                    ToolUseId = e.ToolUseId,
                    ToolIsError = e.ToolIsError,
                    StopReason = e.StopReason,
                    ApiCallId = e.ApiCallId,
                    InputTokens = e.InputTokens,
                    OutputTokens = e.OutputTokens,
                    CacheReadTokens = e.CacheReadTokens,
                    CacheCreationTokens = e.CacheCreationTokens,
                    IsApiError = e.IsApiError,
                    ApiErrorClass = e.ApiErrorClass,
                    ApiErrorStatus = e.ApiErrorStatus,
                    Model = e.Model,
                    ModelCalls = e.ModelCalls,
                    CreatedAt = now,
                });
                added = true;
            }

            if (added)
                await db.SaveChangesAsync();

            return added
                ? new PersistResult(maxSeq, addedTurnBoundary, addedAssistantText, addedManualCompactBoundary)
                : PersistResult.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist transcript entries for session {SessionId}", sessionId);
            return PersistResult.Empty;
        }
    }

    private static object ToTranscriptPayload(SessionRunnerTranscriptEvent e) => new
    {
        sessionId = e.SessionId,
        sequence = e.Sequence,
        kind = e.Kind,
        uuid = e.Uuid,
        parentUuid = e.ParentUuid,
        timestamp = e.Timestamp,
        role = e.Role,
        text = e.Text,
        toolName = e.ToolName,
        toolInput = e.ToolInput,
        toolUseId = e.ToolUseId,
        toolIsError = e.ToolIsError,
        stopReason = e.StopReason,
        apiCallId = e.ApiCallId,
        inputTokens = e.InputTokens,
        outputTokens = e.OutputTokens,
        cacheReadTokens = e.CacheReadTokens,
        cacheCreationTokens = e.CacheCreationTokens,
        isApiError = e.IsApiError,
        apiErrorClass = e.ApiErrorClass,
        apiErrorStatus = e.ApiErrorStatus,
        model = e.Model,
        modelCalls = e.ModelCalls,
    };

    public async Task<bool> WaitForFirstDeltaAsync(Guid sessionId, TimeSpan timeout, CancellationToken ct)
    {
        var firstDelta = _firstDeltas.GetOrAdd(sessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        var completed = await Task.WhenAny(firstDelta.Task, Task.Delay(timeout, ct));
        return completed == firstDelta.Task;
    }

    public long GetDeltaSequence(Guid sessionId)
    {
        if (_lastSequences.TryGetValue(sessionId, out var sequence))
            return sequence;

        throw new NotFoundException("AgentSessionRuntime", sessionId);
    }

    public long GetDeltaSequenceOrDefault(Guid sessionId) =>
        _lastSequences.GetValueOrDefault(sessionId);

    public async Task<bool> WaitForDeltaAfterAsync(
        Guid sessionId,
        long sequence,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (GetDeltaSequenceOrDefault(sessionId) > sequence)
            return true;

        var delay = Task.Delay(timeout, ct);
        while (!delay.IsCompleted)
        {
            if (GetDeltaSequenceOrDefault(sessionId) > sequence)
                return true;

            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
        }

        return GetDeltaSequenceOrDefault(sessionId) > sequence;
    }

    public AgentSessionRuntimeBufferSnapshot GetBufferSnapshot(Guid sessionId)
    {
        if (_testBuffers.TryGetValue(sessionId, out var testBuffer))
            return new AgentSessionRuntimeBufferSnapshot(testBuffer.ToString(), GetDeltaSequenceOrDefault(sessionId));

        var buffer = _runnerClient.GetBufferAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();
        return new AgentSessionRuntimeBufferSnapshot(buffer.Buffer, buffer.LastSequence);
    }

    public IReadOnlyList<Guid> ListLiveSessions() =>
        _runnerClient.ListAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Where(session => session.Status is "Running" or "Starting")
            .Select(session => session.SessionId)
            .Concat(_testAdapters.Keys)
            .Distinct()
            .ToList();

    public bool TryGetLiveSnapshot(Guid sessionId, out AgentSessionLiveSnapshot snapshot)
    {
        try
        {
            if (_testAdapters.TryGetValue(sessionId, out var adapter))
            {
                snapshot = new AgentSessionLiveSnapshot(
                    sessionId,
                    adapter.SnapshotRawOutput(),
                    adapter.SnapshotRenderedScreen(),
                    _testBuffers.GetValueOrDefault(sessionId)?.ToString() ?? string.Empty,
                    GetDeltaSequenceOrDefault(sessionId));
                return true;
            }

            var runnerSnapshot = _runnerClient.GetSnapshotAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();
            var buffer = _runnerClient.GetBufferAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();
            snapshot = new AgentSessionLiveSnapshot(
                sessionId,
                runnerSnapshot.RawOutput,
                runnerSnapshot.RenderedScreen,
                buffer.Buffer,
                runnerSnapshot.LastSequence);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live snapshot unavailable for agent session {SessionId}", sessionId);
            snapshot = default!;
            return false;
        }
    }

    public bool TryGetLiveMetadata(Guid sessionId, out AgentSessionLiveMetadata metadata)
    {
        if (_testAdapters.ContainsKey(sessionId))
        {
            _testAgentStatuses.TryGetValue(sessionId, out var status);
            _testPending.TryGetValue(sessionId, out var pending);
            metadata = new AgentSessionLiveMetadata(
                sessionId, GetDeltaSequenceOrDefault(sessionId), status, Pending: pending);
            return true;
        }

        try
        {
            var session = _runnerClient.GetAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();
            if (session.Status is not ("Running" or "Starting"))
            {
                metadata = default!;
                return false;
            }

            metadata = new AgentSessionLiveMetadata(
                sessionId, session.LastSequence, session.AgentStatus,
                session.TranscriptBound, session.TranscriptBindHow, session.Pending);
            return true;
        }
        catch
        {
            metadata = default!;
            return false;
        }
    }

    public async Task SendInputAsync(
        Guid sessionId, string input, CancellationToken ct, bool trackManualTurn = true)
    {
        if (string.IsNullOrEmpty(input))
            return;

        if (_testAdapters.TryGetValue(sessionId, out var adapter))
        {
            var testSequenceBeforeInput = GetDeltaSequenceOrDefault(sessionId);
            var testSubmittedCommand = RecordTerminalInput(sessionId, input);
            await adapter.SendInputAsync(input, ct);
            if (testSubmittedCommand && trackManualTurn)
                TryStartManualTurnTracking(sessionId, testSequenceBeforeInput);
            return;
        }

        var sequenceBeforeInput = GetRunnerSequenceOrDefault(sessionId, ct);
        var submittedCommand = RecordTerminalInput(sessionId, input);
        await _runnerClient.SendInputAsync(sessionId, input, ct);
        if (submittedCommand && trackManualTurn)
            TryStartManualTurnTracking(sessionId, sequenceBeforeInput);
    }

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        _testAdapters.TryGetValue(sessionId, out var adapter)
            ? adapter.ResizeAsync(cols, rows, ct)
            : _runnerClient.ResizeAsync(sessionId, cols, rows, ct);

    public async Task<bool> KillAsync(Guid sessionId, TimeSpan timeout, CancellationToken ct)
    {
        if (_testAdapters.TryGetValue(sessionId, out var adapter))
            return await adapter.KillAsync(timeout, ct);

        var session = await _runnerClient.KillAsync(sessionId, ct);
        return session.Status == "Exited" || session.ExitCode is not null;
    }

    public Task<SessionRunnerSessionDto> GetSessionAsync(Guid sessionId, CancellationToken ct)
    {
        if (_testAdapters.TryGetValue(sessionId, out var adapter))
        {
            int? exitCode = adapter.Exited.IsCompletedSuccessfully
                ? adapter.Exited.Result
                : null;
            return Task.FromResult(new SessionRunnerSessionDto(
                sessionId,
                adapter.Pid,
                UtcNow(),
                exitCode is null ? "Running" : "Exited",
                exitCode,
                adapter.ExitReason,
                GetDeltaSequenceOrDefault(sessionId)));
        }

        return _runnerClient.GetAsync(sessionId, ct);
    }

    /// <summary>
    /// CARD-0164 test seam: set the herdr <c>agent_status</c> reported by
    /// <see cref="TryGetLiveMetadata"/> for an in-process test adapter. Production reads this from
    /// the runner; tests have no runner session DTO.
    /// </summary>
    public void SetTestAgentStatus(Guid sessionId, string? status) =>
        _testAgentStatuses[sessionId] = status;

    /// <summary>
    /// CARD-0186 S3 test seam: set the runner <c>Pending</c> reported by
    /// <see cref="TryGetLiveMetadata"/> for an in-process test adapter.
    /// </summary>
    public void SetTestPending(Guid sessionId, string? pending) =>
        _testPending[sessionId] = pending;

    public bool TryRemove(Guid sessionId, out IAgentProtocolAdapter? adapter)
    {
        _pendingInputs.TryRemove(sessionId, out _);
        _manualTurns.TryRemove(sessionId, out _);
        _lastSequences.TryRemove(sessionId, out _);
        _firstDeltas.TryRemove(sessionId, out _);
        _testBuffers.TryRemove(sessionId, out _);
        _testAgentStatuses.TryRemove(sessionId, out _);
        _testPending.TryRemove(sessionId, out _);
        return _testAdapters.TryRemove(sessionId, out adapter);
    }

    public async Task DisposeSessionAsync(Guid sessionId)
    {
        if (TryRemove(sessionId, out var adapter) && adapter is not null)
            await adapter.DisposeAsync();
    }

    private long GetRunnerSequenceOrDefault(Guid sessionId, CancellationToken ct)
    {
        if (_testBuffers.ContainsKey(sessionId))
            return GetDeltaSequenceOrDefault(sessionId);

        try
        {
            var buffer = _runnerClient.GetBufferAsync(sessionId, ct).GetAwaiter().GetResult();
            return buffer.LastSequence;
        }
        catch
        {
            return GetDeltaSequenceOrDefault(sessionId);
        }
    }

    private async Task RecordActivityAsync(Guid sessionId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session is not null)
                session.LastSeenAt = now;

            var attempt = await db.RunAttempts
                .Where(a => a.AgentSessionId == sessionId
                    && a.Phase == RunPhase.StreamingTurn
                    && a.CompletedAt == null)
                .OrderByDescending(a => a.AttemptNumber)
                .FirstOrDefaultAsync();
            if (attempt is not null)
                attempt.LastEventAt = now;

            if (session is not null || attempt is not null)
                await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity for agent session {SessionId}", sessionId);
        }
    }

    private bool RecordTerminalInput(Guid sessionId, string input)
    {
        var pendingInput = _pendingInputs.GetOrAdd(sessionId, _ => new PendingTerminalInput());
        return pendingInput.Append(input);
    }

    private void TryStartManualTurnTracking(Guid sessionId, long sequenceAtSubmit)
    {
        if (!_manualTurns.TryAdd(sessionId, 0))
            return;

        _ = Task.Run(() => TrackManualTurnAsync(sessionId, sequenceAtSubmit));
    }

    private async Task TrackManualTurnAsync(Guid sessionId, long sequenceAtSubmit)
    {
        ManualTurnStart? turn = null;
        try
        {
            turn = await TryCreateManualRunAttemptAsync(sessionId);
            if (turn is null)
                return;

            var result = await WaitForManualTurnQuietAsync(sessionId, sequenceAtSubmit);
            await CompleteManualRunAttemptAsync(turn, result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track manual terminal turn for session {SessionId}", sessionId);
            if (turn is not null)
                await TryFailManualRunAttemptAsync(turn, ex.Message);
        }
        finally
        {
            _manualTurns.TryRemove(sessionId, out _);
        }
    }

    private async Task<ManualTurnStart?> TryCreateManualRunAttemptAsync(Guid sessionId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = UtcNow();

        var session = await db.AgentSessions
            .Include(s => s.Card)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null || session.Status != SessionStatus.Running)
            return null;
        // Cardless interactive sessions have no card/run-attempt to record manual turns against.
        if (session.CardId is not Guid cardId)
            return null;

        var latestAttempt = await db.RunAttempts
            .Where(a => a.CardId == cardId)
            .OrderByDescending(a => a.AttemptNumber)
            .ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
        if (latestAttempt is not null && !RunAttemptStateMachine.IsTerminal(latestAttempt.Phase))
            return null;

        var nextAttemptNumber = (await db.RunAttempts
            .Where(a => a.CardId == cardId)
            .MaxAsync(a => (int?)a.AttemptNumber)) ?? 0;

        var attempt = new RunAttempt
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            AgentSessionId = session.Id,
            WorktreeId = session.WorktreeId,
            BoardWorkflowDefinitionId = latestAttempt?.BoardWorkflowDefinitionId,
            AttemptNumber = nextAttemptNumber + 1,
            Phase = RunPhase.StreamingTurn,
            CreatedAt = now,
            StartedAt = now,
            LastEventAt = now,
            PhaseStartedAt = now,
            Prompt = "Manual terminal input from xterm.",
            Card = session.Card,
            AgentSession = session
        };

        db.RunAttempts.Add(attempt);
        session.LastSeenAt = now;
        session.EndedAt = null;
        session.FailureReason = null;
        session.Card.OwnerSessionId = session.Id;
        session.Card.OwnerSession = session;
        session.Card.ConcurrencyToken = Guid.NewGuid();
        session.Card.UpdatedAt = now;
        await db.SaveChangesAsync();

        var turn = new ManualTurnStart(
            session.Id,
            attempt.Id,
            cardId,
            session.Card.BoardId);
        await PublishRunAttemptChangedAsync(turn, RunPhase.StreamingTurn);
        return turn;
    }

    private async Task<ManualTurnWaitResult> WaitForManualTurnQuietAsync(Guid sessionId, long sequenceAtSubmit)
    {
        var firstDeltaDeadline = UtcNow()
            + TimeSpan.FromMilliseconds(Math.Max(100, _settings.FirstDeltaTimeoutMs));
        var sawDelta = false;
        while (UtcNow() < firstDeltaDeadline)
        {
            if (!ListLiveSessions().Contains(sessionId))
                return ManualTurnWaitResult.RuntimeMissing;

            if (GetRunnerSequenceOrDefault(sessionId, CancellationToken.None) > sequenceAtSubmit)
            {
                sawDelta = true;
                break;
            }

            await Task.Delay(ManualTurnPollInterval);
        }

        if (!sawDelta)
            return ManualTurnWaitResult.Completed;

        var quietPeriodMs = Math.Max(250, _settings.ManualTurnQuietPeriodMs);
        var quietPeriod = TimeSpan.FromMilliseconds(quietPeriodMs);
        var maxWait = TimeSpan.FromMilliseconds(Math.Max(_settings.StallTimeoutMs, quietPeriodMs * 2));
        var deadline = UtcNow() + maxWait;
        var lastSequence = GetRunnerSequenceOrDefault(sessionId, CancellationToken.None);
        var lastChange = UtcNow();

        while (UtcNow() < deadline)
        {
            await Task.Delay(ManualTurnPollInterval);
            if (!ListLiveSessions().Contains(sessionId))
                return ManualTurnWaitResult.RuntimeMissing;

            var currentSequence = GetRunnerSequenceOrDefault(sessionId, CancellationToken.None);
            if (currentSequence != lastSequence)
            {
                lastSequence = currentSequence;
                lastChange = UtcNow();
                continue;
            }

            if (UtcNow() - lastChange >= quietPeriod)
                return ManualTurnWaitResult.Completed;
        }

        return ManualTurnWaitResult.TimedOut;
    }

    private async Task CompleteManualRunAttemptAsync(ManualTurnStart turn, ManualTurnWaitResult result)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = await db.RunAttempts
            .Include(a => a.AgentSession)
            .Include(a => a.Card)
            .FirstOrDefaultAsync(a => a.Id == turn.AttemptId);
        if (attempt is null || attempt.Phase != RunPhase.StreamingTurn)
            return;

        var now = UtcNow();
        switch (result)
        {
            case ManualTurnWaitResult.Completed:
                RunAttemptStateMachine.Transition(attempt, RunPhase.Finishing, now);
                RunAttemptStateMachine.Transition(attempt, RunPhase.Succeeded, UtcNow());
                break;
            case ManualTurnWaitResult.RuntimeMissing:
                RunAttemptStateMachine.Transition(attempt, RunPhase.Canceled, now);
                attempt.ErrorDetails = "Runtime session ended before the manual terminal turn completed.";
                break;
            case ManualTurnWaitResult.TimedOut:
                RunAttemptStateMachine.Transition(attempt, RunPhase.TimedOut, now);
                attempt.ErrorDetails = "Timed out waiting for the manual terminal turn to become quiet.";
                break;
        }

        if (attempt.AgentSession is not null)
            attempt.AgentSession.LastSeenAt = UtcNow();

        await db.SaveChangesAsync();
        await PublishRunAttemptChangedAsync(turn, attempt.Phase);
    }

    private async Task TryFailManualRunAttemptAsync(ManualTurnStart turn, string errorDetails)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var attempt = await db.RunAttempts.FirstOrDefaultAsync(a => a.Id == turn.AttemptId);
            if (attempt is null || attempt.Phase != RunPhase.StreamingTurn)
                return;

            RunAttemptStateMachine.Transition(attempt, RunPhase.Failed, UtcNow());
            attempt.ErrorDetails = errorDetails;
            await db.SaveChangesAsync();
            await PublishRunAttemptChangedAsync(turn, RunPhase.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark manual terminal turn {RunAttemptId} failed", turn.AttemptId);
        }
    }

    private async Task PublishRunAttemptChangedAsync(ManualTurnStart turn, RunPhase phase)
    {
        var payload = new
        {
            boardId = turn.BoardId,
            cardId = turn.CardId,
            sessionId = turn.SessionId,
            runAttemptId = turn.AttemptId,
            phase = phase.ToString()
        };
        await _eventBus.PublishToAllAsync("RunAttemptChanged", payload);
        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = turn.BoardId, cardId = turn.CardId });
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private sealed class PendingTerminalInput
    {
        private readonly object _gate = new();
        private readonly StringBuilder _buffer = new();

        public bool Append(string input)
        {
            var submittedCommand = false;
            lock (_gate)
            {
                foreach (var ch in input)
                {
                    if (ch is '\r' or '\n')
                    {
                        submittedCommand |= _buffer.ToString().Trim().Length > 0;
                        _buffer.Clear();
                        continue;
                    }

                    if (ch is '\b' or '\u007f')
                    {
                        if (_buffer.Length > 0)
                            _buffer.Length--;
                        continue;
                    }

                    if (ch == '\u0003')
                    {
                        _buffer.Clear();
                        continue;
                    }

                    if (!char.IsControl(ch))
                        _buffer.Append(ch);
                }
            }

            return submittedCommand;
        }
    }

    private sealed record ManualTurnStart(
        Guid SessionId,
        Guid AttemptId,
        Guid CardId,
        Guid BoardId);

    private enum ManualTurnWaitResult
    {
        Completed,
        RuntimeMissing,
        TimedOut
    }

    private sealed class EmptySessionRunnerClient : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException("No session runner client is configured for this test runtime.");

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotFoundException("AgentSessionRuntime", sessionId);

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, string.Empty, 0));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotFoundException("AgentSessionRuntime", sessionId);

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId,
                null,
                DateTime.UtcNow,
                "Exited",
                0,
                AgentExitReason.KilledByRequest,
                0));

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}

public sealed record AgentSessionRuntimeBufferSnapshot(string Buffer, long LastSequence);

public sealed record AgentSessionLiveSnapshot(
    Guid SessionId,
    string RawOutput,
    string RenderedScreen,
    string Buffer,
    long LastSequence);

/// <param name="AgentStatus">
/// CARD-0161: herdr <c>pane.get.agent_status</c> verbatim for herdr sessions; null for pty
/// sessions and older runners. Only the literal <c>"blocked"</c> may gate delivery.
/// </param>
public sealed record AgentSessionLiveMetadata(
    Guid SessionId,
    long LastSequence,
    string? AgentStatus = null,
    bool? TranscriptBound = null,
    string? TranscriptBindHow = null,
    string? Pending = null);

public static class AgentSessionGroups
{
    public static string Session(Guid sessionId) => $"session-{sessionId}";
}
