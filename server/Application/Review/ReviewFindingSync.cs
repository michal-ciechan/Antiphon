using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// Antiphon first, then the code host (design 6): a response stored with sync state Pending is
/// posted as a reply in the finding's thread, the thread is resolved when the response closes the
/// finding and the host can resolve threads, and the response is marked Synced. A finding with no
/// thread (a local host, a summary-only finding) marks it NotApplicable. A transient failure keeps
/// it Pending for the next pass; a rejected one is marked Failed.
/// </summary>
public sealed class ReviewFindingSync(AppDbContext db, CodeHostRouter router, IEventBus events, TimeProvider time, ILogger<ReviewFindingSync> logger)
{
    public const int MaxEventsPerPass = 50;

    private static readonly ReviewFindingAction[] Resolving =
        [ReviewFindingAction.Dismiss, ReviewFindingAction.NotABlocker, ReviewFindingAction.Defer];

    /// <summary>Syncs pending responses on runs resolved through a code host. Returns the responses handled.</summary>
    public async Task<int> SyncAsync(CancellationToken ct)
    {
        var pending = await db.ReviewFindingEvents
            .Where(e => e.SyncState == ReviewSyncState.Pending)
            .Join(db.ReviewFindings, e => e.FindingId, f => f.Id, (e, f) => new { Event = e, f.RunId })
            .Where(x => db.ReviewIterations.Any(i => i.RunId == x.RunId && i.HeadSha != null))
            .OrderBy(x => x.Event.CreatedAt)
            .Take(MaxEventsPerPass)
            .Select(x => x.Event.FindingId)
            .Distinct()
            .ToListAsync(ct);
        if (pending.Count == 0)
            return 0;

        var findings = await db.ReviewFindings.Include(f => f.Events).Where(f => pending.Contains(f.Id)).ToListAsync(ct);
        var runIds = findings.Select(f => f.RunId).Distinct().ToList();
        var subjects = await db.ReviewRuns.Where(r => runIds.Contains(r.Id)).Select(r => new { r.Id, r.Subject }).ToDictionaryAsync(r => r.Id, r => r.Subject, ct);
        var handled = 0;
        foreach (var finding in findings)
        {
            var subject = subjects[finding.RunId];
            CodeHostBinding? binding = null;
            try
            {
                binding = router.Resolve(subject);
            }
            catch (CodeHostException ex)
            {
                logger.LogWarning("Review finding {Key} on run {RunId}: no code host to sync to ({Message})", finding.Key, finding.RunId, ex.Message);
            }

            foreach (var evt in finding.Events.Where(e => e.SyncState == ReviewSyncState.Pending).OrderBy(e => e.CreatedAt))
            {
                handled++;
                if (binding is null || !binding.Host.Capabilities.CanReplyToThreads || finding.ExternalCommentId is null || subject.PrNumber is null)
                {
                    evt.SyncState = ReviewSyncState.NotApplicable;
                    continue;
                }

                try
                {
                    await binding.Host.ReplyToThreadAsync(binding.Target, finding.ExternalCommentId, ReplyText(evt), ct);
                    var resolves = Resolving.Contains(evt.Action) || (evt.Action == ReviewFindingAction.Fixed && evt.ActorLogin == Execution.ReviewWalk.BotLogin);
                    if (resolves && binding.Host.Capabilities.CanResolveThreads && finding.ExternalThreadId is { } thread)
                        await binding.Host.ResolveThreadAsync(binding.Target, thread, ct);
                    evt.SyncState = ReviewSyncState.Synced;
                }
                catch (CodeHostException ex) when (ex.Kind is CodeHostFailureKind.Unavailable or CodeHostFailureKind.RateLimited)
                {
                    logger.LogWarning("Review finding {Key}: reply not synced yet ({Message}); retrying on the next pass", finding.Key, ex.Message);
                    break;
                }
                catch (CodeHostException ex)
                {
                    logger.LogWarning("Review finding {Key}: reply rejected by the code host ({Message})", finding.Key, ex.Message);
                    evt.SyncState = ReviewSyncState.Failed;
                }
            }

            finding.SyncState = finding.Events.Any(e => e.SyncState == ReviewSyncState.Pending) ? ReviewSyncState.Pending
                : finding.Events.Any(e => e.SyncState == ReviewSyncState.Failed) ? ReviewSyncState.Failed
                : finding.Events.Any(e => e.SyncState == ReviewSyncState.Synced) ? ReviewSyncState.Synced
                : ReviewSyncState.NotApplicable;
            finding.UpdatedAt = time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(ct);
        foreach (var runId in runIds)
            await events.PublishToAllAsync(ReviewService.RunChangedEvent, new ReviewRunChangedEvent(runId), ct);
        return handled;
    }

    /// <summary>The reply posted for a response: who did what, then their text.</summary>
    public static string ReplyText(ReviewFindingEvent evt)
    {
        var who = evt.ActorDisplayName ?? evt.ActorLogin;
        var reason = evt.ReasonCategory is { } r ? $" ({r})" : "";
        var head = evt.Action switch
        {
            ReviewFindingAction.WillFix => $"{who} will fix this.",
            ReviewFindingAction.Fixed => evt.ActorLogin == Execution.ReviewWalk.BotLogin ? "Fixed." : $"{who} says this is fixed; the next push rechecks it.",
            ReviewFindingAction.Dismiss => $"{who} dismissed this{reason}{(evt.Scope is { } s and not ReviewDismissScope.Instance ? $", scope {s}" : "")}.",
            ReviewFindingAction.Dispute => $"{who} disputes this.",
            ReviewFindingAction.Defer => $"{who} deferred this.",
            ReviewFindingAction.NotABlocker => $"{who} cleared this blocker{reason}.",
            ReviewFindingAction.Question => $"{who} asks:",
            _ => $"{who}:",
        };
        return string.IsNullOrWhiteSpace(evt.Text) ? head : $"{head}\n\n{evt.Text}";
    }
}
