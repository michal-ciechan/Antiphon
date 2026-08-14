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
    /// <summary>The <c>Cards.Title</c> column is varchar(300); anything longer is a 400, not a 500.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>
    /// The description ceiling, in CHARACTERS. Deliberately an application constant rather than a
    /// schema fact — the column is <c>text</c>, so raising this is a one-line change with no
    /// migration. 4,000 (the old <c>varchar</c>) was already too small for the house style, where
    /// the detail is the point and a correction only makes a description grow.
    /// </summary>
    /// <remarks>
    /// Callers composing corrections programmatically need a deterministic pre-check, so the limit
    /// and the actual length are both named in the validation error.
    ///
    /// <para>Known exposure, inherited not introduced: <see cref="BuildPrompt"/> embeds the
    /// description verbatim into the spawn prompt, which is TYPED INTO THE PTY and does not yet go
    /// through the ceiling-aware spill that delegation briefs get (CARD-0025). On the modern conpty
    /// backend this deployment runs, a 20,000-character mostly-ASCII description is ~20-22 KB and
    /// sits under the 43,200-byte single-write ceiling; a worst-case multibyte one does not.</para>
    /// </remarks>
    public const int MaxDescriptionLength = 20_000;

    /// <summary>
    /// The ceiling for every free-text reason: a move reason, <c>Card.TerminalReason</c>, an
    /// archive reason, a revision reason. 1,000 was measurably too small — a review verdict had to
    /// be hand-trimmed to exactly 1,000 characters to fit, and two close-outs 500'd on it.
    /// </summary>
    public const int MaxReasonLength = 4_000;

    /// <summary>The <c>EditedBy</c>/<c>ArchivedBy</c> columns are varchar(200).</summary>
    public const int MaxActorLength = 200;

    private readonly AppDbContext _db;
    private readonly AgentRegistry _agentRegistry;
    private readonly OrchestratorService _orchestrator;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly AgentReviewCheckpointService _reviewCheckpoints;

    public CardService(
        AppDbContext db,
        AgentRegistry agentRegistry,
        OrchestratorService orchestrator,
        AgentSessionLaunchQueue launchQueue,
        IEventBus eventBus,
        TimeProvider timeProvider,
        AgentReviewCheckpointService reviewCheckpoints)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _orchestrator = orchestrator;
        _launchQueue = launchQueue;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _reviewCheckpoints = reviewCheckpoints;
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
        ValidateMoveRequest(request);

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

        var wasTerminal = card.BoardColumn.IsTerminal;
        // The dequeue below clears AssignedAgentId — capture it first so the completion
        // checkpoint still knows whose workspace to snapshot.
        var assignedAgentId = card.AssignedAgentId;
        ApplyColumnMove(card, targetColumn, reason: request.Reason);
        var queueRemoval = await CardLifecycleTransitions.DequeueFinishedCardAsync(_db, card, UtcNow(), ct);
        await _db.SaveChangesAsync(ct);

        // Completing a card is the "work is done" sign-off — checkpoint the assigned agent's
        // workspace (HEAD sha + timestamp) so the Files review surface can show changes since
        // this point next time.
        if (targetColumn.IsTerminal && !wasTerminal && assignedAgentId is { } checkpointAgentId)
            await _reviewCheckpoints.CaptureAsync(checkpointAgentId, $"Card {card.Identifier} completed", ct);

        if (targetColumn.IsActive && card.OwnerSessionId is null)
            await SpawnAsync(card.Id, new SpawnCardRequest(), ct);

        if (queueRemoval is not null)
            await CardLifecycleTransitions.PublishQueueRemovalAsync(_eventBus, queueRemoval, ct);
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

    /// <param name="reason">
    /// Why the card is moving, from the caller. Kept as <see cref="Card.TerminalReason"/> on a move
    /// into a terminal column; on any other move there is nowhere to persist it yet (CARD-0019's
    /// card history), so it is deliberately dropped rather than silently written to a field that
    /// means something else.
    /// </param>
    private void ApplyColumnMove(
        Card card, BoardColumn targetColumn, bool enforceStateMachine = true, string? reason = null)
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
            // A supplied reason WINS over an existing one: a card re-closed with a better
            // explanation ("fixed by CARD-0041") should not keep the generic note it got first.
            var supplied = reason?.Trim();
            card.TerminalReason = !string.IsNullOrEmpty(supplied)
                ? supplied
                : card.TerminalReason ?? "Moved to terminal column.";
        }
        else
        {
            card.CompletedAt = null;
            card.TerminalReason = null;
        }
    }

    /// <summary>
    /// The next identifier for a board: one past the HIGHEST suffix already handed out.
    /// </summary>
    /// <remarks>
    /// This used to be <c>count + 1</c>, which reused an identifier after a delete (CARD-0005):
    /// remove CARD-0007 from a seven-card board and the next create hands out CARD-0007 again,
    /// silently pointing every existing reference — commit messages, docs, other cards' terminal
    /// reasons — at a different card. Identifiers are cited outside the database, so the sequence
    /// has to move forward even when rows leave. Suffixes that do not parse (a board synced from a
    /// foreign tracker) are ignored rather than blocking allocation.
    ///
    /// <para>This closes the collision but not the whole hole: deleting the CURRENT HIGHEST card
    /// still frees its number, because the only record that it was ever taken is the row itself.
    /// Full monotonicity needs CARD-0019's archive-instead-of-delete (or a per-board counter), and
    /// that is where it belongs — a card that is cited should not vanish in the first place.</para>
    /// </remarks>
    private async Task<string> NextIdentifierAsync(Guid boardId, CancellationToken ct)
    {
        var identifiers = await _db.Cards
            .Where(c => c.BoardId == boardId)
            .Select(c => c.Identifier)
            .ToListAsync(ct);

        var highest = 0;
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrEmpty(identifier))
                continue;
            var suffix = identifier.AsSpan(identifier.LastIndexOf('-') + 1);
            if (int.TryParse(suffix, out var value) && value > highest)
                highest = value;
        }

        return $"CARD-{highest + 1:0000}";
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
        RequireWithinLimit(errors, nameof(request.Title), request.Title?.Trim(), MaxTitleLength);
        RequireWithinLimit(errors, nameof(request.Description), request.Description?.Trim(), MaxDescriptionLength);
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    /// <summary>
    /// Records an over-length field as a validation error instead of letting it reach Postgres.
    /// A value past its column width comes back as <c>22001: value too long</c> inside a
    /// <see cref="DbUpdateException"/>, which is not an <c>HttpException</c>, so the middleware
    /// answers a raw 500 that names nothing — the failure this method exists to prevent. Both the
    /// limit and the actual length are in the message: callers composing corrections
    /// programmatically need a deterministic pre-check.
    /// </summary>
    private static void RequireWithinLimit(
        Dictionary<string, string[]> errors, string field, string? value, int limit)
    {
        if (value is null || value.Length <= limit)
            return;

        errors[field] = [$"{field} must be at most {limit:N0} characters; got {value.Length:N0}."];
    }

    private static void ValidateMoveRequest(MoveCardRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequireWithinLimit(errors, nameof(request.Reason), request.Reason?.Trim(), MaxReasonLength);
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
