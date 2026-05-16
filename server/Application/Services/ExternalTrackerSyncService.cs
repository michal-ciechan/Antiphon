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

    public async Task<int> SyncAsync(DateTime utcNow, CancellationToken ct)
    {
        var boards = await _db.Boards
            .Include(b => b.Project)
            .Include(b => b.Columns)
            .Include(b => b.WorkflowDefinitions)
            .Where(b => b.TrackerKind != TrackerKind.Internal)
            .ToListAsync(ct);

        if (boards.Count == 0)
            return 0;

        var cache = new TrackerCache();
        var changedBoardIds = new HashSet<Guid>();
        var syncedIssues = 0;

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
            if (await UpsertIssuesAsync(board, config.Kind, issues, blockedIssueIds, utcNow, ct))
                changedBoardIds.Add(board.Id);
            if (await ReconcileStaleIssuesAsync(board, tracker, config, cache, issues, utcNow, ct))
                changedBoardIds.Add(board.Id);

            syncedIssues += issues.Count;
        }

        if (changedBoardIds.Count == 0)
            return syncedIssues;

        await _db.SaveChangesAsync(ct);
        foreach (var boardId in changedBoardIds)
            await _eventBus.PublishToAllAsync("BoardChanged", new { boardId }, ct);

        return syncedIssues;
    }

    private async Task<bool> UpsertIssuesAsync(
        Board board,
        TrackerKind trackerKind,
        IReadOnlyList<TrackedIssue> issues,
        IReadOnlySet<string> blockedIssueIds,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (issues.Count == 0)
            return false;

        var activeColumn = board.Columns
            .OrderBy(c => c.ColumnOrder)
            .FirstOrDefault(c => c.IsActive && !c.IsTerminal)
            ?? board.Columns.OrderBy(c => c.ColumnOrder).FirstOrDefault();
        if (activeColumn is null)
            return false;
        var blockedColumn = board.Columns
            .OrderBy(c => c.ColumnOrder)
            .FirstOrDefault(c => !c.IsActive && !c.IsTerminal);

        var externalIds = issues.Select(i => i.ExternalId).ToList();
        var existingRefs = await _db.ExternalIssueRefs
            .Include(r => r.Card)
            .ThenInclude(c => c.BoardColumn)
            .Where(r => r.TrackerKind == trackerKind && externalIds.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.Ordinal, ct);

        var changed = false;
        foreach (var issue in issues)
        {
            if (string.IsNullOrWhiteSpace(issue.ExternalId) || string.IsNullOrWhiteSpace(issue.ExternalKey))
                continue;
            var isBlocked = blockedIssueIds.Contains(issue.ExternalId);
            if (isBlocked && blockedColumn is null)
            {
                _logger.LogWarning(
                    "Skipping blocked external issue {ExternalId} for board {BoardId}: no non-active waiting column exists",
                    issue.ExternalId,
                    board.Id);
                continue;
            }

            var targetColumn = isBlocked ? blockedColumn! : activeColumn;

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

                changed |= UpdateExisting(existingRef, targetColumn, issue, isBlocked, utcNow);
                continue;
            }

            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = targetColumn.Id,
                Identifier = issue.ExternalKey,
                Title = issue.Title.Trim(),
                Description = issue.Description.Trim(),
                Priority = issue.Priority,
                LabelsJson = BoardService.SerializeLabels(issue.Labels),
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
        DateTime utcNow)
    {
        var card = externalRef.Card;
        var changed = false;
        var title = issue.Title.Trim();
        if (card.Title != title)
        {
            card.Title = title;
            changed = true;
        }

        var description = issue.Description.Trim();
        if (card.Description != description)
        {
            card.Description = description;
            changed = true;
        }

        if (card.Identifier != issue.ExternalKey)
        {
            card.Identifier = issue.ExternalKey;
            changed = true;
        }

        var labelsJson = BoardService.SerializeLabels(issue.Labels);
        if (card.LabelsJson != labelsJson)
        {
            card.LabelsJson = labelsJson;
            changed = true;
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

        if (card.Priority != issue.Priority)
        {
            card.Priority = issue.Priority;
            changed = true;
        }

        var shouldMoveForTrackerState = card.OwnerSessionId is null
            && !card.BoardColumn.IsTerminal
            && card.BoardColumnId != targetColumn.Id;
        if (shouldMoveForTrackerState)
        {
            card.BoardColumnId = targetColumn.Id;
            card.BoardColumn = targetColumn;
            card.Status = targetColumn.CardStatus;
            card.CompletedAt = null;
            card.TerminalReason = isBlocked ? "External tracker blockers are not terminal." : null;
            changed = true;
        }
        else if (!isBlocked && card.TerminalReason == "External tracker blockers are not terminal.")
        {
            card.TerminalReason = null;
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

            changed |= MarkInactive(staleRef, terminalColumn, current?.State, utcNow);
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

        card.BoardColumnId = terminalColumn.Id;
        card.BoardColumn = terminalColumn;
        card.Status = terminalColumn.CardStatus;
        card.CompletedAt = utcNow;
        card.TerminalReason = string.IsNullOrWhiteSpace(trackerState)
            ? "External tracker issue is no longer returned as active."
            : $"External tracker state '{trackerState}' is no longer active.";
        card.UpdatedAt = utcNow;
        card.ConcurrencyToken = Guid.NewGuid();
        externalRef.LastSyncedAt = utcNow;
        return true;
    }

    private static HashSet<string> ActiveStateSet(IssueTrackerConfig config) =>
        config.ActiveStates
            .Where(state => !string.IsNullOrWhiteSpace(state))
            .Select(state => state.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
