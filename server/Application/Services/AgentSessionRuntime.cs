using System.Collections.Concurrent;
using System.Data.Common;
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
public sealed partial class AgentSessionRuntime
{
    private static readonly TimeSpan ManualTurnPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentDictionary<Guid, long> _lastSequences = new();
    private readonly ConcurrentDictionary<Guid, TranscriptPersistFailure> _persistFailures = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _firstDeltas = new();
    private readonly ConcurrentDictionary<Guid, PendingTerminalInput> _pendingInputs = new();
    private readonly ConcurrentDictionary<Guid, byte> _manualTurns = new();
    private readonly ConcurrentDictionary<Guid, IAgentProtocolAdapter> _testAdapters = new();
    // CARD-0164 test seam: herdr AgentStatus on in-process adapters (production reads it from the runner).
    private readonly ConcurrentDictionary<Guid, string?> _testAgentStatuses = new();
    // CARD-0186 S3 test seam: runner Pending on in-process adapters.
    private readonly ConcurrentDictionary<Guid, string?> _testPending = new();
    // CARD-1150 test seam: accepted generation on in-process adapters. Production reads it from
    // the runner DTO. Unset stays null, which recovery treats as generation-unknown.
    private readonly ConcurrentDictionary<Guid, DateTime> _testAcceptedStartedAt = new();
    // CARD-0334 S2 test seam: transcript bind flag on in-process adapters (production reads it from the runner).
    private readonly ConcurrentDictionary<Guid, bool?> _testTranscriptBound = new();
    private readonly ConcurrentDictionary<Guid, StringBuilder> _testBuffers = new();
    // CARD-0679 D-3: when each session's output activity was last written.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastActivityWrites = new();
    private readonly ISessionRunnerClient _runnerClient;
    private readonly IEventBus _eventBus;
    private readonly AgentSessionSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentSessionRuntime> _logger;
    private readonly AgentMentionRouter? _mentionRouter;
    private readonly SessionGenerationCompatState _generationCompat;
    private readonly ISessionRunnerDirectory? _directory;
    private readonly SessionStateStore? _states;
    private readonly ProviderDialogObservationService _dialogs;

    public AgentSessionRuntime(
        ISessionRunnerClient runnerClient,
        IEventBus eventBus,
        IOptions<AgentSessionSettings> settings,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AgentSessionRuntime> logger,
        AgentMentionRouter? mentionRouter = null,
        SessionGenerationCompatState? generationCompat = null,
        // CARD-0679 D-10: absent, only the local runner's sessions and test adapters are live.
        ISessionRunnerDirectory? directory = null,
        SessionStateStore? states = null,
        ProviderDialogObservationService? dialogs = null)
    {
        _states = states;
        _dialogs = dialogs ?? new ProviderDialogObservationService(runnerClient);
        _directory = directory;
        _runnerClient = runnerClient;
        _eventBus = eventBus;
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _mentionRouter = mentionRouter;
        _generationCompat = generationCompat ?? new SessionGenerationCompatState();
    }

    public AgentSessionRuntime(
        IEventBus eventBus,
        IOptions<AgentSessionSettings> settings,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AgentSessionRuntime> logger,
        AgentMentionRouter? mentionRouter = null,
        SessionStateStore? states = null)
        : this(
            new EmptySessionRunnerClient(),
            eventBus,
            settings,
            scopeFactory,
            timeProvider,
            logger,
            mentionRouter,
            states: states)
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

    public Task ObserveOutputAsync(Guid sessionId, long sequence, string text, CancellationToken ct) =>
        ObserveOutputAsync(sessionId, sequence, text, null, ct);

    public async Task ObserveOutputAsync(Guid sessionId, long sequence, string text,
        Action<SessionObservationPhase>? phase, CancellationToken ct)
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
                phase?.Invoke(SessionObservationPhase.OutputMention);
                // Queue the mention and return. The liveness read is the stamp taken while
                // this session is still the observer's source; delivery stays off this call.
                if (_mentionRouter is not null)
                    await _mentionRouter.ObserveCompletedAsync(
                        sessionId, chunk, token => ReadMentionLivenessAsync(sessionId, token), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to observe mention delta for session {SessionId}", sessionId);
            }

