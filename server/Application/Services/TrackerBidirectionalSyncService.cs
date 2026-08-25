using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0166: pull-then-push bidirectional sync. Never invoked from the orchestrator tick —
/// only from an explicit trigger (S7 endpoints) or tests.
/// </summary>
public sealed class TrackerBidirectionalSyncService
{
    private static readonly ConcurrentDictionary<Guid, byte> RunningBoards = new();

    private readonly AppDbContext _db;
    private readonly ExternalTrackerSyncService _readSync;
    private readonly TrackerTokenResolver _tokenResolver;
    private readonly IReadOnlyDictionary<TrackerKind, IIssueTracker> _trackers;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TrackerBidirectionalSyncService> _logger;
    private readonly TimeProvider _timeProvider;

    private const string TrackerActor = "external-tracker";

    public TrackerBidirectionalSyncService(
        AppDbContext db,
        ExternalTrackerSyncService readSync,
        TrackerTokenResolver tokenResolver,
        IEnumerable<IIssueTracker> trackers,
        IEventBus eventBus,
        ILogger<TrackerBidirectionalSyncService> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _readSync = readSync;
        _tokenResolver = tokenResolver;
        _trackers = trackers.ToDictionary(t => t.Kind);
        _eventBus = eventBus;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<TrackerSyncRunResult> RunAsync(Guid? boardId, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var query = _db.Boards
            .Include(b => b.Project)
            .Include(b => b.Columns)
            .Include(b => b.WorkflowDefinitions)
            .Where(b => b.TrackerKind != TrackerKind.Internal);
        if (boardId is Guid id)
            query = query.Where(b => b.Id == id);

        var boards = await query.ToListAsync(ct);
        var results = new List<TrackerSyncBoardResult>();
        var concurrentSkipped = false;

        foreach (var board in boards)
        {
            if (!RunningBoards.TryAdd(board.Id, 0))
            {
                concurrentSkipped = true;
                results.Add(new TrackerSyncBoardResult(
                    board.Id, board.Name, 0, 0, 0, 0, 0, 0,
                    ["concurrent_run"], Error: "Sync already running for this board."));
                continue;
            }

            try
            {
                results.Add(await SyncBoardAsync(board, now, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Bidirectional tracker sync failed for board {BoardId}", board.Id);
                results.Add(new TrackerSyncBoardResult(
                    board.Id, board.Name, 0, 0, 0, 0, 0, 0, [], Error: ex.Message));
            }
            finally
            {
                RunningBoards.TryRemove(board.Id, out _);
            }
        }

        return new TrackerSyncRunResult(results, ConcurrentRunSkipped: concurrentSkipped);
    }

    private async Task<TrackerSyncBoardResult> SyncBoardAsync(Board board, DateTime utcNow, CancellationToken ct)
    {
        var skips = new List<string>();
        // CARD-0171: itemised record of what this run changed. Per SyncBoardAsync invocation —
        // never shared across boards, because a notification is addressed per board.
        var changes = new List<TrackerSyncChange>();
        if (!IssueTrackerConfigParser.TryParse(board, out var config, out var error) || config is null)
        {
            skips.Add(error ?? "tracker config unavailable");
            return new TrackerSyncBoardResult(board.Id, board.Name, 0, 0, 0, 0, 0, 0, skips);
        }

        if (!_trackers.TryGetValue(config.Kind, out var tracker))
        {
            skips.Add($"no adapter for {config.Kind}");
            return new TrackerSyncBoardResult(board.Id, board.Name, 0, 0, 0, 0, 0, 0, skips);
        }

        var resolved = await _tokenResolver.ResolveAsync(config, board.ProjectId, ct);
        if (resolved is null)
        {
            skips.Add($"token_key '{config.TokenKeyName}' unresolved");
            return new TrackerSyncBoardResult(board.Id, board.Name, 0, 0, 0, 0, 0, 0, skips);
        }

        config = resolved;

        // (1) read-side issue upsert
        var issuesPulled = await _readSync.SyncAsync(utcNow, board.Id, ct);

        if (tracker is not IBidirectionalIssueTracker bi)
        {
            skips.Add($"{config.Kind} is read-only");
            return new TrackerSyncBoardResult(board.Id, board.Name, issuesPulled, 0, 0, 0, 0, 0, skips);
        }

        // Reload refs after read sync
        await _db.Entry(board).Collection(b => b.Cards).Query()
            .Include(c => c.ExternalIssueRef)
            .Include(c => c.BoardColumn)
            .Include(c => c.Revisions)
            .LoadAsync(ct);

        var refsByExternalId = board.Cards
            .Where(c => c.ExternalIssueRef is not null && c.ArchivedAt is null)
            .Select(c => c.ExternalIssueRef!)
            .ToDictionary(r => r.ExternalId, StringComparer.Ordinal);

        // Fresh issues list for create orphan pre-check + label/state current
        IReadOnlyList<TrackedIssue> pulledIssues = [];
        try
        {
            pulledIssues = await bi.FetchCandidatesAsync(config, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Issue re-fetch for board {BoardId} failed during bidirectional push", board.Id);
            skips.Add("issue re-fetch failed");
        }

        // IN reopen arm (cursor-proven external reopen of a terminal card)
        var externalReopens = ApplyExternalReopens(board, config, refsByExternalId, pulledIssues, utcNow, changes);

        // (2) comments IN
        var commentsIn = await PullCommentsAsync(board, bi, config, refsByExternalId, utcNow, changes, ct);

        // (3) pushes OUT
        var (commentsOut, labelsChanged, stateChanges) = await PushOutboundAsync(
            board, bi, config, refsByExternalId, pulledIssues, utcNow, changes, ct);

        // (4) creates
        var creates = await CreateMissingIssuesAsync(
            board, bi, config, refsByExternalId, pulledIssues, utcNow, changes, ct);

        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("BoardChanged", new { boardId = board.Id }, ct);

        return new TrackerSyncBoardResult(
            board.Id, board.Name, issuesPulled, commentsIn, commentsOut,
            labelsChanged, stateChanges, creates, skips)
        {
            ExternalReopens = externalReopens,
            Changes = changes
        };
    }

    private int ApplyExternalReopens(
        Board board,
        IssueTrackerConfig config,
        IReadOnlyDictionary<string, ExternalIssueRef> refsByExternalId,
        IReadOnlyList<TrackedIssue> pulledIssues,
        DateTime utcNow,
        List<TrackerSyncChange> changes)
    {
        var issuesById = pulledIssues.ToDictionary(i => i.ExternalId, StringComparer.Ordinal);
        var reopened = 0;
        var landing = TrackerLandingColumn.Resolve(board, config);
        if (landing is null)
            return reopened;

        foreach (var issueRef in refsByExternalId.Values)
        {
            if (!issuesById.TryGetValue(issueRef.ExternalId, out var issue))
                continue;

            var issueOpen = !string.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase);
            var cardTerminal = issueRef.Card.BoardColumn.IsTerminal
                || issueRef.Card.Status is CardStatus.Done or CardStatus.Canceled;
            if (!issueOpen || !cardTerminal)
                continue;

            if (!string.Equals(issueRef.LastKnownExternalState, "closed", StringComparison.OrdinalIgnoreCase))
                continue; // not a cursor-proven reopen — OUT will close later this run

            // Both-transitioned conflict: export-origin keeps Antiphon authority — skip IN reopen.
            if (issueRef.Origin == ExternalIssueOrigin.AntiphonExport)
            {
                _logger.LogWarning(
                    "External reopen of {ExternalId} ignored for export-origin ref; Antiphon terminal state wins",
                    issueRef.ExternalId);
                continue;
            }

            var card = issueRef.Card;
            var reason =
                $"External tracker reopened; superseded local completion at {card.CompletedAt:o}";
            CardRevisionLog.AppendReopen(card, landing, reason, TrackerActor, utcNow);
            card.BoardColumnId = landing.Id;
            card.BoardColumn = landing;
            card.Status = landing.CardStatus;
            card.CompletedAt = null;
            card.TerminalReason = null;
            card.UpdatedAt = utcNow;
            card.ConcurrencyToken = Guid.NewGuid();
            issueRef.LastKnownExternalState = "open";
            reopened++;
            changes.Add(Change(TrackerSyncChangeKind.ReopenedFromGitHub, issueRef));
        }

        return reopened;
    }

    /// <summary>CARD-0171: an itemised change, addressed by the card and its tracker key.</summary>
    private static TrackerSyncChange Change(TrackerSyncChangeKind kind, ExternalIssueRef issueRef) =>
        new(kind, issueRef.Card.Identifier, issueRef.ExternalKey, issueRef.Url);

    private async Task<int> PullCommentsAsync(
        Board board,
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        IReadOnlyDictionary<string, ExternalIssueRef> refsByExternalId,
        DateTime utcNow,
        List<TrackerSyncChange> changes,
        CancellationToken ct)
    {
        var since = board.TrackerCommentsPulledAt is DateTime pulled
            ? pulled.AddSeconds(-60)
            : (DateTime?)null;
        var pullStarted = utcNow;

        IReadOnlyList<TrackedIssueComment> comments;
        try
        {
            comments = await tracker.FetchCommentsSinceAsync(config, since, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Comments pull failed for board {BoardId}", board.Id);
            return 0;
        }

        var inserted = 0;
        foreach (var comment in comments)
        {
            if (string.IsNullOrWhiteSpace(comment.IssueExternalId)
                || !refsByExternalId.TryGetValue(comment.IssueExternalId, out var issueRef))
            {
                continue; // untracked issue / PR comment
            }

            if (TrackerSyncMarkers.TryReadTrailingCommentMarker(comment.Body, out var markerId))
            {
                var origin = await _db.CardComments
                    .FirstOrDefaultAsync(c => c.Id == markerId, ct);
                if (origin is not null)
                {
                    origin.ExternalCommentId ??= comment.ExternalCommentId;
                    origin.ExternalUrl ??= comment.Url;
                    continue; // echo closes the link — zero new rows
                }
                // Unknown marker → fail-open to visible External import below.
            }

            if (TrackerSyncMarkers.TryReadTrailingSystemCommentMarker(comment.Body, out var cardId)
                && cardId == issueRef.CardId)
            {
                continue; // card-state echo — zero synthetic CardComment rows
            }

            var exists = await _db.CardComments
                .AnyAsync(c => c.ExternalCommentId == comment.ExternalCommentId, ct);
            if (exists)
                continue;

            _db.CardComments.Add(new CardComment
            {
                Id = Guid.NewGuid(),
                CardId = issueRef.CardId,
                Body = comment.Body,
                Author = comment.Author,
                Origin = CardCommentOrigin.External,
                ExternalCommentId = comment.ExternalCommentId,
                ExternalUrl = comment.Url,
                CreatedAt = comment.CreatedAt,
                Card = issueRef.Card
            });
            inserted++;
            changes.Add(Change(TrackerSyncChangeKind.CommentIn, issueRef));
        }

        board.TrackerCommentsPulledAt = pullStarted;
        return inserted;
    }

    private async Task<(int CommentsOut, int LabelsChanged, int StateChanges)> PushOutboundAsync(
        Board board,
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        IReadOnlyDictionary<string, ExternalIssueRef> refsByExternalId,
        IReadOnlyList<TrackedIssue> pulledIssues,
        DateTime utcNow,
        List<TrackerSyncChange> changes,
        CancellationToken ct)
    {
        var commentsOut = 0;
        var labelsChanged = 0;
        var stateChanges = 0;
        var issuesByExternalId = pulledIssues.ToDictionary(i => i.ExternalId, StringComparer.Ordinal);

        foreach (var issueRef in refsByExternalId.Values)
        {
            var card = issueRef.Card;
            if (card.ArchivedAt is not null)
                continue;

            commentsOut += await PushDiscussionCommentsAsync(tracker, config, issueRef, utcNow, changes, ct);

            // CARD-0198/CARD-0199: state before labels and content-edit comments. Either stamps
            // LastOutboundSyncedAt = utcNow, which makes SyncStateAsync's reopen.CreatedAt >
            // LastOutboundSyncedAt gate false for a reopen that belongs to this same pass.
            stateChanges += await SyncStateAsync(tracker, config, issueRef, utcNow, changes, ct);

            commentsOut += await PushContentEditCommentsAsync(tracker, config, issueRef, utcNow, changes, ct);

            if (issuesByExternalId.TryGetValue(issueRef.ExternalId, out var current))
                labelsChanged += await SyncLabelsAsync(tracker, config, issueRef, current, utcNow, changes, ct);

            if (issueRef.Origin == ExternalIssueOrigin.AntiphonExport
                && (issueRef.LastOutboundSyncedAt is null || card.UpdatedAt > issueRef.LastOutboundSyncedAt)
                && issuesByExternalId.ContainsKey(issueRef.ExternalId))
            {
                await PushExportTitleBodyAsync(tracker, config, issueRef, ct);
                issueRef.LastOutboundSyncedAt = utcNow;
                changes.Add(Change(TrackerSyncChangeKind.ContentPushed, issueRef));
            }
        }

        return (commentsOut, labelsChanged, stateChanges);
    }

    private async Task<int> PushDiscussionCommentsAsync(
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        ExternalIssueRef issueRef,
        DateTime utcNow,
        List<TrackerSyncChange> changes,
        CancellationToken ct)
    {
        var pending = await _db.CardComments
            .Where(c => c.CardId == issueRef.CardId
                        && c.Origin == CardCommentOrigin.Antiphon
                        && c.ExternalCommentId == null
                        && c.SyncedAt == null)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var posted = 0;
        foreach (var comment in pending)
        {
            var authorPrefix = string.IsNullOrWhiteSpace(comment.Author)
                ? ""
                : $"**{comment.Author}** via Antiphon:\n\n";
            var body = TrackerSyncMarkers.AppendCommentMarker(authorPrefix + comment.Body, comment.Id);

            // Claim-before-post (CARD-0067 shape)
            comment.SyncedAt = utcNow;
            await _db.SaveChangesAsync(ct);

            try
            {
                var remote = await tracker.PostCommentAsync(config, issueRef.ExternalId, body, ct);
                comment.ExternalCommentId = remote.ExternalCommentId;
                comment.ExternalUrl = remote.Url;
                posted++;
                changes.Add(Change(TrackerSyncChangeKind.CommentOut, issueRef));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                comment.SyncedAt = null;
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning(ex, "Outbound comment {CommentId} failed for {ExternalId}", comment.Id, issueRef.ExternalId);
            }
        }

        return posted;
    }

    private async Task<int> PushContentEditCommentsAsync(
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        ExternalIssueRef issueRef,
        DateTime utcNow,
        List<TrackerSyncChange> changes,
        CancellationToken ct)
    {
        if (issueRef.Origin != ExternalIssueOrigin.ExternalImport)
            return 0;

        var card = issueRef.Card;
        var edits = card.Revisions
            .Where(r => r.Kind == CardRevisionKind.ContentEdit
                        && r.RevisionNumber > issueRef.LastRevisionSynced
                        && !string.Equals(r.EditedBy, TrackerActor, StringComparison.Ordinal))
            .OrderBy(r => r.RevisionNumber)
            .ToList();

        if (edits.Count == 0)
            return 0;

        var posted = 0;
        foreach (var edit in edits)
        {
            var body =
                $"Antiphon content edit by {edit.EditedBy ?? "unknown"}"
                + (string.IsNullOrWhiteSpace(edit.Reason) ? "" : $": {edit.Reason}")
                + "\n\n"
                + $"**Title:** {card.Title}\n\n{card.Description}\n\n"
                + "_The issue body remains authoritative on this import-origin link._";
            var marked = TrackerSyncMarkers.AppendCommentMarker(body, edit.Id);
            try
            {
                await tracker.PostCommentAsync(config, issueRef.ExternalId, marked, ct);
                posted++;
                changes.Add(Change(TrackerSyncChangeKind.CommentOut, issueRef));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Content-edit comment failed for {ExternalId}", issueRef.ExternalId);
                return posted;
            }
        }

        issueRef.LastRevisionSynced = edits.Max(e => e.RevisionNumber);
        issueRef.LastOutboundSyncedAt = utcNow;
        return posted;
    }

    private async Task<int> SyncLabelsAsync(
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        ExternalIssueRef issueRef,
        TrackedIssue current,
        DateTime utcNow,
        List<TrackerSyncChange> changes,
        CancellationToken ct)
    {
        var card = issueRef.Card;
        var desiredStatus = TrackerSyncMarkers.StatusLabel(card.Status);
        var desiredPriority = issueRef.Origin == ExternalIssueOrigin.AntiphonExport
            ? TrackerSyncMarkers.PriorityLabel(card.Priority)
            : null;

        var currentLabels = current.Labels.ToList();
        var changed = 0;

        if (issueRef.Origin == ExternalIssueOrigin.AntiphonExport)
        {
            var freeForm = BoardService.ParseLabels(card.LabelsJson);
            var desired = freeForm
                .Concat([desiredStatus])
                .Concat(desiredPriority is null ? [] : new[] { desiredPriority })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentSet = currentLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var desiredSet = desired.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!currentSet.SetEquals(desiredSet))
            {
                await tracker.ReplaceLabelsAsync(config, issueRef.ExternalId, desired, ct);
                changed++;
            }
        }
        else
        {
            // Import-origin: managed prefixes via sub-resource only.
            var staleManaged = currentLabels
                .Where(TrackerSyncMarkers.IsManagedLabel)
                .Where(l => !string.Equals(l, desiredStatus, StringComparison.OrdinalIgnoreCase)
                            && (desiredPriority is null
                                || !string.Equals(l, desiredPriority, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var stale in staleManaged)
            {
                await tracker.RemoveLabelAsync(config, issueRef.ExternalId, stale, ct);
                changed++;
            }

            if (!currentLabels.Any(l => string.Equals(l, desiredStatus, StringComparison.OrdinalIgnoreCase)))
            {
                await tracker.AddLabelsAsync(config, issueRef.ExternalId, [desiredStatus], ct);
                changed++;
            }
        }

        if (changed > 0)
        {
            issueRef.LastOutboundSyncedAt = utcNow;
            // One change per ISSUE that had any label write, while the counter keeps counting
            // writes as it always has.
            changes.Add(Change(TrackerSyncChangeKind.LabelsChanged, issueRef));
        }

        return changed;
    }

    private async Task<int> SyncStateAsync(
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        ExternalIssueRef issueRef,
        DateTime utcNow,
        List<TrackerSyncChange> changes,
        CancellationToken ct)
    {
        var card = issueRef.Card;
        var cursor = issueRef.LastKnownExternalState;
        var terminal = card.BoardColumn.IsTerminal
            || card.Status is CardStatus.Done or CardStatus.Canceled;
        var changed = 0;

        // IN arm for external reopen is handled after we know cursor — applied when issue is open
        // while cursor says closed. Detected via RawPayload / LastKnown — callers pass current
        // state through LastKnown updates from read sync. Here we act on card-side OUT transitions.

        if (terminal && !string.Equals(cursor, "closed", StringComparison.OrdinalIgnoreCase))
        {
            var reason = card.Status == CardStatus.Canceled ? "not_planned" : "completed";
            var closeBody = TrackerSyncMarkers.AppendSystemCommentMarker(
                $"Card {card.Identifier} reached terminal status **{card.Status}**"
                + (string.IsNullOrWhiteSpace(card.TerminalReason) ? "" : $": {card.TerminalReason}"),
                card.Id);
            try
            {
                await tracker.PostCommentAsync(config, issueRef.ExternalId, closeBody, ct);
                await tracker.SetStateAsync(config, issueRef.ExternalId, "closed", reason, ct);
                issueRef.LastKnownExternalState = "closed";
                issueRef.LastOutboundSyncedAt = utcNow;
                changed++;
                changes.Add(Change(TrackerSyncChangeKind.ClosedOnGitHub, issueRef));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Close push failed for {ExternalId}", issueRef.ExternalId);
            }

            return changed;
        }

        if (!terminal
            && string.Equals(cursor, "closed", StringComparison.OrdinalIgnoreCase))
        {
            // Antiphon-side reopen: a Reopen revision newer than LastOutboundSyncedAt
            var reopen = card.Revisions
                .Where(r => r.Kind == CardRevisionKind.Reopen
                            && !string.Equals(r.EditedBy, TrackerActor, StringComparison.Ordinal))
                .OrderByDescending(r => r.RevisionNumber)
                .FirstOrDefault();
            if (reopen is not null
                && (issueRef.LastOutboundSyncedAt is null || reopen.CreatedAt > issueRef.LastOutboundSyncedAt))
            {
                var body = TrackerSyncMarkers.AppendSystemCommentMarker(
                    $"Card {card.Identifier} was reopened on Antiphon"
                    + (string.IsNullOrWhiteSpace(reopen.Reason) ? "" : $": {reopen.Reason}"),
                    card.Id);
                try
                {
                    await tracker.PostCommentAsync(config, issueRef.ExternalId, body, ct);
                    await tracker.SetStateAsync(config, issueRef.ExternalId, "open", "reopened", ct);
                    issueRef.LastKnownExternalState = "open";
                    issueRef.LastOutboundSyncedAt = utcNow;
                    changed++;
                    changes.Add(Change(TrackerSyncChangeKind.ReopenedOnGitHub, issueRef));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Reopen push failed for {ExternalId}", issueRef.ExternalId);
                }
            }
        }

        return changed;
    }

    private static async Task PushExportTitleBodyAsync(
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        ExternalIssueRef issueRef,
        CancellationToken ct)
    {
        // Title/body PATCH for export-origin uses ReplaceLabelsAsync's PATCH path shape —
        // dedicated title/body update via SetState is wrong. Use CreateIssue-style PATCH:
        // GitHubIssuesTracker has no dedicated UpdateIssue; ReplaceLabelsAsync patches the issue.
        // For title/body we post a labels-preserving replace is insufficient. Use PostComment? No —
        // plan says PATCH title/body. Extend with a minimal labels-preserving call via ReplaceLabels
        // is wrong. Call SetState is wrong.
        // Practical approach: PostComment is not right. Add UpdateIssueAsync or reuse ReplaceLabels
        // with a new method. GitHubIssuesTracker.ReplaceLabelsAsync already PATCHes the issue —
        // add UpdateIssueContentAsync.
        await tracker.UpdateIssueContentAsync(config, issueRef.ExternalId, issueRef.Card.Title, issueRef.Card.Description, ct);
    }

    private async Task<int> CreateMissingIssuesAsync(
        Board board,
        IBidirectionalIssueTracker tracker,
        IssueTrackerConfig config,
        IReadOnlyDictionary<string, ExternalIssueRef> refsByExternalId,
        IReadOnlyList<TrackedIssue> pulledIssues,
        DateTime utcNow,
        List<TrackerSyncChange> changes,
        CancellationToken ct)
    {
        if (!IsSyncOutCreateEnabled(config))
            return 0;

        var watermark = ResolveExportWatermark(board, config);
        if (watermark is null)
            return 0;

        // Orphan pre-check: GH issues with card markers but no ref
        var creates = 0;
        foreach (var issue in pulledIssues)
        {
            if (refsByExternalId.ContainsKey(issue.ExternalId))
                continue;
            if (!TrackerSyncMarkers.TryReadCardMarker(issue.Description, out var markedCardId))
                continue;

            var card = board.Cards.FirstOrDefault(c => c.Id == markedCardId);
            if (card is null || card.ExternalIssueRef is not null)
                continue;

            var linked = new ExternalIssueRef
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                TrackerKind = config.Kind,
                ExternalId = issue.ExternalId,
                ExternalKey = issue.ExternalKey,
                Url = issue.Url,
                RawPayloadJson = string.IsNullOrWhiteSpace(issue.RawPayloadJson) ? "{}" : issue.RawPayloadJson,
                LastSyncedAt = utcNow,
                Origin = ExternalIssueOrigin.AntiphonExport,
                LastKnownExternalState = issue.State,
                LastRevisionSynced = card.RevisionCount,
                LastOutboundSyncedAt = utcNow,
                Card = card
            };
            card.ExternalIssueRef = linked;
            _db.ExternalIssueRefs.Add(linked);
            creates++; // linked, not duplicated
            // Recorded so Changes.Count > 0 stays equivalent to the counter sum: this arm
            // increments `creates`, so it must contribute a change too.
            changes.Add(Change(TrackerSyncChangeKind.Created, linked));
        }

        var linkedIds = board.Cards
            .Where(c => c.ExternalIssueRef is not null)
            .Select(c => c.Id)
            .ToHashSet();

        foreach (var card in board.Cards
                     .Where(c => c.ArchivedAt is null
                                 && !c.BoardColumn.IsTerminal
                                 && c.Status is not (CardStatus.Done or CardStatus.Canceled)
                                 && !linkedIds.Contains(c.Id)
                                 && c.ExternalIssueRef is null
                                 && c.CreatedAt >= watermark.Value))
        {
            var labels = BoardService.ParseLabels(card.LabelsJson).ToList();
            labels.Add(TrackerSyncMarkers.StatusLabel(card.Status));
            if (TrackerSyncMarkers.PriorityLabel(card.Priority) is { } p)
                labels.Add(p);

            var body = TrackerSyncMarkers.AppendCardMarkerFooter(
                card.Description, card.Id, card.Identifier, boardLink: null);
            try
            {
                var created = await tracker.CreateIssueAsync(config, card.Title, body, labels, ct);
                var issueRef = new ExternalIssueRef
                {
                    Id = Guid.NewGuid(),
                    CardId = card.Id,
                    TrackerKind = config.Kind,
                    ExternalId = created.ExternalId,
                    ExternalKey = created.ExternalKey,
                    Url = created.Url,
                    RawPayloadJson = string.IsNullOrWhiteSpace(created.RawPayloadJson) ? "{}" : created.RawPayloadJson,
                    LastSyncedAt = utcNow,
                    Origin = ExternalIssueOrigin.AntiphonExport,
                    LastKnownExternalState = "open",
                    LastRevisionSynced = card.RevisionCount,
                    LastOutboundSyncedAt = utcNow,
                    Card = card
                };
                card.ExternalIssueRef = issueRef;
                _db.ExternalIssueRefs.Add(issueRef);
                creates++;
                changes.Add(Change(TrackerSyncChangeKind.Created, issueRef));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Create issue failed for card {CardId}", card.Id);
            }
        }

        return creates;
    }

    private static bool IsSyncOutCreateEnabled(IssueTrackerConfig config) =>
        config.Options.TryGetValue("sync_out_create", out var raw)
        && bool.TryParse(raw, out var enabled)
        && enabled;

    private static DateTime? ResolveExportWatermark(Board board, IssueTrackerConfig config)
    {
        if (config.Options.TryGetValue("export_since", out var raw)
            && DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var since))
        {
            return DateTime.SpecifyKind(since, DateTimeKind.Utc);
        }

        return board.TrackerActivatedAt;
    }
}
