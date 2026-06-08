using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class CardService
{
    private readonly AppDbContext _db;
    private readonly AgentRegistry _agentRegistry;
    private readonly OrchestratorService _orchestrator;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public CardService(
        AppDbContext db,
        AgentRegistry agentRegistry,
        OrchestratorService orchestrator,
        AgentSessionLaunchQueue launchQueue,
        IEventBus eventBus,
        TimeProvider timeProvider)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _orchestrator = orchestrator;
        _launchQueue = launchQueue;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<CardDto> CreateAsync(Guid boardId, CreateCardRequest request, CancellationToken ct)
    {
        ValidateCreateRequest(request);

        var board = await _db.Boards
            .Include(b => b.Columns)
            .Include(b => b.Cards)
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException(nameof(Board), boardId);

        var column = request.BoardColumnId is Guid columnId
            ? board.Columns.FirstOrDefault(c => c.Id == columnId)
            : board.Columns.OrderBy(c => c.ColumnOrder).FirstOrDefault(c => c.CardStatus == CardStatus.Backlog)
                ?? board.Columns.OrderBy(c => c.ColumnOrder).FirstOrDefault();
        if (column is null)
            throw new ValidationException(nameof(request.BoardColumnId), "Board must have at least one column.");

        var now = UtcNow();
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = await NextIdentifierAsync(board.Id, ct),
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Priority = request.Priority,
            LabelsJson = BoardService.SerializeLabels(request.Labels),
            Status = column.CardStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Cards.Add(card);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = board.Id, cardId = card.Id }, ct);

        return await GetByIdAsync(card.Id, ct);
    }

    public async Task<CardDto> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var card = await LoadCardAsync(id, ct);
        return BoardService.ToCardDto(card);
    }

    public async Task<CardDto> MoveAsync(Guid id, MoveCardRequest request, CancellationToken ct)
    {
        var card = await LoadCardForUpdateAsync(id, ct);
        if (request.ConcurrencyToken == Guid.Empty)
            throw new ValidationException(nameof(request.ConcurrencyToken), "Card concurrency token is required.");
        if (request.ConcurrencyToken != card.ConcurrencyToken)
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");

        var targetColumn = await _db.BoardColumns
            .FirstOrDefaultAsync(c => c.Id == request.BoardColumnId, ct)
            ?? throw new NotFoundException(nameof(BoardColumn), request.BoardColumnId);
        if (targetColumn.BoardId != card.BoardId)
            throw new ValidationException(nameof(request.BoardColumnId), "Target column belongs to a different board.");

        ApplyColumnMove(card, targetColumn);
        await _db.SaveChangesAsync(ct);

        if (targetColumn.IsActive && card.OwnerSessionId is null)
            await SpawnAsync(card.Id, new SpawnCardRequest(), ct);

        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
        return await GetByIdAsync(card.Id, ct);
    }

    public async Task<SpawnCardResult> SpawnAsync(Guid id, SpawnCardRequest request, CancellationToken ct)
    {
        ValidateSpawnRequest(request);

        var card = await LoadCardForUpdateAsync(id, ct);
        if (request.ConcurrencyToken is Guid requestedToken && requestedToken != card.ConcurrencyToken)
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");

        if (card.BoardColumn.IsTerminal)
            throw new ConflictException($"Card '{card.Identifier}' is already in a terminal column.");

        if (!card.BoardColumn.IsActive)
        {
            var activeColumn = card.Board.Columns
                .OrderBy(c => c.ColumnOrder)
                .FirstOrDefault(c => c.IsActive && !c.IsTerminal)
                ?? throw new ConflictException($"Board '{card.Board.Name}' has no active column for spawning.");
            ApplyColumnMove(card, activeColumn, enforceStateMachine: false);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");
            }
        }

        var definitionName = string.IsNullOrWhiteSpace(request.DefinitionName)
            ? _agentRegistry.Settings.DefaultDefinition
            : request.DefinitionName.Trim();
        var spec = _agentRegistry.Resolve(definitionName, new AgentLaunchOptions(
            Cwd: null,
            Cols: request.Cols,
            Rows: request.Rows,
            ExtraArgs: null,
            ExtraEnv: null));

        var sessionId = await _orchestrator.TryClaimCardAsync(
            card.Id,
            card.ConcurrencyToken,
            definitionName,
            spec.Kind,
            request.Cols,
            request.Rows,
            UtcNow(),
            ct);
        if (sessionId is null)
            throw new ConflictException($"Card '{card.Identifier}' is already claimed by another session.");

        var activeDefinition = card.Board.WorkflowDefinitions
            .Where(d => d.IsActive)
            .OrderByDescending(d => d.Version)
            .FirstOrDefault();
        var useWorkflowPrompt = string.IsNullOrWhiteSpace(request.Prompt)
            && IsMarkdownWorkflow(activeDefinition?.Content);
        var prompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? BuildPrompt(card, activeDefinition)
            : request.Prompt.Trim();
        _launchQueue.EnqueueInteractive(
            new StartAgentSessionRequest(
                card.Id,
                definitionName,
                spec.Kind,
                prompt,
                request.Cols,
                request.Rows,
                PreclaimedSessionId: sessionId,
                BoardWorkflowDefinitionId: activeDefinition?.Id,
                UseWorkflowPrompt: useWorkflowPrompt,
                RemoteControlName: request.RemoteControlName),
            spec);

        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
        return new SpawnCardResult(card.Id, sessionId.Value);
    }

    private async Task<Card> LoadCardAsync(Guid id, CancellationToken ct)
    {
        return await _db.Cards
            .AsNoTracking()
            .Include(c => c.AgentSessions)
            .Include(c => c.AssignedAgent)
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Card), id);
    }

    private async Task<Card> LoadCardForUpdateAsync(Guid id, CancellationToken ct)
    {
        return await _db.Cards
            .Include(c => c.Board).ThenInclude(b => b.Columns)
            .Include(c => c.Board).ThenInclude(b => b.WorkflowDefinitions)
            .Include(c => c.BoardColumn)
            .Include(c => c.AgentSessions)
            .Include(c => c.AssignedAgent)
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Card), id);
    }

    private void ApplyColumnMove(Card card, BoardColumn targetColumn, bool enforceStateMachine = true)
    {
        if (card.BoardColumnId == targetColumn.Id)
            return;

        if (enforceStateMachine
            && card.Status != targetColumn.CardStatus
            && !CardStateMachine.CanTransition(card.Status, targetColumn.CardStatus))
        {
            throw new ValidationException(
                nameof(targetColumn.CardStatus),
                $"Cannot move card from {card.Status} to {targetColumn.CardStatus}.");
        }

        var now = UtcNow();
        card.BoardColumnId = targetColumn.Id;
        card.BoardColumn = targetColumn;
        card.Status = targetColumn.CardStatus;
        card.UpdatedAt = now;
        card.ConcurrencyToken = Guid.NewGuid();

        if (targetColumn.IsActive)
            card.StartedAt ??= now;
        if (targetColumn.IsTerminal)
        {
            card.CompletedAt ??= now;
            card.TerminalReason ??= "Moved to terminal column.";
        }
        else
        {
            card.CompletedAt = null;
            card.TerminalReason = null;
        }
    }

    private async Task<string> NextIdentifierAsync(Guid boardId, CancellationToken ct)
    {
        var count = await _db.Cards.CountAsync(c => c.BoardId == boardId, ct);
        return $"CARD-{count + 1:0000}";
    }

    private static string BuildPrompt(Card card, BoardWorkflowDefinition? activeDefinition)
    {
        var prompt = $"""
            Work on card {card.Identifier}: {card.Title}

            Description:
            {card.Description}
            """;

        if (activeDefinition is null
            || string.IsNullOrWhiteSpace(activeDefinition.Content)
            || IsMarkdownWorkflow(activeDefinition.Content))
        {
            return prompt;
        }

        var workflow = WorkflowDefinitionParser.ParseYamlDefinition(activeDefinition.Content);
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

    private static void ValidateCreateRequest(CreateCardRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title))
            errors[nameof(request.Title)] = ["Card title is required."];
        if (request.Priority < 0)
            errors[nameof(request.Priority)] = ["Priority must not be negative."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static void ValidateSpawnRequest(SpawnCardRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Cols <= 0)
            errors[nameof(request.Cols)] = ["Terminal cols must be positive."];
        if (request.Rows <= 0)
            errors[nameof(request.Rows)] = ["Terminal rows must be positive."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
