using System.Data;
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
using Npgsql;

namespace Antiphon.Server.Application.Services;

public sealed class AgentService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private const string CreateFailedMessage =
        "Agent could not be created because another operation changed agent data.";

    /// <summary>
    /// Attempts at deriving a free slug/board/project name before giving up. Each retry sees one
    /// more committed row, so contention would have to be extreme to exhaust this.
    /// </summary>
    private const int GeneratedNameAttempts = 5;

    private readonly AppDbContext _db;
    private readonly CardWorkflowRunFactory _workflowRunFactory;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IDirectoryWriter _directoryWriter;
    private readonly ILogger<AgentService> _logger;
    private readonly AgentWorkspaceProvisioner? _workspace;
    private readonly ContextWindowSettings _contextWindow;

    public AgentService(
        AppDbContext db,
        CardWorkflowRunFactory workflowRunFactory,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IDirectoryWriter directoryWriter,
        ILogger<AgentService> logger,
        // Optional so the many harnesses that construct this service by hand keep compiling; without
        // it an agent is simply created without its CLAUDE.md floor, which the next launch writes.
        AgentWorkspaceProvisioner? workspace = null,
        IOptions<ContextWindowSettings>? contextWindow = null)
    {
        _db = db;
        _workflowRunFactory = workflowRunFactory;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _directoryWriter = directoryWriter;
        _logger = logger;
        _workspace = workspace;
        _contextWindow = contextWindow?.Value ?? new ContextWindowSettings();
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> GetAllAsync(CancellationToken ct)
    {
        var agents = await _db.Agents
            .AsNoTracking()
            .Include(a => a.DefaultWorkflowTemplate)
            .Include(a => a.Board)
            .Include(a => a.QueueCards)
            .Include(a => a.TuiProfile)!.ThenInclude(p => p!.ActiveRevision)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        var liveSessions = await LoadLiveSessionsAsync(agents.Select(a => a.PersistentSessionId), ct);
        var supervision = await LoadSupervisionAsync(agents.Where(a => a.AlwaysOn).Select(a => a.Id), ct);
        // One query for every agent's attachments (CARD-0058 slice 6) — the list's drift badges are
        // otherwise N queries. Explicit rather than an Include for the same reason the launch paths
        // are: a missing include reads as "no attachments" and would clear every badge on the page.
        var attachments = await AgentBundleAttachments.LoadAsync(
            _db, [.. agents.Select(a => a.Id)], _logger, ct);
        var result = new List<AgentSummaryDto>(agents.Count);
        foreach (var a in agents)
        {
            var live = ResolveLiveSession(liveSessions, a.PersistentSessionId);
            result.Add(ToSummaryDto(
                a,
                live?.Dto,
                supervision.GetValueOrDefault(a.Id),
                await IsSessionWorkingAsync(live?.Dto, ct),
                IsOutOfDate(live, Compose(a, attachments.GetValueOrDefault(a.Id, [])))));
        }
        return result;
    }

    /// <summary>
    /// What this agent's NEXT launch will compose (CARD-0058) — the same call
    /// <c>AgentControlService</c> makes, so the UI and the launch can never disagree about which
    /// bundles an agent carries. Recomputed per request; nothing composed is stored.
    /// </summary>
    private static ComposedInstructions Compose(Agent agent, IReadOnlyList<string> attachedKeys) =>
        InstructionBundleComposer.Compose(attachedKeys, AgentReplyStyles.ComposedKey(agent.ReplyStyle));

    /// <summary>
    /// Drift: the live session was launched carrying something other than what the repo composes
    /// now. No live session, or a session with no recorded stamp, is NO EVIDENCE — never drift.
    /// </summary>
    private static bool IsOutOfDate(LiveSession? live, ComposedInstructions current) =>
        live is not null && InstructionBundleComposer.IsOutOfDate(live.BundleStamp, current);

    /// <summary>
    /// The transcript-derived "mid-turn right now" signal for a live session — what the agent
    /// cards render as Working (with a spinner). Status=Running only means "started".
    /// </summary>
    private async Task<bool> IsSessionWorkingAsync(AgentSessionSummaryDto? live, CancellationToken ct) =>
        live is { Status: SessionStatus.Running }
        && await SessionMessageQueueService.IsWorkingAsync(_db, live.Id, ct);

    public async Task<AgentDetailDto> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var agent = await LoadAgentDetailAsync(id, asNoTracking: true, ct);
        var liveSessions = await LoadLiveSessionsAsync([agent.PersistentSessionId], ct);
        var supervision = agent.AlwaysOn
            ? (await LoadSupervisionAsync([agent.Id], ct)).GetValueOrDefault(agent.Id)
            : null;
        var live = ResolveLiveSession(liveSessions, agent.PersistentSessionId);
        var attachedKeys = await AgentBundleAttachments.LoadAsync(_db, agent.Id, _logger, ct);
        return ToDetailDto(
            agent, live?.Dto, supervision, await IsSessionWorkingAsync(live?.Dto, ct), live, attachedKeys);
    }

    public async Task<IReadOnlyList<AgentIncidentDto>> GetIncidentsAsync(Guid agentId, int take, CancellationToken ct)
    {
        _ = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        return await _db.AgentIncidents
            .AsNoTracking()
            .Where(i => i.AgentId == agentId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(i => new AgentIncidentDto(
                i.Id, i.AgentId, i.SessionId, i.Kind, i.Severity, i.Message, i.ExitCode, i.FailureReason, i.CreatedAt))
            .ToListAsync(ct);
    }

    private async Task<Dictionary<Guid, AgentSupervisionDto>> LoadSupervisionAsync(
        IEnumerable<Guid> agentIds, CancellationToken ct)
    {
        var ids = agentIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await _db.AgentSupervisionStates
            .AsNoTracking()
            .Where(s => ids.Contains(s.AgentId))
            .ToDictionaryAsync(
                s => s.AgentId,
                s => new AgentSupervisionDto(
                    s.Suspended, s.ConsecutiveFailures, s.NextRestartAt, s.LastEscalationTier),
                ct);
    }

    /// <summary>
    /// A live session as the DTO layer needs it, plus the one thing the DTO does not carry: the
    /// bundle stamp its launch recorded (CARD-0058 slice 6). Kept off
    /// <see cref="AgentSessionSummaryDto"/> deliberately — the stamp is an input to a comparison the
    /// server makes, not a fact the client has any use for.
    /// </summary>
    private sealed record LiveSession(AgentSessionSummaryDto Dto, string? BundleStamp);

    // Loads the live (Starting/Running/Stopping) AgentSession for each agent's persistent session id,
    // keyed by session id. Stale/ended sessions are excluded so the UI only offers to open a real terminal.
    private async Task<Dictionary<Guid, LiveSession>> LoadLiveSessionsAsync(
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
            .Select(s => new LiveSession(
                new AgentSessionSummaryDto(
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
                    null),
                s.ComposedBundleStamp))
            .ToListAsync(ct);

        var fullness = await SessionContextUsage.LoadFullnessAsync(
            _db,
            sessions.Select(s => (s.Dto.Id, s.Dto.EffectiveModelId, s.Dto.AgentKind)).ToList(),
            _contextWindow,
            _logger,
            ct);

        return sessions.ToDictionary(
            s => s.Dto.Id,
            s => s with { Dto = s.Dto with { ContextFullness = fullness.GetValueOrDefault(s.Dto.Id) } });
    }

    private static LiveSession? ResolveLiveSession(
        Dictionary<Guid, LiveSession> liveSessions, string? persistentSessionId)
        => Guid.TryParse(persistentSessionId, out var id) && liveSessions.TryGetValue(id, out var session)
            ? session
            : null;

    public async Task<AgentDetailDto> CreateAsync(CreateAgentRequest request, CancellationToken ct)
    {
        ValidateAgentRequest(request.Name, request.WorkingDirectory);
        ValidateAutoCompactOverrides(request.AutoCompactIdleMinutes, request.AutoCompactContextPercent);
        await EnsureWorkflowTemplateExistsAsync(request.DefaultWorkflowTemplateId, ct);

        var workingDirectory = request.WorkingDirectory.Trim();

        // Create the working directory before persisting so a failed mkdir doesn't leave
        // behind an agent pointing at a directory that was never created.
        if (request.CreateWorkingDirectory)
            _directoryWriter.CreateDirectory(workingDirectory);

        var agentName = request.Name.Trim();

        // The slug, board name and project name are each picked by asking "is this taken?" and then
        // inserting, which races their unique indexes: two agents created with the same name at the
        // same moment both see it free and both insert. The loser retries, and by then the winner's
        // row is visible, so the retry derives the "-2" variant instead of failing the request.
        for (var attempt = 1; ; attempt++)
        {
            var now = UtcNow();

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
                ModelLevel = request.ModelLevel ?? AgentModelLevel.High,
                ReplyStyle = request.ReplyStyle,
                AlwaysOn = request.AlwaysOn,
                RemoteControlEnabled = request.RemoteControlEnabled,
                AutoCompactEnabled = request.AutoCompactEnabled,
                AutoCompactIdleMinutes = request.AutoCompactIdleMinutes,
                AutoCompactContextPercent = request.AutoCompactContextPercent,
                BoardId = board.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            await ApplyTuiSelectionAsync(
                agent,
                request.TuiProfileId,
                request.ModelId,
                profileRequired: false,
                ct);
            _db.Agents.Add(agent);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (attempt < GeneratedNameAttempts && IsGeneratedNameCollision(ex))
            {
                // SaveChanges is transactional, so nothing landed. Drop the whole attempt and
                // re-derive every generated name against the rows that are now visible.
                _logger.LogDebug(
                    ex,
                    "Generated-name collision creating agent {AgentName} on attempt {Attempt}; retrying",
                    agentName, attempt);
                _db.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw ConflictFrom(CreateFailedMessage, ex);
            }
            catch (DbUpdateException ex)
            {
                throw ConflictFrom(CreateFailedMessage, ex);
            }

            // The CLAUDE.md floor (CARD-0059), after the row exists so it can name the agent and its
            // job. Never clobbers an unmarked file, so an agent created in a repository checkout gets
            // nothing written — the repo's own CLAUDE.md already serves it.
            _workspace?.Provision(agent);

            await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = board.Id }, ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

            return await GetByIdAsync(agent.Id, ct);
        }
    }

    public async Task<AgentDetailDto> UpdateAsync(Guid id, UpdateAgentRequest request, CancellationToken ct)
    {
        ValidateAgentRequest(request.Name, request.WorkingDirectory);
        ValidateAutoCompactOverrides(request.AutoCompactIdleMinutes, request.AutoCompactContextPercent);
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
        // Every agent keeps a default board (Add-Work and card routing rely on it): null means
        // "leave unchanged" (mirrors AlwaysOn/RemoteControlEnabled) — an update can MOVE the agent
        // to another board, never clear the link. Unconditional assignment here silently orphaned
        // agents whenever an update omitted the board.
        if (request.BoardId is { } newBoardId)
            agent.BoardId = newBoardId;
        if (request.AlwaysOn is { } alwaysOn)
            agent.AlwaysOn = alwaysOn;
        if (request.RemoteControlEnabled is { } remoteControlEnabled)
            agent.RemoteControlEnabled = remoteControlEnabled;
        if (request.SystemPromptAppend is { } systemPromptAppend)
        {
            // CARD-0106 S2: a placeholder here would be resolved into --append-system-prompt, which
            // is an ARGUMENT (process-listing-visible, quoted into failure reasons) whose text also
            // lands in the transcript. Refused at the moment it is typed, not silently stripped —
            // stripping would launch the agent under a contract that quietly lost a line.
            ApiKeyPlaceholderInPromptGuard(systemPromptAppend);
            agent.SystemPromptAppend = string.IsNullOrWhiteSpace(systemPromptAppend) ? null : systemPromptAppend;
        }

        // Null leaves it alone (an older caller must not wipe a configured environment); an empty
        // dictionary is the explicit clear.
        if (request.LaunchEnv is { } launchEnv)
            agent.LaunchEnvJson = AgentLaunchEnv.Serialize(AgentLaunchEnv.Validate(launchEnv));
        if (request.ModelLevel is { } modelLevel)
            agent.ModelLevel = modelLevel;
        if (request.ReplyStyle is { } replyStyle)
            agent.ReplyStyle = replyStyle;
        // Applied even when null — null IS the "use the global default" state (CARD-0082 S2).
        agent.AutoCompactEnabled = request.AutoCompactEnabled;
        agent.AutoCompactIdleMinutes = request.AutoCompactIdleMinutes;
        agent.AutoCompactContextPercent = request.AutoCompactContextPercent;
        // CARD-0058 slice 6. Null leaves attachments alone; an empty list detaches everything. The
        // rows change here and nothing else does: the agent's RUNNING session keeps the bundles it
        // launched with, which is what the drift badge on the detail DTO is for.
        if (request.BundleKeys is { } bundleKeys)
            await AgentBundleAttachments.SetAsync(_db, agent, bundleKeys, UtcNow(), ct);
        if (request.TuiProfileId is { } profileId)
        {
            await ApplyTuiSelectionAsync(
                agent,
                profileId,
                request.ModelId,
                profileRequired: true,
                ct);
        }
        // CARD-0139. After TuiProfileId so a PATCH that moves the profile AND asserts Kind is
        // checked against the NEW profile, not the one this request is replacing.
        if (request.Kind is { } requestedKind)
            await ApplyKindAssertOrSetAsync(agent, requestedKind, ct);
        agent.UpdatedAt = UtcNow();

        await SaveChangesOrConflictAsync($"Agent '{agent.Name}' was modified by another operation.", ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await GetByIdAsync(agent.Id, ct);
    }

    /// <summary>
    /// Backfill: every agent must have a default board. Agents created before that rule — or whose
    /// board link was cleared by the old update path — are RE-LINKED to their original board when
    /// it still exists (same project, named after the agent, not claimed by another agent);
    /// otherwise a board (and project) is created exactly like <see cref="CreateAsync"/> would
    /// have. Runs at startup; idempotent. Saves per agent so two boardless agents sharing a
    /// working directory reuse one project.
    /// </summary>
    public async Task<int> EnsureAgentBoardsAsync(CancellationToken ct)
    {
        // Work from ids and re-read each agent: this is a global sweep over every boardless agent,
        // and the rows can move under it. One agent that cannot be linked — its project deleted
        // between the resolve and the insert, say — is logged and skipped rather than aborting the
        // backfill for everyone behind it. This runs during startup; failing the whole sweep on one
        // bad row is the worst available outcome.
        var orphanIds = await _db.Agents
            .Where(a => a.BoardId == null)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var linked = 0;
        foreach (var agentId in orphanIds)
        {
            try
            {
                var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
                // Deleted, or linked by someone else, since the sweep began.
                if (agent is null || agent.BoardId is not null)
                    continue;

                var now = UtcNow();
                var project = await ResolveProjectForWorkingDirectoryAsync(agent.WorkingDirectory, agent.Name, now, ct);

                var claimedBoardIds = await _db.Agents
                    .Where(a => a.BoardId != null)
                    .Select(a => a.BoardId!.Value)
                    .ToListAsync(ct);
                var adopted = await _db.Boards
                    .Where(b => b.ProjectId == project.Id && b.Name == agent.Name && !claimedBoardIds.Contains(b.Id))
                    .OrderBy(b => b.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                var boardId = adopted?.Id;
                if (boardId is null)
                {
                    var board = BuildAgentBoard(project, await UniqueBoardNameAsync(project.Id, agent.Name, ct), now);
                    _db.Boards.Add(board);
                    boardId = board.Id;
                }

                agent.BoardId = boardId;
                agent.UpdatedAt = now;
                await SaveChangesOrConflictAsync(
                    $"A default board for agent '{agent.Name}' could not be created because another operation changed agent data.",
                    ct);
                linked++;
                await _eventBus.PublishToAllAsync("BoardChanged", new { boardId }, ct);
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
            }
            catch (ConflictException ex)
            {
                // ConflictException now carries the real database error, so this line says which
                // constraint broke instead of "something changed".
                _logger.LogWarning(ex, "Skipped board backfill for agent {AgentId}", agentId);
                // Drop the failed attempt; the next agent re-reads from a clean tracker.
                _db.ChangeTracker.Clear();
            }
        }

        return linked;
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
            .Include(a => a.TuiProfile)!.ThenInclude(p => p!.ActiveRevision)
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

    private static AgentSummaryDto ToSummaryDto(
        Agent agent, AgentSessionSummaryDto? liveSession, AgentSupervisionDto? supervision,
        bool working = false, bool bundlesOutOfDate = false)
    {
        var (configured, liveSelection) = MapTuiSelection(agent, liveSession);
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
            liveSession,
            agent.AlwaysOn,
            agent.RemoteControlEnabled,
            supervision,
            agent.SystemPromptAppend,
            agent.ModelLevel,
            working,
            agent.TuiProfileId,
            agent.ModelId,
            configured,
            liveSelection,
            agent.ReplyStyle,
            bundlesOutOfDate,
            agent.AutoCompactEnabled,
            agent.AutoCompactIdleMinutes,
            agent.AutoCompactContextPercent,
            AgentLaunchEnv.Parse(agent.LaunchEnvJson),
            agent.Kind);
    }

    private static AgentDetailDto ToDetailDto(
        Agent agent, AgentSessionSummaryDto? liveSession, AgentSupervisionDto? supervision = null,
        bool working = false, LiveSession? live = null, IReadOnlyList<string>? attachedKeys = null)
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
        var (configured, liveSelection) = MapTuiSelection(agent, liveSession);
        var keys = attachedKeys ?? [];
        var composed = Compose(agent, keys);

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
            liveSession,
            agent.AlwaysOn,
            agent.RemoteControlEnabled,
            supervision,
            agent.SystemPromptAppend,
            agent.ModelLevel,
            working,
            agent.TuiProfileId,
            agent.ModelId,
            configured,
            liveSelection,
            agent.ReplyStyle,
            // What the NEXT launch will carry, composed the same way AgentControlService composes it
            // — recomputed per request rather than stored, so the list can never drift from the repo.
            composed.Stamps,
            IsOutOfDate(live, composed),
            keys,
            agent.AutoCompactEnabled,
            agent.AutoCompactIdleMinutes,
            agent.AutoCompactContextPercent,
            AgentLaunchEnv.Parse(agent.LaunchEnvJson),
            agent.Kind);
    }

    private static (AgentTuiConfiguredSelectionDto? Configured, AgentTuiLiveSessionSelectionDto? Live)
        MapTuiSelection(Agent agent, AgentSessionSummaryDto? liveSession)
    {
        var configured = agent.TuiProfileId is null
            ? null
            : new AgentTuiConfiguredSelectionDto(
                agent.TuiProfileId,
                agent.ModelId,
                agent.TuiProfile?.DisplayName,
                agent.TuiProfile?.ActiveRevision?.RevisionNumber);

        if (liveSession is null)
            return (configured, null);

        var pendingRestart =
            liveSession.TuiProfileRevisionId != agent.TuiProfile?.ActiveRevisionId
            || !string.Equals(
                liveSession.EffectiveModelId,
                agent.ModelId,
                StringComparison.Ordinal);

        var liveSelection = new AgentTuiLiveSessionSelectionDto(
            liveSession.TuiProfileRevisionId,
            liveSession.EffectiveModelId,
            pendingRestart);
        return (configured, liveSelection);
    }

    private async Task ApplyTuiSelectionAsync(
        Agent agent,
        Guid? requestedProfileId,
        string? requestedModelId,
        bool profileRequired,
        CancellationToken ct)
    {
        AgentTuiProfile? profile;
        if (requestedProfileId is { } profileId)
        {
            profile = await _db.AgentTuiProfiles
                .Include(candidate => candidate.ActiveRevision)
                .Include(candidate => candidate.Models)
                .SingleOrDefaultAsync(candidate => candidate.Id == profileId, ct)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
        }
        else if (!profileRequired)
        {
            profile = await _db.AgentTuiProfiles
                .Include(candidate => candidate.ActiveRevision)
                .Include(candidate => candidate.Models)
                .SingleOrDefaultAsync(candidate => candidate.IsDefault, ct);
            if (profile is null)
            {
                agent.TuiProfileId = null;
                agent.ModelId = NormalizeModelId(requestedModelId);
                // D1 second half: a row with no profile keeps its own Kind. A pool delegate's
                // Kind is the only fact protecting it from being claimed by the wrong task kind.
                return;
            }
        }
        else
        {
            return;
        }

        if (!profile.IsEnabled)
        {
            throw new ConflictException(
                "The selected runner profile is disabled.",
                "profile_disabled");
        }
        if (profile.ActiveRevisionId is null)
        {
            throw new ConflictException(
                "The selected runner profile has no active revision.",
                "profile_not_validated");
        }

        var modelId = NormalizeModelId(requestedModelId);
        if (modelId is not null)
            EnsureModelInProfile(profile, modelId);

        agent.TuiProfileId = profile.Id;
        agent.ModelId = modelId;
        AgentProfileKind.Sync(agent, profile);
    }

    /// <summary>
    /// CARD-0139 assert-or-set. A pool delegate's Kind is owned by the dispatcher and is refused
    /// even when the requested value agrees. A profiled agent's Kind is derived from the attached
    /// profile (CARD-0138 D1): agreement is a no-op, disagreement is 409. Only a non-pool agent
    /// with no profile at all takes the write.
    /// </summary>
    private async Task ApplyKindAssertOrSetAsync(Agent agent, AgentKind requestedKind, CancellationToken ct)
    {
        if (agent.IsPoolDelegate)
        {
            throw new ConflictException(
                $"Agent '{agent.Name}' is a pool delegate; its Kind is owned by the task dispatcher and cannot be set. Omit kind.",
                "agent_kind_pool_delegate");
        }

        if (agent.TuiProfileId is { } profileId)
        {
            var profile = await _db.AgentTuiProfiles
                .AsNoTracking()
                .Where(p => p.Id == profileId)
                .Select(p => new { p.DisplayName, p.Kind })
                .SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);

            if (requestedKind == profile.Kind)
                return;

            throw new ConflictException(
                $"Agent '{agent.Name}' runs the '{profile.DisplayName}' runner profile ({profile.Kind}); its Kind cannot be set to {requestedKind}. Change the agent's runner profile instead, or omit kind.",
                "agent_kind_profile_mismatch");
        }

        agent.Kind = requestedKind;
    }

    private static string? NormalizeModelId(string? modelId) =>
        string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();

    private void EnsureModelInProfile(AgentTuiProfile profile, string modelId)
    {
        if (profile.Models.Any(model =>
                string.Equals(model.Identifier, modelId, StringComparison.Ordinal)))
        {
            return;
        }

        // Curated suggestions are valid exact selections even before discovery persists them.
        // Keep this aligned with AgentTuiLaunchResolver.
        var curated = new AgentTuiRunnerCatalog()
            .Get(profile.Kind)
            .CuratedModels
            .Any(model => string.Equals(model.Identifier, modelId, StringComparison.Ordinal));
        if (curated)
            return;

        throw new ConflictException(
            "The selected model is not part of the profile catalogue.",
            "model_not_in_profile");
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

    /// <summary>
    /// CARD-0106 S2 — placeholders are legal in environment VALUES only. System-prompt text becomes
    /// a launch ARGUMENT and lands in the transcript, so a key resolved into one would be a key
    /// published. Refused as a 422 the operator sees, ahead of the launch tripwire that would
    /// otherwise catch it much later and only when they tried to start the agent.
    /// </summary>
    private static void ApiKeyPlaceholderInPromptGuard(string? systemPromptAppend)
    {
        if (!ApiKeyPlaceholder.ContainsMarker(systemPromptAppend))
            return;

        throw new ValidationException(
            "systemPromptAppend",
            "An API key placeholder is not supported in system-prompt text: it becomes a launch "
            + "argument, which is visible to any process lister and is written into the agent's "
            + "transcript. Put the placeholder in the agent's launch environment instead.");
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

    private static void ValidateAutoCompactOverrides(int? idleMinutes, int? contextPercent)
    {
        var errors = new Dictionary<string, string[]>();
        if (idleMinutes is <= 0)
            errors["AutoCompactIdleMinutes"] = ["Must be a positive number of minutes, or empty to use the default."];
        if (contextPercent is < 1 or > 100)
            errors["AutoCompactContextPercent"] = ["Must be between 1 and 100, or empty to use the default."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static void ValidateReorderRequest(ReorderAgentQueueRequest request)
    {
        if (request.CardIds is null)
            throw new ValidationException(nameof(request.CardIds), "Card ids are required.");
    }

    /// <summary>
    /// Turns a failed save into a 409. Both the inner exception and a log line are kept: this used
    /// to swallow the exception whole and report every failure as "another operation changed agent
    /// data", which is only true for a genuine concurrency conflict. A unique-index violation from
    /// the check-then-insert in <see cref="UniqueSlugAsync"/> reported the same sentence, so an
    /// intermittent test failure looked like a race on the row it had just created rather than a
    /// collision on a name.
    /// </summary>
    private async Task SaveChangesOrConflictAsync(string message, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw ConflictFrom(message, ex);
        }
        catch (DbUpdateException ex)
        {
            throw ConflictFrom(message, ex);
        }
    }

    /// <summary>
    /// Builds the 409 for a failed save, logging the real error and keeping it attached.
    /// A genuine concurrency conflict keeps the caller's wording; anything else appends what the
    /// database actually said, because the two are not the same failure and used to be reported
    /// identically.
    /// </summary>
    private ConflictException ConflictFrom(string message, DbUpdateException ex)
    {
        if (ex is DbUpdateConcurrencyException)
        {
            _logger.LogWarning(ex, "Concurrency conflict saving agent data: {Message}", message);
            return new ConflictException(message, ex);
        }

        var detail = DescribeDbFailure(ex);
        _logger.LogWarning(ex, "Save failed changing agent data ({Detail}): {Message}", detail, message);
        return new ConflictException($"{message} ({detail})", ex);
    }

    /// <summary>
    /// True when the save failed because a name this service generates was taken between the
    /// "is it free?" check and the insert — the only failure worth retrying, since retrying
    /// re-derives the name. Enumerated deliberately: retrying any other duplicate would loop on a
    /// collision the retry cannot resolve.
    /// </summary>
    private static bool IsGeneratedNameCollision(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Agents_Slug" or "IX_Boards_ProjectId_Name" or "IX_Projects_Name"
        };

    /// <summary>
    /// Names the actual database failure so a 409 says which constraint broke rather than
    /// paraphrasing. Falls back to the innermost exception message for non-Postgres providers.
    /// </summary>
    internal static string DescribeDbFailure(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg)
        {
            var constraint = string.IsNullOrEmpty(pg.ConstraintName) ? pg.TableName : pg.ConstraintName;
            var kind = pg.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => "duplicate value",
                PostgresErrorCodes.ForeignKeyViolation => "referenced row missing or still referenced",
                PostgresErrorCodes.NotNullViolation => "required value missing",
                PostgresErrorCodes.CheckViolation => "check constraint failed",
                PostgresErrorCodes.SerializationFailure => "serialization failure",
                PostgresErrorCodes.DeadlockDetected => "deadlock",
                _ => $"database error {pg.SqlState}"
            };
            return string.IsNullOrEmpty(constraint) ? kind : $"{kind} on {constraint}";
        }

        var innermost = ex.GetBaseException();
        return innermost.Message;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
