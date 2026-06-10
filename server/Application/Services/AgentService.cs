using System.Data;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class AgentService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly CardWorkflowRunFactory _workflowRunFactory;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IDirectoryWriter _directoryWriter;

    public AgentService(
        AppDbContext db,
        CardWorkflowRunFactory workflowRunFactory,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IDirectoryWriter directoryWriter)
    {
        _db = db;
        _workflowRunFactory = workflowRunFactory;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _directoryWriter = directoryWriter;
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> GetAllAsync(CancellationToken ct)
    {
        var agents = await _db.Agents
            .AsNoTracking()
            .Include(a => a.DefaultWorkflowTemplate)
            .Include(a => a.Board)
            .Include(a => a.QueueCards)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        var liveSessions = await LoadLiveSessionsAsync(agents.Select(a => a.PersistentSessionId), ct);
        return agents.Select(a => ToSummaryDto(a, ResolveLiveSession(liveSessions, a.PersistentSessionId))).ToList();
    }

    public async Task<AgentDetailDto> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var agent = await LoadAgentDetailAsync(id, asNoTracking: true, ct);
        var liveSessions = await LoadLiveSessionsAsync([agent.PersistentSessionId], ct);
        return ToDetailDto(agent, ResolveLiveSession(liveSessions, agent.PersistentSessionId));
    }

    // Loads the live (Starting/Running/Stopping) AgentSession for each agent's persistent session id,
    // keyed by session id. Stale/ended sessions are excluded so the UI only offers to open a real terminal.
    private async Task<Dictionary<Guid, AgentSessionSummaryDto>> LoadLiveSessionsAsync(
        IEnumerable<string?> persistentSessionIds, CancellationToken ct)
    {
        var ids = persistentSessionIds
            .Select(s => Guid.TryParse(s, out var g) ? (Guid?)g : null)
            .Where(g => g is not null)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return [];

        var sessions = await _db.AgentSessions
            .AsNoTracking()
            .Where(s => ids.Contains(s.Id) && LiveSessionStatuses.Contains(s.Status))
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
            .ToListAsync(ct);

        return sessions.ToDictionary(s => s.Id);
    }

    private static AgentSessionSummaryDto? ResolveLiveSession(
        Dictionary<Guid, AgentSessionSummaryDto> liveSessions, string? persistentSessionId)
        => Guid.TryParse(persistentSessionId, out var id) && liveSessions.TryGetValue(id, out var session)
            ? session
            : null;

    public async Task<AgentDetailDto> CreateAsync(CreateAgentRequest request, CancellationToken ct)
    {
        ValidateAgentRequest(request.Name, request.WorkingDirectory);
        await EnsureWorkflowTemplateExistsAsync(request.DefaultWorkflowTemplateId, ct);

        var workingDirectory = request.WorkingDirectory.Trim();

        // Create the working directory before persisting so a failed mkdir doesn't leave
        // behind an agent pointing at a directory that was never created.
        if (request.CreateWorkingDirectory)
            _directoryWriter.CreateDirectory(workingDirectory);

        var now = UtcNow();
        var agentName = request.Name.Trim();

        // Every agent gets its own board to organise its work. Boards belong to a project, so
        // find-or-create a project keyed on the agent's working directory and hang the board off it.
        var project = await ResolveProjectForWorkingDirectoryAsync(workingDirectory, agentName, now, ct);
        var board = BuildAgentBoard(project, await UniqueBoardNameAsync(project.Id, agentName, ct), now);
        _db.Boards.Add(board);

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = agentName,
            Slug = await UniqueSlugAsync(Slugify(request.Name), excludeAgentId: null, ct),
            WorkingDirectory = workingDirectory,
            Details = request.Details?.Trim() ?? string.Empty,
            DefaultWorkflowTemplateId = request.DefaultWorkflowTemplateId,
            AssignmentPolicy = request.AssignmentPolicy,
            Status = AgentStatus.Idle,
            BoardId = board.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Agents.Add(agent);
        await SaveChangesOrConflictAsync("Agent could not be created because another operation changed agent data.", ct);
        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = board.Id }, ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await GetByIdAsync(agent.Id, ct);
    }

    public async Task<AgentDetailDto> UpdateAsync(Guid id, UpdateAgentRequest request, CancellationToken ct)
    {
        ValidateAgentRequest(request.Name, request.WorkingDirectory);
        await EnsureWorkflowTemplateExistsAsync(request.DefaultWorkflowTemplateId, ct);
        await EnsureBoardExistsAsync(request.BoardId, ct);

        var agent = await _db.Agents
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Agent), id);

        agent.Name = request.Name.Trim();
        agent.Slug = await UniqueSlugAsync(Slugify(request.Name), agent.Id, ct);
        agent.WorkingDirectory = request.WorkingDirectory.Trim();
        agent.Details = request.Details?.Trim() ?? string.Empty;
        agent.DefaultWorkflowTemplateId = request.DefaultWorkflowTemplateId;
        agent.AssignmentPolicy = request.AssignmentPolicy;
        agent.BoardId = request.BoardId;
        agent.UpdatedAt = UtcNow();

        await SaveChangesOrConflictAsync($"Agent '{agent.Name}' was modified by another operation.", ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await GetByIdAsync(agent.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var agent = await _db.Agents
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Agent), id);

        // Release the agent's hold on any cards and drop its workflow runs. CardWorkflowRun.AgentId
        // uses Restrict, so the runs must be removed explicitly before the agent can be deleted.
        var now = UtcNow();
        var assignedCards = await _db.Cards
            .Where(c => c.AssignedAgentId == id)
            .ToListAsync(ct);
        foreach (var card in assignedCards)
        {
            card.AssignedAgentId = null;
            card.AgentQueuePosition = null;
            card.ActiveWorkflowRunId = null;
            card.ActiveWorkflowRun = null;
            card.UpdatedAt = now;
            card.ConcurrencyToken = Guid.NewGuid();
        }

        var runs = await _db.CardWorkflowRuns.Where(r => r.AgentId == id).ToListAsync(ct);

        // Card<->CardWorkflowRun and CardWorkflowRun<->CardWorkflowStage reference each other, so
        // deleting in one batch forms a cycle EF can't order. Null the back-references and persist
        // that first, then delete the runs (their stages cascade) and the agent.
        foreach (var run in runs)
            run.CurrentStageId = null;

        if (assignedCards.Count > 0 || runs.Count > 0)
            await SaveChangesOrConflictAsync($"Agent '{agent.Name}' was modified by another operation.", ct);

        _db.CardWorkflowRuns.RemoveRange(runs);
        _db.Agents.Remove(agent);
        await SaveChangesOrConflictAsync($"Agent '{agent.Name}' was modified by another operation.", ct);

        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(id), ct);
        foreach (var card in assignedCards)
            await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
    }

    public async Task<AgentDetailDto> AssignCardAsync(Guid id, AssignAgentCardRequest request, CancellationToken ct)
    {
        Guid cardId;
        Guid boardId;
        await using (var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            var agent = await LoadAgentForQueueUpdateAsync(id, ct);
            var card = await _db.Cards
                .Include(c => c.Board)
                .FirstOrDefaultAsync(c => c.Id == request.CardId, ct)
                ?? throw new NotFoundException(nameof(Card), request.CardId);

            if (card.AssignedAgentId is not null)
                throw new ConflictException($"Card '{card.Identifier}' is already assigned to an agent.");

            var nextPosition = await _db.Cards
                .Where(c => c.AssignedAgentId == agent.Id && c.AgentQueuePosition != null)
                .MaxAsync(c => (int?)c.AgentQueuePosition, ct) ?? 0;

            var now = UtcNow();
            var run = await _workflowRunFactory.CreateFromAgentDefaultAsync(card, agent, ct);
            var currentStageId = run.CurrentStageId;
            run.CurrentStageId = null;
            _db.CardWorkflowRuns.Add(run);

            card.AssignedAgentId = agent.Id;
            card.AgentQueuePosition = nextPosition + 1;
            card.ActiveWorkflowRun = run;
            card.ActiveWorkflowRunId = run.Id;
            card.UpdatedAt = now;
            card.ConcurrencyToken = Guid.NewGuid();

            await SaveChangesOrConflictAsync($"Card '{card.Identifier}' was modified by another operation.", ct);
            run.CurrentStageId = currentStageId;
            await SaveChangesOrConflictAsync($"Card '{card.Identifier}' workflow was modified by another operation.", ct);
            await transaction.CommitAsync(ct);

            cardId = card.Id;
            boardId = card.BoardId;
        }

        await _eventBus.PublishToAllAsync(
            "AgentQueueChanged",
            new AgentQueueChangedEventDto(id, CardId: cardId, BoardId: boardId),
            ct);
        await _eventBus.PublishToAllAsync("CardChanged", new { boardId, cardId }, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<AgentDetailDto> ReorderQueueAsync(Guid id, ReorderAgentQueueRequest request, CancellationToken ct)
    {
        ValidateReorderRequest(request);

        List<Card> changedCards;
        List<Guid> orderedCardIds;
        await using (var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            await LoadAgentForQueueUpdateAsync(id, ct);

            var cards = await _db.Cards
                .Where(c => c.AssignedAgentId == id)
                .OrderBy(c => c.AgentQueuePosition)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync(ct);
            var cardsById = cards.ToDictionary(c => c.Id);
            var requestedIds = request.CardIds
                .Where(cardsById.ContainsKey)
                .Distinct()
                .ToList();
            var orderedCards = requestedIds
                .Select(cardId => cardsById[cardId])
                .Concat(cards.Where(c => !requestedIds.Contains(c.Id)))
                .ToList();

            var now = UtcNow();
            changedCards = [];
            for (var index = 0; index < orderedCards.Count; index++)
            {
                var card = orderedCards[index];
                var position = index + 1;
                if (card.AgentQueuePosition == position)
                    continue;

                card.AgentQueuePosition = position;
                card.UpdatedAt = now;
                card.ConcurrencyToken = Guid.NewGuid();
                changedCards.Add(card);
            }

            orderedCardIds = orderedCards.Select(c => c.Id).ToList();
            await SaveChangesOrConflictAsync("Agent queue was modified by another operation.", ct);
            await transaction.CommitAsync(ct);
        }

        await _eventBus.PublishToAllAsync(
            "AgentQueueChanged",
            new AgentQueueChangedEventDto(id, CardIds: orderedCardIds),
            ct);

        foreach (var card in changedCards)
            await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task RemoveCardAsync(Guid id, Guid cardId, CancellationToken ct)
    {
        Card removedCard;
        List<Card> shiftedCards;
        await using (var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            await LoadAgentForQueueUpdateAsync(id, ct);

            removedCard = await _db.Cards
                .FirstOrDefaultAsync(c => c.Id == cardId, ct)
                ?? throw new NotFoundException(nameof(Card), cardId);
            if (removedCard.AssignedAgentId != id)
                throw new ConflictException($"Card '{removedCard.Identifier}' is not assigned to this agent.");

            var now = UtcNow();
            removedCard.AssignedAgentId = null;
            removedCard.AgentQueuePosition = null;
            removedCard.ActiveWorkflowRunId = null;
            removedCard.ActiveWorkflowRun = null;
            removedCard.UpdatedAt = now;
            removedCard.ConcurrencyToken = Guid.NewGuid();

            shiftedCards = await CompactQueueAsync(id, cardId, now, ct);
            await SaveChangesOrConflictAsync($"Card '{removedCard.Identifier}' was modified by another operation.", ct);
            await transaction.CommitAsync(ct);
        }

        await _eventBus.PublishToAllAsync(
            "AgentQueueChanged",
            new AgentQueueChangedEventDto(
                id,
                CardId: cardId,
                CardIds: shiftedCards.Select(c => c.Id).ToList(),
                BoardId: removedCard.BoardId),
            ct);
        await _eventBus.PublishToAllAsync(
            "CardChanged",
            new { boardId = removedCard.BoardId, cardId = removedCard.Id },
            ct);
        foreach (var card in shiftedCards)
            await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
    }

    private async Task<List<Card>> CompactQueueAsync(Guid agentId, Guid excludedCardId, DateTime now, CancellationToken ct)
    {
        var cards = await _db.Cards
            .Where(c => c.AssignedAgentId == agentId && c.Id != excludedCardId)
            .OrderBy(c => c.AgentQueuePosition)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var changedCards = new List<Card>();
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var position = index + 1;
            if (card.AgentQueuePosition == position)
                continue;

            card.AgentQueuePosition = position;
            card.UpdatedAt = now;
            card.ConcurrencyToken = Guid.NewGuid();
            changedCards.Add(card);
        }

        return changedCards;
    }

    private async Task<Agent> LoadAgentForQueueUpdateAsync(Guid id, CancellationToken ct)
    {
        return await _db.Agents
            .FromSqlInterpolated($"""SELECT * FROM "Agents" WHERE "Id" = {id} FOR UPDATE""")
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Agent), id);
    }

    private async Task<Agent> LoadAgentDetailAsync(Guid id, bool asNoTracking, CancellationToken ct)
    {
        var query = _db.Agents
            .Include(a => a.DefaultWorkflowTemplate)
            .Include(a => a.Board)
            .Include(a => a.QueueCards)
                .ThenInclude(c => c.Board)
            .Include(a => a.QueueCards)
                .ThenInclude(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .AsSplitQuery();

        if (asNoTracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Agent), id);
    }

    private async Task EnsureWorkflowTemplateExistsAsync(Guid? templateId, CancellationToken ct)
    {
        if (templateId is not Guid id)
            return;

        var exists = await _db.WorkflowTemplates.AnyAsync(t => t.Id == id, ct);
        if (!exists)
            throw new NotFoundException(nameof(WorkflowTemplate), id);
    }

    private async Task EnsureBoardExistsAsync(Guid? boardId, CancellationToken ct)
    {
        if (boardId is not Guid id)
            return;

        var exists = await _db.Boards.AnyAsync(b => b.Id == id, ct);
        if (!exists)
            throw new NotFoundException(nameof(Board), id);
    }

    // Reuse an existing project that already points at the same working directory, otherwise create
    // a lightweight internal project for it. The git URL is left blank — an agent's working directory
    // is a local path, and the project exists only to anchor the agent's board.
    private async Task<Project> ResolveProjectForWorkingDirectoryAsync(
        string workingDirectory, string fallbackName, DateTime now, CancellationToken ct)
    {
        var existing = await _db.Projects
            .FirstOrDefaultAsync(p => p.LocalRepositoryPath == workingDirectory, ct);
        if (existing is not null)
            return existing;

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = await UniqueProjectNameAsync(DeriveProjectName(workingDirectory, fallbackName), ct),
            GitRepositoryUrl = string.Empty,
            LocalRepositoryPath = workingDirectory,
            BaseBranch = "master",
            ConstitutionPath = "AGENTS.md;CLAUDE.md;README.md",
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Projects.Add(project);
        return project;
    }

    private static Board BuildAgentBoard(Project project, string name, DateTime now)
    {
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
            Name = name,
            Description = string.Empty,
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var column in BoardService.CreateDefaultColumns(board, now))
            board.Columns.Add(column);
        return board;
    }

    private static string DeriveProjectName(string workingDirectory, string fallback)
    {
        var trimmed = workingDirectory.TrimEnd('/', '\\');
        var separator = trimmed.LastIndexOfAny(['/', '\\']);
        var leaf = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(leaf) ? fallback : leaf;
    }

    private async Task<string> UniqueProjectNameAsync(string baseName, CancellationToken ct)
    {
        var name = Cap(baseName);
        var suffix = 2;
        while (await _db.Projects.AnyAsync(p => p.Name == name, ct))
            name = $"{Cap(baseName, suffix)} ({suffix++})";
        return name;
    }

    private async Task<string> UniqueBoardNameAsync(Guid projectId, string baseName, CancellationToken ct)
    {
        var name = Cap(baseName);
        var suffix = 2;
        while (await _db.Boards.AnyAsync(b => b.ProjectId == projectId && b.Name == name, ct))
            name = $"{Cap(baseName, suffix)} ({suffix++})";
        return name;
    }

    // Project/board names are capped at 200 chars in the schema; leave room for the dedupe suffix.
    private static string Cap(string value, int suffix = 0)
    {
        var reserve = suffix == 0 ? 0 : $" ({suffix})".Length;
        var max = 200 - reserve;
        return value.Length <= max ? value : value[..max].TrimEnd();
    }

    private static AgentSummaryDto ToSummaryDto(Agent agent, AgentSessionSummaryDto? liveSession)
    {
        return new AgentSummaryDto(
            agent.Id,
            agent.Name,
            agent.Slug,
            agent.WorkingDirectory,
            agent.Details,
            agent.DefaultWorkflowTemplateId,
            agent.DefaultWorkflowTemplate?.Name,
            agent.AssignmentPolicy,
            agent.Status,
            agent.PersistentSessionId,
            agent.CurrentCardId,
            agent.BoardId,
            agent.Board?.Name,
            agent.QueueCards.Count,
            agent.CreatedAt,
            agent.UpdatedAt,
            liveSession);
    }

    private static AgentDetailDto ToDetailDto(Agent agent, AgentSessionSummaryDto? liveSession)
    {
        var queue = agent.QueueCards
            .Where(c => c.AgentQueuePosition is not null)
            .OrderBy(c => c.AgentQueuePosition)
            .ThenBy(c => c.CreatedAt)
            .Select(c => new AgentQueueCardDto(
                c.Id,
                c.BoardId,
                c.Board.Name,
                c.Identifier,
                c.Title,
                c.Priority,
                c.AgentQueuePosition!.Value,
                c.ActiveWorkflowRunId,
                c.ActiveWorkflowRun?.Status,
                c.ActiveWorkflowRun?.CurrentStage?.Name))
            .ToList();

        return new AgentDetailDto(
            agent.Id,
            agent.Name,
            agent.Slug,
            agent.WorkingDirectory,
            agent.Details,
            agent.DefaultWorkflowTemplateId,
            agent.DefaultWorkflowTemplate?.Name,
            agent.AssignmentPolicy,
            agent.Status,
            agent.PersistentSessionId,
            agent.CurrentCardId,
            agent.BoardId,
            agent.Board?.Name,
            queue,
            agent.CreatedAt,
            agent.UpdatedAt,
            liveSession);
    }

    private async Task<string> UniqueSlugAsync(string baseSlug, Guid? excludeAgentId, CancellationToken ct)
    {
        var slug = TrimSlug(baseSlug);
        var suffix = 2;
        while (await _db.Agents.AnyAsync(a => a.Slug == slug && a.Id != excludeAgentId, ct))
        {
            var suffixText = $"-{suffix++}";
            slug = $"{TrimSlug(baseSlug, 120 - suffixText.Length)}{suffixText}";
        }

        return slug;
    }

    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? $"agent-{Guid.NewGuid():N}" : slug;
    }

    private static string TrimSlug(string slug, int maxLength = 120)
    {
        if (slug.Length <= maxLength)
            return slug;

        return slug[..maxLength].Trim('-');
    }

    private static void ValidateAgentRequest(string name, string workingDirectory)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name))
            errors["Name"] = ["Agent name is required."];
        if (string.IsNullOrWhiteSpace(workingDirectory))
            errors["WorkingDirectory"] = ["Working directory is required."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static void ValidateReorderRequest(ReorderAgentQueueRequest request)
    {
        if (request.CardIds is null)
            throw new ValidationException(nameof(request.CardIds), "Card ids are required.");
    }

    private async Task SaveChangesOrConflictAsync(string message, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(message);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(message);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
