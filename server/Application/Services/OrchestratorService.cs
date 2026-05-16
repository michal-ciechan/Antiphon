using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class OrchestratorService
{
    private readonly AppDbContext _db;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentSessionService _sessionService;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly RetryScheduler _retryScheduler;
    private readonly OrchestratorControlState _controlState;
    private readonly IEventBus _eventBus;
    private readonly OrchestratorSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrchestratorService> _logger;

    public OrchestratorService(
        AppDbContext db,
        AgentRegistry agentRegistry,
        AgentSessionService sessionService,
        AgentSessionLaunchQueue launchQueue,
        RetryScheduler retryScheduler,
        OrchestratorControlState controlState,
        IEventBus eventBus,
        IOptions<OrchestratorSettings> settings,
        TimeProvider timeProvider,
        ILogger<OrchestratorService> logger)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _sessionService = sessionService;
        _launchQueue = launchQueue;
        _retryScheduler = retryScheduler;
        _controlState = controlState;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
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
            try
            {
                spec = _agentRegistry.Resolve(request.DefinitionName, new AgentLaunchOptions(
                    Cwd: null,
                    Cols: request.Cols,
                    Rows: request.Rows,
                    ExtraArgs: null,
                    ExtraEnv: null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to resolve agent definition {DefinitionName}", request.DefinitionName);
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
                ct);
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
        var runningSessions = await _db.AgentSessions
            .Where(s => s.Card.Board.TrackerKind == TrackerKind.Internal)
            .Where(s => string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
                || (s.Card.Board.Project.LocalRepositoryPath != null
                    && s.Card.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)))
            .CountAsync(s => activeStatuses.Contains(s.Status), ct);
        var retryQueueLength = await _db.RetrySchedules
            .Where(r => r.Card.Board.TrackerKind == TrackerKind.Internal)
            .Where(r => string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
                || (r.Card.Board.Project.LocalRepositoryPath != null
                    && r.Card.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)))
            .CountAsync(r => r.AttemptCount < r.MaxAttempts
                && r.NextRetryAt != null
                && r.NextRetryAt <= now, ct);

        return new OrchestratorStateDto(_controlState.IsPaused, runningSessions, retryQueueLength);
    }

    public OrchestratorPauseResult Pause() => new(_controlState.Pause());

    public OrchestratorPauseResult Resume() => new(_controlState.Resume());

    internal async Task<Guid?> TryClaimCardAsync(
        Guid cardId,
        Guid concurrencyToken,
        string definitionName,
        AgentKind agentKind,
        int cols,
        int rows,
        DateTime utcNow,
        CancellationToken ct)
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
            LastSeenAt = utcNow
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
            .Include(c => c.OwnerSession)
            .Where(c => c.Board.TrackerKind == TrackerKind.Internal)
            .Where(c => string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
                || (c.Board.Project.LocalRepositoryPath != null
                    && c.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)))
            .Where(c => c.OwnerSessionId != null)
            .ToListAsync(ct);

        var reconciled = 0;
        foreach (var card in claimedCards)
        {
            ct.ThrowIfCancellationRequested();
            if (card.OwnerSession is null)
            {
                ClearCardClaim(card, utcNow);
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
            await _db.SaveChangesAsync(ct);

        return reconciled;
    }

    private async Task<IReadOnlyList<DispatchCandidate>> LoadEligibleCandidatesAsync(
        DateTime utcNow,
        CancellationToken ct)
    {
        var activeStatuses = ActiveSessionStatuses();
        return await _db.Cards
            .AsNoTracking()
            .Where(c => c.Board.TrackerKind == TrackerKind.Internal)
            .Where(c => string.IsNullOrWhiteSpace(_settings.InternalTrackerRepositoryPathPrefix)
                || (c.Board.Project.LocalRepositoryPath != null
                    && c.Board.Project.LocalRepositoryPath.StartsWith(_settings.InternalTrackerRepositoryPathPrefix)))
            .Where(c => c.BoardColumn.IsActive && !c.BoardColumn.IsTerminal)
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
            var definitionName = _agentRegistry.Settings.DefaultDefinition;
            return new StartAgentSessionRequest(
                candidate.CardId,
                definitionName,
                AgentKind.Raw,
                BuildPrompt(candidate),
                _settings.DefaultCols,
                _settings.DefaultRows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to prepare card {CardId} for dispatch", candidate.CardId);
            await _retryScheduler.ScheduleFailureAsync(_db, candidate.CardId, ex.Message, utcNow, ct);
            await _db.SaveChangesAsync(ct);
            return null;
        }
    }

    private static void ClearCardClaim(Card card, DateTime utcNow)
    {
        card.OwnerSessionId = null;
        card.OwnerSession = null;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = utcNow;
    }

    private static string BuildPrompt(DispatchCandidate candidate)
    {
        var workflow = string.IsNullOrWhiteSpace(candidate.WorkflowContent)
            ? null
            : WorkflowDefinitionParser.ParseYamlDefinition(ExtractWorkflowYaml(candidate.WorkflowContent));
        var prompt = $"""
            Work on card {candidate.Identifier}: {candidate.Title}

            Description:
            {candidate.Description}
            """;

        if (workflow is null)
            return prompt;

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

    private static string ExtractWorkflowYaml(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
            return content;

        using var reader = new StringReader(content);
        var first = reader.ReadLine();
        if (first != "---")
            return content;

        var yamlLines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line == "---")
                break;

            yamlLines.Add(line);
        }

        return yamlLines.Count == 0 ? content : string.Join(Environment.NewLine, yamlLines);
    }

    private static SessionStatus[] ActiveSessionStatuses() =>
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

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
        string? WorkflowContent);

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
