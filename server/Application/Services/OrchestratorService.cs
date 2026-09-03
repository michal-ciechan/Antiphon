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
    private readonly AgentSessionLaunchComposer _launchComposer;
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
    private readonly DelegationSettings _delegationSettings;

    public OrchestratorService(
        AppDbContext db,
        AgentRegistry agentRegistry,
        AgentSessionLaunchComposer launchComposer,
        AgentSessionService sessionService,
        AgentSessionLaunchQueue launchQueue,
        RetryScheduler retryScheduler,
        ExternalTrackerSyncService externalTrackerSyncService,
        OrchestratorControlState controlState,
        IEventBus eventBus,
        IOptions<OrchestratorSettings> settings,
        IOptions<DelegationSettings> delegationSettings,
        TimeProvider timeProvider,
        ILogger<OrchestratorService> logger,
        AgentSessionRuntime? runtime = null,
        AgentTuiLaunchResolver? launchResolver = null,
        ApiKeyEnvResolver? apiKeyEnvResolver = null)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _launchResolver = launchResolver;
        _launchComposer = launchComposer;
        _sessionService = sessionService;
        _launchQueue = launchQueue;
        _retryScheduler = retryScheduler;
        _externalTrackerSyncService = externalTrackerSyncService;
        _controlState = controlState;
        _eventBus = eventBus;
        _settings = settings.Value;
        _delegationSettings = delegationSettings.Value;
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
            string? delegationTokenHash = null;
            string? composedStamp = null;
            try
            {
                var resolved = await ResolveDispatchLaunchAsync(candidate, request, ct);
                spec = resolved.Launch.Spec;
                tuiProfileRevisionId = resolved.Launch.ProfileRevisionId;
                effectiveModelId = resolved.Launch.EffectiveModelId;
                delegationTokenHash = resolved.DelegationTokenHash;
                composedStamp = resolved.ComposedStamp;
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
                effectiveModelId,
                delegationTokenHash,
                composedStamp);
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

        var openTasks = await _db.AgentTasks
            .AsNoTracking()
            .Where(t => t.AgentSessionId != null
                && t.Role != AgentTaskRole.Check
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
            .Select(t => new OpenRunningTask(
                t.Id,
                t.Title,
                t.Role,
                t.Status,
                t.Kind,
                t.RootTaskId,
                t.ParentTaskId,
                t.AgentName,
                t.CardId,
                t.TokensIn,
                t.TokensOut,
                t.CostUsd,
                t.AgentSessionId!.Value))
            .ToListAsync(ct);

        var sessionIds = openTasks.Select(t => t.AgentSessionId).Distinct().ToList();
        var tasksBySession = openTasks
            .GroupBy(t => t.AgentSessionId)
            .ToDictionary(g => g.Key, g => g.First());

        var cardIds = openTasks
            .Where(t => t.CardId != null)
            .Select(t => t.CardId!.Value)
            .Distinct()
            .ToList();
        var taskCards = cardIds.Count == 0
            ? new Dictionary<Guid, RunningTaskCard>()
            : await _db.Cards
                .AsNoTracking()
                .Where(c => cardIds.Contains(c.Id))
                .Select(c => new RunningTaskCard(
                    c.Id,
                    c.Identifier,
                    c.Title,
                    c.BoardId,
                    c.Board.Name,
                    c.Board.TrackerKind,
                    c.Board.Project.LocalRepositoryPath))
                .ToDictionaryAsync(c => c.Id, ct);

        var parentByTaskId = new Dictionary<Guid, Guid?>();
        var rootIds = openTasks.Select(t => t.RootTaskId).Distinct().ToList();
        if (rootIds.Count > 0)
        {
            var links = await _db.AgentTasks
                .AsNoTracking()
                .Where(t => rootIds.Contains(t.RootTaskId))
                .Select(t => new { t.Id, t.ParentTaskId })
                .ToListAsync(ct);
            foreach (var link in links)
                parentByTaskId[link.Id] = link.ParentTaskId;
        }

        var scopedSessions = ApplyScope(_db.AgentSessions
            .AsNoTracking()
            .Include(s => s.Card).ThenInclude(c => c.Board).ThenInclude(b => b.Project)
            .Include(s => s.RunAttempts).ThenInclude(a => a.TokenUsage)
            .AsSplitQuery());
        // Card-spawn sessions (CardId set) plus the current session of every open non-Check
        // task. A cardless interactive terminal with no such task stays out.
        var activeSessions = await scopedSessions
            .Where(s => activeStatuses.Contains(s.Status)
                && (s.CardId != null || sessionIds.Contains(s.Id)))
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

        var candidates = new List<RunningFamilyRow>();
        foreach (var session in activeSessions)
        {
            tasksBySession.TryGetValue(session.Id, out var openTask);
            var source = session.CardId != null
                ? OrchestratorSessionSource.Card
                : OrchestratorSessionSource.Delegation;
            if (source == OrchestratorSessionSource.Delegation && openTask is null)
                continue;

            Guid? cardId;
            string? cardIdentifier;
            string? cardTitle;
            Guid? boardId;
            string? boardName;
            if (source == OrchestratorSessionSource.Card)
            {
                cardId = session.CardId;
                cardIdentifier = session.Card.Identifier;
                cardTitle = session.Card.Title;
                boardId = session.Card.BoardId;
                boardName = session.Card.Board.Name;
            }
            else if (openTask!.CardId is Guid taskCardId)
            {
                if (!taskCards.TryGetValue(taskCardId, out var taskCard)
                    || !CardInScope(taskCard.TrackerKind, taskCard.ProjectPath))
                    continue;
                cardId = taskCard.Id;
                cardIdentifier = taskCard.Identifier;
                cardTitle = taskCard.Title;
                boardId = taskCard.BoardId;
                boardName = taskCard.BoardName;
            }
            else
            {
                cardId = null;
                cardIdentifier = null;
                cardTitle = null;
                boardId = null;
                boardName = null;
            }

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

            var fromTask = source == OrchestratorSessionSource.Delegation;
            var dto = new OrchestratorRunningSessionDto(
                session.Id,
                source,
                Depth: 0,
                cardId,
                cardIdentifier,
                cardTitle,
                boardId,
                boardName,
                openTask is null ? null : ToRunningTaskDto(openTask),
                session.DefinitionName,
                session.AgentKind.ToString(),
                session.Status.ToString(),
                fromTask ? null : attempt?.Id,
                fromTask ? 0 : session.RunAttempts.Count,
                fromTask ? null : attempt?.AttemptNumber,
                fromTask ? null : attempt?.Phase.ToString(),
                session.StartedAt,
                session.LastSeenAt,
                fromTask ? null : attempt?.LastEventAt,
                Math.Max(0, (long)(now - session.StartedAt).TotalSeconds),
                fromTask ? openTask!.TokensIn : attempt?.TokenUsage?.TokensIn ?? 0,
                fromTask ? openTask!.TokensOut : attempt?.TokenUsage?.TokensOut ?? 0,
                fromTask ? openTask!.CostUsd : attempt?.TokenUsage?.CostUsd ?? 0,
                live,
                lastSequence);
            candidates.Add(new RunningFamilyRow(dto, session.StartedAt, openTask?.Id, openTask?.ParentTaskId));
        }

        var running = OrderRunningFamilies(candidates, parentByTaskId);
        var runningCardSessions = running.Count(r => r.Source == OrchestratorSessionSource.Card);
        var runningDelegateSessions = running.Count(r => r.Source == OrchestratorSessionSource.Delegation);

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
            runningCardSessions,
            runningDelegateSessions,
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
            : query.Where(s => s.CardId == null
                || s.Card.Board.TrackerKind != TrackerKind.Internal
                || (s.Card.Board.Project.LocalRepositoryPath != null
                    && s.Card.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)));
    }

    private bool CardInScope(TrackerKind trackerKind, string? localRepositoryPath)
    {
        if (string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix))
            return true;
        return trackerKind != TrackerKind.Internal
            || (localRepositoryPath != null
                && localRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix));
    }

    private static OrchestratorRunningTaskDto ToRunningTaskDto(OpenRunningTask task) =>
        new(
            task.Id,
            DelegationReportFormatter.Short(task.Id),
            task.Title,
            task.Role,
            task.Status,
            task.Kind,
            task.RootTaskId,
            task.ParentTaskId,
            task.AgentName);

    private static List<OrchestratorRunningSessionDto> OrderRunningFamilies(
        List<RunningFamilyRow> rows,
        IReadOnlyDictionary<Guid, Guid?> parentByTaskId)
    {
        var presentByTaskId = rows
            .Where(r => r.TaskId != null && r.Dto.Source != OrchestratorSessionSource.Card)
            .GroupBy(r => r.TaskId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        Guid? NearestPresentAncestor(Guid? parentId)
        {
            var seen = new HashSet<Guid>();
            while (parentId is Guid id && seen.Add(id))
            {
                if (presentByTaskId.ContainsKey(id))
                    return id;
                if (!parentByTaskId.TryGetValue(id, out parentId))
                    return null;
            }

            return null;
        }

        var roots = new List<RunningFamilyRow>();
        var children = new Dictionary<Guid, List<RunningFamilyRow>>();
        foreach (var row in rows)
        {
            if (row.Dto.Source == OrchestratorSessionSource.Card || row.TaskId is null)
            {
                roots.Add(row);
                continue;
            }

            var ancestor = NearestPresentAncestor(row.ParentTaskId);
            if (ancestor is null)
            {
                roots.Add(row);
                continue;
            }

            if (!children.TryGetValue(ancestor.Value, out var siblings))
            {
                siblings = [];
                children[ancestor.Value] = siblings;
            }

            siblings.Add(row);
        }

        roots.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        foreach (var siblings in children.Values)
            siblings.Sort((a, b) => a.StartedAt.CompareTo(b.StartedAt));

        var ordered = new List<OrchestratorRunningSessionDto>(rows.Count);
        void Emit(RunningFamilyRow row, int depth)
        {
            ordered.Add(row.Dto with { Depth = row.Dto.Source == OrchestratorSessionSource.Card ? 0 : depth });
            if (row.TaskId is Guid taskId && children.TryGetValue(taskId, out var kids))
            {
                foreach (var kid in kids)
                    Emit(kid, depth + 1);
            }
        }

        foreach (var root in roots)
            Emit(root, 0);

        return ordered;
    }

    private sealed record OpenRunningTask(
        Guid Id,
        string Title,
        AgentTaskRole Role,
        AgentTaskStatus Status,
        AgentTaskKind Kind,
        Guid RootTaskId,
        Guid? ParentTaskId,
        string? AgentName,
        Guid? CardId,
        long TokensIn,
        long TokensOut,
        decimal CostUsd,
        Guid AgentSessionId);

    private sealed record RunningTaskCard(
        Guid Id,
        string Identifier,
        string Title,
        Guid BoardId,
        string BoardName,
        TrackerKind TrackerKind,
        string? ProjectPath);

    private sealed record RunningFamilyRow(
        OrchestratorRunningSessionDto Dto,
        DateTime StartedAt,
        Guid? TaskId,
        Guid? ParentTaskId);

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
        string? effectiveModelId = null,
        string? delegationTokenHash = null,
        string? composedStamp = null)
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
            EffectiveModelId = effectiveModelId,
            DelegationTokenHash = delegationTokenHash,
            ComposedBundleStamp = composedStamp,
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
        var rows = await _db.Cards
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
            .Select(c => new DispatchCandidate(
                c.Id,
                c.Identifier,
                c.Title,
                c.Description,
                c.Importance,
                c.Urgency,
                c.DueAt,
                c.Position,
                c.CreatedAt,
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
        return rows
            .OrderBy(c => CardRanking.OrderKey(c.Importance, c.Urgency, c.DueAt, c.Position, c.CreatedAt, utcNow))
            .ToList();
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

    private async Task<DispatchLaunchResolution> ResolveDispatchLaunchAsync(
        DispatchCandidate candidate,
        StartAgentSessionRequest request,
        CancellationToken ct)
    {
        if (candidate.AssignedAgentId is { } assignedAgentId)
        {
            var agent = await _db.Agents.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == assignedAgentId, ct);
            if (agent is not null)
            {
                var composition = await _launchComposer.ComposeForAgentAsync(agent, ct);
                var launch = await AgentLaunchResolution.ResolveForAgentAsync(
                    agent,
                    _agentRegistry,
                    _launchResolver,
                    new AgentLaunchOptions(
                        Cwd: null,
                        Cols: request.Cols,
                        Rows: request.Rows,
                        ExtraArgs: composition.ExtraArgs,
                        ExtraEnv: composition.ExtraEnv),
                    ct,
                    _apiKeyEnvResolver);
                return new DispatchLaunchResolution(
                    launch, composition.DelegationTokenHash, composition.ComposedStamp);
            }
        }

        var credential = MintCardDelegationCredential();
        var defaultLaunch = await AgentLaunchResolution.ResolveDefaultAsync(
            _agentRegistry,
            _launchResolver,
            new AgentLaunchOptions(
                Cwd: null,
                Cols: request.Cols,
                Rows: request.Rows,
                ExtraArgs: null,
                ExtraEnv: credential.ExtraEnv),
            ct,
            _apiKeyEnvResolver);
        return new DispatchLaunchResolution(defaultLaunch, credential.DelegationTokenHash, null);
    }

    private AgentLaunchComposition MintCardDelegationCredential()
    {
        var (token, hash) = AgentTaskService.NewToken();
        return new AgentLaunchComposition(
            new Dictionary<string, string>
            {
                ["ANTIPHON_API"] = _delegationSettings.ApiBaseUrl,
                ["ANTIPHON_TASK_TOKEN"] = token,
            },
            [],
            hash,
            null);
    }

    private sealed record DispatchLaunchResolution(
        ResolvedAgentTuiLaunch Launch,
        string DelegationTokenHash,
        string? ComposedStamp);

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
        SessionTermination.Record(card.OwnerSession, SessionTerminationSource.SystemRequest);
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
        CardImportance Importance,
        CardUrgency Urgency,
        DateTime? DueAt,
        int? Position,
        DateTime CreatedAt,
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
            SessionTermination.Record(card.OwnerSession, SessionTerminationSource.SystemRequest);
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
