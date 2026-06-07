using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class BoardService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public BoardService(AppDbContext db, IEventBus eventBus, TimeProvider timeProvider)
    {
        _db = db;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<BoardSummaryDto>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Boards
            .AsNoTracking()
            .Include(b => b.Project)
            .Include(b => b.Cards)
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
                b.Cards.Count,
                b.CreatedAt,
                b.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<BoardDetailDto> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var board = await LoadBoardAsync(id, ct);
        return ToDetailDto(board);
    }

    public async Task<BoardDetailDto> CreateAsync(CreateBoardRequest request, CancellationToken ct)
    {
        ValidateBoardRequest(request);

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct)
            ?? throw new NotFoundException(nameof(Project), request.ProjectId);

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

    internal static BoardDetailDto ToDetailDto(Board board)
    {
        var cardsByColumn = board.Cards
            .GroupBy(c => c.BoardColumnId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Priority).ThenBy(c => c.CreatedAt).ToList());

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
                    .Select(ToCardDto)
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
            board.UpdatedAt);
    }

    internal static CardDto ToCardDto(Card card)
    {
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
            card.Priority,
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
                    s.FailureReason))
                .ToList());
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
            NewColumn(board, "done", "Done", 3, CardStatus.Done, isActive: false, isTerminal: true, utcNow)
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

    private static IReadOnlyList<string> ParseLabels(string labelsJson)
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
