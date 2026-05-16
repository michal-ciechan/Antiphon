using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class AgentSessionService
{
    private readonly AppDbContext _db;
    private readonly IWorktreeManager _worktreeManager;
    private readonly WorkspaceHookService _hookService;
    private readonly IAgentProtocolAdapterFactory _adapterFactory;
    private readonly AgentSessionRuntime _runtime;
    private readonly IEventBus _eventBus;
    private readonly AgentSessionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentSessionService> _logger;

    public AgentSessionService(
        AppDbContext db,
        IWorktreeManager worktreeManager,
        WorkspaceHookService hookService,
        IAgentProtocolAdapterFactory adapterFactory,
        AgentSessionRuntime runtime,
        IEventBus eventBus,
        IOptions<AgentSessionSettings> settings,
        TimeProvider timeProvider,
        ILogger<AgentSessionService> logger)
    {
        _db = db;
        _worktreeManager = worktreeManager;
        _hookService = hookService;
        _adapterFactory = adapterFactory;
        _runtime = runtime;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AgentSessionStartResult> StartAsync(
        StartAgentSessionRequest request,
        AgentLaunchSpec launchSpec,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ValidationException(nameof(request.Prompt), "Prompt must not be empty.");

        var card = await LoadCardAsync(request.CardId, ct);
        var hasActiveSession = await _db.AgentSessions.AnyAsync(
            s => s.CardId == card.Id
                && (s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running
                    || s.Status == SessionStatus.Stopping)
                && (request.PreclaimedSessionId == null || s.Id != request.PreclaimedSessionId.Value),
            ct);
        if (hasActiveSession)
            throw new ConflictException($"Card '{card.Identifier}' already has an active agent session.");

        var now = UtcNow();
        var activeDefinition = card.Board.WorkflowDefinitions
            .FirstOrDefault(d => request.BoardWorkflowDefinitionId is not null && d.Id == request.BoardWorkflowDefinitionId)
            ?? card.Board.WorkflowDefinitions
                .Where(d => d.IsActive)
                .OrderByDescending(d => d.Version)
                .FirstOrDefault();
        if (request.BoardWorkflowDefinitionId is not null && activeDefinition?.Id != request.BoardWorkflowDefinitionId)
        {
            throw new ValidationException(
                nameof(request.BoardWorkflowDefinitionId),
                "Pinned board workflow definition does not belong to this card's board.");
        }
        var hooks = ParseHooks(activeDefinition);
        var prompt = request.Prompt;

        var attempt = new RunAttempt
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            BoardWorkflowDefinitionId = activeDefinition?.Id,
            AttemptNumber = await NextAttemptNumberAsync(card.Id, ct),
            Phase = RunPhase.PreparingWorkspace,
            CreatedAt = now,
            StartedAt = now,
            LastEventAt = now,
            PhaseStartedAt = now,
            Prompt = prompt,
            Card = card
        };
        _db.RunAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        AgentSession? session = null;
        IAgentProtocolAdapter? adapter = null;
        var runtimeRegistered = false;

        try
        {
            var (worktree, createdWorktree) = await ResolveOrCreateWorktreeAsync(card, attempt, ct);

            var hookContext = new WorkspaceHookContext(
                worktree.Path,
                CardId: card.Identifier,
                WorktreePath: worktree.Path);
            if (createdWorktree)
                await _hookService.RunAfterCreateAsync(hookContext, hooks, ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.BuildingPrompt, UtcNow());
            prompt = BuildLaunchPrompt(request, card, worktree, activeDefinition);
            attempt.Prompt = prompt;
            await _db.SaveChangesAsync(ct);

            await _hookService.RunBeforeRunAsync(hookContext, hooks, ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.LaunchingAgent, UtcNow());
            await _db.SaveChangesAsync(ct);

            session = await ResolveSessionAsync(request, card, worktree, ct);
            card.OwnerSessionId = session.Id;
            attempt.AgentSessionId = session.Id;
            attempt.AgentSession = session;
            await _db.SaveChangesAsync(ct);

            adapter = _adapterFactory.Create(request.AgentKind);
            _runtime.Register(session.Id, adapter);
            runtimeRegistered = true;

            var spec = launchSpec with
            {
                Cwd = worktree.Path,
                Cols = request.Cols,
                Rows = request.Rows
            };
            await adapter.StartAsync(spec, ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.InitializingSession, UtcNow());
            await _db.SaveChangesAsync(ct);

            await adapter.WaitForReadyAsync(ct);
            session.Status = SessionStatus.Running;
            session.LastSeenAt = UtcNow();
            await _db.SaveChangesAsync(ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.StreamingTurn, UtcNow());
            await _db.SaveChangesAsync(ct);

            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(session.Id),
                "SessionStarted",
                new { sessionId = session.Id, cardId = card.Id, runAttemptId = attempt.Id },
                ct);
            await _eventBus.PublishToAllAsync(
                "CardChanged",
                new { boardId = card.BoardId, cardId = card.Id },
                ct);

            await adapter.SendPromptAsync(prompt, ct);
            var firstDeltaReceived = await adapter.WaitForFirstPromptOutputAsync(
                TimeSpan.FromMilliseconds(Math.Max(100, _settings.FirstDeltaTimeoutMs)),
                ct);

            attempt.LastEventAt = UtcNow();
            session.LastSeenAt = UtcNow();
            if (!firstDeltaReceived)
            {
                RunAttemptStateMachine.Transition(attempt, RunPhase.TimedOut, UtcNow());
                session.Status = SessionStatus.Failed;
                session.FailureReason = "Timed out waiting for first agent output.";
                session.EndedAt = UtcNow();
                await _runtime.KillAsync(
                    session.Id,
                    TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)),
                    ct);
                await _runtime.DisposeSessionAsync(session.Id);
            }
            else
            {
                var turn = await adapter.WaitForTurnCompleteAsync(ct);
                if (turn.TurnCompleted)
                {
                    RunAttemptStateMachine.Transition(attempt, RunPhase.Finishing, UtcNow());
                    await _db.SaveChangesAsync(ct);

                    await _hookService.RunAfterRunAsync(hookContext, hooks, ct);

                    RunAttemptStateMachine.Transition(attempt, RunPhase.Succeeded, UtcNow());
                }
                else
                {
                    RunAttemptStateMachine.Transition(attempt, RunPhase.TimedOut, UtcNow());
                    session.Status = SessionStatus.Failed;
                    session.FailureReason = "Timed out waiting for the agent turn to complete.";
                    session.EndedAt = UtcNow();
                    await _runtime.KillAsync(
                        session.Id,
                        TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)),
                        ct);
                    await _runtime.DisposeSessionAsync(session.Id);
                }
            }

            await _db.SaveChangesAsync(ct);

            return new AgentSessionStartResult(session.Id, attempt.Id, worktree.Id, firstDeltaReceived);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to start agent session for card {CardId}", card.Id);
            attempt.ErrorDetails = ex.Message;
            if (!RunAttemptStateMachine.IsTerminal(attempt.Phase))
                RunAttemptStateMachine.Transition(attempt, RunPhase.Failed, UtcNow());

            if (session is not null)
            {
                session.Status = SessionStatus.Failed;
                session.FailureReason = ex.Message;
                session.EndedAt = UtcNow();
            }

            await _db.SaveChangesAsync(CancellationToken.None);

            if (runtimeRegistered && session is not null)
                await _runtime.DisposeSessionAsync(session.Id);
            else if (adapter is not null)
                await adapter.DisposeAsync();

            throw;
        }
    }

    public async Task KillAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);

        session.Status = SessionStatus.Stopping;
        session.LastSeenAt = UtcNow();
        await _db.SaveChangesAsync(ct);

        var killed = await _runtime.KillAsync(
            sessionId,
            TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)),
            ct);

        session.Status = killed ? SessionStatus.Stopped : SessionStatus.Failed;
        session.EndedAt = UtcNow();
        session.LastSeenAt = session.EndedAt.Value;
        session.FailureReason = killed ? null : "Agent process did not exit within the configured grace period.";

        if (_runtime.TryRemove(sessionId, out var adapter) && adapter is not null)
        {
            if (adapter.Exited.IsCompletedSuccessfully)
                session.ExitCode = adapter.Exited.Result;

            await adapter.DisposeAsync();
        }

        var attempt = await _db.RunAttempts
            .Where(a => a.AgentSessionId == sessionId && a.CompletedAt == null)
            .OrderByDescending(a => a.AttemptNumber)
            .FirstOrDefaultAsync(ct);
        if (attempt is not null && !RunAttemptStateMachine.IsTerminal(attempt.Phase))
        {
            RunAttemptStateMachine.Transition(
                attempt,
                killed ? RunPhase.Canceled : RunPhase.Failed,
                UtcNow());
            attempt.ExitCode = session.ExitCode;
            attempt.ErrorDetails = killed
                ? "Agent session was killed by request."
                : session.FailureReason;
        }

        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(sessionId),
            "SessionExited",
            new { sessionId, status = session.Status.ToString(), session.ExitCode },
            ct);
        var card = await _db.Cards
            .AsNoTracking()
            .Where(c => c.Id == session.CardId)
            .Select(c => new { c.BoardId, c.Id })
            .FirstOrDefaultAsync(ct);
        if (card is not null)
        {
            await _eventBus.PublishToAllAsync(
                "CardChanged",
                new { boardId = card.BoardId, cardId = card.Id },
                ct);
        }
    }

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _runtime.SendInputAsync(sessionId, input, ct);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        if (cols <= 0 || rows <= 0)
            throw new ValidationException("size", "Terminal cols and rows must be positive.");

        return ResizeAndPersistAsync(sessionId, cols, rows, ct);
    }

    public string GetBuffer(Guid sessionId) => _runtime.GetBuffer(sessionId);

    public async Task<AgentSessionBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var exists = await _db.AgentSessions.AnyAsync(s => s.Id == sessionId, ct);
        if (!exists)
            throw new NotFoundException(nameof(AgentSession), sessionId);

        return new AgentSessionBufferDto(
            sessionId,
            _runtime.GetBuffer(sessionId),
            _runtime.GetDeltaSequenceOrDefault(sessionId));
    }

    private async Task<Card> LoadCardAsync(Guid cardId, CancellationToken ct)
    {
        return await _db.Cards
            .Include(c => c.Board).ThenInclude(b => b.Project)
            .Include(c => c.Board).ThenInclude(b => b.WorkflowDefinitions)
            .Include(c => c.CurrentWorktree)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
    }

    private async Task<AgentSession> ResolveSessionAsync(
        StartAgentSessionRequest request,
        Card card,
        Worktree worktree,
        CancellationToken ct)
    {
        var now = UtcNow();
        if (request.PreclaimedSessionId is Guid preclaimedSessionId)
        {
            var preclaimed = await _db.AgentSessions
                .FirstOrDefaultAsync(s => s.Id == preclaimedSessionId && s.CardId == card.Id, ct)
                ?? throw new NotFoundException(nameof(AgentSession), preclaimedSessionId);
            if (preclaimed.Status != SessionStatus.Starting)
                throw new ConflictException($"Preclaimed agent session '{preclaimedSessionId}' is not in Starting state.");

            preclaimed.WorktreeId = worktree.Id;
            preclaimed.DefinitionName = request.DefinitionName;
            preclaimed.AgentKind = request.AgentKind;
            preclaimed.Cwd = worktree.Path;
            preclaimed.Cols = request.Cols;
            preclaimed.Rows = request.Rows;
            preclaimed.LastSeenAt = now;
            preclaimed.Worktree = worktree;
            return preclaimed;
        }

        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            WorktreeId = worktree.Id,
            DefinitionName = request.DefinitionName,
            AgentKind = request.AgentKind,
            Status = SessionStatus.Starting,
            Cwd = worktree.Path,
            Cols = request.Cols,
            Rows = request.Rows,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            Card = card,
            Worktree = worktree
        };
        _db.AgentSessions.Add(session);
        return session;
    }

    private async Task<(Worktree Worktree, bool Created)> ResolveOrCreateWorktreeAsync(
        Card card,
        RunAttempt attempt,
        CancellationToken ct)
    {
        var now = UtcNow();
        if (card.CurrentWorktree is { Status: WorktreeStatus.Active } existing
            && Directory.Exists(existing.Path))
        {
            existing.LastTouchedAt = now;
            attempt.WorktreeId = existing.Id;
            attempt.Worktree = existing;
            await _db.SaveChangesAsync(ct);
            try
            {
                await _worktreeManager.TouchAsync(existing.Path, ct);
            }
            catch (NotFoundException)
            {
                // Older rows or tests may not have sidecar metadata yet; DB timestamp is still updated.
            }
            return (existing, Created: false);
        }

        var repoPath = await ResolveRepoPathAsync(card.Board.Project, ct);
        WorktreeInfo worktreeInfo;
        try
        {
            worktreeInfo = await _worktreeManager.CreateAsync(
                repoPath,
                card.Identifier,
                card.Board.Project.BaseBranch,
                ct);
        }
        catch (ConflictException)
        {
            var orphanedWorktree = (await _worktreeManager.ListAsync(repoPath, ct))
                .FirstOrDefault(w => w.CardId == card.Identifier && Directory.Exists(w.Path));
            if (orphanedWorktree is null)
                throw;

            worktreeInfo = orphanedWorktree;
        }

        var worktree = new Worktree
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            RepoPath = worktreeInfo.RepoPath,
            Path = worktreeInfo.Path,
            Branch = worktreeInfo.Branch,
            BaseRef = worktreeInfo.BaseRef,
            Status = WorktreeStatus.Active,
            CreatedAt = worktreeInfo.CreatedAt.UtcDateTime,
            LastTouchedAt = worktreeInfo.LastTouchedAt.UtcDateTime,
            Card = card
        };
        _db.Worktrees.Add(worktree);
        card.CurrentWorktreeId = worktree.Id;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = now;
        attempt.WorktreeId = worktree.Id;
        attempt.Worktree = worktree;
        await _db.SaveChangesAsync(ct);
        return (worktree, Created: true);
    }

    private async Task<int> NextAttemptNumberAsync(Guid cardId, CancellationToken ct)
    {
        var last = await _db.RunAttempts
            .Where(a => a.CardId == cardId)
            .MaxAsync(a => (int?)a.AttemptNumber, ct);
        return (last ?? 0) + 1;
    }

    private static Task<string> ResolveRepoPathAsync(Project project, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(project.LocalRepositoryPath))
            throw new ValidationException(nameof(Project.LocalRepositoryPath), "Project local repository path is required to start an agent session.");

        return Task.FromResult(project.LocalRepositoryPath);
    }

    private static string BuildLaunchPrompt(
        StartAgentSessionRequest request,
        Card card,
        Worktree worktree,
        BoardWorkflowDefinition? activeDefinition)
    {
        if (!request.UseWorkflowPrompt
            || activeDefinition is null
            || !WorkflowDefinitionLoader.TryParseContent(activeDefinition.Content, out var definition, out _)
            || definition is null)
        {
            return request.Prompt;
        }

        return WorkflowDefinitionLoader.RenderPrompt(
            definition.PromptMarkdown,
            WorkflowDefinitionLoader.BuildPromptVariables(card, worktree));
    }

    private static WorkflowHooks ParseHooks(BoardWorkflowDefinition? activeDefinition)
    {
        if (activeDefinition is null
            || string.IsNullOrWhiteSpace(activeDefinition.Content)
            || !activeDefinition.Content.Contains("hooks:", StringComparison.Ordinal))
        {
            return WorkflowHooks.Empty;
        }

        if (WorkflowDefinitionLoader.TryParseContent(activeDefinition.Content, out var definition, out _) && definition is not null)
            return definition.Hooks;

        if (activeDefinition.Content.Contains("stages:", StringComparison.Ordinal))
            return WorkflowDefinitionParser.ParseYamlDefinition(activeDefinition.Content).Hooks;

        return WorkflowDefinitionParser.ParseYamlHooks(activeDefinition.Content);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private async Task ResizeAndPersistAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        await _runtime.ResizeAsync(sessionId, cols, rows, ct);

        var session = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);
        session.Cols = cols;
        session.Rows = rows;
        session.LastSeenAt = UtcNow();
        await _db.SaveChangesAsync(ct);
    }
}
