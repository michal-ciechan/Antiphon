using System.Text.RegularExpressions;
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
using Npgsql;

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
    /// <para><see cref="BuildPrompt"/> still embeds the description verbatim. CARD-0025's
    /// <c>TypedBodySpill</c> then measures the composed spawn prompt against
    /// <c>BriefInlineMaxBytes</c> (inbox 900 B, modern 43 200 B) and writes an oversize body to
    /// <c>{cwd}/.antiphon/inbox/spawn-{{id}}.md</c> so the pty is typed a pointer, not 20 k
    /// characters. A filesystem failure falls back to typing the original.</para>
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

    /// <summary>
    /// The author recorded on a revision nothing human asked for. Self-reported like every other
    /// actor here — the server has no principals — but distinguishable from a name a caller chose.
    /// </summary>
    internal const string SystemActor = "system";

    private readonly AppDbContext _db;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentTuiLaunchResolver? _launchResolver;
    private readonly OrchestratorService _orchestrator;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly AgentReviewCheckpointService _reviewCheckpoints;
    private readonly ContextWindowSettings _contextWindow;
    private readonly ILogger<CardService>? _logger;
    // CARD-0106 S2. Optional like the launch resolver beside it: absent, placeholders go
    // unresolved and the launch tripwire refuses them by name. Production always registers it.
    private readonly ApiKeyEnvResolver? _apiKeyEnvResolver;

    public CardService(
        AppDbContext db,
        AgentRegistry agentRegistry,
        OrchestratorService orchestrator,
        AgentSessionLaunchQueue launchQueue,
        IEventBus eventBus,
        TimeProvider timeProvider,
        AgentReviewCheckpointService reviewCheckpoints,
        AgentTuiLaunchResolver? launchResolver = null,
        IOptions<ContextWindowSettings>? contextWindow = null,
        ILogger<CardService>? logger = null,
        ApiKeyEnvResolver? apiKeyEnvResolver = null)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _launchResolver = launchResolver;
        _orchestrator = orchestrator;
        _launchQueue = launchQueue;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _reviewCheckpoints = reviewCheckpoints;
        _contextWindow = contextWindow?.Value ?? new ContextWindowSettings();
        _logger = logger;
        _apiKeyEnvResolver = apiKeyEnvResolver;
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
        return await SessionContextUsage.AttachToCardAsync(
            _db, BoardService.ToCardDto(card), _contextWindow, _logger, ct);
    }

    /// <summary>
    /// A card id as the caller ALREADY KNOWS it: the guid, or the identifier in any form that gets
    /// written down — <c>CARD-0051</c>, <c>card-51</c>, <c>#51</c>, <c>51</c> — plus a foreign
    /// tracker's own exact form (<c>PROJ-12</c>). Modeled on
    /// <see cref="AgentTaskService.ResolveTaskIdAsync"/>: 0 matches is a 404, 1 is the id, more is
    /// a 409.
    /// </summary>
    /// <remarks>
    /// The match is EXACT, never a prefix. <c>client/src/shared/cardIdentifier.ts</c> deliberately
    /// matches identifier prefixes so incremental typing in the search box narrows (<c>4</c> finds
    /// #4, #40, #41); an API where <c>5</c> could mean CARD-0005 or CARD-0051 would be a footgun on
    /// a route that writes.
    ///
    /// <para><c>Identifier</c> is unique PER BOARD (<c>IX_Cards_BoardId_Identifier</c>), not
    /// globally, so two boards can each hold a CARD-0001. Every card in this deployment sits on one
    /// board today, which makes the 409 arm look theoretical — it is not: the day a second board
    /// files its first card it gets CARD-0001 again, and a resolver that took the first row would
    /// silently address the wrong card.</para>
    ///
    /// <para>An input that is neither a guid nor identifier-SHAPED is a 422 rather than a 404: it
    /// is a caller mistake, not a missing card, and the message can say so. The shape is narrow on
    /// purpose (digits, or <c>PREFIX-digits</c>) — it is what makes a literal route segment that
    /// ever stopped outranking <c>{id}</c> fail loudly instead of reporting "no such card".</para>
    /// </remarks>
    public async Task<Guid> ResolveCardIdAsync(string idOrIdentifier, CancellationToken ct)
    {
        var raw = (idOrIdentifier ?? string.Empty).Trim();
        if (Guid.TryParse(raw, out var cardId))
        {
            if (!await _db.Cards.AsNoTracking().AnyAsync(c => c.Id == cardId, ct))
                throw new NotFoundException(nameof(Card), cardId);
            return cardId;
        }

        var canonical = TryCanonicalIdentifier(raw);
        if (canonical is null && !ForeignIdentifier.IsMatch(raw))
        {
            throw new ValidationException(
                nameof(idOrIdentifier),
                $"'{raw}' is neither a card id nor a card identifier. "
                + "Use the card's guid, or its identifier (CARD-0051, card-51, #51, 51).");
        }

        var lowered = raw.ToLowerInvariant();
        var canonicalOrRaw = canonical ?? raw;
        // Canonical form is OUR identifier (CARD-000N / #N / 51). A foreign tracker key
        // (ANT-12) is resolved through ExternalIssueRef.ExternalKey as well as Identifier, so
        // import-origin cards that store CARD-nnnn and keep the tracker key on the ref still
        // answer to `card.ps1 get ANT-12`. #N is deliberately NOT routed to a GitHub key — it
        // is the canonical CARD-000N form.
        IQueryable<Card> query = _db.Cards.AsNoTracking()
            .Where(c => c.Identifier == canonicalOrRaw || c.Identifier.ToLower() == lowered);
        if (canonical is null)
        {
            query = query.Union(
                _db.Cards.AsNoTracking()
                    .Where(c => c.ExternalIssueRef != null
                        && (c.ExternalIssueRef.ExternalKey == raw
                            || c.ExternalIssueRef.ExternalKey.ToLower() == lowered)));
        }

        var matches = await query
            .Select(c => c.Id)
            .Distinct()
            .Take(2)
            .ToListAsync(ct);

        return matches.Count switch
        {
            0 => throw new NotFoundException(nameof(Card), canonicalOrRaw),
            1 => matches[0],
            _ => throw new ConflictException(
                $"Card identifier '{canonicalOrRaw}' matches more than one card "
                + "— use the card's guid."),
        };
    }

    /// <summary>
    /// The forms <c>cardIdentifier.ts</c> accepts, normalized to the stored canonical identifier:
    /// trim, drop a leading <c>#</c>, drop a <c>card-</c>/<c>card </c> prefix, drop leading zeros.
    /// Null when what is left is not all digits — i.e. not one of OUR identifiers.
    /// </summary>
    private static string? TryCanonicalIdentifier(string raw)
    {
        var value = raw.StartsWith('#') ? raw[1..].Trim() : raw;
        if (value.StartsWith("card", StringComparison.OrdinalIgnoreCase)
            && value.Length > 5
            && (value[4] == '-' || value[4] == ' '))
        {
            value = value[5..].Trim();
        }

        if (value.Length == 0 || !value.All(char.IsAsciiDigit))
            return null;

        // TryParse rather than Parse: a 40-digit "identifier" is not one of ours, and overflowing
        // here would be a 500 on input a caller typed.
        return int.TryParse(value, out var number) ? $"CARD-{number:0000}" : null;
    }

    /// <summary>
    /// <c>PREFIX-123</c>: the shape every tracker worth syncing from uses. Anything else is told to
    /// use the guid rather than being reported as a missing card.
    /// </summary>
    private static readonly Regex ForeignIdentifier =
        new(@"^[A-Za-z][A-Za-z0-9_]*-\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Moves a card, and starts an agent on it only if the caller asked for that.
    /// </summary>
    /// <remarks>
    /// A move into an active, unowned column used to spawn unconditionally and discard the
    /// <see cref="SpawnCardResult"/>, returning a bare <see cref="CardDto"/> — so a scripted move
    /// could start an agent and the response said nothing at all. Now the spawn is opt-in and the
    /// result reports both halves: which session started, or that one deliberately did not.
    /// </remarks>
    public async Task<MoveCardResult> MoveAsync(Guid id, MoveCardRequest request, CancellationToken ct)
    {
        ValidateMoveRequest(request);

        var card = await LoadCardForUpdateAsync(id, ct);
        if (request.ConcurrencyToken == Guid.Empty)
            throw new ValidationException(nameof(request.ConcurrencyToken), "Card concurrency token is required.");
        if (request.ConcurrencyToken != card.ConcurrencyToken)
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");
        // An archived card is out of play: moving it into an active column would spawn an agent on
        // a record the operator has taken off the board. Unarchive first — that is one call.
        if (card.ArchivedAt is not null)
            throw new ConflictException($"Card '{card.Identifier}' is archived; unarchive it before moving it.");

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

        // Hold/clear in the SAME write as the move. A tick between two saves can still claim
        // (CARD-0087). Spawn knowledge stays here, not in ApplyColumnMove (CARD-0051).
        if (!targetColumn.IsActive)
            card.AutoDispatchHeldAt = null;
        else if (card.OwnerSessionId is null && !request.Spawn)
            card.AutoDispatchHeldAt = UtcNow();

        await SaveCardWriteAsync(card, ct);

        // Completing a card is the "work is done" sign-off — checkpoint the assigned agent's
        // workspace (HEAD sha + timestamp) so the Files review surface can show changes since
        // this point next time.
        if (targetColumn.IsTerminal && !wasTerminal && assignedAgentId is { } checkpointAgentId)
            await _reviewCheckpoints.CaptureAsync(checkpointAgentId, $"Card {card.Identifier} completed", ct);

        Guid? spawnedSessionId = null;
        var spawnSuppressed = false;
        if (targetColumn.IsActive && card.OwnerSessionId is null)
        {
            if (request.Spawn)
                spawnedSessionId = (await SpawnAsync(card.Id, new SpawnCardRequest(), ct)).SessionId;
            else
                spawnSuppressed = true;
        }

        if (queueRemoval is not null)
            await CardLifecycleTransitions.PublishQueueRemovalAsync(_eventBus, queueRemoval, ct);
        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
        return new MoveCardResult(await GetByIdAsync(card.Id, ct), spawnedSessionId, spawnSuppressed);
    }

    /// <summary>
    /// Corrects a card's text. The card SURFACE is correctable; the card RECORD is append-only —
    /// the superseded values are archived as a revision, with a mandatory reason, before anything
    /// is overwritten. Allowed in any column, including terminal ones: correcting the record of
    /// work already done is the case that motivated this (CARD-0026's known-wrong closing note).
    /// </summary>
    public async Task<CardDto> UpdateContentAsync(
        Guid id, UpdateCardContentRequest request, CancellationToken ct)
    {
        ValidateUpdateContentRequest(request);

        var card = await LoadCardForUpdateAsync(id, ct);
        if (request.ConcurrencyToken == Guid.Empty)
            throw new ValidationException(nameof(request.ConcurrencyToken), "Card concurrency token is required.");
        if (request.ConcurrencyToken != card.ConcurrencyToken)
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");

        var now = UtcNow();
        // Snapshot BEFORE the overwrite — the revision holds what is being superseded.
        CardRevisionLog.AppendContentEdit(card, request.Reason, request.EditedBy, now);

        if (request.Title is not null)
            card.Title = request.Title.Trim();
        if (request.Description is not null)
            card.Description = request.Description.Trim();
        if (request.Priority is int priority)
            card.Priority = priority;
        if (request.Labels is not null)
            card.LabelsJson = BoardService.SerializeLabels(request.Labels);
        card.UpdatedAt = now;
        card.ConcurrencyToken = Guid.NewGuid();

        await SaveCardWriteAsync(card, ct);

        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
        return await GetByIdAsync(card.Id, ct);
    }

    /// <summary>
    /// Archives a card. This is what "delete" means here — the row stays, so every reference to its
    /// identifier (commit messages, docs, other cards' terminal reasons) keeps resolving, and the
    /// identifier allocator keeps seeing the number as taken.
    /// </summary>
    /// <remarks>
    /// Refused while an agent is actually working the card: archiving the record out from under a
    /// live session is the one genuinely destructive shape here, and unlike everything else on this
    /// card it cannot be undone by reading the history.
    /// </remarks>
    public async Task<CardDto> ArchiveAsync(Guid id, ArchiveCardRequest request, CancellationToken ct)
    {
        ValidateArchiveRequest(request.Reason, nameof(request.Reason), request.ArchivedBy, nameof(request.ArchivedBy));

        var card = await LoadCardForArchiveAsync(id, request.ConcurrencyToken, ct);
        if (card.ArchivedAt is not null)
            throw new ConflictException($"Card '{card.Identifier}' is already archived.");

        if (card.OwnerSessionId is Guid ownerSessionId)
        {
            var liveSession = await _db.AgentSessions.AnyAsync(
                s => s.Id == ownerSessionId
                    && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running),
                ct);
            if (liveSession)
            {
                throw new ConflictException(
                    $"Card '{card.Identifier}' has a live owner session; stop it before archiving.");
            }
        }

        if (card.ActiveWorkflowRunId is Guid runId)
        {
            var runningWorkflow = await _db.CardWorkflowRuns.AnyAsync(
                r => r.Id == runId
                    && r.Status != CardWorkflowRunStatus.Completed
                    && r.Status != CardWorkflowRunStatus.Failed
                    && r.Status != CardWorkflowRunStatus.Canceled,
                ct);
            if (runningWorkflow)
            {
                throw new ConflictException(
                    $"Card '{card.Identifier}' has an active workflow run; cancel it before archiving.");
            }
        }

        var now = UtcNow();
        CardRevisionLog.AppendArchiveChange(
            card, CardRevisionKind.Archive, request.Reason, request.ArchivedBy, now);
        card.ArchivedAt = now;
        card.ArchivedReason = request.Reason.Trim();
        card.ArchivedBy = string.IsNullOrWhiteSpace(request.ArchivedBy) ? null : request.ArchivedBy.Trim();

        return await SaveArchiveChangeAsync(card, now, ct);
    }

    /// <summary>Undoes an archive. Mistakes in archiving need correcting too.</summary>
    public async Task<CardDto> UnarchiveAsync(Guid id, UnarchiveCardRequest request, CancellationToken ct)
    {
        ValidateArchiveRequest(
            request.Reason, nameof(request.Reason), request.UnarchivedBy, nameof(request.UnarchivedBy));

        var card = await LoadCardForArchiveAsync(id, request.ConcurrencyToken, ct);
        if (card.ArchivedAt is null)
            throw new ConflictException($"Card '{card.Identifier}' is not archived.");

        var now = UtcNow();
        // The Unarchive revision is why clearing these three fields is not a loss: the archive, its
        // reason and its undoing all stay readable in the history.
        CardRevisionLog.AppendArchiveChange(
            card, CardRevisionKind.Unarchive, request.Reason, request.UnarchivedBy, now);
        card.ArchivedAt = null;
        card.ArchivedReason = null;
        card.ArchivedBy = null;

        return await SaveArchiveChangeAsync(card, now, ct);
    }

    /// <summary>
    /// Undoes a terminal close. Dedicated verb, not a move-table edge: <c>Done</c>/<c>Canceled</c>
    /// stay unreachable via <see cref="MoveAsync"/>. Calls <see cref="ApplyColumnMove"/> directly
    /// so a reopen is structurally incapable of spawning (CARD-0051's opt-in lives only in
    /// <see cref="MoveAsync"/>).
    /// </summary>
    public async Task<CardDto> ReopenAsync(Guid id, ReopenCardRequest request, CancellationToken ct)
    {
        ValidateArchiveRequest(
            request.Reason, nameof(request.Reason), request.ReopenedBy, nameof(request.ReopenedBy));

        var card = await LoadCardForUpdateAsync(id, ct);
        if (request.ConcurrencyToken == Guid.Empty)
            throw new ValidationException(nameof(request.ConcurrencyToken), "Card concurrency token is required.");
        if (request.ConcurrencyToken != card.ConcurrencyToken)
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");

        if (card.ArchivedAt is not null)
            throw new ConflictException($"Card '{card.Identifier}' is archived; unarchive it before reopening it.");

        if (!CardStateMachine.CanReopenFrom(card.Status))
            throw new ConflictException($"Card '{card.Identifier}' is not closed.");

        var target = ResolveReopenTarget(card, request.BoardColumnId);

        // Snapshot BEFORE ApplyColumnMove clears CompletedAt/TerminalReason on the non-terminal landing.
        CardRevisionLog.AppendReopen(card, target, request.Reason, request.ReopenedBy, UtcNow());
        ApplyColumnMove(
            card,
            target,
            enforceStateMachine: false,
            recordRevision: false,
            reason: request.Reason,
            movedBy: request.ReopenedBy);

        // Same hole as a Spawn=false move: reopen cannot spawn (CARD-0054), so land in an
        // active column and the tick would otherwise pick the card up (CARD-0087).
        if (target.IsActive && card.OwnerSessionId is null)
            card.AutoDispatchHeldAt = UtcNow();

        await SaveCardWriteAsync(card, ct);
        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
        return await GetByIdAsync(card.Id, ct);
    }

    private static BoardColumn ResolveReopenTarget(Card card, Guid? requestedColumnId)
    {
        if (requestedColumnId is Guid columnId)
        {
            var column = card.Board.Columns.FirstOrDefault(c => c.Id == columnId)
                ?? throw new ValidationException(
                    nameof(ReopenCardRequest.BoardColumnId),
                    "Target column belongs to a different board.");
            if (column.IsTerminal)
            {
                throw new ValidationException(
                    nameof(ReopenCardRequest.BoardColumnId),
                    "A closed card cannot be reopened into a terminal column.");
            }

            return column;
        }

        return card.Board.Columns
                .Where(c => c.CardStatus == CardStatus.Backlog)
                .OrderBy(c => c.ColumnOrder)
                .FirstOrDefault()
            ?? card.Board.Columns
                .Where(c => !c.IsTerminal)
                .OrderBy(c => c.ColumnOrder)
                .FirstOrDefault()
            ?? throw new ConflictException(
                $"Board '{card.Board.Name}' has no live column to reopen into.");
    }

    private async Task<Card> LoadCardForArchiveAsync(Guid id, Guid concurrencyToken, CancellationToken ct)
    {
        var card = await LoadCardForUpdateAsync(id, ct);
        if (concurrencyToken == Guid.Empty)
            throw new ValidationException("ConcurrencyToken", "Card concurrency token is required.");
        if (concurrencyToken != card.ConcurrencyToken)
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");

        return card;
    }

    private async Task<CardDto> SaveArchiveChangeAsync(Card card, DateTime now, CancellationToken ct)
    {
        card.UpdatedAt = now;
        card.ConcurrencyToken = Guid.NewGuid();

        await SaveCardWriteAsync(card, ct);

        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
        return await GetByIdAsync(card.Id, ct);
    }

    /// <summary>The card's history, newest first. One interleaved sequence across every kind.</summary>
    public async Task<IReadOnlyList<CardRevisionDto>> GetRevisionsAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.Cards.AnyAsync(c => c.Id == id, ct))
            throw new NotFoundException(nameof(Card), id);

        var revisions = await _db.CardRevisions
            .AsNoTracking()
            .Where(r => r.CardId == id)
            .OrderByDescending(r => r.RevisionNumber)
            .ToListAsync(ct);

        return revisions.Select(BoardService.ToRevisionDto).ToList();
    }

    public async Task<SpawnCardResult> SpawnAsync(Guid id, SpawnCardRequest request, CancellationToken ct)
    {
        ValidateSpawnRequest(request);

        var card = await LoadCardForUpdateAsync(id, ct);
        if (request.ConcurrencyToken is Guid requestedToken && requestedToken != card.ConcurrencyToken)
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.");

        if (card.ArchivedAt is not null)
            throw new ConflictException($"Card '{card.Identifier}' is archived; unarchive it before spawning.");

        if (card.BoardColumn.IsTerminal)
            throw new ConflictException($"Card '{card.Identifier}' is already in a terminal column.");

        // Explicit start always lifts the hold, including the backlog path that moves into
        // active itself. Persist before TryClaimCardAsync so a later tick does not still see it.
        var hadHold = card.AutoDispatchHeldAt is not null;
        card.AutoDispatchHeldAt = null;

        if (!card.BoardColumn.IsActive)
        {
            var activeColumn = card.Board.Columns
                .OrderBy(c => c.ColumnOrder)
                .FirstOrDefault(c => c.IsActive && !c.IsTerminal)
                ?? throw new ConflictException($"Board '{card.Board.Name}' has no active column for spawning.");
            ApplyColumnMove(
                card,
                activeColumn,
                enforceStateMachine: false,
                reason: "Moved into an active column to spawn an agent session.",
                movedBy: SystemActor);
            await SaveCardWriteAsync(card, ct);
        }
        else if (hadHold)
        {
            await SaveCardWriteAsync(card, ct);
        }

        string definitionName;
        AgentLaunchSpec spec;
        Guid? tuiProfileRevisionId = null;
        string? effectiveModelId = null;
        if (!string.IsNullOrWhiteSpace(request.DefinitionName))
        {
            definitionName = request.DefinitionName.Trim();
            spec = _agentRegistry.Resolve(definitionName, new AgentLaunchOptions(
                Cwd: null,
                Cols: request.Cols,
                Rows: request.Rows,
                ExtraArgs: null,
                ExtraEnv: null,
                ApiKeyProjectId: card.Board.ProjectId,
                LaunchEnvOverride: request.LaunchEnvOverride,
                ProjectDefaultEnv: _apiKeyEnvResolver is not null
                    ? await _apiKeyEnvResolver.GetProjectDefaultEnvAsync(card.Board.ProjectId, ct)
                    : null));
            // This is a direct registry resolve with no agent, so it is where the spec is finalized
            // and where API keys have to be resolved (CARD-0106 S2). The card's board names the
            // project, so an explicitly-named definition spawned onto a project's card still gets
            // that project's keys (and, CARD-0106 gap 2, its default env).
            if (_apiKeyEnvResolver is not null)
            {
                spec = await _apiKeyEnvResolver.ResolveSpecAsync(
                    spec,
                    card.Board.ProjectId,
                    $"card {card.Identifier}",
                    ct);
            }
        }
        else if (card.AssignedAgent is { } assignedAgent)
        {
            var resolved = await AgentLaunchResolution.ResolveForAgentAsync(
                assignedAgent,
                _agentRegistry,
                _launchResolver,
                new AgentLaunchOptions(
                    Cwd: null,
                    Cols: request.Cols,
                    Rows: request.Rows,
                    ExtraArgs: null,
                    ExtraEnv: null,
                    // The CARD's board names the project for a card spawn (plan section 4), whether
                    // or not the agent that runs it happens to sit on the same board.
                    ApiKeyProjectId: card.Board.ProjectId,
                    LaunchEnvOverride: request.LaunchEnvOverride),
                ct,
                _apiKeyEnvResolver);
            definitionName = resolved.Spec.DefinitionName;
            spec = resolved.Spec;
            tuiProfileRevisionId = resolved.ProfileRevisionId;
            effectiveModelId = resolved.EffectiveModelId;
        }
        else
        {
            var resolved = await AgentLaunchResolution.ResolveDefaultAsync(
                _agentRegistry,
                _launchResolver,
                new AgentLaunchOptions(
                    Cwd: null,
                    Cols: request.Cols,
                    Rows: request.Rows,
                    ExtraArgs: null,
                    ExtraEnv: null,
                    // The CARD's board names the project for a card spawn (plan section 4), whether
                    // or not the agent that runs it happens to sit on the same board.
                    ApiKeyProjectId: card.Board.ProjectId,
                    LaunchEnvOverride: request.LaunchEnvOverride),
                ct,
                _apiKeyEnvResolver);
            definitionName = resolved.Spec.DefinitionName;
            spec = resolved.Spec;
            tuiProfileRevisionId = resolved.ProfileRevisionId;
            effectiveModelId = resolved.EffectiveModelId;
        }

        var sessionId = await _orchestrator.TryClaimCardAsync(
            card.Id,
            card.ConcurrencyToken,
            definitionName,
            spec.Kind,
            request.Cols,
            request.Rows,
            UtcNow(),
            ct,
            tuiProfileRevisionId,
            effectiveModelId);
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

    /// <summary>
    /// Saves a card write, turning "somebody else got there first" into a 409 rather than a 500.
    /// </summary>
    /// <remarks>
    /// Two shapes mean the same thing. The card's concurrency token catches the ordinary race. The
    /// revision sequence catches the rest: two writers who both read <c>RevisionCount = n</c> both
    /// allocate n + 1, and the unique <c>(CardId, RevisionNumber)</c> index rejects the loser — as
    /// a <see cref="DbUpdateException"/>, which is NOT a concurrency exception and would otherwise
    /// escape as an unexplained 500. Two concurrent channel delegations of one card did exactly
    /// that. The database's own message is attached rather than paraphrased, so a 409 that turns
    /// out to be a misdiagnosis still has something to debug from.
    /// </remarks>
    private async Task SaveCardWriteAsync(Card card, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException($"Card '{card.Identifier}' was modified by another operation.", ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateRevisionNumber(ex))
        {
            throw new ConflictException(
                $"Card '{card.Identifier}' was modified by another operation "
                + $"({AgentService.DescribeDbFailure(ex)}).",
                ex);
        }
    }

    private static bool IsDuplicateRevisionNumber(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_CardRevisions_CardId_RevisionNumber"
        };

    private async Task<Card> LoadCardAsync(Guid id, CancellationToken ct)
    {
        return await _db.Cards
            .AsNoTracking()
            .Include(c => c.AgentSessions)
            .Include(c => c.AssignedAgent)
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .Include(c => c.ExternalIssueRef)
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
            .Include(c => c.ExternalIssueRef)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Card), id);
    }

    /// <param name="reason">
    /// Why the card is moving, from the caller. It now PERSISTS on every move, as the reason on the
    /// card's <c>Move</c> revision — before CARD-0019 there was nowhere to put it and a non-terminal
    /// move's reason was accepted and silently dropped. A move into a terminal column additionally
    /// keeps stamping <see cref="Card.TerminalReason"/>, which stays as the cheap-to-read summary
    /// it is today.
    /// </param>
    /// <param name="recordRevision">
    /// When false, skip the automatic <c>Move</c> row. Reopen writes its own <c>Kind.Reopen</c>
    /// row first (the transition AND the superseded terminal facts) and must not also get a
    /// sibling Move. Every other caller keeps the default.
    /// </param>
    private void ApplyColumnMove(
        Card card,
        BoardColumn targetColumn,
        bool enforceStateMachine = true,
        bool recordRevision = true,
        string? reason = null,
        string? movedBy = null)
    {
        if (card.BoardColumnId == targetColumn.Id)
            return;

        if (enforceStateMachine
            && card.Status != targetColumn.CardStatus
            && !CardStateMachine.CanTransition(card.Status, targetColumn.CardStatus))
        {
            var detail = $"Cannot move card from {card.Status} to {targetColumn.CardStatus}";
            if (CardStateMachine.CanReopenFrom(card.Status))
                detail += ", a closed card is reopened via POST /cards/{id}/reopen";
            throw new ValidationException(nameof(targetColumn.CardStatus), detail + ".");
        }

        var now = UtcNow();
        if (recordRevision)
        {
            CardRevisionLog.AppendMove(
                card, card.BoardColumnId, card.Status, targetColumn, reason, movedBy, now);
        }
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

    private async Task<string> NextIdentifierAsync(Guid boardId, CancellationToken ct)
    {
        var allocator = await CardIdentifierAllocator.ForBoardAsync(_db, boardId, ct);
        return allocator.Next();
    }

    private static string BuildPrompt(Card card, BoardWorkflowDefinition? activeDefinition)
    {
        var prompt = $"""
            Work on card {card.Identifier}{TrackerIssueCitation.Suffix(card)}: {card.Title}

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

    private static void ValidateUpdateContentRequest(UpdateCardContentRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Reason))
            errors[nameof(request.Reason)] = ["A reason is required for a card correction."];
        RequireWithinLimit(errors, nameof(request.Reason), request.Reason?.Trim(), MaxReasonLength);

        var hasContent = request.Title is not null
            || request.Description is not null
            || request.Priority is not null
            || request.Labels is not null;
        if (!hasContent)
        {
            errors[nameof(request.Title)] =
                ["At least one of Title, Description, Priority or Labels must be provided."];
        }

        if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title))
            errors[nameof(request.Title)] = ["Card title must not be blank."];
        if (request.Priority is int priority && priority < 0)
            errors[nameof(request.Priority)] = ["Priority must not be negative."];

        RequireWithinLimit(errors, nameof(request.Title), request.Title?.Trim(), MaxTitleLength);
        RequireWithinLimit(errors, nameof(request.Description), request.Description?.Trim(), MaxDescriptionLength);
        RequireWithinLimit(errors, nameof(request.EditedBy), request.EditedBy?.Trim(), MaxActorLength);

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static void ValidateArchiveRequest(
        string reason, string reasonField, string? actor, string actorField)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(reason))
            errors[reasonField] = ["A reason is required."];
        RequireWithinLimit(errors, reasonField, reason?.Trim(), MaxReasonLength);
        RequireWithinLimit(errors, actorField, actor?.Trim(), MaxActorLength);
        if (errors.Count > 0)
            throw new ValidationException(errors);
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

        AgentLaunchEnv.ValidateOverride(request.LaunchEnvOverride, "launchEnvOverride");
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
