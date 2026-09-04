using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0347: the per-card outbound state write (comment → state → managed <c>status:*</c> label),
/// shared with the scheduled bidirectional run so both paths produce byte-identical GitHub comments.
/// </summary>
public sealed class TrackerCardStatePushService
{
    internal const string TrackerActor = "external-tracker";

    private readonly AppDbContext _db;
    private readonly TrackerTokenResolver _tokenResolver;
    private readonly IReadOnlyDictionary<TrackerKind, IIssueTracker> _trackers;
    private readonly TrackerSettings _settings;
    private readonly ILogger<TrackerCardStatePushService> _logger;
    private readonly TimeProvider _timeProvider;

    public TrackerCardStatePushService(
        AppDbContext db,
        TrackerTokenResolver tokenResolver,
        IEnumerable<IIssueTracker> trackers,
        IOptions<TrackerSettings>? settings,
        ILogger<TrackerCardStatePushService> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _tokenResolver = tokenResolver;
        _trackers = trackers.ToDictionary(t => t.Kind);
        _settings = settings?.Value ?? new TrackerSettings();
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public sealed record StatePushOutcome(int Changed, string? Failure);

    public async Task<TrackerCardStatePushResult?> PushForCardAsync(Guid cardId, CancellationToken ct)
    {
        Card? card = null;
        try
        {
            card = await _db.Cards
                .Include(c => c.ExternalIssueRef)
                .Include(c => c.BoardColumn)
                .Include(c => c.Board).ThenInclude(b => b.Columns)
                .Include(c => c.Board).ThenInclude(b => b.WorkflowDefinitions)
                .Include(c => c.Revisions)
                .FirstOrDefaultAsync(c => c.Id == cardId, ct);

            if (card?.ExternalIssueRef is null)
                return null;

            var issueRef = card.ExternalIssueRef;

            if (!_settings.PushStateOnCardTransition)
                return Skip(issueRef, "disabled");

            if (card.Board.TrackerKind == TrackerKind.Internal
                || !IssueTrackerConfigParser.TryParse(card.Board, out var config, out _)
                || config is null)
            {
                return Skip(issueRef, "tracker_inactive");
            }

            if (!_trackers.TryGetValue(config.Kind, out var adapter)
                || adapter is not IBidirectionalIssueTracker tracker)
            {
                return Skip(issueRef, "tracker_read_only");
            }

            var resolved = await _tokenResolver.ResolveAsync(config, card.Board.ProjectId, ct);
            if (resolved is null)
                return Skip(issueRef, "token_unresolved");
            config = resolved;

            if (TrackerBidirectionalSyncService.IsRunning(card.BoardId))
                return Skip(issueRef, "sync_running");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.CardStatePushTimeoutSeconds));

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var changes = new List<TrackerSyncChange>();
            var state = await PushStateAsync(tracker, config, issueRef, utcNow, changes, timeout.Token);
            if (state.Failure is not null)
                return Fail(issueRef, state.Failure);

            if (state.Changed > 0)
            {
                try
                {
                    var currentIssues = await tracker.FetchByIdsAsync(
                        config, [issueRef.ExternalId], timeout.Token);
                    if (currentIssues.Count > 0)
                    {
                        await SyncLabelsAsync(
                            tracker, config, issueRef, currentIssues[0], utcNow, changes, timeout.Token);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex,
                        "Label refresh after card state push failed for {ExternalId}",
                        issueRef.ExternalId);
                }
            }

            await _db.SaveChangesAsync(timeout.Token);

            if (changes.Any(c => c.Kind == TrackerSyncChangeKind.ClosedOnGitHub))
                return Ok(issueRef, TrackerCardStatePushOutcome.Closed);
            if (changes.Any(c => c.Kind == TrackerSyncChangeKind.ReopenedOnGitHub))
                return Ok(issueRef, TrackerCardStatePushOutcome.Reopened);
            return Ok(issueRef, TrackerCardStatePushOutcome.InSync);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Card state push failed for card {CardId}", cardId);
            var reason = ex is OperationCanceledException ? "timeout" : ex.Message;
            return card?.ExternalIssueRef is { } failedRef
                ? Fail(failedRef, reason)
                : null;
        }
    }

    public async Task<StatePushOutcome> PushStateAsync(
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

        if (terminal && !string.Equals(cursor, "closed", StringComparison.OrdinalIgnoreCase))
        {
            var reason = card.Status == CardStatus.Canceled ? "not_planned" : "completed";
            var closeBody = BuildCloseComment(card);
            try
            {
                await tracker.PostCommentAsync(config, issueRef.ExternalId, closeBody, ct);
                await tracker.SetStateAsync(config, issueRef.ExternalId, "closed", reason, ct);
                issueRef.LastKnownExternalState = "closed";
                issueRef.LastOutboundSyncedAt = utcNow;
                changes.Add(Change(TrackerSyncChangeKind.ClosedOnGitHub, issueRef));
                return new StatePushOutcome(1, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Close push failed for {ExternalId}", issueRef.ExternalId);
                return new StatePushOutcome(0, ex.Message);
            }
        }

        if (!terminal
            && string.Equals(cursor, "closed", StringComparison.OrdinalIgnoreCase))
        {
            var reopen = card.Revisions
                .Where(r => r.Kind == CardRevisionKind.Reopen
                            && !string.Equals(r.EditedBy, TrackerActor, StringComparison.Ordinal))
                .OrderByDescending(r => r.RevisionNumber)
                .FirstOrDefault();
            if (reopen is not null
                && (issueRef.LastOutboundSyncedAt is null || reopen.CreatedAt > issueRef.LastOutboundSyncedAt))
            {
                var body = BuildReopenComment(card, reopen);
                try
                {
                    await tracker.PostCommentAsync(config, issueRef.ExternalId, body, ct);
                    await tracker.SetStateAsync(config, issueRef.ExternalId, "open", "reopened", ct);
                    issueRef.LastKnownExternalState = "open";
                    issueRef.LastOutboundSyncedAt = utcNow;
                    changes.Add(Change(TrackerSyncChangeKind.ReopenedOnGitHub, issueRef));
                    return new StatePushOutcome(1, null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Reopen push failed for {ExternalId}", issueRef.ExternalId);
                    return new StatePushOutcome(0, ex.Message);
                }
            }
        }

        return new StatePushOutcome(0, null);
    }

    public async Task<int> SyncLabelsAsync(
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
            ? TrackerSyncMarkers.PriorityLabel(card.Importance)
            : null;

        var currentLabels = current.Labels.ToList();
        var changed = 0;
        IReadOnlyList<string>? added = null;
        IReadOnlyList<string>? removed = null;

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
                added = SortedUnique(desiredSet.Where(l => !currentSet.Contains(l)));
                removed = SortedUnique(currentSet.Where(l => !desiredSet.Contains(l)));
                await tracker.ReplaceLabelsAsync(config, issueRef.ExternalId, desired, ct);
                changed++;
            }
        }
        else
        {
            var addedList = new List<string>();
            var removedList = new List<string>();
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
                removedList.Add(stale);
            }

            if (!currentLabels.Any(l => string.Equals(l, desiredStatus, StringComparison.OrdinalIgnoreCase)))
            {
                await tracker.AddLabelsAsync(config, issueRef.ExternalId, [desiredStatus], ct);
                changed++;
                addedList.Add(desiredStatus);
            }

            if (changed > 0)
            {
                added = SortedUnique(addedList);
                removed = SortedUnique(removedList);
            }
        }

        if (changed > 0)
        {
            issueRef.LastOutboundSyncedAt = utcNow;
            changes.Add(Change(TrackerSyncChangeKind.LabelsChanged, issueRef, added, removed));
        }

        return changed;
    }

    public static string BuildCloseComment(Card card)
    {
        var headline = $"Card {card.Identifier} closed as **{card.Status}** on Antiphon.";
        var body = string.IsNullOrWhiteSpace(card.TerminalReason)
            ? headline
            : headline + "\n\n" + card.TerminalReason.Trim();
        return TrackerSyncMarkers.AppendSystemCommentMarker(body, card.Id);
    }

    public static string BuildReopenComment(Card card, CardRevision reopen)
    {
        var headline = $"Card {card.Identifier} reopened on Antiphon.";
        var body = string.IsNullOrWhiteSpace(reopen.Reason)
            ? headline
            : headline + "\n\n" + reopen.Reason.Trim();
        return TrackerSyncMarkers.AppendSystemCommentMarker(body, card.Id);
    }

    private static List<string> SortedUnique(IEnumerable<string> labels) =>
        labels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TrackerSyncChange Change(
        TrackerSyncChangeKind kind,
        ExternalIssueRef issueRef,
        IReadOnlyList<string>? added = null,
        IReadOnlyList<string>? removed = null) =>
        new(kind, issueRef.Card.Identifier, issueRef.ExternalKey, issueRef.Url)
        {
            Added = added,
            Removed = removed
        };

    private static TrackerCardStatePushResult Skip(ExternalIssueRef issueRef, string reason) =>
        new(TrackerCardStatePushOutcome.Skipped, issueRef.TrackerKind, issueRef.ExternalKey, issueRef.Url, reason);

    private static TrackerCardStatePushResult Fail(ExternalIssueRef issueRef, string reason) =>
        new(TrackerCardStatePushOutcome.Failed, issueRef.TrackerKind, issueRef.ExternalKey, issueRef.Url, reason);

    private static TrackerCardStatePushResult Ok(
        ExternalIssueRef issueRef, TrackerCardStatePushOutcome outcome) =>
        new(outcome, issueRef.TrackerKind, issueRef.ExternalKey, issueRef.Url, null);
}
