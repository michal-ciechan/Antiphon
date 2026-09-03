using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class BoardService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ContextWindowSettings _contextWindow;
    private readonly CardsSettings _cards;
    private readonly ILogger<BoardService>? _logger;

    public BoardService(
        AppDbContext db,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IOptions<ContextWindowSettings>? contextWindow = null,
        ILogger<BoardService>? logger = null,
        IOptions<CardsSettings>? cards = null)
    {
        _db = db;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _contextWindow = contextWindow?.Value ?? new ContextWindowSettings();
        _logger = logger;
        _cards = cards?.Value ?? new CardsSettings();
    }

    public async Task<IReadOnlyList<BoardSummaryDto>> GetAllAsync(CancellationToken ct) =>
        await GetAllAsync(includeArchived: false, ct);

    public async Task<IReadOnlyList<BoardSummaryDto>> GetAllAsync(bool includeArchived, CancellationToken ct)
    {
        return await _db.Boards
            .AsNoTracking()
            .Include(b => b.Project)
            .Include(b => b.Cards)
            .Where(b => includeArchived
                || (b.ArchivedAt == null && b.Project.ArchivedAt == null))
            .OrderBy(b => b.Project.Name)
            .ThenBy(b => b.Name)
            .Select(b => new BoardSummaryDto(
                b.Id,
                b.ProjectId,
                b.Project.Name,
                b.Name,
                b.Description,
                b.TrackerKind,
                b.MaxConcurrentSessions,
                // Archived cards are off the board, so they are not what "this board has N cards"
                // means to a reader of the list.
                b.Cards.Count(c => c.ArchivedAt == null),
                b.CreatedAt,
                b.UpdatedAt,
                b.ArchivedAt,
                b.ArchivedReason,
                b.ArchivedBy))
            .ToListAsync(ct);
    }

    public async Task<BoardDetailDto> GetByIdAsync(Guid id, CancellationToken ct) =>
        await GetByIdAsync(id, includeArchived: false, summary: false, ct);

    public async Task<BoardDetailDto> GetByIdAsync(Guid id, bool includeArchived, CancellationToken ct)
        => await GetByIdAsync(id, includeArchived, summary: false, ct);

    public async Task<BoardDetailDto> GetByIdAsync(
        Guid id, bool includeArchived, bool summary, CancellationToken ct)
    {
        var board = await LoadBoardAsync(id, ct);
        var detail = ToDetailDto(board, includeArchived);
        if (summary)
            return ToSummaryBoardDto(detail, _cards.SummaryPreviewChars);

        return await SessionContextUsage.AttachToBoardAsync(_db, detail, _contextWindow, _logger, ct);
    }

    /// <summary>
    /// The board's columns and nothing else — every column carries the <c>IsActive</c> /
    /// <c>IsTerminal</c> / <c>Name</c> a caller needs to pick a move target, and no cards.
    /// </summary>
    /// <remarks>
    /// <see cref="GetByIdAsync(Guid, CancellationToken)"/> is the only way to learn a column id
    /// today, and it carries every card on the board with it — 252 KB measured on the Antiphon
    /// board. A script resolving "which column is Done" should not have to download the board to
    /// find out, and neither should anything else that only wants the shape.
    /// </remarks>
    public async Task<IReadOnlyList<BoardColumnDto>> GetColumnsAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.Boards.AsNoTracking().AnyAsync(b => b.Id == id, ct))
            throw new NotFoundException(nameof(Board), id);

        var columns = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => c.BoardId == id)
            .OrderBy(c => c.ColumnOrder)
            .ToListAsync(ct);

        return columns
            .Select(c => new BoardColumnDto(
                c.Id,
                c.StateKey,
                c.Name,
                c.ColumnOrder,
                c.CardStatus,
                c.IsActive,
                c.IsTerminal,
                c.MaxConcurrentSessions,
                []))
            .ToList();
    }

    public async Task<BoardDetailDto> CreateAsync(CreateBoardRequest request, CancellationToken ct)
    {
        ValidateBoardRequest(request);

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct)
            ?? throw new NotFoundException(nameof(Project), request.ProjectId);
        if (project.ArchivedAt is not null)
            throw new ConflictException($"Project '{project.Name}' is archived; unarchive it before adding a board.");

        var duplicate = await _db.Boards.AnyAsync(
            b => b.ProjectId == request.ProjectId && b.Name == request.Name.Trim(),
            ct);
        if (duplicate)
            throw new ConflictException($"Board '{request.Name}' already exists for project '{project.Name}'.");

        var now = UtcNow();
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = request.MaxConcurrentSessions,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        foreach (var column in CreateDefaultColumns(board, now))
            board.Columns.Add(column);

        _db.Boards.Add(board);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = board.Id }, ct);

        return await GetByIdAsync(board.Id, ct);
    }

    /// <summary>
    /// Deletes a board and everything beneath it. If that leaves its project with nothing at all —
    /// no other boards, no workflows — the project goes too: a board and its project are usually
    /// created together and named the same, so an empty project left behind is just litter.
    /// </summary>
    public async Task<DeleteBoardResultDto> DeleteAsync(Guid id, CancellationToken ct)
    {
        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException(nameof(Board), id);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await ProjectCascade.DeleteBoardsAsync(_db, [id], ct);

        var projectIsEmpty = !await _db.Boards.AnyAsync(b => b.ProjectId == board.ProjectId, ct)
            && !await _db.Workflows.AnyAsync(w => w.ProjectId == board.ProjectId, ct);
        if (projectIsEmpty)
            await _db.Projects.Where(p => p.Id == board.ProjectId).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = id, deleted = true }, ct);

        return new DeleteBoardResultDto(id, board.ProjectId, projectIsEmpty);
    }

    public async Task<BoardSummaryDto> ArchiveAsync(
        Guid id, ArchiveBoardRequest request, CancellationToken ct)
    {
        EntityArchive.Validate(request.Reason, request.ArchivedBy);

        var board = await _db.Boards
            .Include(b => b.Project)
            .Include(b => b.Cards)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException(nameof(Board), id);
        if (board.ArchivedAt is not null)
            throw new ConflictException($"Board '{board.Name}' is already archived.");

        await EntityArchive.EnsureBoardArchiveableAsync(_db, board.Id, board.Name, ct);

        var now = UtcNow();
        board.ArchivedAt = now;
        board.ArchivedReason = request.Reason.Trim();
        board.ArchivedBy = string.IsNullOrWhiteSpace(request.ArchivedBy) ? null : request.ArchivedBy.Trim();
        board.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = board.Id, archived = true }, ct);
        return ToSummaryDto(board);
    }

    public async Task<BoardSummaryDto> UnarchiveAsync(
        Guid id, UnarchiveBoardRequest request, CancellationToken ct)
    {
        EntityArchive.Validate(request.Reason, request.UnarchivedBy);

        var board = await _db.Boards
            .Include(b => b.Project)
            .Include(b => b.Cards)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException(nameof(Board), id);
        if (board.ArchivedAt is null)
            throw new ConflictException($"Board '{board.Name}' is not archived.");

        board.ArchivedAt = null;
        board.ArchivedReason = null;
        board.ArchivedBy = null;
        board.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = board.Id, archived = false }, ct);
        return ToSummaryDto(board);
    }

    internal static BoardDetailDto ToDetailDto(Board board) => ToDetailDto(board, includeArchived: false);

    /// <remarks>
    /// Archived cards are filtered HERE, not by a global EF query filter. A global filter would
    /// also shrink <c>CardService.NextIdentifierAsync</c>'s view of the board, which is how the
    /// identifier allocator learns a number is taken — and resurrect CARD-0005 (an archived
    /// CARD-0007's number handed out again to a new card) for every card ever archived. A test
    /// pins that.
    /// </remarks>
    internal static BoardDetailDto ToDetailDto(Board board, bool includeArchived)
    {
        var now = DateTime.UtcNow;
        var cardsByColumn = board.Cards
            .Where(c => includeArchived || c.ArchivedAt == null)
            .GroupBy(c => c.BoardColumnId)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(c => CardRanking.OrderKey(c, now))
                .ToList());

        var columns = board.Columns
            .OrderBy(c => c.ColumnOrder)
            .Select(column => new BoardColumnDto(
                column.Id,
                column.StateKey,
                column.Name,
                column.ColumnOrder,
                column.CardStatus,
                column.IsActive,
                column.IsTerminal,
                column.MaxConcurrentSessions,
                cardsByColumn.GetValueOrDefault(column.Id, [])
                    .Select(c => ToCardDto(c, now))
                    .ToList()))
            .ToList();

        return new BoardDetailDto(
            board.Id,
            board.ProjectId,
            board.Project.Name,
            board.Name,
            board.Description,
            board.TrackerKind,
            board.MaxConcurrentSessions,
            columns,
            board.CreatedAt,
            board.UpdatedAt,
            board.ArchivedAt,
            board.ArchivedReason,
            board.ArchivedBy);
    }

    internal static BoardSummaryDto ToSummaryDto(Board board) =>
        new(
            board.Id,
            board.ProjectId,
            board.Project.Name,
            board.Name,
            board.Description,
            board.TrackerKind,
            board.MaxConcurrentSessions,
            board.Cards.Count(c => c.ArchivedAt == null),
            board.CreatedAt,
            board.UpdatedAt,
            board.ArchivedAt,
            board.ArchivedReason,
            board.ArchivedBy);

    internal static CardDto ToCardDto(Card card, DateTime? now = null)
    {
        var utc = now ?? DateTime.UtcNow;
        var effective = CardRanking.EffectiveUrgency(card, utc);
        return new CardDto(
            card.Id,
            card.BoardId,
            card.BoardColumnId,
            card.OwnerSessionId,
            card.CurrentWorktreeId,
            card.AssignedAgentId,
            card.AssignedAgent?.Name,
            card.AgentQueuePosition,
            card.ActiveWorkflowRunId,
            card.ActiveWorkflowRun?.Status,
            card.ActiveWorkflowRun?.CurrentStage?.Name,
            card.Identifier,
            card.Title,
            card.Description,
            card.Importance,
            card.ImportanceProvenance,
            card.Urgency,
            card.DueAt,
            card.UrgentSince,
            effective,
            CardRanking.Quadrant(card.Importance, effective),
            CardRanking.Rank(card, utc),
            card.Position,
            ParseLabels(card.LabelsJson),
            card.Status,
            card.ConcurrencyToken,
            card.CreatedAt,
            card.UpdatedAt,
            card.StartedAt,
            card.CompletedAt,
            card.TerminalReason,
            card.AgentSessions
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new AgentSessionSummaryDto(
                    s.Id,
                    s.DefinitionName,
                    s.AgentKind,
                    s.Status,
                    s.Cwd,
                    s.CreatedAt,
                    s.StartedAt,
                    s.LastSeenAt,
                    s.EndedAt,
                    s.ExitCode,
                    s.FailureReason,
                    s.TuiProfileRevisionId,
                    s.EffectiveModelId,
                    TerminationSource: s.TerminationSource))
                .ToList(),
            card.RevisionCount,
            card.ArchivedAt,
            card.ArchivedReason,
            card.ArchivedBy,
            card.AutoDispatchHeldAt,
            card.ExternalIssueRef is { } ext
                ? new ExternalIssueDto(
                    ext.TrackerKind,
                    ext.ExternalKey,
                    ext.Url,
                    ext.Author,
                    ext.AuthorIsOperator,
                    NeedsHumanReview(card))
                : null);
    }

    /// <summary>
    /// Derived at read time (CARD-0327 decision 7): an import-origin card from a non-operator
    /// author that nobody has rated, still in Backlog. Rating, moving, or archiving clears it.
    /// </summary>
    internal static bool NeedsHumanReview(Card card)
    {
        var ext = card.ExternalIssueRef;
        return ext is not null
            && ext.Origin == ExternalIssueOrigin.ExternalImport
            && ext.AuthorIsOperator == false
            && card.ImportanceProvenance == CardImportanceProvenance.Auto
            && card.Status == CardStatus.Backlog
            && card.ArchivedAt is null;
    }

    /// <summary>Reuses the full-card projection, then removes data list surfaces never render.</summary>
    internal static CardDto ToSummaryDto(CardDto card, int previewChars)
    {
        var description = Preview(card.Description, previewChars);
        var terminalReason = card.TerminalReason is null
            ? new TextPreview(null, false)
            : Preview(card.TerminalReason, previewChars);

        return card with
        {
            Description = description.Text ?? string.Empty,
            TerminalReason = terminalReason.Text,
            Sessions = [],
            HasMore = description.HasMore || terminalReason.HasMore,
        };
    }

    private static BoardDetailDto ToSummaryBoardDto(BoardDetailDto board, int previewChars) => board with
    {
        Columns = board.Columns
            .Select(column => column with
            {
                Cards = column.Cards.Select(card => ToSummaryDto(card, previewChars)).ToList(),
            })
            .ToList(),
    };

    private static TextPreview Preview(string? value, int previewChars)
    {
        if (value is null || value.Length <= previewChars)
            return new TextPreview(value, false);

        var limit = Math.Max(1, previewChars);
        var boundary = value.LastIndexOfAny([' ', '\t', '\r', '\n'], Math.Min(limit - 1, value.Length - 1));
        if (boundary <= 0)
        {
            // A single word longer than the limit has no earlier legal cut point. Keep the word
            // intact rather than producing a misleading fragment, then mark it as a preview.
            boundary = value.IndexOfAny([' ', '\t', '\r', '\n'], limit);
            if (boundary < 0)
                return new TextPreview(value, false);
        }

        return new TextPreview(value[..boundary].TrimEnd() + "…", true);
    }

    private sealed record TextPreview(string? Text, bool HasMore);

    internal static CardRevisionDto ToRevisionDto(CardRevision revision)
    {
        return new CardRevisionDto(
            revision.Id,
            revision.CardId,
            revision.RevisionNumber,
            revision.Kind,
            revision.Title,
            revision.Description,
            revision.Importance,
            revision.Urgency,
            revision.DueAt,
            revision.Position,
            revision.LabelsJson is null ? null : ParseLabels(revision.LabelsJson),
            revision.FromColumnId,
            revision.ToColumnId,
            revision.FromStatus,
            revision.ToStatus,
            revision.Reason,
            revision.EditedBy,
            revision.CreatedAt,
            revision.TerminalReason,
            revision.CompletedAt);
    }

    internal async Task<Board> LoadBoardAsync(Guid id, CancellationToken ct)
    {
        return await _db.Boards
            .Include(b => b.Project)
            .Include(b => b.Columns)
            .Include(b => b.Cards)
                .ThenInclude(c => c.AgentSessions)
            .Include(b => b.Cards)
                .ThenInclude(c => c.AssignedAgent)
            .Include(b => b.Cards)
                .ThenInclude(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .Include(b => b.Cards)
                .ThenInclude(c => c.ExternalIssueRef)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException(nameof(Board), id);
    }

    internal static IReadOnlyList<BoardColumn> CreateDefaultColumns(Board board, DateTime utcNow)
    {
        return
        [
            NewColumn(board, "backlog", "Backlog", 0, CardStatus.Backlog, isActive: false, isTerminal: false, utcNow),
            NewColumn(board, "in-progress", "In Progress", 1, CardStatus.InProgress, isActive: true, isTerminal: false, utcNow),
            NewColumn(board, "review", "Review", 2, CardStatus.Review, isActive: false, isTerminal: false, utcNow),
            NewColumn(board, "done", "Done", 3, CardStatus.Done, isActive: false, isTerminal: true, utcNow),
            NewColumn(board, "needs-decision", "Needs decision", 4, CardStatus.NeedsDecision, isActive: false, isTerminal: false, utcNow)
        ];
    }

    private static BoardColumn NewColumn(
        Board board,
        string stateKey,
        string name,
        int order,
        CardStatus status,
        bool isActive,
        bool isTerminal,
        DateTime utcNow)
    {
        return new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = stateKey,
            Name = name,
            ColumnOrder = order,
            CardStatus = status,
            IsActive = isActive,
            IsTerminal = isTerminal,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Board = board
        };
    }

    internal static IReadOnlyList<string> ParseLabels(string labelsJson)
    {
        if (string.IsNullOrWhiteSpace(labelsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(labelsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string SerializeLabels(IReadOnlyList<string>? labels) =>
        JsonSerializer.Serialize(labels ?? [], JsonOptions);

    private static void ValidateBoardRequest(CreateBoardRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ProjectId == Guid.Empty)
            errors[nameof(request.ProjectId)] = ["Project id is required."];
        if (string.IsNullOrWhiteSpace(request.Name))
            errors[nameof(request.Name)] = ["Board name is required."];
        if (request.MaxConcurrentSessions <= 0)
            errors[nameof(request.MaxConcurrentSessions)] = ["Max concurrent sessions must be positive."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
