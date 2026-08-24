using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class OrchestratorService
{
    private readonly AppDbContext _db;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentTuiLaunchResolver? _launchResolver;
    private readonly AgentSessionService _sessionService;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly RetryScheduler _retryScheduler;
    private readonly ExternalTrackerSyncService _externalTrackerSyncService;
    private readonly OrchestratorControlState _controlState;
    private readonly IEventBus _eventBus;
    private readonly OrchestratorSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrchestratorService> _logger;
    private readonly AgentSessionRuntime? _runtime;
    // CARD-0106 S2. Optional like the launch resolver beside it: absent, placeholders go
    // unresolved and the launch tripwire refuses them by name. Production always registers it.
    private readonly ApiKeyEnvResolver? _apiKeyEnvResolver;

    public OrchestratorService(
        AppDbContext db,
        AgentRegistry agentRegistry,
        AgentSessionService sessionService,
        AgentSessionLaunchQueue launchQueue,
        RetryScheduler retryScheduler,
        ExternalTrackerSyncService externalTrackerSyncService,
        OrchestratorControlState controlState,
        IEventBus eventBus,
        IOptions<OrchestratorSettings> settings,
        TimeProvider timeProvider,
        ILogger<OrchestratorService> logger,
        AgentSessionRuntime? runtime = null,
        AgentTuiLaunchResolver? launchResolver = null,
        ApiKeyEnvResolver? apiKeyEnvResolver = null)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _launchResolver = launchResolver;
        _sessionService = sessionService;
        _launchQueue = launchQueue;
        _retryScheduler = retryScheduler;
        _externalTrackerSyncService = externalTrackerSyncService;
        _controlState = controlState;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _runtime = runtime;
        _apiKeyEnvResolver = apiKeyEnvResolver;
    }

    public async Task<OrchestratorTickResult> PollTickAsync(CancellationToken ct)
    {
        var now = UtcNow();
        var reconciled = await ReconcileAsync(now, ct);
        if (_controlState.IsPaused || !_settings.Enabled)
        {
            var pausedResult = new OrchestratorTickResult(true, 0, 0, reconciled, 0, 0, 0, 0);
            await _eventBus.PublishToAllAsync("OrchestratorTick", pausedResult, ct);
            return pausedResult;
        }

        if (ShouldRunTrackerSync(now))
        {
            await _externalTrackerSyncService.SyncAsync(now, ct);
            _controlState.MarkTrackerSynced(now);
        }

        var candidates = await LoadEligibleCandidatesAsync(now, ct);
        var activeByBoard = await CountActiveSessionsByBoardAsync(ct);
        var activeByColumn = await CountActiveSessionsByColumnAsync(ct);

        var dispatched = 0;
        var skippedGlobal = 0;
        var skippedColumn = 0;
        var claimedElsewhere = 0;
        var failures = 0;

        foreach (var candidate in candidates)
        {
            if (dispatched >= _settings.MaxDispatchesPerTick)
                break;

            var boardActive = activeByBoard.GetValueOrDefault(candidate.BoardId);
            if (boardActive >= Math.Max(1, candidate.BoardMaxConcurrentSessions))
            {
                skippedGlobal++;
                continue;
            }

            if (candidate.ColumnMaxConcurrentSessions is int columnMax
                && activeByColumn.GetValueOrDefault(candidate.BoardColumnId) >= Math.Max(0, columnMax))
            {
                skippedColumn++;
                continue;
            }

            var request = await PrepareStartRequestAsync(candidate, now, ct);
            if (request is null)
            {
                failures++;
                continue;
            }

            AgentLaunchSpec spec;
            Guid? tuiProfileRevisionId = null;
            string? effectiveModelId = null;
            try
            {
                var resolved = await ResolveDispatchLaunchAsync(candidate, request, ct);
                spec = resolved.Spec;
                tuiProfileRevisionId = resolved.ProfileRevisionId;
                effectiveModelId = resolved.EffectiveModelId;
                request = request with
                {
                    DefinitionName = spec.DefinitionName,
                    AgentKind = spec.Kind
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to resolve agent launch for card {CardId}", candidate.CardId);
                await _retryScheduler.ScheduleFailureAsync(_db, candidate.CardId, ex.Message, now, ct);
                await _db.SaveChangesAsync(ct);
                failures++;
                continue;
            }
            var claimedSessionId = await TryClaimCardAsync(
                candidate.CardId,
                candidate.ConcurrencyToken,
                request.DefinitionName,
                spec.Kind,
                request.Cols,
                request.Rows,
                now,
                ct,
                tuiProfileRevisionId,
                effectiveModelId);
            if (claimedSessionId is null)
            {
                claimedElsewhere++;
                continue;
            }

            _launchQueue.Enqueue(request with { AgentKind = spec.Kind, PreclaimedSessionId = claimedSessionId }, spec);
            dispatched++;
            activeByBoard[candidate.BoardId] = boardActive + 1;
            activeByColumn[candidate.BoardColumnId] = activeByColumn.GetValueOrDefault(candidate.BoardColumnId) + 1;
        }

        var result = new OrchestratorTickResult(
            Paused: false,
            EligibleCards: candidates.Count,
            Dispatched: dispatched,
            Reconciled: reconciled,
            SkippedGlobalConcurrency: skippedGlobal,
            SkippedColumnConcurrency: skippedColumn,
            ClaimedElsewhere: claimedElsewhere,
            Failures: failures);

        await _eventBus.PublishToAllAsync("OrchestratorTick", result, ct);
        return result;
    }

    public async Task<OrchestratorStateDto> GetStateAsync(CancellationToken ct)
    {
        var activeStatuses = ActiveSessionStatuses();
        var now = UtcNow();
        var scopedSessions = ApplyScope(_db.AgentSessions
            .AsNoTracking()
            .Include(s => s.Card).ThenInclude(c => c.Board).ThenInclude(b => b.Project)
            .Include(s => s.RunAttempts).ThenInclude(a => a.TokenUsage)
            .AsSplitQuery());
        var activeSessions = await scopedSessions
            // Cardless interactive sessions aren't orchestrated card work — keep them out of this view.
            .Where(s => activeStatuses.Contains(s.Status) && s.CardId != null)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(ct);

        var retryQueue = await ApplyScope(_db.RetrySchedules
                .AsNoTracking()
                .Include(r => r.Card).ThenInclude(c => c.Board).ThenInclude(b => b.Project)
                .AsSplitQuery())
            .Where(r => r.AttemptCount < r.MaxAttempts
                && r.NextRetryAt != null
                && r.NextRetryAt <= now)
            .OrderBy(r => r.NextRetryAt)
            .ThenBy(r => r.Card.Identifier)
            .ToListAsync(ct);

        var tokenTotals = await ApplyScope(_db.RunAttempts
                .AsNoTracking())
            .Where(a => a.TokenUsage != null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TokensIn = g.Sum(a => (long?)a.TokenUsage!.TokensIn) ?? 0,
                TokensOut = g.Sum(a => (long?)a.TokenUsage!.TokensOut) ?? 0,
                CostUsd = g.Sum(a => (decimal?)a.TokenUsage!.CostUsd) ?? 0
            })
            .FirstOrDefaultAsync(ct);

        var running = activeSessions
            .Select(session =>
            {
                var attempt = session.RunAttempts
                    .OrderByDescending(a => a.AttemptNumber)
                    .FirstOrDefault();
                var live = false;
                var lastSequence = 0L;
                if (_runtime is not null && _runtime.TryGetLiveMetadata(session.Id, out var metadata))
                {
                    live = true;
                    lastSequence = metadata.LastSequence;
                }

                return new OrchestratorRunningSessionDto(
                    session.Id,
                    session.CardId!.Value,
                    session.Card.Identifier,
                    session.Card.Title,
                    session.Card.BoardId,
                    session.Card.Board.Name,
                    session.DefinitionName,
                    session.AgentKind.ToString(),
                    session.Status.ToString(),
                    attempt?.Id,
                    session.RunAttempts.Count,
                    attempt?.AttemptNumber,
                    attempt?.Phase.ToString(),
                    session.StartedAt,
                    session.LastSeenAt,
                    attempt?.LastEventAt,
                    Math.Max(0, (long)(now - session.StartedAt).TotalSeconds),
                    attempt?.TokenUsage?.TokensIn ?? 0,
                    attempt?.TokenUsage?.TokensOut ?? 0,
                    attempt?.TokenUsage?.CostUsd ?? 0,
                    live,
                    lastSequence);
            })
            .ToList();

        var retryItems = retryQueue
            .Select(retry => new OrchestratorRetryQueueItemDto(
                retry.CardId,
                retry.Card.Identifier,
                retry.Card.Title,
                retry.Card.BoardId,
                retry.Card.Board.Name,
                retry.AttemptCount,
                retry.MaxAttempts,
                retry.NextRetryAt,
                retry.LastAttemptAt,
                retry.LastError))
            .ToList();

        var totals = new OrchestratorStateTotalsDto(
            tokenTotals?.TokensIn ?? 0,
            tokenTotals?.TokensOut ?? 0,
            tokenTotals?.CostUsd ?? 0,
            running.Sum(s => s.RuntimeSeconds));
        var limits = new OrchestratorStateLimitsDto(
            _settings.PollIntervalSeconds,
            _settings.MaxDispatchesPerTick,
            _settings.FailureBackoffBaseMs,
            _settings.FailureBackoffMaxMs,
            _settings.StartingSessionGraceSeconds);

        return new OrchestratorStateDto(
            _controlState.IsPaused,
            _settings.Enabled,
            now,
            running.Count,
            retryItems.Count,
            totals,
            limits,
            running,
            retryItems);
    }

    public OrchestratorPauseResult Pause() => new(_controlState.Pause());

    public OrchestratorPauseResult Resume() => new(_controlState.Resume());

    private IQueryable<AgentSession> ApplyScope(IQueryable<AgentSession> query)
    {
        return string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
            ? query
            : query.Where(s => s.Card.Board.TrackerKind != TrackerKind.Internal
                || (s.Card.Board.Project.LocalRepositoryPath != null
                    && s.Card.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)));
    }

    private IQueryable<RetrySchedule> ApplyScope(IQueryable<RetrySchedule> query)
    {
        return string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
            ? query
            : query.Where(r => r.Card.Board.TrackerKind != TrackerKind.Internal
                || (r.Card.Board.Project.LocalRepositoryPath != null
                    && r.Card.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)));
    }

    private IQueryable<RunAttempt> ApplyScope(IQueryable<RunAttempt> query)
    {
        return string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
            ? query
            : query.Where(a => a.Card.Board.TrackerKind != TrackerKind.Internal
                || (a.Card.Board.Project.LocalRepositoryPath != null
                    && a.Card.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)));
    }

    internal async Task<Guid?> TryClaimCardAsync(
        Guid cardId,
        Guid concurrencyToken,
        string definitionName,
        AgentKind agentKind,
        int cols,
        int rows,
        DateTime utcNow,
        CancellationToken ct,
        Guid? tuiProfileRevisionId = null,
        string? effectiveModelId = null)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var card = await _db.Cards
            .FirstOrDefaultAsync(c => c.Id == cardId && c.OwnerSessionId == null, ct);
        if (card is null || card.ConcurrencyToken != concurrencyToken)
            return null;

        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            DefinitionName = definitionName,
            AgentKind = agentKind,
            Status = SessionStatus.Starting,
            Cwd = string.Empty,
            Cols = cols,
            Rows = rows,
            CreatedAt = utcNow,
            StartedAt = utcNow,
            LastSeenAt = utcNow,
            TuiProfileRevisionId = tuiProfileRevisionId,
            EffectiveModelId = effectiveModelId
        };
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        card.OwnerSessionId = session.Id;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = utcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return session.Id;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }
    }

    private async Task<int> ReconcileAsync(DateTime utcNow, CancellationToken ct)
    {
        var activeStatuses = ActiveSessionStatuses();
        var claimedCards = await _db.Cards
            .Include(c => c.BoardColumn)
            .Include(c => c.Board).ThenInclude(b => b.Columns)
            .Include(c => c.OwnerSession)
            .Include(c => c.RunAttempts)
            .Where(c => string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
                || c.Board.TrackerKind != TrackerKind.Internal
                || (c.Board.Project.LocalRepositoryPath != null
                    && c.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)))
            .Where(c => c.OwnerSessionId != null)
            .ToListAsync(ct);

        var reconciled = 0;
        var changedCards = new List<CardChangedNotification>();
        var queueRemovals = new List<AgentQueueRemoval>();
        foreach (var card in claimedCards)
        {
            ct.ThrowIfCancellationRequested();
            if (card.OwnerSession is null)
            {
                ClearCardClaim(card, utcNow);
                reconciled++;
                continue;
            }

            if (card.BoardColumn.IsActive
                && CardLifecycleTransitions.LatestAttemptSucceeded(card)
                && CardLifecycleTransitions.TryMoveToReview(card, utcNow))
            {
                changedCards.Add(new CardChangedNotification(card.BoardId, card.Id));
                // Review ends the card's stay in its agent's queue — a finished card left
                // enqueued re-spawns a session on the next agent start (CARD-0001 respawn loop).
                var queueRemoval = await CardLifecycleTransitions.DequeueFinishedCardAsync(_db, card, utcNow, ct);
                if (queueRemoval is not null)
                    queueRemovals.Add(queueRemoval);
                if (!activeStatuses.Contains(card.OwnerSession.Status))
                {
                    ClearCardClaim(card, utcNow);
                }
                else if (!await HasLiveRuntimeSessionAsync(card.OwnerSession.Id, ct))
                {
                    MarkSuccessfulRuntimeStopped(card, utcNow);
                    ClearCardClaim(card, utcNow);
                }

                reconciled++;
                continue;
            }

            if (card.BoardColumn.IsTerminal && activeStatuses.Contains(card.OwnerSession.Status))
            {
                try
                {
                    await _sessionService.KillAsync(card.OwnerSession.Id, ct);
                }
                catch (NotFoundException ex)
                {
                    _logger.LogWarning(ex, "Runtime session missing while reconciling card {CardId}", card.Id);
                    await MarkMissingRuntimeCanceledAsync(card, utcNow, ct);
                }

                ClearCardClaim(card, utcNow);
                card.TerminalReason ??= "Card reached a terminal column while an agent session was active.";
                reconciled++;
                continue;
            }

            if (ShouldProbeMissingRuntime(card.OwnerSession, utcNow)
                && !await HasLiveRuntimeSessionAsync(card.OwnerSession.Id, ct))
            {
                await MarkMissingRuntimeCanceledAsync(card, utcNow, ct);
                await _retryScheduler.ScheduleFailureAsync(
                    _db,
                    card.Id,
                    "Runtime session was not found during reconciliation.",
                    utcNow,
                    ct);
                ClearCardClaim(card, utcNow);
                reconciled++;
                continue;
            }

            if (!activeStatuses.Contains(card.OwnerSession.Status))
            {
                ClearCardClaim(card, utcNow);
                reconciled++;
            }
        }

        if (reconciled > 0)
        {
            await _db.SaveChangesAsync(ct);
            foreach (var changedCard in changedCards.Distinct())
            {
                await _eventBus.PublishToAllAsync(
                    "CardChanged",
                    new { boardId = changedCard.BoardId, cardId = changedCard.CardId },
                    ct);
            }

            foreach (var queueRemoval in queueRemovals)
                await CardLifecycleTransitions.PublishQueueRemovalAsync(_eventBus, queueRemoval, ct);
        }

        return reconciled;
    }

    private async Task<IReadOnlyList<DispatchCandidate>> LoadEligibleCandidatesAsync(
        DateTime utcNow,
        CancellationToken ct)
    {
        var activeStatuses = ActiveSessionStatuses();
        return await _db.Cards
            .AsNoTracking()
            .Where(c => string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
                || c.Board.TrackerKind != TrackerKind.Internal
                || (c.Board.Project.LocalRepositoryPath != null
                    && c.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)))
            .Where(c => c.BoardColumn.IsActive && !c.BoardColumn.IsTerminal)
            // An archived card is off the board; auto-dispatch must not pick it back up.
            .Where(c => c.ArchivedAt == null)
            // A declined spawn is a hold, not a race against the next tick (CARD-0087).
            .Where(c => c.AutoDispatchHeldAt == null)
            .Where(c => c.OwnerSessionId == null)
            .Where(c => !c.AgentSessions.Any(s => activeStatuses.Contains(s.Status)))
            .Where(c => c.RetrySchedule == null
                || (c.RetrySchedule.AttemptCount < c.RetrySchedule.MaxAttempts
                    && (c.RetrySchedule.NextRetryAt == null || c.RetrySchedule.NextRetryAt <= utcNow)))
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.CreatedAt)
            .Select(c => new DispatchCandidate(
                c.Id,
                c.Identifier,
                c.Title,
                c.Description,
                c.Priority,
                c.BoardId,
                c.Board.MaxConcurrentSessions,
                c.BoardColumnId,
                c.BoardColumn.MaxConcurrentSessions,
                c.ConcurrencyToken,
                c.AssignedAgentId,
                c.Board.WorkflowDefinitions
                    .Where(d => d.IsActive)
                    .OrderByDescending(d => d.Version)
                    .Select(d => (Guid?)d.Id)
                    .FirstOrDefault(),
                c.Board.WorkflowDefinitions
                    .Where(d => d.IsActive)
                    .OrderByDescending(d => d.Version)
                    .Select(d => d.Content)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    private async Task<Dictionary<Guid, int>> CountActiveSessionsByBoardAsync(CancellationToken ct)
    {
        var activeStatuses = ActiveSessionStatuses();
        return await _db.AgentSessions
            .Where(s => activeStatuses.Contains(s.Status))
            .Join(
                _db.Cards,
                session => session.CardId,
                card => card.Id,
                (session, card) => card.BoardId)
            .GroupBy(boardId => boardId)
            .Select(g => new { BoardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BoardId, x => x.Count, ct);
    }

    private async Task<Dictionary<Guid, int>> CountActiveSessionsByColumnAsync(CancellationToken ct)
    {
        var activeStatuses = ActiveSessionStatuses();
        return await _db.AgentSessions
            .Where(s => activeStatuses.Contains(s.Status))
            .Join(
                _db.Cards,
                session => session.CardId,
                card => card.Id,
                (session, card) => card.BoardColumnId)
            .GroupBy(columnId => columnId)
            .Select(g => new { ColumnId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ColumnId, x => x.Count, ct);
    }

    private async Task<StartAgentSessionRequest?> PrepareStartRequestAsync(
        DispatchCandidate candidate,
        DateTime utcNow,
        CancellationToken ct)
    {
        try
        {
            return new StartAgentSessionRequest(
                candidate.CardId,
                _agentRegistry.Settings.DefaultDefinition,
                AgentKind.Raw,
                BuildPrompt(candidate),
                _settings.DefaultCols,
                _settings.DefaultRows,
                BoardWorkflowDefinitionId: candidate.WorkflowDefinitionId,
                UseWorkflowPrompt: IsMarkdownWorkflow(candidate.WorkflowContent));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to prepare card {CardId} for dispatch", candidate.CardId);
            await _retryScheduler.ScheduleFailureAsync(_db, candidate.CardId, ex.Message, utcNow, ct);
            await _db.SaveChangesAsync(ct);
            return null;
        }
    }

    private async Task<ResolvedAgentTuiLaunch> ResolveDispatchLaunchAsync(
        DispatchCandidate candidate,
        StartAgentSessionRequest request,
        CancellationToken ct)
    {
        var options = new AgentLaunchOptions(
            Cwd: null,
            Cols: request.Cols,
            Rows: request.Rows,
            ExtraArgs: null,
            ExtraEnv: null);

        if (candidate.AssignedAgentId is { } assignedAgentId)
        {
            var agent = await _db.Agents.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == assignedAgentId, ct);
            if (agent is not null)
                return await AgentLaunchResolution.ResolveForAgentAsync(
                    agent,
                    _agentRegistry,
                    _launchResolver,
                    options,
                    ct,
                    _apiKeyEnvResolver);
        }

        return await AgentLaunchResolution.ResolveDefaultAsync(
            _agentRegistry,
            _launchResolver,
            options,
            ct,
            _apiKeyEnvResolver);
    }

    private static void ClearCardClaim(Card card, DateTime utcNow)
    {
        card.OwnerSessionId = null;
        card.OwnerSession = null;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = utcNow;
    }

    private static void MarkSuccessfulRuntimeStopped(Card card, DateTime utcNow)
    {
        if (card.OwnerSession is null)
            return;

        card.OwnerSession.Status = SessionStatus.Stopped;
        card.OwnerSession.EndedAt ??= utcNow;
        card.OwnerSession.LastSeenAt = utcNow;
        card.OwnerSession.FailureReason = null;
    }

    private static string BuildPrompt(DispatchCandidate candidate)
    {
        var prompt = $"""
            Work on card {candidate.Identifier}: {candidate.Title}

            Description:
            {candidate.Description}
            """;

        if (string.IsNullOrWhiteSpace(candidate.WorkflowContent)
            || IsMarkdownWorkflow(candidate.WorkflowContent))
            return prompt;

        var workflow = WorkflowDefinitionParser.ParseYamlDefinition(candidate.WorkflowContent);
        var stages = string.Join(
            Environment.NewLine,
            workflow.Stages.Select(stage => $"- {stage.Name} ({stage.ExecutorType})"));
        return string.IsNullOrWhiteSpace(stages)
            ? prompt
            : $"""
                {prompt}

                Workflow: {workflow.Name}
                {stages}
                """;
    }

    private static bool IsMarkdownWorkflow(string? content) =>
        !string.IsNullOrWhiteSpace(content)
        && content.TrimStart().StartsWith("---", StringComparison.Ordinal)
        && WorkflowDefinitionLoader.TryParseContent(content, out _, out _);

    private static SessionStatus[] ActiveSessionStatuses() =>
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private bool ShouldRunTrackerSync(DateTime utcNow)
    {
        var intervalMinutes = _settings.TrackerSyncIntervalMinutes;
        if (intervalMinutes <= 0)
            return true;

        var last = _controlState.LastTrackerSyncAt;
        if (last is null)
            return true;

        return utcNow - last.Value >= TimeSpan.FromMinutes(intervalMinutes);
    }

    private bool ShouldProbeMissingRuntime(AgentSession session, DateTime utcNow)
    {
        if (!ActiveSessionStatuses().Contains(session.Status))
            return false;
        if (session.Status != SessionStatus.Starting)
            return true;

        var grace = TimeSpan.FromSeconds(Math.Max(1, _settings.StartingSessionGraceSeconds));
        return utcNow - session.LastSeenAt >= grace;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private async Task<bool> HasLiveRuntimeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            if (_runtime is not null)
                return _runtime.ListLiveSessions().Contains(sessionId);

            await _sessionService.SendInputAsync(sessionId, string.Empty, ct);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }

    private sealed record DispatchCandidate(
        Guid CardId,
        string Identifier,
        string Title,
        string Description,
        int Priority,
        Guid BoardId,
        int BoardMaxConcurrentSessions,
        Guid BoardColumnId,
        int? ColumnMaxConcurrentSessions,
        Guid ConcurrencyToken,
        Guid? AssignedAgentId,
        Guid? WorkflowDefinitionId,
        string? WorkflowContent);

    private sealed record CardChangedNotification(Guid BoardId, Guid CardId);

    private async Task MarkMissingRuntimeCanceledAsync(Card card, DateTime utcNow, CancellationToken ct)
    {
        if (card.OwnerSession is not null)
        {
            card.OwnerSession.Status = SessionStatus.Failed;
            card.OwnerSession.EndedAt = utcNow;
            card.OwnerSession.LastSeenAt = utcNow;
            card.OwnerSession.FailureReason = "Runtime session was not found during reconciliation.";
        }

        var attempt = await _db.RunAttempts
            .Where(a => a.AgentSessionId == card.OwnerSessionId && a.CompletedAt == null)
            .OrderByDescending(a => a.AttemptNumber)
            .FirstOrDefaultAsync(ct);
        if (attempt is not null && !RunAttemptStateMachine.IsTerminal(attempt.Phase))
        {
            RunAttemptStateMachine.Transition(attempt, RunPhase.Canceled, utcNow);
            attempt.ErrorDetails = "Runtime session was not found during reconciliation.";
        }
    }
}
