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
    private const string MemoryKilledFailureReason = "MemoryKilled: agent exceeded the configured memory limit.";
    public const string ClaudeSessionNotFoundFailureReason =
        "Claude resume session was not found. Continue from last context in this worktree or start a new Claude session.";
    private const string ClaudeSessionNotFoundNeedle = "No conversation found with session ID:";

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

            var spec = BuildRuntimeLaunchSpec(launchSpec, session, worktree.Path, resumeMode: null);
            await adapter.StartAsync(spec, ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.InitializingSession, UtcNow());
            await _db.SaveChangesAsync(ct);

            await WaitForReadyOrThrowAsync(adapter, ct);
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
                if (!await TryMarkMemoryKilledAsync(session, attempt, adapter))
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
            }
            else
            {
                var turn = await adapter.WaitForTurnCompleteAsync(ct);
                if (await TryMarkMemoryKilledAsync(session, attempt, adapter))
                {
                    // Persisted below.
                }
                else if (turn.TurnCompleted)
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

        var exitReason = AgentExitReason.Unknown;
        if (_runtime.TryRemove(sessionId, out var adapter) && adapter is not null)
        {
            exitReason = adapter.ExitReason;
            if (adapter.Exited.IsCompletedSuccessfully)
                session.ExitCode = adapter.Exited.Result;

            await adapter.DisposeAsync();
        }

        var memoryKilled = exitReason == AgentExitReason.MemoryKilled;
        session.Status = memoryKilled
            ? SessionStatus.Failed
            : killed ? SessionStatus.Stopped : SessionStatus.Failed;
        session.EndedAt = UtcNow();
        session.LastSeenAt = session.EndedAt.Value;
        session.FailureReason = memoryKilled
            ? MemoryKilledFailureReason
            : killed ? null : "Agent process did not exit within the configured grace period.";

        var attempt = await _db.RunAttempts
            .Where(a => a.AgentSessionId == sessionId && a.CompletedAt == null)
            .OrderByDescending(a => a.AttemptNumber)
            .FirstOrDefaultAsync(ct);
        if (attempt is not null && !RunAttemptStateMachine.IsTerminal(attempt.Phase))
        {
            RunAttemptStateMachine.Transition(
                attempt,
                memoryKilled ? RunPhase.Failed : killed ? RunPhase.Canceled : RunPhase.Failed,
                UtcNow());
            attempt.ExitCode = session.ExitCode;
            attempt.ErrorDetails = memoryKilled
                ? MemoryKilledFailureReason
                : killed ? "Agent session was killed by request."
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

    public async Task<AgentSessionResumeResult> ResumeAsync(
        Guid sessionId,
        AgentLaunchSpec launchSpec,
        AgentSessionResumeMode resumeMode,
        CancellationToken ct)
    {
        if (!Enum.IsDefined(resumeMode))
            throw new ValidationException(nameof(resumeMode), "Resume mode is not supported.");

        var session = await _db.AgentSessions
            .Include(s => s.Card)
            .Include(s => s.Worktree)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);

        if (session.AgentKind != AgentKind.ClaudeCode)
            throw new ConflictException("Only Claude Code sessions can be resumed.");
        if (session.Status is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping)
            throw new ConflictException($"Agent session '{sessionId}' is already active.");
        if (_runtime.ListLiveSessions().Contains(sessionId))
            throw new ConflictException($"Agent session '{sessionId}' is already running.");

        var activeOtherSession = await _db.AgentSessions.AnyAsync(
            s => s.CardId == session.CardId
                && s.Id != session.Id
                && (s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running
                    || s.Status == SessionStatus.Stopping),
            ct);
        if (activeOtherSession)
            throw new ConflictException($"Card '{session.Card.Identifier}' already has an active agent session.");

        var cwd = !string.IsNullOrWhiteSpace(session.Worktree?.Path)
            ? session.Worktree.Path
            : session.Cwd;
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
            throw new ConflictException($"Agent session '{sessionId}' has no usable worktree path to resume.");

        var now = UtcNow();
        session.Status = SessionStatus.Starting;
        session.StartedAt = now;
        session.LastSeenAt = now;
        session.EndedAt = null;
        session.ExitCode = null;
        session.FailureReason = null;
        session.Cwd = cwd;
        session.Card.OwnerSessionId = session.Id;
        session.Card.ConcurrencyToken = Guid.NewGuid();
        session.Card.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        IAgentProtocolAdapter? adapter = null;
        var runtimeRegistered = false;
        try
        {
            adapter = _adapterFactory.Create(session.AgentKind);
            _runtime.Register(session.Id, adapter);
            runtimeRegistered = true;

            var spec = BuildRuntimeLaunchSpec(launchSpec, session, cwd, resumeMode);
            await adapter.StartAsync(spec, ct);
            await WaitForReadyOrThrowAsync(adapter, ct);

            session.Status = SessionStatus.Running;
            session.LastSeenAt = UtcNow();
            await _db.SaveChangesAsync(ct);

            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(session.Id),
                "SessionResumed",
                new { sessionId = session.Id, cardId = session.CardId },
                ct);
            await _eventBus.PublishToAllAsync(
                "CardChanged",
                new { boardId = session.Card.BoardId, cardId = session.CardId },
                ct);

            return new AgentSessionResumeResult(session.Id, session.CardId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to resume agent session {SessionId}", session.Id);
            var sessionNotFound = IsClaudeSessionNotFound(adapter, ex);
            var failureReason = sessionNotFound
                ? ClaudeSessionNotFoundFailureReason
                : ex.Message;
            session.Status = SessionStatus.Failed;
            session.FailureReason = failureReason;
            session.EndedAt = UtcNow();
            session.LastSeenAt = session.EndedAt.Value;
            session.Card.OwnerSessionId = null;
            session.Card.ConcurrencyToken = Guid.NewGuid();
            session.Card.UpdatedAt = session.EndedAt.Value;
            await _db.SaveChangesAsync(CancellationToken.None);

            if (runtimeRegistered)
                await _runtime.DisposeSessionAsync(session.Id);
            else if (adapter is not null)
                await adapter.DisposeAsync();

            if (sessionNotFound)
                throw new ConflictException(ClaudeSessionNotFoundFailureReason);

            throw;
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

    public string GetBuffer(Guid sessionId) => _runtime.GetBufferSnapshot(sessionId).Buffer;

    public async Task<AgentSessionBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var exists = await _db.AgentSessions.AnyAsync(s => s.Id == sessionId, ct);
        if (!exists)
            throw new NotFoundException(nameof(AgentSession), sessionId);

        var snapshot = _runtime.GetBufferSnapshot(sessionId);
        return new AgentSessionBufferDto(sessionId, snapshot.Buffer, snapshot.LastSequence);
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

    private AgentLaunchSpec BuildRuntimeLaunchSpec(
        AgentLaunchSpec launchSpec,
        AgentSession session,
        string cwd,
        AgentSessionResumeMode? resumeMode)
    {
        var args = session.AgentKind == AgentKind.ClaudeCode
            ? BuildClaudeSessionArgs(launchSpec.Args, session.Id, resumeMode)
            : launchSpec.Args;

        return launchSpec with
        {
            Args = args,
            Cwd = cwd,
            Cols = session.Cols,
            Rows = session.Rows,
            MemoryLimitMb = _settings.MemoryLimitMb
        };
    }

    private static IReadOnlyList<string> BuildClaudeSessionArgs(
        IReadOnlyList<string> args,
        Guid sessionId,
        AgentSessionResumeMode? resumeMode)
    {
        var filtered = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (IsClaudeSessionArg(arg))
            {
                if (ClaudeSessionArgConsumesValue(arg)
                    && i + 1 < args.Count
                    && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                }
                continue;
            }

            filtered.Add(arg);
        }

        if (resumeMode == AgentSessionResumeMode.Continue)
        {
            filtered.Add("--continue");
            return filtered.AsReadOnly();
        }

        filtered.Add(resumeMode == AgentSessionResumeMode.Resume ? "--resume" : "--session-id");
        filtered.Add(sessionId.ToString("D"));
        return filtered.AsReadOnly();
    }

    private static bool ClaudeSessionArgConsumesValue(string arg) =>
        arg == "--session-id"
        || arg == "--resume"
        || arg == "-r";

    private static bool IsClaudeSessionArg(string arg) =>
        arg == "--session-id"
        || arg == "--resume"
        || arg == "-r"
        || arg == "--continue"
        || arg == "-c"
        || arg.StartsWith("--session-id=", StringComparison.Ordinal)
        || arg.StartsWith("--resume=", StringComparison.Ordinal)
        || arg.StartsWith("--continue=", StringComparison.Ordinal);

    private static async Task WaitForReadyOrThrowAsync(IAgentProtocolAdapter adapter, CancellationToken ct)
    {
        if (!await adapter.WaitForReadyAsync(ct))
            throw new InvalidOperationException("Agent process did not become ready.");
    }

    private static bool IsClaudeSessionNotFound(IAgentProtocolAdapter? adapter, Exception ex)
    {
        if (ex.Message.Contains(ClaudeSessionNotFoundNeedle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (adapter is null)
            return false;

        try
        {
            return adapter.SnapshotRawOutput().Contains(ClaudeSessionNotFoundNeedle, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
            catch (Exception ex) when (ex is NotFoundException or ValidationException)
            {
                // Older rows may predate sidecar metadata or the current worktree root. DB timestamp is still updated.
                _logger.LogDebug(ex, "Skipping optional touch for existing worktree {WorktreePath}", existing.Path);
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

    private async Task<bool> TryMarkMemoryKilledAsync(
        AgentSession session,
        RunAttempt attempt,
        IAgentProtocolAdapter adapter)
    {
        if (adapter.ExitReason != AgentExitReason.MemoryKilled)
            return false;

        if (!RunAttemptStateMachine.IsTerminal(attempt.Phase))
            RunAttemptStateMachine.Transition(attempt, RunPhase.Failed, UtcNow());

        session.Status = SessionStatus.Failed;
        session.EndedAt = UtcNow();
        session.LastSeenAt = session.EndedAt.Value;
        session.FailureReason = MemoryKilledFailureReason;
        if (adapter.Exited.IsCompletedSuccessfully)
            session.ExitCode = adapter.Exited.Result;
        attempt.ExitCode = session.ExitCode;
        attempt.ErrorDetails = MemoryKilledFailureReason;
        await _runtime.DisposeSessionAsync(session.Id);
        return true;
    }

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