            phase?.Invoke(SessionObservationPhase.OutputPublish);
            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(sessionId),
                "AgentTextDelta",
                new { sessionId, sequence, text = chunk },
                ct);
        }

        phase?.Invoke(SessionObservationPhase.OutputActivity);
        await RecordActivityAsync(sessionId);
    }

    private async ValueTask<MentionSourceLiveness?> ReadMentionLivenessAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.Status, s.StartedAt, s.CardId, s.StandingAgentId })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return null;

        var live = row.Status is SessionStatus.Starting or SessionStatus.Running;
        return new MentionSourceLiveness(row.StartedAt, row.CardId, row.StandingAgentId, live);
    }

    /// <summary>
    /// Direct observation. A missing <paramref name="acceptedStartedAt"/> is declined
    /// (CARD-0502 D-3/D-5); this must not stamp the current row's generation onto an unbound exit.
    /// Prefer the producer envelope overload.
    /// </summary>
    public Task<SessionExitDisposition> ObserveExitAsync(
        Guid sessionId, int? exitCode, AgentExitReason exitReason, CancellationToken ct,
        DateTime? acceptedStartedAt = null) =>
        ObserveExitAsync(
            new SessionRunnerExitedEvent(sessionId, exitCode, exitReason, 0, acceptedStartedAt), ct);

    public async Task<SessionExitDisposition> ObserveExitAsync(
        SessionRunnerExitedEvent evt, CancellationToken ct)
    {
        var (disposition, observation) = await CloseSessionOnExitAsync(evt, ct);
        if (disposition == SessionExitDisposition.Applied)
        {
            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(evt.SessionId),
                "SessionExited",
                new
                {
                    sessionId = evt.SessionId,
                    status = "Exited",
                    exitCode = observation!.ExitCode,
                    exitSignal = observation!.ExitSignal,
                    exitStopIntent = observation!.ExitStopIntent,
                    exitReason = evt.ExitReason.ToString(),
                    acceptedStartedAt = evt.AcceptedStartedAt,
                },
                ct);
        }

        return disposition;
    }

    // A runner exit must also land in the DATABASE, not just the SignalR group: the session row is
    // what the UI's liveSession/agent-status derive from. Before this, an exit that arrived via the
    // event pump left the session Running and its agent Working forever — a phantom with no process.
    // Sessions already closed by another path keep their lifecycle and source while missing
    // actual observation fields can still be enriched. Card-owned agents are skipped; their lifecycle belongs to the orchestrator.
    private async Task<(SessionExitDisposition Disposition, AgentExitObservation? Observation)> CloseSessionOnExitAsync(
        SessionRunnerExitedEvent evt, CancellationToken ct)
    {
        var sessionId = evt.SessionId;
        var exitCode = evt.ExitCode;
        var exitReason = evt.ExitReason;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var session = await db.AgentSessions.FromSqlInterpolated(
                $"""SELECT * FROM "AgentSessions" WHERE "Id" = {sessionId} FOR UPDATE""").FirstOrDefaultAsync(ct);
            if (session is null)
                return (SessionExitDisposition.Unknown, null);

            if (evt.AcceptedStartedAt is null)
            {
                await ReportLegacyExitOnceAsync(sessionId, ct);
                return (SessionExitDisposition.Missing, null);
            }

            var cmp = SessionGeneration.Compare(evt.AcceptedStartedAt.Value, session.StartedAt);
            if (cmp < 0)
                return (SessionExitDisposition.Stale, null);
            if (cmp > 0)
                return (SessionExitDisposition.Unknown, null);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var changed = false;
            var previousCode = session.ExitCode;
            var previousSignal = session.ExitSignal;
            var previousIntent = session.ExitStopIntentJson;
            if (!SessionExitEvidence.Apply(session, exitCode, evt.ExitSignal, evt.ExitStopIntent))
                _logger.LogWarning("Conflicting exit observation for session {SessionId}; retaining first evidence", sessionId);
            changed |= previousCode != session.ExitCode || previousSignal != session.ExitSignal || previousIntent != session.ExitStopIntentJson;
            var oldHerdrEvidence = session.HerdrSupervisionFailureKind;
            HerdrSupervisionFailureEvidence.Record(session, HerdrSupervisionFailureEvidence.FromExitReason(exitReason));
            changed |= oldHerdrEvidence != session.HerdrSupervisionFailureKind;
            // A CPU-spin watchdog kill reclaims an IDLE session whose process was busy-looping a
            // core after its turn completed — the non-zero exit code is just the kill's. It must
            // land as Stopped, not Failed, so the next message resumes the session by id.
            // CARD-0186: herdr pane-closed / presumed-dead / child-gone / pane-left-open, and
            // CARD-0383 detect-timeout keep-pane, are never a clean stop — Stopped is operator
            // intent and reconciliation's auto-kill key.
            var cleanStop = SessionExitPolicy.IsCleanStop(session.ExitCode, session.ExitSignal, exitReason,
                session.TerminationSource);
            if (session.Status is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping)
            {
                session.Status = cleanStop ? SessionStatus.Stopped : SessionStatus.Failed;
                // Actual code and optional signal were merged under the generation fence above.
                if (session.Status == SessionStatus.Failed)
                    session.FailureReason = $"Process exited ({exitReason}, code {session.ExitCode?.ToString() ?? "unknown"}). "
                        + SessionExitPolicy.Describe(session.ExitCode, session.ExitSignal, session.TerminationSource,
                            SessionExitEvidence.ReadIntent(session.ExitStopIntentJson));
                // A prior KillAsync may already have persisted OperatorRequest/SystemRequest;
                // an exit event must not erase it (CARD-0256).
                SessionTermination.Record(session, SessionTermination.FromExitReason(exitReason, session.ExitSignal, SessionExitEvidence.ReadIntent(session.ExitStopIntentJson)));
                session.EndedAt ??= now;
                session.LastSeenAt = now;
                changed = true;
            }
            else if (session.Status is SessionStatus.Stopped or SessionStatus.Failed
                     && session.TerminationSource == SessionTerminationSource.Unknown)
            {
                // Race C.1: a closer wrote the terminal status first and left the column empty.
                // The exit event is the backfill path; it must not change the already-closed status.
                SessionTermination.Record(session, SessionTermination.FromExitReason(exitReason, session.ExitSignal, SessionExitEvidence.ReadIntent(session.ExitStopIntentJson)));
                session.ExitCode ??= exitCode;
                changed = true;
            }

            Guid? changedAgentId = null;
            var sessionIdText = sessionId.ToString("D");
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.PersistentSessionId == sessionIdText, ct);
            if (agent is not null)
            {
                await db.Entry(agent).ReloadAsync(ct);
                if (!string.Equals(agent.PersistentSessionId, sessionIdText, StringComparison.OrdinalIgnoreCase))
                    agent = null;
            }

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
            else if (exitReason == AgentExitReason.HerdrLaunchDetectTimeout)
            {
                // CARD-0383: idle-shell detect-timeout keep-pane. Visibility is the Failed
                // row + FailureReason naming this enum; a HerdrPaneLeftOpen incident would tell
                // the operator to close the pane and recreate the original bug. No new incident
                // kind — AlwaysOn retries Failed, and closing the pane is the wrong action.
                _logger.LogWarning(
                    "Session {SessionId} herdr launch detect timeout; pane kept as last-pane for in-place relaunch (not a foreign process).",
                    sessionId);
            }

            if (changed)
                await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            // CARD-0664 D-2/D-3: this generation has ended (whoever closed it); release after the commit.
            if (session.Status is SessionStatus.Stopped or SessionStatus.Failed
                && scope.ServiceProvider.GetService<WorkspaceUseAdmission>() is { } admission)
                await admission.ReleaseSessionConsumersAsync(sessionId, CancellationToken.None);
            if (changedAgentId is Guid agentId)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
            return (SessionExitDisposition.Applied, new(session.ExitCode, session.ExitSignal,
                SessionExitEvidence.ReadIntent(session.ExitStopIntentJson)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist exit of session {SessionId}", sessionId);
            return (SessionExitDisposition.PersistenceFailed, null);
        }
    }

    private async Task ReportLegacyExitOnceAsync(Guid sessionId, CancellationToken ct)
    {
        if (!_generationCompat.TryReport(sessionId))
            return;

        _logger.LogWarning(
            "Session {SessionId} produced a legacy exit without an accepted generation; automatic close is declined.",
            sessionId);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var alerts = scope.ServiceProvider.GetService<IAlertService>();
            if (alerts is null)
                return;
            await alerts.RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning,
                    Source: "runtime",
                    Title: "Legacy session exit without generation identity",
                    Detail: $"Session {sessionId} produced an exit event without an accepted generation. Automatic close is declined.",
                    DedupKey: $"compat:generation:{sessionId}",
                    SessionId: sessionId),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Legacy generation compatibility alert failed for session {SessionId}", sessionId);
        }
    }

    /// <summary>Commits the ordered transcript and its effects without awaiting delivery.</summary>
    public async Task ObserveTranscriptAsync(SessionRunnerTranscriptEvent entry, CancellationToken ct)
    {
        await PersistTranscriptAsync(entry.SessionId, [entry], ct);
    }

    internal async Task ExecuteActionPhaseAsync(SessionRuntimeAction action, TranscriptEntry? row, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var sessionId = action.AgentSessionId;
        var kind = row?.Kind;
        var route = RoutesReplies(action, row);
        switch (action.Phase)
        {
            case 0 when row is not null:
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "transcript.signalr-publish"))
                    await _eventBus.PublishToGroupAsync(AgentSessionGroups.Session(sessionId), "SessionTranscript",
                        ToTranscriptPayload(SessionRuntimeActionCoordinator.RestoreTranscriptPayload(row, action)), ct);
                break;
            case 1 when row is not null && (kind == TranscriptKinds.UserPrompt
                || kind == TranscriptKinds.QueuedUserPrompt || kind == TranscriptKinds.TurnEnd):
                if (services.GetService<CapacityRecoveryService>() is { } capacity)
                    await capacity.ObserveTranscriptAsync(
                        sessionId, kind, row.IsApiError == true, ct, row.Sequence, row.StopReason);
                break;
            case 2 when kind == TranscriptKinds.UserPrompt || action.TriggerKind == "catchup":
                if (row is not null && kind == TranscriptKinds.UserPrompt)
                    await RearmInterruptedAttemptAsync(services, sessionId, row, ct);
                if (services.GetService<SessionMessageQueueService>() is { } refinementQueue)
                    await refinementQueue.ConfirmSentRefinementsAsync(sessionId, ct);
                break;
            case 2 when kind == TranscriptKinds.TurnEnd && row is not null:
                await EndRearmedTurnAsync(services, sessionId, ct);
                break;
            case 3 when kind == TranscriptKinds.CompactBoundary:
                if (services.GetService<CompactionRecoveryService>() is { } recovery)
                    await recovery.OnCompactBoundaryAsync(sessionId, action.Sequence, ct);
                break;
            case 3 when action.TriggerKind == "catchup":
                if (services.GetService<GrokRulesRefreshService>() is { } rules)
                    await rules.ReconcileAsync(sessionId, ct);
                break;
            case SessionRuntimeActionCoordinator.ChannelReplyPhase when route:
                await ExecuteChannelReplyAsync(services, sessionId, action.Sequence, ct);
                break;
            case 5 when route:
                if (services.GetService<ReviewReplyDispatcher>() is { } review)
                    await review.OnTranscriptActionAsync(sessionId, action.Sequence, ct);
                break;
            case 6 when route || action.TriggerKind == "catchup":
                if (services.GetService<AgentTaskReplyService>() is { } task)
                {
                    await task.OnTranscriptActionAsync(action, ct);
                }
                break;
        }
    }

    /// <summary>Whether this action routes its turn to the channel, review and task replies.</summary>
    internal static bool RoutesReplies(SessionRuntimeAction action, TranscriptEntry? row) =>
        action.TurnBoundary || row?.Kind == TranscriptKinds.AssistantText;

    internal async Task ExecuteActionChannelReplyAsync(Guid sessionId, long sequence, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await ExecuteChannelReplyAsync(scope.ServiceProvider, sessionId, sequence, ct);
    }

    /// <summary>CARD-1434: the destination the route action at <paramref name="sequence"/> publishes to; null when nothing publishes.</summary>
    internal async Task<ChannelReplyRoute?> ResolveActionChannelRouteAsync(Guid sessionId, long sequence, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return scope.ServiceProvider.GetService<ChannelReplyDispatcher>() is { } channel
            ? await channel.ResolveActionRouteAsync(sessionId, sequence, ct) : null;
    }

    /// <summary>CARD-1434: true when the debt row at <paramref name="sequence"/> reached the give-up bound and was reported lost.</summary>
    internal async Task<bool> TryAbandonExhaustedChannelDebtAsync(Guid sessionId, long sequence,
        IReadOnlyCollection<Guid> lateConfirmedIds, ChannelReplyDebtState state, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return scope.ServiceProvider.GetService<ChannelReplyDispatcher>() is { } channel
            && await channel.TryAbandonExhaustedDebtAsync(sessionId, sequence, lateConfirmedIds, state, ct);
    }

    private static async Task ExecuteChannelReplyAsync(
        IServiceProvider services, Guid sessionId, long sequence, CancellationToken ct)
    {
        if (services.GetService<ChannelReplyDispatcher>() is { } channel)
            await channel.OnTranscriptActionAsync(sessionId, sequence, ct);
    }

    private async Task RearmInterruptedAttemptAsync(
        IServiceProvider services, Guid sessionId, TranscriptEntry row, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var running = await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
        if (!running)
            return;

        var attempt = await db.RunAttempts
            .Include(a => a.Card)
            .Where(a => a.AgentSessionId == sessionId && a.CompletedAt == null)
            .OrderByDescending(a => a.AttemptNumber)
            .ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (attempt?.Card is not { } card
            || attempt.Phase is not (RunPhase.AwaitingInput or RunPhase.StreamingTurn))
            return;

        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId
                && e.CreatedAt >= attempt.PhaseStartedAt
                && (e.Kind == TranscriptKinds.UserPrompt || e.Kind == TranscriptKinds.QueuedUserPrompt))
            .Select(e => new { e.Kind, e.Text })
            .ToListAsync(ct);
        var names = prompts
            .Select(p => TranscriptKinds.TryReadLocalCommandName(p.Kind, p.Text))
            .OfType<string>()
            .ToList();
        var provisional = RunAttemptStateMachine.IsProvisionalStreamingTurn(attempt);
        if (provisional
            && prompts.Count > 0
            && prompts.All(p => IsCommandHousekeeping(p.Kind, p.Text, names)))
        {
            RunAttemptStateMachine.RestoreProvisionalAwaitingInput(attempt);
            await db.SaveChangesAsync(ct);
            await PublishInterruptedAttemptAsync(card, sessionId, attempt);
            return;
        }

        if (TranscriptKinds.IsInterruptPrompt(row.Kind, row.Text))
        {
            // A marker saved before this re-arm already ran: stored sequences only increase,
            // and one session's actions run in that order.
            if (RunAttemptStateMachine.IsRearmedStreamingTurn(attempt))
                await EndRearmedStreamingTurnAsync(db, card, sessionId, attempt, ct);
            return;
        }

        // Drop only the launch prompt: its sequence is at or below the ceiling recorded
        // when StartAsync entered AwaitingInput. A provisional slash arm does not remember
        // a re-arm sequence, and the interrupt or TurnEnd that ends it moves PhaseStartedAt
        // to the processing clock. A follow-up saved in that same burst is above the ceiling
        // and still re-arms. An attempt with no ceiling keeps the save-clock drop until a
        // real re-arm is remembered.
        if (IsLaunchPrompt(attempt, row))
            return;
        if (attempt.Phase == RunPhase.StreamingTurn && row.CreatedAt <= attempt.LastEventAt)
            return;
        if (IsCommandHousekeeping(row.Kind, row.Text, names)
            || TranscriptKinds.IsCompactionContinuationPrompt(row.Kind, row.Text)
            || TranscriptKinds.IsTaskNotificationPrompt(row.Kind, row.Text))
            return;

        var now = UtcNow();
        if (attempt.Phase == RunPhase.AwaitingInput)
        {
            if (row.Text?.Trim().StartsWith('/') == true)
                RunAttemptStateMachine.ArmProvisionalStreamingTurn(attempt, now);
            else
            {
                RunAttemptStateMachine.Transition(attempt, RunPhase.StreamingTurn, now);
                RunAttemptStateMachine.RememberRearmPrompt(attempt, row.Sequence);
            }

            await db.SaveChangesAsync(ct);
            await PublishInterruptedAttemptAsync(card, sessionId, attempt);
            return;
        }

        if (provisional)
        {
            attempt.LastEventAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task EndRearmedTurnAsync(
        IServiceProvider services, Guid sessionId, CancellationToken ct)
    {
        // Any stop reason ends the current re-armed turn. Master does not kill a
        // finished turn that stopped for error, max_tokens, stop_sequence, refusal, or null.
        var db = services.GetRequiredService<AppDbContext>();
        var running = await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
        if (!running)
            return;

        var attempt = await db.RunAttempts
            .Include(a => a.Card)
            .Where(a => a.AgentSessionId == sessionId && a.CompletedAt == null)
            .OrderByDescending(a => a.AttemptNumber)
            .ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        // Stored sequences only increase and this session's actions run in that order,
        // so a TurnEnd from before the re-arm was already applied. Comparing Sequence
        // with the re-arming prompt never sees that row.
        if (attempt?.Card is not { } card
            || !RunAttemptStateMachine.IsRearmedStreamingTurn(attempt))
            return;

        await EndRearmedStreamingTurnAsync(db, card, sessionId, attempt, ct);
    }

    private async Task EndRearmedStreamingTurnAsync(
        AppDbContext db, Card card, Guid sessionId, RunAttempt attempt, CancellationToken ct)
    {
        RunAttemptStateMachine.Transition(attempt, RunPhase.AwaitingInput, UtcNow());
        await db.SaveChangesAsync(ct);
        await PublishInterruptedAttemptAsync(card, sessionId, attempt);
    }

    private static bool IsLaunchPrompt(RunAttempt attempt, TranscriptEntry row)
    {
        if (RunAttemptStateMachine.TryGetLaunchPromptCeiling(attempt, out var ceiling))
            return row.Sequence <= ceiling;

        return RunAttemptStateMachine.RearmPromptSequence(attempt) == 0
            && row.CreatedAt <= attempt.PhaseStartedAt;
    }

    private static bool IsCommandHousekeeping(
        string? kind, string? text, IReadOnlyCollection<string> names) =>
        TranscriptKinds.IsLocalCommandRecord(kind, text)
        || TranscriptKinds.IsRawLocalCommandEcho(kind, text, names);

    private async Task PublishInterruptedAttemptAsync(Card card, Guid sessionId, RunAttempt attempt)
    {
        var payload = new
        {
            boardId = card.BoardId,
            cardId = card.Id,
            sessionId,
            runAttemptId = attempt.Id,
            phase = attempt.Phase.ToString()
        };
        await _eventBus.PublishToAllAsync("RunAttemptChanged", payload);
        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id });
    }

    internal async Task<SessionQueueTurnEndResult> ExecuteActionQueueAsync(SessionRuntimeAction action,
        bool superseded, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetService<SessionMessageQueueService>();
        return queue is null ? new(new HashSet<Guid>(), new HashSet<Guid>())
            : await queue.OnRuntimeActionAsync(action, superseded, ct);
    }

    internal async Task ExecuteActionLateConfirmAsync(SessionRuntimeAction action, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var ids = action.LateConfirmedChannelMessageIds.ToHashSet();
        if (ids.Count > 0
            && scope.ServiceProvider.GetService<ChannelReplyDispatcher>() is { } channel)
        {
            var dispatched = await channel.OnLateConfirmedAsync(action.AgentSessionId, ids, ct);
            await WarnOnUnpublishedLateConfirmedChannelRepliesAsync(action.AgentSessionId,
                ids, dispatched, ct);
            if (dispatched.FailedCorrelationIds.Count > 0)
                throw new InvalidOperationException("Late-confirmed channel publication remains pending.");
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
        await Actions().RequestAsync(evt.SessionId, "status", ct);
    }

    /// <summary>A transcript entry that means "the agent stopped and is waiting" (see the callers of
    /// <see cref="FlushQueueOnIdleAsync"/> for why the interrupt marker counts).</summary>
    internal static bool IsTurnBoundary(SessionRunnerTranscriptEvent entry) =>
        (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == TranscriptKinds.StopReasons.EndTurn)
        // Grok's Esc interrupt is an EXPLICIT turn_completed with stop_reason "cancelled"
        // (measured 1.0.5, CARD-0080 S1) — the structured analog of Claude's "[Request
        // interrupted…" marker below, and like it, the queue must flush on it or every WhenIdle
        // delivery strands until some later turn completes. Claude's API never emits "cancelled"
        // as a stop_reason, so this arm cannot change Claude behaviour. CARD-0159: this stays
        // an idle boundary (the queue must flush) but is never a report boundary.
        || (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == TranscriptKinds.StopReasons.Cancelled)
        // Grok's API-error TurnEnd (CARD-0281): stop_reason "error" with IsApiError stamped on
        // the row. Claude never emits it (stubs are stop_sequence); Codex synthesizes end_turn.
        // Without this arm the dispatchers wait for the next 60s sweep and the channel user
        // hears nothing for 30 minutes.
        || (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == TranscriptKinds.StopReasons.Error)
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

    private async Task DispatchCapacityRecoveryTranscriptAsync(
        SessionRunnerTranscriptEvent entry, long sequence, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var recovery = scope.ServiceProvider.GetService<CapacityRecoveryService>();
            if (recovery is not null)
                await recovery.ObserveTranscriptAsync(
                    entry.SessionId, entry.Kind, entry.IsApiError == true, ct, sequence, entry.StopReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Capacity recovery transcript dispatch failed for session {SessionId}", entry.SessionId);
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
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "routing.channel"))
                await channelReplies.OnTurnEndAsync(sessionId, ct);
            var reviewReplies = scope.ServiceProvider.GetService<ReviewReplyDispatcher>();
            if (reviewReplies is not null)
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "routing.review"))
                await reviewReplies.OnTurnEndAsync(sessionId, ct);

            // Same reason as the channel dispatcher above: Claude can write the turn's stop marker
            // BEFORE its report text, so a delegate's task would stay Working forever if only the
            // TurnEnd triggered settlement. The text's own arrival re-triggers it (no-op otherwise).
            var taskReplies = scope.ServiceProvider.GetService<AgentTaskReplyService>();
            if (taskReplies is not null)
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "routing.task-settlement"))
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
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "routing.channel"))
                await channelReplies.OnTurnEndAsync(sessionId, ct);

            // Review-thread replies land the same way channel replies do — before the queue
            // injects the next prompt, so extraction sees the finished turn intact.
            var reviewReplies = scope.ServiceProvider.GetService<ReviewReplyDispatcher>();
            if (reviewReplies is not null)
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "routing.review"))
                await reviewReplies.OnTurnEndAsync(sessionId, ct);

            // A delegate's finished turn IS its report — settle the task and deliver the note to
            // its parent before the queue injects anything else into this session.
            var taskReplies = scope.ServiceProvider.GetService<AgentTaskReplyService>();
            if (taskReplies is not null)
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "routing.task-settlement"))
                await taskReplies.OnTurnEndAsync(sessionId, ct);

            var queue = scope.ServiceProvider.GetService<SessionMessageQueueService>();
            if (queue is not null)
            {
                SessionQueueTurnEndResult queueResult;
                using (new RuntimePhase(_logger, _timeProvider, sessionId, "queue.turn-end-confirm"))
                    queueResult = await queue.OnTurnEndAsync(sessionId, ct);
                if (channelReplies is not null && queueResult.LateConfirmedChannelMessageIds.Count > 0)
                {
                    // The first pass intentionally runs before queue delivery. A row promoted from
                    // Pending to Sent by the queue afterwards was therefore invisible to it; run
                    // the same durable dispatcher once more, now that this exact row is eligible.
                    var dispatchResult = await channelReplies.OnLateConfirmedAsync(
                        sessionId, queueResult.LateConfirmedChannelMessageIds, ct);
                    await WarnOnUnpublishedLateConfirmedChannelRepliesAsync(
                        sessionId, queueResult.LateConfirmedChannelMessageIds, dispatchResult, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to flush message queue on idle for session {SessionId}", sessionId);
        }
    }

    private async Task WarnOnUnpublishedLateConfirmedChannelRepliesAsync(
        Guid sessionId,
        IReadOnlySet<Guid> lateConfirmedChannelMessageIds,
        ChannelReplyDispatchResult dispatchResult,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.SessionQueuedMessages
            .AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && lateConfirmedChannelMessageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ConversationKey, m.ChannelReplySettledAt })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var outcome = dispatchResult.OutcomeFor(row.Id);
            if (outcome is ChannelReplyDispatchOutcome.Published
                or ChannelReplyDispatchOutcome.Deferred
                or ChannelReplyDispatchOutcome.IntentionallyWithheld
                || row.ChannelReplySettledAt is not null)
            {
                continue;
            }

            _logger.LogWarning(
                "Late-confirmed channel reply was not published after recovery dispatch: session {SessionId}, "
                + "queue message/correlation {QueueMessageId}, conversation {ConversationKey}, dispatch outcome "
                + "{DispatchOutcome}. The correlation remains owed for the existing loss/TTL path unless durable preparation owns it.",
                sessionId, row.Id, row.ConversationKey, outcome);
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
    public async Task<bool> CatchUpTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        (await PullAndPersistAsync(sessionId, ct)).Persisted.LastStoredSeq is not null;

    /// <summary>
    /// Stores the newest Codex <c>token_count</c> time on the session. A null observation does
    /// not clear a previous value: a missed pull is unknown, not silence. A stamp failure is
    /// logged and swallowed so it is not reported as a failed transcript pull.
    /// </summary>
    internal async Task StampCodexTokenCountAsync(Guid sessionId, DateTime? observedUtc, CancellationToken ct)
    {
        if (observedUtc is not DateTime at)
            return;
        var normalized = SessionGeneration.Normalize(at);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AgentSessions
                .Where(s => s.Id == sessionId && (s.CodexTokenCountAt == null || s.CodexTokenCountAt < normalized))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CodexTokenCountAt, normalized), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Codex token_count heartbeat was not stored for session {SessionId}", sessionId);
        }
    }

    // The one pull shared by catch-up and the receipt observation. Pulled is false when the runner
    // pull itself failed; a swallowed persist failure is reported through NeedsReload/_persistFailures.
    private async Task<(bool Pulled, PersistResult Persisted)> PullAndPersistAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            var snapshot = await _runnerClient.GetTranscriptAsync(sessionId, ct);
            await StampCodexTokenCountAsync(sessionId, snapshot.CodexTokenCountAt, ct);
            return (true, await PersistTranscriptAsync(sessionId, snapshot.Entries, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not live in the runner, or the runner is unreachable: the caller falls back to
            // whatever the stream has already stored, which is exactly today's behaviour.
            _logger.LogDebug(ex, "Transcript catch-up skipped for session {SessionId}", sessionId);
            return (false, PersistResult.Empty);
        }
    }

    /// <summary>
    /// CARD-1205 checked evidence recovery. T10..T16 in WorkingTranscriptRecoveryTests
    /// bind generation, frontier, busy-tail, failure and commit-gate outcomes.
    /// Scheduling and completion-note wakeups belong to the caller after this method returns.
    /// </summary>
    public async Task<TranscriptRecoveryResult> ProbeWorkingTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        var started = _timeProvider.GetTimestamp();
        RecoveryObservation? before = null;
        DateTime? generation = null;
        long postSequence = 0, addedCount = 0;
        TranscriptRecoveryResult Result(TranscriptRecoveryOutcome outcome, string reason) => new(
            outcome, reason, generation, before?.Rows.LastOrDefault()?.Sequence ?? 0,
            postSequence, addedCount, _timeProvider.GetElapsedTime(started));

        if (_states is null) return Result(TranscriptRecoveryOutcome.Unknown, "store_absent");
        try
        {
            SessionStateSnapshot stamp;
            using (var lease = await _states.BeginWriteAsync(sessionId, ct))
            {
                before = await ReadRecoveryObservationAsync(sessionId, ct);
                if (before is null || !before.LocalRunning)
                    return Result(TranscriptRecoveryOutcome.Unknown, "ownership_unavailable");
                generation = before.Generation;
                postSequence = before.Rows.LastOrDefault()?.Sequence ?? 0;
                stamp = lease.Snapshot;
            }

            // T13 commits through ordinary ingestion while this HTTP pull is held.
            var snapshot = await _runnerClient.GetTranscriptAsync(sessionId, ct);
            ct.ThrowIfCancellationRequested();
            if (snapshot.SessionId != sessionId || snapshot.Entries.Any(e => e.SessionId != sessionId))
                return Result(TranscriptRecoveryOutcome.Unknown, "session_mismatch");
            if (snapshot.AcceptedStartedAt is null || !SessionGeneration.Equal(snapshot.AcceptedStartedAt, generation))
                return Result(TranscriptRecoveryOutcome.Unknown, "generation_mismatch");
            if (snapshot.Entries.Count == 0 || snapshot.LastSequence != snapshot.Entries[^1].Sequence)
                return Result(TranscriptRecoveryOutcome.Unknown, "incomplete_snapshot");

            // T13 protects the whole revision, including annotations. T16 proves runner
            // coverage can omit those server-only annotation rows.
            var frontier = before.Rows.LastOrDefault(row => RecoveryActivity(sessionId, row));
            if (frontier is not null && !snapshot.Entries.Any(e => RecoveryIdentityMatches(frontier, e)))
                return Result(TranscriptRecoveryOutcome.Unknown, "frontier_missing");
            if (frontier is null)
                return Result(TranscriptRecoveryOutcome.Unknown, "activity_frontier_absent");

            // T14/T16 classify returned source order independently of rebased durable sequences.
            var runnerState = new SessionStateSnapshot(sessionId).Append(snapshot.Entries.Select((e, index) =>
                new TranscriptEntry { AgentSessionId = sessionId, Sequence = index + 1, Kind = e.Kind,
                    Text = e.Text, Timestamp = e.Timestamp is { } time ? SessionGeneration.Normalize(time.UtcDateTime) : null }));

            using (var lease = await _states.BeginWriteAsync(sessionId, ct))
            {
                var current = await ReadRecoveryObservationAsync(sessionId, ct);
                postSequence = current?.Rows.LastOrDefault()?.Sequence ?? postSequence;
                if (current is null || !current.LocalRunning || !SessionGeneration.Equal(current.Generation, generation)
                    || current.RunnerId != before.RunnerId || current.RunnerStoreId != before.RunnerStoreId
                    || current.RunnerCwd != before.RunnerCwd)
                    return Result(TranscriptRecoveryOutcome.Unknown, "ownership_changed");
                if (lease.Snapshot.ServerEpoch != stamp.ServerEpoch || lease.Snapshot.Revision != stamp.Revision
                    || lease.Snapshot.ResetEpoch != stamp.ResetEpoch || !current.Rows.SequenceEqual(before.Rows))
                    return Result(TranscriptRecoveryOutcome.Unknown, "frontier_changed");
                ct.ThrowIfCancellationRequested();
                if (runnerState.Working)
                    return TryGetTranscriptPersistFailure(sessionId, out _)
                        ? Result(TranscriptRecoveryOutcome.Unknown, "persist_failure_retained")
                        : Result(TranscriptRecoveryOutcome.CheckedWorking, "runner_working");

                var persisted = await PersistTranscriptCoreAsync(sessionId, snapshot.Entries, before, ct: ct);
                if (persisted.RecoveryFenceRejection is { } rejection)
                {
                    _logger.LogWarning("Checked transcript recovery fence rejected session {SessionId}, generation {Generation}, reason {Reason}",
                        sessionId, generation, rejection);
                    return Result(TranscriptRecoveryOutcome.Unknown, rejection);
                }
                addedCount = persisted.CommittedRows.Count;
                postSequence = persisted.LastStoredSeq ?? postSequence;
                await PublishCommittedAsync(lease, persisted);
                ct.ThrowIfCancellationRequested();

                // T10 covers both cache modes; T11 faults the fresh SQL verdict. T15 checks
                // publication under the same lease used for validation and persistence.
                var after = await ReadRecoveryObservationAsync(sessionId, ct);
                if (after is not null)
                {
                    var previousIds = before.Rows.Select(row => row.Id).ToHashSet();
                    addedCount = after.Rows.LongCount(row => !previousIds.Contains(row.Id));
                    postSequence = after.Rows.LastOrDefault()?.Sequence ?? postSequence;
                }
                if (persisted.NeedsReload || TryGetTranscriptPersistFailure(sessionId, out _))
                    return Result(TranscriptRecoveryOutcome.Unknown, "persist_uncertain");
                if (after is null || !after.LocalRunning || !SessionGeneration.Equal(after.Generation, generation))
                    return Result(TranscriptRecoveryOutcome.Unknown, "ownership_changed_after_persist");
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
                    return Result(TranscriptRecoveryOutcome.Unknown, "committed_working");
                if (_states.Enabled && lease.Snapshot.Working)
                    return Result(TranscriptRecoveryOutcome.Unknown, "publication_working");
                return Result(TranscriptRecoveryOutcome.RecoveredIdle, "committed_idle");
            }
        }
        catch (OperationCanceledException)
        {
            return Result(TranscriptRecoveryOutcome.Unknown, "cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checked transcript recovery unavailable for session {SessionId}", sessionId);
            return Result(TranscriptRecoveryOutcome.Unknown, "observation_failed");
        }
    }

    private sealed record RecoveryRow(Guid Id, long Sequence, string Kind, string? Uuid, string? ParentUuid,
        DateTime? Timestamp, string? Role, string? Text, string? ToolName, string? ToolInput,
        string? ToolUseId, bool? ToolIsError, string? StopReason, string? ApiCallId, int? InputTokens, int? OutputTokens,
        int? CacheReadTokens, int? CacheCreationTokens, bool? IsApiError, string? ApiErrorClass, int? ApiErrorStatus,
        string? ApiErrorTimeZoneId, string? Model, int? ModelCalls, DateTime CreatedAt);

    private sealed record RecoveryObservation(DateTime Generation, SessionStatus Status, string? RunnerId,
        Guid? RunnerStoreId, string? RunnerCwd, RecoveryRow[] Rows)
    {
        public bool LocalRunning => Status == SessionStatus.Running && Generation != default
            && (string.IsNullOrWhiteSpace(RunnerId) || RunnerId == PhoneHomeProtocol.LocalRunnerId)
            && RunnerStoreId is null && RunnerCwd is null;
    }

    private async Task<RecoveryObservation?> ReadRecoveryObservationAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ReadRecoveryObservationAsync(db, sessionId, ct);
    }

    private static async Task<RecoveryObservation?> ReadRecoveryObservationAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => new { s.StartedAt, s.Status, s.RunnerId, s.RunnerStoreId, s.RunnerCwd }).SingleOrDefaultAsync(ct);
        if (session is null) return null;
        var rows = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == sessionId)
            .OrderBy(t => t.Sequence).Select(t => new RecoveryRow(t.Id, t.Sequence, t.Kind, t.Uuid, t.ParentUuid,
                t.Timestamp, t.Role, t.Text, t.ToolName, t.ToolInput, t.ToolUseId, t.ToolIsError, t.StopReason,
                t.ApiCallId, t.InputTokens, t.OutputTokens, t.CacheReadTokens, t.CacheCreationTokens, t.IsApiError,
                t.ApiErrorClass, t.ApiErrorStatus, t.ApiErrorTimeZoneId, t.Model, t.ModelCalls, t.CreatedAt))
            .ToArrayAsync(ct);
        return new(session.StartedAt, session.Status, session.RunnerId, session.RunnerStoreId, session.RunnerCwd, rows);
    }

    private static bool RecoveryActivity(Guid sessionId, RecoveryRow row) => new SessionStateSnapshot(sessionId)
        .Append([new TranscriptEntry { AgentSessionId = sessionId, Sequence = 1, Kind = row.Kind,
            Text = row.Text, Timestamp = row.Timestamp }]).HasActivity;

    private static bool RecoveryIdentityMatches(RecoveryRow row, SessionRunnerTranscriptEvent e) =>
        row.Uuid is not null ? row.Uuid == e.Uuid && row.Kind == e.Kind : e.Uuid is null
            && row.Sequence == e.Sequence && row.Kind == e.Kind && row.ParentUuid == e.ParentUuid
            && row.Timestamp == (e.Timestamp is { } time ? SessionGeneration.Normalize(time.UtcDateTime) : null)
            && row.Role == e.Role && row.Text == e.Text && row.ToolName == e.ToolName && row.ToolInput == e.ToolInput
            && row.ToolUseId == e.ToolUseId && row.ToolIsError == e.ToolIsError && row.StopReason == e.StopReason
            && row.ApiCallId == e.ApiCallId && row.InputTokens == e.InputTokens && row.OutputTokens == e.OutputTokens
            && row.CacheReadTokens == e.CacheReadTokens && row.CacheCreationTokens == e.CacheCreationTokens
            && row.IsApiError == e.IsApiError && row.ApiErrorClass == e.ApiErrorClass && row.ApiErrorStatus == e.ApiErrorStatus
            && row.ApiErrorTimeZoneId == e.ApiErrorTimeZoneId && row.Model == e.Model && row.ModelCalls == e.ModelCalls;

    /// <summary>
    /// CARD-1121 D-4 as amended by A-2: the same catch-up pull, then the committed state of this
    /// runtime's own store read after it. A failed pull is the expected answer only for a destination
    /// the caller found terminal; for any other destination it is unknown. A persist that needs a
    /// reload, a retained persist failure, a missing or disabled store and a failed state read are
    /// unknown too. Unknown never certifies anything; cancellation propagates.
    /// </summary>
    internal async Task<LandReceiptScanCache.Observation> CatchUpForReceiptAsync(
        Guid sessionId, bool destinationTerminal, CancellationToken ct)
    {
        var (pulled, persisted) = await PullAndPersistAsync(sessionId, ct);
        if (!pulled && !destinationTerminal) return LandReceiptScanCache.Observation.Unknown("pull_failed");
        if (persisted.NeedsReload) return LandReceiptScanCache.Observation.Unknown("persist_needs_reload");
        return await ObserveReceiptStateAsync(sessionId, ct);
    }

    /// <summary>
    /// CARD-1121: a serialized read of this runtime's store (no pull), used for the before and after
    /// stamps around a receipt scan. Unknown when the store is absent or disabled, the read faults, or
    /// a persist failure is retained for the session.
    /// </summary>
    internal async Task<LandReceiptScanCache.Observation> ObserveReceiptStateAsync(Guid sessionId, CancellationToken ct)
    {
        if (_states is null) return LandReceiptScanCache.Observation.Unknown("store_absent");
        if (!_states.Enabled) return LandReceiptScanCache.Observation.Unknown("store_disabled");
        SessionStateSnapshot snapshot;
        try
        {
            snapshot = await _states.ReadAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Receipt state observation unavailable for session {SessionId}", sessionId);
            return LandReceiptScanCache.Observation.Unknown("state_read_failed");
        }
        // Checked after the read so a failure recorded under the gate before it is never missed.
        return TryGetTranscriptPersistFailure(sessionId, out _)
            ? LandReceiptScanCache.Observation.Unknown("persist_failure_retained")
            : LandReceiptScanCache.Observation.Known(snapshot);
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
            await StampCodexTokenCountAsync(sessionId, snapshot.CodexTokenCountAt, ct);
            await PersistTranscriptAsync(sessionId, snapshot.Entries, ct);
            await Actions().RequestAsync(sessionId, "catchup", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Transcript sync skipped for session {SessionId}", sessionId);
        }
    }

    private SessionRuntimeActionCoordinator Actions()
    {
        using var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SessionRuntimeActionCoordinator>();
    }
    /// <summary>
    /// CARD-0288: if this session still has an open task, hand catch-up to settlement in its own
    /// try/catch so a throw in channel/review/queue flush cannot skip it. No-op when the reply
    /// service is unregistered (same as <see cref="FlushQueueOnIdleAsync"/>).
    /// </summary>
    private async Task ConfirmRefinementDeliveriesAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetService<SessionMessageQueueService>();
            if (queue is null)
                return;
            await queue.ConfirmSentRefinementsAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Refinement confirm failed for session {SessionId}", sessionId);
        }
    }

    private async Task TryCatchUpSettlementAsync(Guid sessionId, bool addedTurnBoundary, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasOpenTask = await db.AgentTasks.AnyAsync(
                t => t.AgentSessionId == sessionId
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working),
                ct);
            if (!hasOpenTask)
                return;

            var taskReplies = scope.ServiceProvider.GetService<AgentTaskReplyService>();
            if (taskReplies is null)
                return;

            _logger.LogInformation(
                "Catch-up settlement for session {SessionId} (AddedTurnBoundary={AddedTurnBoundary})",
                sessionId, addedTurnBoundary);
            using (new RuntimePhase(_logger, _timeProvider, sessionId, "routing.task-settlement"))
                await taskReplies.OnTurnEndAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Catch-up settlement failed for session {SessionId}", sessionId);
        }
    }

    // What one persist call actually changed — LastStoredSeq is the stored (session-monotonic)
    // sequence of the last NEWLY persisted entry, or null when everything deduped away or
    // persistence failed.
    internal sealed record PersistResult(
        long? LastStoredSeq, bool AddedTurnBoundary, bool AddedAssistantText, bool AddedManualCompactBoundary)
    {
        public static PersistResult Empty { get; } = new(null, false, false, false);
        public IReadOnlyList<TranscriptEntry> CommittedRows { get; init; } = [];
        public bool NeedsReload { get; init; }
        public DateTime? AcceptedGeneration { get; init; }
        public string? RecoveryFenceRejection { get; init; }
    }

    internal const string TranscriptStubPrefix = "[transcript text not persistable:";

    internal sealed record TranscriptPersistFailure(DateTime LastFailedAtUtc, int Failures, string Detail);

    internal bool TryGetTranscriptPersistFailure(Guid sessionId, out TranscriptPersistFailure failure) =>
        _persistFailures.TryGetValue(sessionId, out failure!);

    private void RecordTranscriptPersistFailure(Guid sessionId, string detail)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _persistFailures.AddOrUpdate(
            sessionId,
            _ => new TranscriptPersistFailure(now, 1, detail),
            (_, existing) => existing with { LastFailedAtUtc = now, Failures = existing.Failures + 1, Detail = detail });
    }

    internal async Task<PersistResult> PersistTranscriptAsync(Guid sessionId, IReadOnlyList<SessionRunnerTranscriptEvent> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return PersistResult.Empty;
        try
        {
            using var lease = await (_states ?? throw new InvalidOperationException("Transcript ingestion requires SessionStateStore.")).BeginWriteAsync(sessionId, ct);
            PersistResult result;
            try { result = await PersistTranscriptCoreAsync(sessionId, entries, ct: ct); }
            catch (OperationCanceledException)
            {
                // Cancellation after a database commit can obscure its outcome. Reload under
                // the same lease before another reader is allowed to observe this session.
                await lease.ReconcileAsync(false, CancellationToken.None);
                throw;
            }
            await PublishCommittedAsync(lease, result);
            Actions().Hint(sessionId);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordTranscriptPersistFailure(sessionId, PersistFailureDetail(ex));
            _logger.LogWarning(ex, "Transcript state unavailable for session {SessionId}", sessionId);
            return PersistResult.Empty;
        }
    }

    private static async Task PublishCommittedAsync(SessionStateStore.Lease? lease, PersistResult result)
    {
        if (lease is null) return;
        var previousCount = lease.Snapshot.Count;
        if (result.NeedsReload) await lease.ReconcileAsync(false, CancellationToken.None);
        else await lease.PublishAsync(result.CommittedRows, result.AcceptedGeneration, CancellationToken.None);
        lease.RecordIngest(previousCount, result.LastStoredSeq is null && result.AcceptedGeneration is not null && !result.NeedsReload);
    }

    /// <summary>The synthetic append shares serialization, persistence and publication with runner ingestion.</summary>
    internal async Task<bool> WriteRestartBoundaryIfInterruptedAsync(Guid sessionId, CancellationToken ct)
    {
        using var lease = await (_states ?? throw new InvalidOperationException("Transcript ingestion requires SessionStateStore.")).BeginWriteAsync(sessionId, ct);
        bool working;
        if (lease is not null) working = lease.Snapshot.Working;
        else
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            working = await SessionMessageQueueService.IsWorkingAsync(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(), sessionId, ct);
        }
        if (!working) return false;
        var boundary = new SessionRunnerTranscriptEvent(sessionId, 0, TranscriptKinds.SessionRestartBoundary,
            Guid.NewGuid().ToString("D"), null, _timeProvider.GetUtcNow(), "system",
            "Session relaunched; the previous turn had been interrupted mid-flight.", null, null, null, null, null);
        var result = await PersistTranscriptCoreAsync(sessionId, [boundary], ct: ct);
        await PublishCommittedAsync(lease, result);
        Actions().Hint(sessionId);
        return result.LastStoredSeq is not null;
    }

    private async Task<PersistResult> PersistTranscriptCoreAsync(Guid sessionId, IReadOnlyList<SessionRunnerTranscriptEvent> entries,
        RecoveryObservation? expectedRecovery = null, ReceiptLaunchStamp? expectedReceipt = null, CancellationToken ct = default)
    {
        using var observation = new RuntimePhase(_logger, _timeProvider, sessionId, "transcript.persist");
        if (entries.Count == 0)
            return PersistResult.Empty;

        var originalEntries = entries;
        entries = SanitizeEntries(entries);

        try
        {
            if (System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("Transcript ingestion must own its commit boundary.");
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Dedup by transcript line uuid, NOT by sequence: the runner tailer numbers entries per
            // tailer LIFETIME, restarting from 1 after a session relaunch or fork re-discovery. A
            // resumed session (same id, fresh tailer) therefore re-issues low sequences that collide
            // with last generation's rows — sequence-dedup silently dropped every new entry, which
            // killed reply routing after a relaunch (2026-07-23). Uuids come from the transcript
            // lines themselves, so they survive re-numbering, and history re-emitted on adoption
            // dedups cleanly. Entries without a uuid keep the old sequence-dedup.
            var incomingUuids = entries.Where(e => e.Uuid is not null).Select(e => e.Uuid!).ToHashSet();
            var incomingSeqs = entries.Where(e => e.Uuid is null).Select(e => e.Sequence).ToHashSet();
            // A catch-up snapshot can contain the whole history. Bound each index probe without
            // changing the (UUID, kind) identity or event order; one DbContext is used serially.
            // Chunk emits no batches for an empty set, so null-UUID-only input needs no UUID read.
            var uuidBatches = incomingUuids.Chunk(512).ToArray();

            // CARD-1403 S3: one command reads the session (FK safety: only persist for sessions the
            // DB actually knows about, which skips test/transient ids), the first UUID chunk, the
            // null-UUID sequences and the stored max. Each further UUID chunk is its own command.
            var prelude = await TranscriptIngestPrelude.ReadAsync(
                db, sessionId, uuidBatches.FirstOrDefault() ?? [], incomingSeqs, ct);
            if (prelude.StartedAt is not DateTime startedAt)
                return PersistResult.Empty with { RecoveryFenceRejection = expectedRecovery is null ? null : "ownership_changed_at_commit" };

            var seenUuids = prelude.Identities;
            foreach (var uuidBatch in uuidBatches.Skip(1))
            {
                var existing = await db.TranscriptEntries
                    .TagWith(TranscriptIngestPrelude.IdentityTag)
                    .Where(t => t.AgentSessionId == sessionId && t.Uuid != null && uuidBatch.Contains(t.Uuid!))
                    .Select(t => new { t.Uuid, t.Kind })
                    .ToListAsync(ct);
                seenUuids.UnionWith(existing.Select(x => (x.Uuid!, x.Kind)));
            }
            var seenSeqs = prelude.Sequences;

            // Stored sequences stay strictly monotonic per SESSION: a new tailer generation's
            // restarted numbering is rebased past the session's current max, so "latest turn"
            // queries (reply dispatcher, queue idle checks, UI ordering) keep working across
            // relaunches without schema changes.
            var maxSeq = prelude.MaxSequence ?? 0;

            var now = SessionGeneration.Normalize(_timeProvider.GetUtcNow().UtcDateTime);
            var pending = new List<(TranscriptEntry Row, SessionRunnerTranscriptEvent Source)>();
            for (var sourceIndex = 0; sourceIndex < entries.Count; sourceIndex++)
            {
                var e = entries[sourceIndex];
                if (e.Uuid is not null)
                {
                    if (!seenUuids.Add((e.Uuid, e.Kind)))
                        continue; // already persisted, or a duplicate within this batch
                }
                else if (!seenSeqs.Add(e.Sequence))
                {
                    continue;
                }

                var storedSeq = e.Sequence > maxSeq ? e.Sequence : maxSeq + 1;
                maxSeq = storedSeq;

                var row = new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = storedSeq,
                    Kind = e.Kind,
                    Uuid = e.Uuid,
                    ParentUuid = e.ParentUuid,
                    Timestamp = e.Timestamp is { } timestamp ? SessionGeneration.Normalize(timestamp.UtcDateTime) : null,
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
                    ApiErrorTimeZoneId = e.ApiErrorTimeZoneId,
                    Model = e.Model,
                    ModelCalls = e.ModelCalls,
                    CreatedAt = now,
                };
                db.TranscriptEntries.Add(row);
                pending.Add((row, originalEntries[sourceIndex]));
            }

            await AdmitActionsAsync(db, sessionId, pending, startedAt, ct);

            // Checked recovery owns one SQL fence through every batch/fallback save. The
            // lifecycle's UPDATE/DELETE conflicts with this row lock even when it does not
            // own the transcript lease. Order: optional queue lock -> state lease -> session
            // row lock; never Agents/registration locks or queue callbacks from here.
            // Prepare identities first, then revalidate inside this transaction before any
            // INSERT. Publication happens in the caller only after this transaction commits.
            await using var recoveryTransaction = expectedRecovery is null && expectedReceipt is null
                ? null : await db.Database.BeginTransactionAsync(ct);
            if (expectedRecovery is not null || expectedReceipt is not null)
                await SessionMessageQueueService.LockAgentSessionRowAsync(db, sessionId, expectedReceipt is null ? CancellationToken.None : ct);
            if (expectedReceipt is not null
                && await ReadReceiptLaunchStampAsync(db, sessionId, expectedReceipt.StandingAgentId, ct) != expectedReceipt)
                return PersistResult.Empty with { RecoveryFenceRejection = "ownership_changed_at_commit" };
            if (expectedRecovery is not null)
            {
                var fenced = await ReadRecoveryObservationAsync(db, sessionId, CancellationToken.None);
                if (fenced is null || !fenced.LocalRunning
                    || !SessionGeneration.Equal(fenced.Generation, expectedRecovery.Generation)
                    || fenced.RunnerId != expectedRecovery.RunnerId || fenced.RunnerStoreId != expectedRecovery.RunnerStoreId
                    || fenced.RunnerCwd != expectedRecovery.RunnerCwd)
                    return PersistResult.Empty with { RecoveryFenceRejection = "ownership_changed_at_commit" };
                if (!fenced.Rows.SequenceEqual(expectedRecovery.Rows))
                    return PersistResult.Empty with { RecoveryFenceRejection = "frontier_changed_at_commit" };
            }

            if (pending.Count == 0)
                return PersistResult.Empty with { AcceptedGeneration = startedAt };

            // Closure rows only. Ordinary batches do not take the session row lock.
            // The lock is the same AgentSessions FOR UPDATE the overlay insert holds
            // across its deciding read and commit. Sessions only, never Agents.
            var closesQuestion = await BatchClosesOpenQuestionAsync(db, sessionId, pending);
            try
            {
                using var save = new RuntimePhase(_logger, _timeProvider, sessionId, "transcript.save", storedSequence: maxSeq);
                await SaveTrackedChangesAsync(db, sessionId, closesQuestion, ct);
                save.Completed();
            }
            catch (DbUpdateException batchFailure)
            {
                db.ChangeTracker.Clear();
                var fallback = await PersistRowsIndividuallyAsync(db, sessionId, pending, batchFailure, startedAt, ct);
                if (recoveryTransaction is not null) await recoveryTransaction.CommitAsync(ct);
                return fallback with { AcceptedGeneration = startedAt };
            }

            if (recoveryTransaction is not null) await recoveryTransaction.CommitAsync(ct);
            _persistFailures.TryRemove(sessionId, out _);
            return ResultFrom(pending.Select(p => p.Source), pending.Max(p => p.Row.Sequence))
                with { CommittedRows = pending.Select(p => p.Row).ToArray(), AcceptedGeneration = startedAt };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordTranscriptPersistFailure(sessionId, PersistFailureDetail(ex));
            _logger.LogWarning(ex, "Failed to persist transcript entries for session {SessionId}", sessionId);
            return PersistResult.Empty with { NeedsReload = true };
        }
    }

    private static async Task AdmitActionsAsync(AppDbContext db, Guid sessionId,
        List<(TranscriptEntry Row, SessionRunnerTranscriptEvent Source)> rows, DateTime generation, CancellationToken ct)
    {
        var task = await db.AgentTasks.AsNoTracking().Where(t => t.AgentSessionId == sessionId
            && (t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Dispatched))
            .Select(t => new { t.Id, t.Attempt }).FirstOrDefaultAsync(ct);
        var apiIds = rows.Where(p => p.Row.Kind == TranscriptKinds.TurnEnd && p.Row.ApiCallId != null)
            .Select(p => p.Row.ApiCallId!).Distinct().ToArray();
        var seenApis = (await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.TurnEnd && t.ApiCallId != null && apiIds.Contains(t.ApiCallId))
            .Select(t => t.ApiCallId!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var (row, source) in rows)
        {
            // Classification is based on the final stored row, including safe-stub fallback.
            var boundary = IsTurnBoundary(TranscriptUiProjection.Stored(row, row.Sequence));
            if (row.Kind == TranscriptKinds.TurnEnd && row.ApiCallId is { } api)
                boundary &= seenApis.Add(api);
            SessionRuntimeActionCoordinator.Admit(db, row, source, generation, boundary, row.CreatedAt, task?.Id, task?.Attempt);
        }
    }

    private async Task<PersistResult> PersistRowsIndividuallyAsync(
        AppDbContext db,
        Guid sessionId,
        List<(TranscriptEntry Row, SessionRunnerTranscriptEvent Source)> rows,
        DbUpdateException batchFailure, DateTime generation, CancellationToken ct)
    {
        var landed = new List<SessionRunnerTranscriptEvent>();
        var committed = new List<TranscriptEntry>();
        var needsReload = false;
        long? lastStoredSeq = null;
        var anyStubOrSkip = false;
        foreach (var (row, source) in rows)
        {
            db.TranscriptEntries.Add(row);
            await AdmitActionsAsync(db, sessionId, [(row, source)], generation, ct);
            try
            {
                await SaveTrackedChangesAsync(db, sessionId, await RowClosesOpenQuestionAsync(db, sessionId, row), ct);
                committed.Add(row);
                landed.Add(source);
                lastStoredSeq = lastStoredSeq is { } seq && seq > row.Sequence ? seq : row.Sequence;
                continue;
            }
            catch (DbUpdateException rowFailure)
            {
                db.ChangeTracker.Clear();
                if (SqlStateOf(rowFailure) == "23505")
                {
                    _logger.LogDebug(
                        rowFailure,
                        "Transcript row uuid {Uuid} already stored for session {SessionId} (23505); skipped",
                        row.Uuid, sessionId);
                    // A collision proves only that SOMETHING already committed. Recover its
                    // actual identity/sequence; never fold our proposed row or classification.
                    needsReload = true;
                    var durable = await db.TranscriptEntries.AsNoTracking().FirstOrDefaultAsync(t =>
                        t.AgentSessionId == sessionId && (row.Uuid != null
                            ? t.Uuid == row.Uuid && t.Kind == row.Kind : t.Sequence == row.Sequence));
                    if (durable is not null)
                    {
                        landed.Add(source);
                        lastStoredSeq = lastStoredSeq is { } seq && seq > durable.Sequence ? seq : durable.Sequence;
                    }
                    continue;
                }

                anyStubOrSkip = true;
                var detail = PersistFailureDetail(rowFailure);
                ApplyPersistStub(row, detail);
                db.TranscriptEntries.Add(row);
                await AdmitActionsAsync(db, sessionId, [(row, source)], generation, ct);
                try
                {
                    await SaveTrackedChangesAsync(db, sessionId, await RowClosesOpenQuestionAsync(db, sessionId, row), ct);
                    committed.Add(row);
                    landed.Add(source);
                    lastStoredSeq = lastStoredSeq is { } seq && seq > row.Sequence ? seq : row.Sequence;
                    RecordTranscriptPersistFailure(sessionId, detail);
                    _logger.LogWarning(
                        "Transcript row for session {SessionId} sequence {Sequence} uuid {Uuid} kind {Kind} sqlState {SqlState} ({ExceptionType}) stored as a persist stub",
                        sessionId, row.Sequence, row.Uuid, row.Kind, SqlStateOf(rowFailure), rowFailure.GetType().Name);
                }
                catch (DbUpdateException stubFailure)
                {
                    db.ChangeTracker.Clear();
                    if (IsTransientDbFailure(stubFailure))
                    {
                        RecordTranscriptPersistFailure(sessionId, PersistFailureDetail(stubFailure));
                        return ResultFrom(landed, lastStoredSeq) with { CommittedRows = committed, NeedsReload = true };
                    }

                    RecordTranscriptPersistFailure(sessionId, PersistFailureDetail(stubFailure));
                    _logger.LogWarning(
                        "Transcript row not persistable for session {SessionId} sequence {Sequence} uuid {Uuid} kind {Kind} sqlState {SqlState} ({ExceptionType}); skipped",
                        sessionId, row.Sequence, row.Uuid, row.Kind, SqlStateOf(stubFailure), stubFailure.GetType().Name);
                }
            }
        }

        if (!anyStubOrSkip)
            _persistFailures.TryRemove(sessionId, out _);
        return ResultFrom(landed, lastStoredSeq) with { CommittedRows = committed, NeedsReload = needsReload };
    }

    /// <summary>
    /// Saves tracked rows. A question closure takes <see cref="SessionMessageQueueService.LockAgentSessionRowAsync"/>
    /// for the save's transaction so an overlay insert cannot commit between its unlocked read and its insert.
    /// </summary>
    private static async Task SaveTrackedChangesAsync(AppDbContext db, Guid sessionId, bool lockSession, CancellationToken ct)
    {
        // Checked recovery already holds the session row in its append transaction.
        // EF's savepoint rollback keeps that fence across batch and row fallbacks.
        if (db.Database.CurrentTransaction is not null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }
        await using var tx = lockSession ? await db.Database.BeginTransactionAsync(ct) : null;
        if (lockSession)
            await SessionMessageQueueService.LockAgentSessionRowAsync(db, sessionId, CancellationToken.None);
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    private static async Task<bool> BatchClosesOpenQuestionAsync(
        AppDbContext db,
        Guid sessionId,
        List<(TranscriptEntry Row, SessionRunnerTranscriptEvent Source)> pending)
    {
        if (pending.Any(p => RowClosesOpenQuestion(p.Row)))
            return true;

        var newQuestionIds = pending
            .Where(p => p.Row.Kind == TranscriptKinds.ToolCall
                && GrokQuestionTool.IsQuestionToolName(p.Row.ToolName)
                && !string.IsNullOrEmpty(p.Row.ToolUseId))
            .Select(p => p.Row.ToolUseId!)
            .ToHashSet(StringComparer.Ordinal);
        if (pending.Any(p => p.Row.Kind == TranscriptKinds.ToolResult
                && p.Row.ToolUseId is { } id
                && newQuestionIds.Contains(id)))
            return true;

        var resultIds = pending
            .Where(p => p.Row.Kind == TranscriptKinds.ToolResult && !string.IsNullOrEmpty(p.Row.ToolUseId))
            .Select(p => p.Row.ToolUseId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (resultIds.Length == 0)
            return false;

        var openIds = await OpenQuestionToolUseIdsAsync(db, sessionId, resultIds);
        return openIds.Count > 0;
    }

    private static async Task<bool> RowClosesOpenQuestionAsync(AppDbContext db, Guid sessionId, TranscriptEntry row)
    {
        if (RowClosesOpenQuestion(row))
            return true;
        if (row.Kind != TranscriptKinds.ToolResult || string.IsNullOrEmpty(row.ToolUseId))
            return false;
        var openIds = await OpenQuestionToolUseIdsAsync(db, sessionId, [row.ToolUseId]);
        return openIds.Contains(row.ToolUseId);
    }

    /// <summary>
    /// A row that ends the live question: any turn boundary the reply gate treats as current-turn
    /// end, or a question-tool <c>ToolResult</c>. A <c>ToolResult</c> that only matches by tool id
    /// is decided by <see cref="OpenQuestionToolUseIdsAsync"/>.
    /// </summary>
    private static bool RowClosesOpenQuestion(TranscriptEntry row) =>
        row.Kind == TranscriptKinds.TurnEnd
        || row.Kind == TranscriptKinds.SessionRestartBoundary
        || TranscriptKinds.IsManualCompactBoundary(row.Kind, row.Text)
        || TranscriptKinds.IsInterruptPrompt(row.Kind, row.Text)
        || (row.Kind == TranscriptKinds.ToolResult
            && GrokQuestionTool.IsConfirmingResult(row.ToolName, row.Text));

    private static async Task<HashSet<string>> OpenQuestionToolUseIdsAsync(
        AppDbContext db, Guid sessionId, IReadOnlyCollection<string> toolUseIds)
    {
        var calls = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.ToolCall
                && t.ToolUseId != null
                && toolUseIds.Contains(t.ToolUseId))
            .Select(t => new { t.ToolUseId, t.ToolName, t.Sequence })
            .ToListAsync();
        var open = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls.Where(c => GrokQuestionTool.IsQuestionToolName(c.ToolName)))
        {
            var id = call.ToolUseId!;
            var closed = await db.TranscriptEntries.AnyAsync(t =>
                t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.ToolResult
                && t.ToolUseId == id
                && t.Sequence > call.Sequence);
            if (!closed)
                open.Add(id);
        }

        return open;
    }

    private static PersistResult ResultFrom(IEnumerable<SessionRunnerTranscriptEvent> landed, long? lastStoredSeq)
    {
        var addedTurnBoundary = false;
        var addedAssistantText = false;
        var addedManualCompactBoundary = false;
        var any = false;
        foreach (var e in landed)
        {
            any = true;
            addedTurnBoundary |= IsTurnBoundary(e);
            addedAssistantText |= e.Kind == TranscriptKinds.AssistantText;
            addedManualCompactBoundary |= TranscriptKinds.IsManualCompactBoundary(e.Kind, e.Text);
        }

        return any
            ? new PersistResult(lastStoredSeq, addedTurnBoundary, addedAssistantText, addedManualCompactBoundary)
            : PersistResult.Empty;
    }

    private static void ApplyPersistStub(TranscriptEntry row, string detail)
    {
        row.Text = $"{TranscriptStubPrefix} {detail}]";
        row.ToolInput = null;
        row.Kind = ColumnText.Clip(row.Kind, 40);
        row.Uuid = ColumnText.ClipOrNull(row.Uuid, 64);
        row.ParentUuid = ColumnText.ClipOrNull(row.ParentUuid, 64);
        row.Role = ColumnText.ClipOrNull(row.Role, 40);
        row.ToolName = ColumnText.ClipOrNull(row.ToolName, 200);
        row.ToolUseId = ColumnText.ClipOrNull(row.ToolUseId, 120);
        row.StopReason = ColumnText.ClipOrNull(row.StopReason, 60);
    }

    private static IReadOnlyList<SessionRunnerTranscriptEvent> SanitizeEntries(
        IReadOnlyList<SessionRunnerTranscriptEvent> entries)
    {
        List<SessionRunnerTranscriptEvent>? copy = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var sanitized = Sanitize(entries[i]);
            if (ReferenceEquals(sanitized, entries[i]))
                continue;
            copy ??= entries.ToList();
            copy[i] = sanitized;
        }

        return copy ?? entries;
    }

    private static SessionRunnerTranscriptEvent Sanitize(SessionRunnerTranscriptEvent e)
    {
        if (!ContainsNul(e))
            return e;
        return e with
        {
            Kind = ColumnText.WithoutNul(e.Kind)!,
            Uuid = ColumnText.WithoutNul(e.Uuid),
            ParentUuid = ColumnText.WithoutNul(e.ParentUuid),
            Role = ColumnText.WithoutNul(e.Role),
            Text = ColumnText.WithoutNul(e.Text),
            ToolName = ColumnText.WithoutNul(e.ToolName),
            ToolInput = ColumnText.WithoutNul(e.ToolInput),
            ToolUseId = ColumnText.WithoutNul(e.ToolUseId),
            StopReason = ColumnText.WithoutNul(e.StopReason),
            ApiCallId = ColumnText.WithoutNul(e.ApiCallId),
            ApiErrorClass = ColumnText.WithoutNul(e.ApiErrorClass),
            ApiErrorTimeZoneId = ColumnText.WithoutNul(e.ApiErrorTimeZoneId),
            Model = ColumnText.WithoutNul(e.Model),
        };
    }

    private static bool ContainsNul(SessionRunnerTranscriptEvent e) =>
        HasNul(e.Kind) || HasNul(e.Uuid) || HasNul(e.ParentUuid) || HasNul(e.Role) || HasNul(e.Text)
        || HasNul(e.ToolName) || HasNul(e.ToolInput) || HasNul(e.ToolUseId) || HasNul(e.StopReason)
        || HasNul(e.ApiCallId) || HasNul(e.ApiErrorClass) || HasNul(e.ApiErrorTimeZoneId) || HasNul(e.Model);

    private static bool HasNul(string? value) => value is not null && value.IndexOf('\0') >= 0;

    private static string? SqlStateOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { Length: > 0 } sqlState })
                return sqlState;
        }

        return null;
    }

    private static bool IsTransientDbFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true })
                return true;
        }

        return false;
    }

    private static string PersistFailureDetail(Exception exception)
    {
        var sqlState = SqlStateOf(exception);
        if (sqlState is not null)
            return sqlState;
        var innermost = exception;
        while (innermost.InnerException is not null)
            innermost = innermost.InnerException;
        return innermost.GetType().Name;
    }

    internal static object ToTranscriptPayload(SessionRunnerTranscriptEvent e) => new
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
        apiErrorTimeZoneId = e.ApiErrorTimeZoneId,
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

    /// <summary>
    /// CARD-1362 D-2: one deadline per live runner read, on the injected clock so a test advances
    /// a FakeTimeProvider instead of sleeping. A linked source cannot take a TimeProvider, hence
    /// two disposables. Every bounded read is awaited through <c>WaitAsync(linked.Token)</c> as
    /// well, so the deadline holds against a client that ignores the token it is handed.
    /// </summary>
    private (CancellationTokenSource Deadline, CancellationTokenSource Linked) LiveReadDeadline(CancellationToken ct)
    {
        var deadline = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(Math.Max(250, _settings.RunnerLiveReadTimeoutMs)), _timeProvider);
        return (deadline, CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token));
    }

    /// <summary>S1 compatibility wrapper; S2-S5 move every caller onto <see cref="GetBufferSnapshotAsync"/>.</summary>
    public AgentSessionRuntimeBufferSnapshot GetBufferSnapshot(Guid sessionId) =>
        GetBufferSnapshotAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>CARD-1362 D-1: the buffer read, bounded by <see cref="LiveReadDeadline"/>; a failure propagates.</summary>
    public async Task<AgentSessionRuntimeBufferSnapshot> GetBufferSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        if (_testBuffers.TryGetValue(sessionId, out var testBuffer))
            return new AgentSessionRuntimeBufferSnapshot(testBuffer.ToString(), GetDeltaSequenceOrDefault(sessionId));

        var (deadline, linked) = LiveReadDeadline(ct);
        using (deadline)
        using (linked)
        {
            var buffer = await _runnerClient.GetBufferAsync(sessionId, linked.Token).WaitAsync(linked.Token);
            return new AgentSessionRuntimeBufferSnapshot(buffer.Buffer, buffer.LastSequence);
        }
    }

    /// <summary>
    /// Sessions a runner holds live: the local runner's List, the in-process test adapters and,
    /// CARD-0679 D-10, the connected phone-home runner's cached inventory (no RPC). Every caller
    /// used to read a live phone-home session as dead: its stranded brief was never swept, a
    /// WhenIdle reply stayed Pending and send-now answered 409.
    /// S1 compatibility wrapper; S2-S5 move every caller onto <see cref="ListLiveSessionsAsync"/>.
    /// </summary>
    public IReadOnlyList<Guid> ListLiveSessions() =>
        ListLiveSessionsAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// CARD-1362 D-1/D-3: the List read, bounded by <see cref="LiveReadDeadline"/>. Any failure,
    /// the deadline included, propagates as it does from the client's own 3 s list cap; nothing
    /// substitutes an empty live set (CARD-0679: that reads every session as dead).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListLiveSessionsAsync(CancellationToken ct) =>
        (await ListLocalLiveSessionsAsync(ct))
            .Concat(_directory?.LiveRemoteSessionIds() ?? [])
            .Distinct()
            .ToList();

    internal IReadOnlySet<Guid> ListLocalLiveSessions() =>
        ListLocalLiveSessionsAsync(CancellationToken.None).GetAwaiter().GetResult();

    internal async Task<IReadOnlySet<Guid>> ListLocalLiveSessionsAsync(CancellationToken ct)
    {
        var (deadline, linked) = LiveReadDeadline(ct);
        using (deadline)
        using (linked)
        {
            var sessions = await _runnerClient.ListAsync(linked.Token).WaitAsync(linked.Token);
            return sessions
                .Where(session => session.Status is "Running" or "Starting")
                .Select(session => session.SessionId)
                .Concat(_testAdapters.Keys)
                .ToHashSet();
        }
    }

    internal IReadOnlyList<string> PendingRunnerIds() =>
        _directory?.KnownRunnerIds.Where(RemoteInventoryPending).ToArray() ?? [];

    /// <summary>
    /// CARD-0679 (review 87af1bf6): phone-home sessions whose liveness is unknown, neither in
    /// <see cref="ListLiveSessions"/> nor confirmed gone (no exit, no kill, and no List from a
    /// connected runner without them). Nothing destructive acts on one: it is not failed, its
    /// attempt is not canceled and its card keeps its claim. Queued delivery keeps trying it.
    /// Optional probes that act only on a confirmed live session (usage polls, compaction,
    /// watchdog answers, policy relaunch) skip it until the runner confirms it again.
    /// </summary>
    public IReadOnlyList<Guid> ListUnknownSessions() =>
        ListUnknownSessionsAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<Guid>> ListUnknownSessionsAsync(CancellationToken ct)
    {
        var unknown = _directory?.UnknownRemoteSessionIds() ?? [];
        if (unknown.Count == 0)
            return [];
        var live = await ListLiveSessionsAsync(ct);
        return unknown.Where(id => !live.Contains(id)).Distinct().ToList();
    }

    /// <summary>
    /// CARD-0679 (review 87af1bf6): every session not confirmed gone, live or unknown. The set a
    /// destructive "not live" decision (fail, cancel, clear a claim) and queued delivery read.
    /// </summary>
    public IReadOnlyList<Guid> ListLiveOrUnknownSessions() =>
        ListLiveOrUnknownSessionsAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<Guid>> ListLiveOrUnknownSessionsAsync(CancellationToken ct) =>
        (await ListLiveSessionsAsync(ct))
            .Concat(_directory?.UnknownRemoteSessionIds() ?? [])
            .Distinct()
            .ToList();

    /// <summary>
    /// CARD-0679 (review 87af1bf6): whether a session bound to <paramref name="runnerId"/> is live
    /// or unknown, including the window before that remote runner's first catch-up List, when the
    /// directory has no inventory to name it in.
    /// </summary>
    public bool IsLiveOrUnknown(Guid sessionId, string? runnerId) =>
        IsLiveOrUnknownAsync(sessionId, runnerId, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// CARD-1362 D-3: a List failure, the deadline included, propagates; a destructive caller must
    /// never read a timeout as "gone".
    /// </summary>
    public async Task<bool> IsLiveOrUnknownAsync(Guid sessionId, string? runnerId, CancellationToken ct) =>
        RemoteInventoryPending(runnerId)
        || (await ListLiveOrUnknownSessionsAsync(ct)).Contains(sessionId);

    /// <summary>
    /// CARD-0679 (review f87b49a7): true while <paramref name="runnerId"/> is a remote runner no
    /// connection has answered for since this process started, so nothing can reach its sessions.
    /// </summary>
    public bool RemoteInventoryPending(string? runnerId) =>
        _directory?.RemoteInventoryPending(runnerId) ?? false;

    /// <summary>A known terminal row never gains liveness from a stale membership snapshot.</summary>
    internal bool IsLiveOrUnknown(AgentSession session) =>
        IsLiveOrUnknownAsync(session, CancellationToken.None).GetAwaiter().GetResult();

    internal async Task<bool> IsLiveOrUnknownAsync(AgentSession session, CancellationToken ct)
    {
        if (session.Status is not (SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping)
            || !IsAcceptedRunnerBinding(session.RunnerId))
            return false;
        return string.IsNullOrWhiteSpace(session.RunnerId) || session.RunnerId == PhoneHomeProtocol.LocalRunnerId
            ? (await ListLocalLiveSessionsAsync(ct)).Contains(session.Id)
            : await IsLiveOrUnknownAsync(session.Id, session.RunnerId, ct);
    }

    internal bool IsAcceptedRunnerBinding(string? runnerId) =>
        string.IsNullOrWhiteSpace(runnerId) || runnerId == PhoneHomeProtocol.LocalRunnerId
        || _directory is null || _directory.KnownRunnerIds.Contains(runnerId);

    /// <summary>
    /// Immediate input admission only; never retain the resolved client for the later write.
    /// A caller with a loaded session can reuse its binding rather than read it a second time.
    /// </summary>
    internal async Task EnsureInputTransportAvailableAsync(
        Guid sessionId, SessionRunnerBinding? binding, CancellationToken ct)
    {
        if (_directory is null) return;
        binding ??= await _directory.GetBindingAsync(sessionId, ct);
        if (binding is SessionRunnerBinding.Missing)
            throw new NotFoundException(nameof(AgentSession), sessionId);
        if (binding is SessionRunnerBinding.Remote remote)
            _directory.Resolve(remote.Owner.RunnerId);
    }

    internal Task EnsureInputTransportAvailableAsync(AgentSession session, CancellationToken ct) =>
        EnsureInputTransportAvailableAsync(session.Id,
            session.RunnerId is not null && session.RunnerStoreId is Guid store && session.RunnerCwd is not null
                ? new SessionRunnerBinding.Remote(new SessionRunnerOwner(session.RunnerId, store, session.RunnerCwd))
                : SessionRunnerBinding.Local.Instance, ct);

    /// <summary>S1 compatibility wrapper; S2-S5 move every caller onto <see cref="TryGetLiveSnapshotAsync"/>.</summary>
    public bool TryGetLiveSnapshot(Guid sessionId, out AgentSessionLiveSnapshot snapshot)
    {
        var result = TryGetLiveSnapshotAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();
        snapshot = result!;
        return result is not null;
    }

    /// <summary>
    /// CARD-1362 D-3: null is "unavailable" (any failure, the live-read deadline included), never
    /// "gone": a probe that acts only on a confirmed live session skips it. A cancelled caller token
    /// rethrows instead, so a cancelled sweep records no decision. Snapshot and buffer share one
    /// deadline (D-2).
    /// </summary>
    public async Task<AgentSessionLiveSnapshot?> TryGetLiveSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            if (_testAdapters.TryGetValue(sessionId, out var adapter))
            {
                return new AgentSessionLiveSnapshot(
                    sessionId,
                    adapter.SnapshotRawOutput(),
                    adapter.SnapshotRenderedScreen(),
                    _testBuffers.GetValueOrDefault(sessionId)?.ToString() ?? string.Empty,
                    GetDeltaSequenceOrDefault(sessionId));
            }

            var (deadline, linked) = LiveReadDeadline(ct);
            using (deadline)
            using (linked)
            {
                var runnerSnapshot = await _runnerClient.GetSnapshotAsync(sessionId, linked.Token).WaitAsync(linked.Token);
                var buffer = await _runnerClient.GetBufferAsync(sessionId, linked.Token).WaitAsync(linked.Token);
                return new AgentSessionLiveSnapshot(
                    sessionId,
                    runnerSnapshot.RawOutput,
                    runnerSnapshot.RenderedScreen,
                    buffer.Buffer,
                    runnerSnapshot.LastSequence,
                    runnerSnapshot.AcceptedStartedAt,
                    runnerSnapshot.Dialog);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live snapshot unavailable for agent session {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>S1 compatibility wrapper; S2-S5 move every caller onto <see cref="TryGetLiveMetadataAsync"/>.</summary>
    public bool TryGetLiveMetadata(Guid sessionId, out AgentSessionLiveMetadata metadata)
    {
        var result = TryGetLiveMetadataAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();
        metadata = result!;
        return result is not null;
    }

    /// <summary>
    /// CARD-1362 D-3: null is "unavailable" (any failure, the live-read deadline included) or a
    /// runner status other than Running/Starting; a cancelled caller token rethrows.
    /// </summary>
    public async Task<AgentSessionLiveMetadata?> TryGetLiveMetadataAsync(Guid sessionId, CancellationToken ct)
    {
        if (_testAdapters.ContainsKey(sessionId))
        {
            _testAgentStatuses.TryGetValue(sessionId, out var status);
            _testPending.TryGetValue(sessionId, out var pending);
            _testTranscriptBound.TryGetValue(sessionId, out var bound);
            return new AgentSessionLiveMetadata(
                sessionId, GetDeltaSequenceOrDefault(sessionId), status,
                TranscriptBound: bound, Pending: pending);
        }

        try
        {
            SessionRunnerSessionDto session;
            var (deadline, linked) = LiveReadDeadline(ct);
            using (deadline)
            using (linked)
            {
                session = await _runnerClient.GetAsync(sessionId, linked.Token).WaitAsync(linked.Token);
            }

            if (session.Status is not ("Running" or "Starting"))
                return null;

            return new AgentSessionLiveMetadata(
                sessionId, session.LastSequence, session.AgentStatus,
                session.TranscriptBound, session.TranscriptBindHow, session.Pending);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live metadata unavailable for agent session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task SendInputAsync(
        Guid sessionId, string input, CancellationToken ct, bool trackManualTurn = true)
    {
        if (string.IsNullOrEmpty(input)) return;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);
        if (session.AgentKind != AgentKind.ClaudeCode)
        {
            await SendInteractiveInputAsync(sessionId, input, ct, trackManualTurn);
            return;
        }
        var observation = await ObserveDialogAsync(sessionId, ct);
        var request = ProviderDialogObservationService.Request(observation, sessionId,
            SessionGeneration.Normalize(session.StartedAt), input);
        ProviderDialogObservationService.RequireWritten(await WriteConditionalAsync(sessionId, request, ct));
        var submitted = RecordTerminalInput(sessionId, input);
        if (submitted && trackManualTurn) TryStartManualTurnTracking(sessionId, observation!.OutputSequence);
    }

    internal Task<ProviderDialogObservation?> ObserveDialogAsync(Guid sessionId, CancellationToken ct) =>
        _testAdapters.TryGetValue(sessionId, out var adapter)
            ? ProviderDialogObservationService.ReadBoundedAsync(token => adapter.ObserveDialogAsync(sessionId, token), ct)
            : _dialogs.ReadAsync(sessionId, ct);

    /// <summary>The explicit authenticated terminal-input route; never inferred from turn tracking.</summary>
    public async Task SendInteractiveInputAsync(
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

        var sequenceBeforeInput = await GetRunnerSequenceOrDefaultAsync(sessionId, ct);
        var submittedCommand = RecordTerminalInput(sessionId, input);
        await _runnerClient.SendInputAsync(sessionId, input, ct);
        if (submittedCommand && trackManualTurn)
            TryStartManualTurnTracking(sessionId, sequenceBeforeInput);
    }

    /// <summary>Unsubmitted human text owns the terminal; a modal key must never type over it.</summary>
    public bool HasPendingTerminalInput(Guid sessionId) =>
        _pendingInputs.TryGetValue(sessionId, out var pending) && pending.HasText;

    /// <summary>
    /// A numbered approval key is consumed by the provider modal, not by the composer. Do not
    /// retain it as unfinished human text or create a manual turn from it.
    /// </summary>
    public Task SendModalKeyAsync(Guid sessionId, string key, CancellationToken ct)
    {
        if (key != "2" || HasPendingTerminalInput(sessionId))
            throw new ProviderDialogInputRefusedException(ConditionalInputOutcomes.ModalBlocked);
        return SendDialogActionAsync(sessionId, key, ClaudeDialogInputIntent.UnsafeDeleteNo, ct);
    }

    internal Task DismissRemoteControlAsync(Guid sessionId, CancellationToken ct) =>
        SendDialogActionAsync(sessionId, "\u001b", ClaudeDialogInputIntent.RemoteControlDismiss, ct);

    private async Task SendDialogActionAsync(Guid sessionId, string key, ClaudeDialogInputIntent intent, CancellationToken ct)
    {
        var observation = await ObserveDialogAsync(sessionId, ct);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generation = await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync(ct);
        var request = ProviderDialogObservationService.Request(observation, sessionId, generation, key, intent);
        ProviderDialogObservationService.RequireWritten(await WriteConditionalAsync(sessionId, request, ct));
    }

    public async Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.AgentKind).SingleAsync(ct) == AgentKind.ClaudeCode)
        {
            var observation = await ObserveDialogAsync(sessionId, ct);
            if (observation is null || observation.OutputSequence != request.ExpectedLastSequence)
                return new(sessionId, ConditionalInputOutcomes.StaleObservation);
            // A caller retaining a typed body may refresh output evidence after a refused write,
            // but must not acquire authority over input written by somebody else in between.
            if ((request.ExpectedInputRevision is { } revision && observation.InputRevision != revision)
                || (request.ExpectedRuntimeEpoch is { } epoch && observation.RuntimeEpoch != epoch))
                return new(sessionId, ConditionalInputOutcomes.StaleObservation);
            try
            {
                request = ProviderDialogObservationService.Request(observation, sessionId,
                    request.ExpectedAcceptedStartedAt, request.Input, request.DialogIntent);
            }
            catch (ProviderDialogInputRefusedException ex) { return new(sessionId, ex.Reason); }
        }
        return await WriteConditionalAsync(sessionId, request, ct);
    }

    private async Task<RunnerConditionalInputResult> WriteConditionalAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
    {
        if (_testAdapters.TryGetValue(sessionId, out var adapter))
            return await adapter.SendConditionalInputAsync(request, ct);

        return await _runnerClient.SendConditionalInputAsync(sessionId, request, ct);
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
        return session.Status == "Exited" || session.ExitCode is not null || session.ExitSignal is > 0;
    }

    public async Task<bool> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, TimeSpan timeout, CancellationToken ct)
    {
        if (_testAdapters.TryGetValue(sessionId, out var adapter))
            return (await adapter.KillGenerationAsync(expectedAcceptedStartedAt, timeout, ct)).Killed;

        var result = await _runnerClient.KillGenerationAsync(sessionId, expectedAcceptedStartedAt, ct);
        return result.Killed;
    }

    public Task<SessionRunnerSessionDto> GetSessionAsync(Guid sessionId, CancellationToken ct)
    {
        if (_testAdapters.TryGetValue(sessionId, out var adapter))
        {
            var completion = adapter.Completion;
            var observation = completion.IsCompletedSuccessfully ? completion.Result : null;
            int? exitCode = observation?.ExitCode;
            _testPending.TryGetValue(sessionId, out var pending);
            return Task.FromResult(new SessionRunnerSessionDto(
                sessionId,
                adapter.Pid,
                UtcNow(),
                observation is null ? "Running" : "Exited",
                exitCode,
                adapter.ExitReason,
                GetDeltaSequenceOrDefault(sessionId),
                Pending: pending,
                AcceptedStartedAt: _testAcceptedStartedAt.TryGetValue(sessionId, out var accepted)
                    ? accepted
                    : null,
                ExitSignal: observation?.ExitSignal, ExitStopIntent: observation?.ExitStopIntent));
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

    /// <summary>
    /// CARD-1150 test seam: the accepted generation <see cref="GetSessionAsync"/> reports for an
    /// in-process adapter. Production local sessions take this from the runner client instead.
    /// </summary>
    public void SetTestAcceptedStartedAt(Guid sessionId, DateTime acceptedStartedAt) =>
        _testAcceptedStartedAt[sessionId] = acceptedStartedAt;

    /// <summary>
    /// CARD-0334 S2 test seam: set <see cref="AgentSessionLiveMetadata.TranscriptBound"/> for an
    /// in-process test adapter. Production reads this from the runner; a resume that is not
    /// bound would fall back to fresh and lose the conversation.
    /// </summary>
    public void SetTestTranscriptBound(Guid sessionId, bool? bound) =>
        _testTranscriptBound[sessionId] = bound;

    public bool TryRemove(Guid sessionId, out IAgentProtocolAdapter? adapter)
    {
        _pendingInputs.TryRemove(sessionId, out _);
        _manualTurns.TryRemove(sessionId, out _);
        _lastSequences.TryRemove(sessionId, out _);
        _persistFailures.TryRemove(sessionId, out _);
        _firstDeltas.TryRemove(sessionId, out _);
        _testBuffers.TryRemove(sessionId, out _);
        _testAgentStatuses.TryRemove(sessionId, out _);
        _testPending.TryRemove(sessionId, out _);
        _testAcceptedStartedAt.TryRemove(sessionId, out _);
        _testTranscriptBound.TryRemove(sessionId, out _);
        return _testAdapters.TryRemove(sessionId, out adapter);
    }

    public async Task DisposeSessionAsync(Guid sessionId)
    {
        if (TryRemove(sessionId, out var adapter) && adapter is not null)
            await adapter.DisposeAsync();
    }

    private async Task<long> GetRunnerSequenceOrDefaultAsync(Guid sessionId, CancellationToken ct)
    {
        if (_testBuffers.ContainsKey(sessionId))
            return GetDeltaSequenceOrDefault(sessionId);

        try
        {
            var (deadline, linked) = LiveReadDeadline(ct);
            using (deadline)
            using (linked)
            {
                var buffer = await _runnerClient.GetBufferAsync(sessionId, linked.Token).WaitAsync(linked.Token);
                return buffer.LastSequence;
            }
        }
        catch
        {
            return GetDeltaSequenceOrDefault(sessionId);
        }
    }

    private async Task RecordActivityAsync(Guid sessionId)
    {
        var at = _timeProvider.GetUtcNow();
        if (!ClaimActivityWrite(sessionId, at))
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = at.UtcDateTime;

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

    /// <summary>
    /// CARD-0679 D-3: true when this session's activity has not been written within
    /// <see cref="AgentSessionSettings.ActivityWriteMinIntervalMs"/>, and records <paramref name="at"/>
    /// as its last write. One output chunk used to cost two queries and a save; a phone-home burst
    /// of them was the pump's main database load.
    /// </summary>
    private bool ClaimActivityWrite(Guid sessionId, DateTimeOffset at)
    {
        if (_settings.ActivityWriteMinIntervalMs <= 0)
            return true;

        var interval = TimeSpan.FromMilliseconds(_settings.ActivityWriteMinIntervalMs);
        while (true)
        {
            if (!_lastActivityWrites.TryGetValue(sessionId, out var last))
            {
                if (_lastActivityWrites.TryAdd(sessionId, at))
                    return true;
                continue;
            }

            if (at - last < interval)
                return false;
            if (_lastActivityWrites.TryUpdate(sessionId, at, last))
                return true;
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

            var result = await WaitForManualTurnQuietAsync(sessionId, turn.RunnerId, sequenceAtSubmit);
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
            session.Card.BoardId,
            session.RunnerId);
        await PublishRunAttemptChangedAsync(turn, RunPhase.StreamingTurn);
        return turn;
    }

    private async Task<ManualTurnWaitResult> WaitForManualTurnQuietAsync(Guid sessionId, string? runnerId, long sequenceAtSubmit)
    {
        var firstDeltaDeadline = UtcNow()
            + TimeSpan.FromMilliseconds(Math.Max(100, _settings.FirstDeltaTimeoutMs));
        var sawDelta = false;
        while (UtcNow() < firstDeltaDeadline)
        {
            if (!await IsLiveOrUnknownAsync(sessionId, runnerId, CancellationToken.None))
                return ManualTurnWaitResult.RuntimeMissing;

            if (await GetRunnerSequenceOrDefaultAsync(sessionId, CancellationToken.None) > sequenceAtSubmit)
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
        var lastSequence = await GetRunnerSequenceOrDefaultAsync(sessionId, CancellationToken.None);
        var lastChange = UtcNow();

        while (UtcNow() < deadline)
        {
            await Task.Delay(ManualTurnPollInterval);
            if (!await IsLiveOrUnknownAsync(sessionId, runnerId, CancellationToken.None))
                return ManualTurnWaitResult.RuntimeMissing;

            var currentSequence = await GetRunnerSequenceOrDefaultAsync(sessionId, CancellationToken.None);
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

        public bool HasText
        {
            get
            {
                lock (_gate)
                    return _buffer.Length > 0;
            }
        }

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
        Guid BoardId,
        string? RunnerId);

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
    long LastSequence,
    DateTime? AcceptedStartedAt = null,
    ProviderDialogObservation? Dialog = null);

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
