using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class ExternalTrackerSyncService
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyDictionary<TrackerKind, IIssueTracker> _trackers;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ExternalTrackerSyncService> _logger;

    // The self-reported author on revisions this service writes: a tracker-driven move is nobody's
    // decision on this side of the sync, and a history that attributed it to a person would lie.
    private const string TrackerActor = "external-tracker";

    // Queue removals accumulated while reconciling boards; published after SyncAsync's save.
    private readonly List<AgentQueueRemoval> _pendingQueueRemovals = [];

    public ExternalTrackerSyncService(
        AppDbContext db,
        IEnumerable<IIssueTracker> trackers,
        IEventBus eventBus,
        ILogger<ExternalTrackerSyncService> logger)
    {
        _db = db;
        _trackers = trackers.ToDictionary(t => t.Kind);
        _eventBus = eventBus;
        _logger = logger;
    }

    public Task<int> SyncAsync(DateTime utcNow, CancellationToken ct) =>
        SyncAsync(utcNow, boardId: null, ct);

    /// <summary>
    /// Read-only issue upsert. When <paramref name="boardId"/> is set, only that board is synced
    /// (CARD-0166 bidirectional pass scopes the read half to the boards it is about to write).
    /// </summary>
    public async Task<int> SyncAsync(DateTime utcNow, Guid? boardId, CancellationToken ct)
    {
        var query = _db.Boards
            .Include(b => b.Project)
            .Include(b => b.Columns)
            .Include(b => b.WorkflowDefinitions)
            .Where(b => b.TrackerKind != TrackerKind.Internal);
        if (boardId is Guid id)
            query = query.Where(b => b.Id == id);

        var boards = await query.ToListAsync(ct);

        if (boards.Count == 0)
            return 0;

        var cache = new TrackerCache();
        var changedBoardIds = new HashSet<Guid>();
        var syncedIssues = 0;
        _pendingQueueRemovals.Clear();

        foreach (var board in boards)
        {
            if (!IssueTrackerConfigParser.TryParse(board, out var config, out var error) || config is null)
            {
                _logger.LogDebug(
                    "Skipping tracker sync for board {BoardId}: {Reason}",
                    board.Id,
                    error ?? "tracker config unavailable");
                continue;
            }

            if (!_trackers.TryGetValue(config.Kind, out var tracker))
            {
                _logger.LogWarning(
                    "Skipping tracker sync for board {BoardId}: no tracker adapter registered for {TrackerKind}",
                    board.Id,
                    config.Kind);
                continue;
            }

            IReadOnlyList<TrackedIssue> issues;
            try
            {
                issues = await cache.FetchCandidatesAsync(tracker, config, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Tracker sync failed for board {BoardId}", board.Id);
                continue;
            }

            var blockedIssueIds = await ResolveBlockedIssueIdsAsync(board.Id, tracker, config, cache, issues, ct);
            if (await UpsertIssuesAsync(board, config, issues, blockedIssueIds, utcNow, ct))
                changedBoardIds.Add(board.Id);
            if (await ReconcileStaleIssuesAsync(board, tracker, config, cache, issues, utcNow, ct))
                changedBoardIds.Add(board.Id);

            syncedIssues += issues.Count;
        }

        if (changedBoardIds.Count == 0)
            return syncedIssues;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Lost race against a concurrent manual create on the same board: the unique index
            // is the arbiter. Log the constraint name (never paraphrase a DB failure) and
            // return — the next tick re-reads and re-allocates. No retry loop inside the tick.
            _logger.LogWarning(
                ex,
                "Tracker sync save failed ({Detail}); next tick will retry",
                AgentService.DescribeDbFailure(ex));
            _db.ChangeTracker.Clear();
            _pendingQueueRemovals.Clear();
            return syncedIssues;
        }

        foreach (var changedBoardId in changedBoardIds)
            await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = changedBoardId }, ct);
        foreach (var queueRemoval in _pendingQueueRemovals)
            await CardLifecycleTransitions.PublishQueueRemovalAsync(_eventBus, queueRemoval, ct);

        return syncedIssues;
    }

    private async Task<bool> UpsertIssuesAsync(
        Board board,
        IssueTrackerConfig config,
        IReadOnlyList<TrackedIssue> issues,
        IReadOnlySet<string> blockedIssueIds,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (issues.Count == 0)
            return false;

        var trackerKind = config.Kind;
        var ownsNonTerminal = TrackerLandingColumn.TrackerOwnsNonTerminalColumn(config);
        var landingColumn = TrackerLandingColumn.Resolve(board, config);
        if (landingColumn is null)
            return false;
        var blockedColumn = ownsNonTerminal
            ? board.Columns
                .OrderBy(c => c.ColumnOrder)
                .FirstOrDefault(c => !c.IsActive && !c.IsTerminal)
            : null;

        var externalIds = issues.Select(i => i.ExternalId).ToList();
        var existingRefs = await _db.ExternalIssueRefs
            .Include(r => r.Card)
            .ThenInclude(c => c.BoardColumn)
            .Where(r => r.TrackerKind == trackerKind && externalIds.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.Ordinal, ct);

        var changed = false;
        CardIdentifierAllocator? allocator = null;
        foreach (var issue in issues)
        {
            if (string.IsNullOrWhiteSpace(issue.ExternalId) || string.IsNullOrWhiteSpace(issue.ExternalKey))
                continue;
            var isBlocked = blockedIssueIds.Contains(issue.ExternalId);
            if (ownsNonTerminal && isBlocked && blockedColumn is null)
            {
                _logger.LogWarning(
                    "Skipping blocked external issue {ExternalId} for board {BoardId}: no non-active waiting column exists",
                    issue.ExternalId,
                    board.Id);
                continue;
            }

            var targetColumn = ownsNonTerminal && isBlocked ? blockedColumn! : landingColumn;

            if (existingRefs.TryGetValue(issue.ExternalId, out var existingRef))
            {
                if (existingRef.Card.BoardId != board.Id)
                {
                    _logger.LogWarning(
                        "External issue {ExternalId} from {TrackerKind} is already linked to a different board",
                        issue.ExternalId,
                        trackerKind);
                    continue;
                }

                if (UpdateExisting(existingRef, targetColumn, issue, isBlocked, ownsNonTerminal, utcNow, config.OperatorLogins))
                {
                    changed = true;
                    // Self-guarding: only acts if the tracker state landed the card in
                    // Review/Done/Canceled (possible when a board maps its active column oddly).
                    var upsertQueueRemoval = await CardLifecycleTransitions.DequeueFinishedCardAsync(
                        _db, existingRef.Card, utcNow, ct);
                    if (upsertQueueRemoval is not null)
                        _pendingQueueRemovals.Add(upsertQueueRemoval);
                }

                continue;
            }

            // CARD-0166: an issue whose body already carries an Antiphon card marker for an
            // unlinked card on this board is a create-phase orphan, not a fresh import. Link it
            // instead of spawning a duplicate card.
            if (TrackerSyncMarkers.TryReadCardMarker(issue.Description, out var markedCardId))
            {
                var existingCard = board.Cards.FirstOrDefault(c => c.Id == markedCardId && c.ExternalIssueRef is null);
                if (existingCard is null)
                {
                    var alreadyLinked = await _db.ExternalIssueRefs.AnyAsync(r => r.CardId == markedCardId, ct);
                    if (!alreadyLinked)
                    {
                        existingCard = await _db.Cards.FirstOrDefaultAsync(
                            c => c.Id == markedCardId && c.BoardId == board.Id, ct);
                    }
                }
                if (existingCard is not null && existingCard.ExternalIssueRef is null)
                {
                    var linkedAuthor = NormalizeAuthor(issue.Author);
                    var linked = new ExternalIssueRef
                    {
                        Id = Guid.NewGuid(),
                        CardId = existingCard.Id,
                        TrackerKind = trackerKind,
                        ExternalId = issue.ExternalId,
                        ExternalKey = issue.ExternalKey,
                        Url = issue.Url,
                        RawPayloadJson = string.IsNullOrWhiteSpace(issue.RawPayloadJson) ? "{}" : issue.RawPayloadJson,
                        LastSyncedAt = utcNow,
                        Origin = ExternalIssueOrigin.AntiphonExport,
                        LastKnownExternalState = string.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase)
                            ? "closed"
                            : "open",
                        LastRevisionSynced = existingCard.RevisionCount,
                        LastOutboundSyncedAt = utcNow,
                        Author = linkedAuthor,
                        AuthorIsOperator = JudgeAuthorIsOperator(linkedAuthor, config.OperatorLogins),
                        Card = existingCard
                    };
                    existingCard.ExternalIssueRef = linked;
                    _db.ExternalIssueRefs.Add(linked);
                    existingRefs[issue.ExternalId] = linked;
                    changed = true;
                    continue;
                }
            }

            allocator ??= await CardIdentifierAllocator.ForBoardAsync(_db, board.Id, ct);
            var author = NormalizeAuthor(issue.Author);
            var authorIsOperator = JudgeAuthorIsOperator(author, config.OperatorLogins);
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = targetColumn.Id,
                Identifier = allocator.Next(),
                Title = issue.Title.Trim(),
                Description = issue.Description.Trim(),
                Importance = CardRanking.FromTrackedIssue(issue.Priority, authorIsOperator),
                ImportanceProvenance = CardImportanceProvenance.Auto,
                LabelsJson = BoardService.SerializeLabels(
                    TrackerSyncMarkers.StripManagedLabels(issue.Labels)),
                Status = targetColumn.CardStatus,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
                Board = board,
                BoardColumn = targetColumn
            };
            var externalRef = new ExternalIssueRef
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                TrackerKind = trackerKind,
                ExternalId = issue.ExternalId,
                ExternalKey = issue.ExternalKey,
                Url = issue.Url,
                RawPayloadJson = string.IsNullOrWhiteSpace(issue.RawPayloadJson) ? "{}" : issue.RawPayloadJson,
                LastSyncedAt = utcNow,
                Origin = ExternalIssueOrigin.ExternalImport,
                LastKnownExternalState = string.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase)
                    ? "closed"
                    : "open",
                LastRevisionSynced = 0,
                Author = author,
                AuthorIsOperator = authorIsOperator,
                Card = card
            };
            card.ExternalIssueRef = externalRef;
            board.Cards.Add(card);
            _db.Cards.Add(card);
            _db.ExternalIssueRefs.Add(externalRef);
            changed = true;
        }

        if (changed)
            board.UpdatedAt = utcNow;

        return changed;
    }

    private static bool UpdateExisting(
        ExternalIssueRef externalRef,
        BoardColumn targetColumn,
        TrackedIssue issue,
        bool isBlocked,
        bool trackerOwnsNonTerminalColumn,
        DateTime utcNow,
        IReadOnlyList<string>? operatorLogins)
    {
        var card = externalRef.Card;
        var changed = false;
        // CARD-0166 decision 11: export-origin cards are Antiphon-authoritative for
        // title/body/free-form labels/priority — IN skips those fields.
        var importAuthoritative = externalRef.Origin != ExternalIssueOrigin.AntiphonExport;

        ApplyAuthor(externalRef, issue, operatorLogins, ref changed);

        if (importAuthoritative)
        {
            var title = issue.Title.Trim();
            var titleChanged = card.Title != title;

            var description = issue.Description.Trim();
            var descriptionChanged = card.Description != description;

            // Strip managed status:*/priority:* so card labels never accumulate sync-owned prefixes.
            var labelsJson = BoardService.SerializeLabels(
                TrackerSyncMarkers.StripManagedLabels(issue.Labels));
            var labelsChanged = card.LabelsJson != labelsJson;

            var importedImportance = CardRanking.FromTrackedIssue(issue.Priority, externalRef.AuthorIsOperator);
            var importanceChanged = card.ImportanceProvenance != CardImportanceProvenance.Human
                && card.Importance != importedImportance;

            if (titleChanged || descriptionChanged || labelsChanged || importanceChanged)
            {
                var fields = new List<string>(4);
                if (titleChanged) fields.Add("title");
                if (descriptionChanged) fields.Add("description");
                if (labelsChanged) fields.Add("labels");
                if (importanceChanged) fields.Add("importance");
                CardRevisionLog.AppendContentEdit(
                    card,
                    $"External tracker {issue.ExternalKey} changed: {string.Join(", ", fields)}.",
                    TrackerActor,
                    utcNow);

                if (titleChanged)
                    card.Title = title;
                if (descriptionChanged)
                    card.Description = description;
                if (labelsChanged)
                    card.LabelsJson = labelsJson;
                if (importanceChanged)
                    card.Importance = importedImportance;
                changed = true;
            }
        }

        if (externalRef.ExternalKey != issue.ExternalKey)
        {
            externalRef.ExternalKey = issue.ExternalKey;
            changed = true;
        }

        if (externalRef.Url != issue.Url)
        {
            externalRef.Url = issue.Url;
            changed = true;
        }

        var rawPayload = string.IsNullOrWhiteSpace(issue.RawPayloadJson) ? "{}" : issue.RawPayloadJson;
        if (externalRef.RawPayloadJson != rawPayload)
        {
            externalRef.RawPayloadJson = rawPayload;
            changed = true;
        }

        var shouldMoveForTrackerState = trackerOwnsNonTerminalColumn
            && card.OwnerSessionId is null
            && !card.BoardColumn.IsTerminal
            && card.BoardColumnId != targetColumn.Id;
        if (shouldMoveForTrackerState)
        {
            CardRevisionLog.AppendMove(
                card,
                card.BoardColumnId,
                card.Status,
                targetColumn,
                reason: $"External tracker state '{issue.State.Trim()}' maps to this column.",
                movedBy: TrackerActor,
                utcNow);
            card.BoardColumnId = targetColumn.Id;
            card.BoardColumn = targetColumn;
            card.Status = targetColumn.CardStatus;
            card.CompletedAt = null;
            card.TerminalReason = isBlocked ? "External tracker blockers are not terminal." : null;
            changed = true;
        }
        else if (trackerOwnsNonTerminalColumn
            && !isBlocked
            && card.TerminalReason == "External tracker blockers are not terminal.")
        {
            card.TerminalReason = null;
            changed = true;
        }

        // Keep the external state cursor honest for the bidirectional pass — but do NOT advance
        // closed→open here when the card is still terminal: that cursor-proven reopen is handled
        // by TrackerBidirectionalSyncService.ApplyExternalReopens (CARD-0166 §8).
        var normalizedState = string.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase)
            ? "closed"
            : "open";
        var deferOpenCursor = normalizedState == "open"
            && string.Equals(externalRef.LastKnownExternalState, "closed", StringComparison.OrdinalIgnoreCase)
            && (card.BoardColumn.IsTerminal || card.Status is CardStatus.Done or CardStatus.Canceled);
        if (!deferOpenCursor
            && !string.Equals(externalRef.LastKnownExternalState, normalizedState, StringComparison.Ordinal))
        {
            externalRef.LastKnownExternalState = normalizedState;
            changed = true;
        }

        externalRef.LastSyncedAt = utcNow;
        if (changed)
        {
            card.UpdatedAt = utcNow;
            card.ConcurrencyToken = Guid.NewGuid();
        }

        return changed;
    }

    private static void ApplyAuthor(
        ExternalIssueRef externalRef,
        TrackedIssue issue,
        IReadOnlyList<string>? operatorLogins,
        ref bool changed)
    {
        var author = NormalizeAuthor(issue.Author);
        if (!string.Equals(externalRef.Author, author, StringComparison.Ordinal))
        {
            externalRef.Author = author;
            changed = true;
        }

        var judged = JudgeAuthorIsOperator(author, operatorLogins);
        if (externalRef.AuthorIsOperator != judged)
        {
            externalRef.AuthorIsOperator = judged;
            changed = true;
        }
    }

    internal static bool? JudgeAuthorIsOperator(string? author, IReadOnlyList<string>? operatorLogins)
    {
        if (operatorLogins is null || operatorLogins.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(author))
            return false;

        var normalized = NormalizeLogin(author);
        foreach (var login in operatorLogins)
        {
            if (string.IsNullOrWhiteSpace(login))
                continue;
            if (string.Equals(NormalizeLogin(login), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? NormalizeAuthor(string? author) =>
        string.IsNullOrWhiteSpace(author) ? null : author.Trim();

    private static string NormalizeLogin(string login) => login.Trim().TrimStart('@');

    private async Task<IReadOnlySet<string>> ResolveBlockedIssueIdsAsync(
        Guid boardId,
        IIssueTracker tracker,
        IssueTrackerConfig config,
        TrackerCache cache,
        IReadOnlyList<TrackedIssue> issues,
        CancellationToken ct)
    {
        var blockerIds = issues
            .SelectMany(issue => issue.BlockedByExternalIds)
            .Select(id => id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (blockerIds.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        IReadOnlyList<TrackedIssue> blockers;
        try
        {
            blockers = await cache.FetchByIdsAsync(tracker, config, blockerIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tracker blocker lookup failed for board {BoardId}", boardId);
            return issues
                .Where(issue => issue.BlockedByExternalIds.Count > 0)
                .Select(issue => issue.ExternalId)
                .ToHashSet(StringComparer.Ordinal);
        }

        var blockerStates = blockers
            .Where(blocker => !string.IsNullOrWhiteSpace(blocker.ExternalId))
            .GroupBy(blocker => blocker.ExternalId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().State,
                StringComparer.Ordinal);
        var activeStates = ActiveStateSet(config);
        var blockedIssueIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var issue in issues)
        {
            foreach (var blockerId in issue.BlockedByExternalIds)
            {
                var normalizedBlockerId = blockerId.Trim();
                if (string.IsNullOrWhiteSpace(normalizedBlockerId)
                    || !blockerStates.TryGetValue(normalizedBlockerId, out var blockerState)
                    || activeStates.Contains(blockerState.Trim()))
                {
                    blockedIssueIds.Add(issue.ExternalId);
                    break;
                }
            }
        }

        return blockedIssueIds;
    }

    private async Task<bool> ReconcileStaleIssuesAsync(
        Board board,
        IIssueTracker tracker,
        IssueTrackerConfig config,
        TrackerCache cache,
        IReadOnlyList<TrackedIssue> activeIssues,
        DateTime utcNow,
        CancellationToken ct)
    {
        var activeExternalIds = activeIssues
            .Select(issue => issue.ExternalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var staleRefs = await _db.ExternalIssueRefs
            .Include(r => r.Card)
            .ThenInclude(c => c.BoardColumn)
            .Where(r => r.TrackerKind == config.Kind
                && r.Card.BoardId == board.Id
                && !activeExternalIds.Contains(r.ExternalId))
            .ToListAsync(ct);
        if (staleRefs.Count == 0)
            return false;

        IReadOnlyList<TrackedIssue> currentIssues = [];
        try
        {
            currentIssues = await cache.FetchByIdsAsync(
                tracker,
                config,
                staleRefs.Select(r => r.ExternalId).ToList(),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tracker stale issue reconciliation failed for board {BoardId}", board.Id);
            return false;
        }

        var currentByExternalId = currentIssues
            .Where(issue => !string.IsNullOrWhiteSpace(issue.ExternalId))
            .GroupBy(issue => issue.ExternalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var activeStates = ActiveStateSet(config);
        var terminalColumn = board.Columns
            .OrderBy(c => c.ColumnOrder)
            .FirstOrDefault(c => c.IsTerminal);
        if (terminalColumn is null)
            return false;

        var changed = false;
        foreach (var staleRef in staleRefs)
        {
            if (currentByExternalId.TryGetValue(staleRef.ExternalId, out var current)
                && activeStates.Contains(current.State.Trim()))
            {
                continue;
            }

            if (!MarkInactive(staleRef, terminalColumn, current?.State, utcNow))
                continue;

            changed = true;
            // A terminal status ends the card's stay in its agent's queue (work remaining only) —
            // otherwise the next agent start re-spawns a session onto the closed card.
            var queueRemoval = await CardLifecycleTransitions.DequeueFinishedCardAsync(
                _db, staleRef.Card, utcNow, ct);
            if (queueRemoval is not null)
                _pendingQueueRemovals.Add(queueRemoval);
        }

        if (changed)
            board.UpdatedAt = utcNow;

        return changed;
    }

    private static bool MarkInactive(
        ExternalIssueRef externalRef,
        BoardColumn terminalColumn,
        string? trackerState,
        DateTime utcNow)
    {
        var card = externalRef.Card;
        if (card.OwnerSessionId is not null || card.BoardColumn.IsTerminal)
            return false;

        var terminalReason = string.IsNullOrWhiteSpace(trackerState)
            ? "External tracker issue is no longer returned as active."
            : $"External tracker state '{trackerState}' is no longer active.";
        CardRevisionLog.AppendMove(
            card,
            card.BoardColumnId,
            card.Status,
            terminalColumn,
            terminalReason,
            TrackerActor,
            utcNow);
        card.BoardColumnId = terminalColumn.Id;
        card.BoardColumn = terminalColumn;
        card.Status = terminalColumn.CardStatus;
        card.CompletedAt = utcNow;
        card.TerminalReason = terminalReason;
        card.UpdatedAt = utcNow;
        card.ConcurrencyToken = Guid.NewGuid();
        externalRef.LastSyncedAt = utcNow;
        externalRef.LastKnownExternalState = "closed";
        return true;
    }

    private static HashSet<string> ActiveStateSet(IssueTrackerConfig config) =>
        config.ActiveStates
            .Where(state => !string.IsNullOrWhiteSpace(state))
            .Select(state => state.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
