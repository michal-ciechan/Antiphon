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
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(sessionId),
            "SessionExited",
            new { sessionId, status = "Exited", exitCode, exitReason = exitReason.ToString() },
            ct);
    }

    /// <summary>Relays a structured transcript entry to the session's SignalR group and persists it (idempotently).</summary>
    public async Task ObserveTranscriptAsync(SessionRunnerTranscriptEvent entry, CancellationToken ct)
    {
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(entry.SessionId),
            "SessionTranscript",
            ToTranscriptPayload(entry),
            ct);
        await PersistTranscriptAsync(entry.SessionId, new[] { entry });

        // A completed turn (the agent stopped and is waiting) is the trigger to flush the next queued
        // "wait until idle" message, or — when nothing is queued — to mark the session finished.
        if (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == "end_turn")
            await FlushQueueOnIdleAsync(entry.SessionId, ct);
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
    /// Best-effort catch-up: pull the full transcript snapshot from the live runner and upsert it, so the
    /// persisted history stays complete even if some live SessionTranscript events were missed during a
    /// stream disconnect. No-op (swallowed) when the session is not live in the runner.
    /// </summary>
    public async Task SyncTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            var snapshot = await _runnerClient.GetTranscriptAsync(sessionId, ct);
            await PersistTranscriptAsync(sessionId, snapshot.Entries);
        }
        catch (Exception ex)
        {
            // Session is not live in the runner (or runner unavailable) — the DB still holds whatever streamed.
            _logger.LogDebug(ex, "Transcript sync skipped for session {SessionId}", sessionId);
        }
    }

    private async Task PersistTranscriptAsync(Guid sessionId, IReadOnlyList<SessionRunnerTranscriptEvent> entries)
    {
        if (entries.Count == 0)
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // FK safety: only persist for sessions the DB actually knows about (skips test/transient ids).
            if (!await db.AgentSessions.AnyAsync(s => s.Id == sessionId))
                return;

            var incoming = entries.Select(e => e.Sequence).ToHashSet();
            var seen = (await db.TranscriptEntries
                    .Where(t => t.AgentSessionId == sessionId && incoming.Contains(t.Sequence))
                    .Select(t => t.Sequence)
                    .ToListAsync())
                .ToHashSet();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var added = false;
            foreach (var e in entries)
            {
                if (!seen.Add(e.Sequence))
                    continue; // already persisted, or a duplicate within this batch

                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = e.Sequence,
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
                    CreatedAt = now,
                });
                added = true;
            }

            if (added)
                await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist transcript entries for session {SessionId}", sessionId);
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
            metadata = new AgentSessionLiveMetadata(sessionId, GetDeltaSequenceOrDefault(sessionId));
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

            metadata = new AgentSessionLiveMetadata(sessionId, session.LastSequence);
            return true;
        }
        catch
        {
            metadata = default!;
            return false;
        }
    }

    public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input))
            return;

        if (_testAdapters.TryGetValue(sessionId, out var adapter))
        {
            var testSequenceBeforeInput = GetDeltaSequenceOrDefault(sessionId);
            var testSubmittedCommand = RecordTerminalInput(sessionId, input);
            await adapter.SendInputAsync(input, ct);
            if (testSubmittedCommand)
                TryStartManualTurnTracking(sessionId, testSequenceBeforeInput);
            return;
        }

        var sequenceBeforeInput = GetRunnerSequenceOrDefault(sessionId, ct);
        var submittedCommand = RecordTerminalInput(sessionId, input);
        await _runnerClient.SendInputAsync(sessionId, input, ct);
        if (submittedCommand)
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

    public bool TryRemove(Guid sessionId, out IAgentProtocolAdapter? adapter)
    {
        _pendingInputs.TryRemove(sessionId, out _);
        _manualTurns.TryRemove(sessionId, out _);
        _lastSequences.TryRemove(sessionId, out _);
        _firstDeltas.TryRemove(sessionId, out _);
        _testBuffers.TryRemove(sessionId, out _);
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

public sealed record AgentSessionLiveMetadata(Guid SessionId, long LastSequence);

public static class AgentSessionGroups
{
    public static string Session(Guid sessionId) => $"session-{sessionId}";
}
