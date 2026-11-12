using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0747 follow-up (Debug 3b0adade). The land-note rule settles notes, but most of a stale
/// caller backlog is Pending queue rows with no open land note: plain completion, block and failure
/// notices and Check notes, many with no land note at all. This rule cancels such a row when all of
/// these hold:
/// <list type="bullet">
/// <item>it is Pending and was never typed (<c>DeliveryAttempts == 0</c>; a typed row is the queue's to finish);</item>
/// <item>its origin is <see cref="QueuedMessageOrigin.Delegation"/> or <see cref="QueuedMessageOrigin.Check"/>
/// and it names a source task (the reported task, or the checked task);</item>
/// <item>that task is Succeeded, Failed or Canceled;</item>
/// <item>it was created at or before the cutoff;</item>
/// <item>no open land note owns it (that note's own rule settles it), no provider-wait note is linked
/// to it in any state, and it is not a capacity wait.</item>
/// </list>
/// Cancel marks the row Canceled under the session's queue lock and never deletes it. Running it
/// again finds nothing: a canceled row is no longer Pending.
/// </summary>
public sealed class AgedQueueRowExpiry(AppDbContext db, SessionMessageQueueService queue)
{
    /// <summary>What happened, or would happen, to one scanned row.</summary>
    public enum Outcome { TypedPending, Busy, Changed, WouldCancel, Canceled }

    private const int PageSize = 256;

    /// <param name="lockWait">
    /// The hosted cleanup passes a bound: a session whose queue lock stays held is skipped for the
    /// rest of the pass (<see cref="Outcome.Busy"/>) and retried on the next one. The sweep passes null and waits.
    /// </param>
    public async Task<AgedQueueRowExpiryDto> ExpireAsync(DateTime cutoff, bool apply, TimeSpan? lockWait, CancellationToken ct)
    {
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var eligibleByKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var busy = new HashSet<Guid>();
        DateTime? oldest = null, newest = null;
        var scanned = 0;
        var failures = 0;
        Guid? cursor = null;
        while (true)
        {
            var query = Candidates(cutoff);
            if (cursor is Guid after) query = query.Where(m => m.Id.CompareTo(after) > 0);
            var page = await query.OrderBy(m => m.Id)
                .Select(m => new Row(m.Id, m.AgentSessionId, m.Origin, m.DeliveryAttempts, m.CreatedAt))
                .Take(PageSize).ToListAsync(ct);
            scanned += page.Count;
            var typed = page.Count(r => r.DeliveryAttempts > 0);
            if (typed > 0) outcomes[nameof(Outcome.TypedPending)] = outcomes.GetValueOrDefault(nameof(Outcome.TypedPending)) + typed;
            foreach (var session in page.Where(r => r.DeliveryAttempts == 0).GroupBy(r => r.SessionId))
            {
                var rows = session.ToList();
                if (!apply)
                {
                    foreach (var row in rows) Count(row, Outcome.WouldCancel);
                    continue;
                }
                if (busy.Contains(session.Key))
                {
                    foreach (var row in rows) Increment(outcomes, nameof(Outcome.Busy));
                    continue;
                }
                try
                {
                    var canceled = (await queue.CancelPendingIfUntypedAsync(session.Key, rows.Select(r => r.Id).ToList(), ct, lockWait))
                        .ToHashSet();
                    foreach (var row in rows)
                    {
                        if (canceled.Contains(row.Id)) Count(row, Outcome.Canceled);
                        else Increment(outcomes, nameof(Outcome.Changed));
                    }
                }
                catch (SessionQueueBusyException)
                {
                    busy.Add(session.Key);
                    foreach (var row in rows) Increment(outcomes, nameof(Outcome.Busy));
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    failures += rows.Count;
                }
            }
            if (page.Count < PageSize) break;
            cursor = page[^1].Id;
        }
        return new AgedQueueRowExpiryDto(scanned, outcomes, eligibleByKind, oldest, newest, failures);

        void Count(Row row, Outcome outcome)
        {
            Increment(outcomes, outcome.ToString());
            Increment(eligibleByKind, row.Origin.ToString());
            if (oldest is null || row.CreatedAt < oldest) oldest = row.CreatedAt;
            if (newest is null || row.CreatedAt > newest) newest = row.CreatedAt;
        }
    }

    private IQueryable<Domain.Entities.SessionQueuedMessage> Candidates(DateTime cutoff) =>
        db.SessionQueuedMessages.AsNoTracking().Where(m =>
            m.Status == QueuedMessageStatus.Pending
            && m.CreatedAt <= cutoff
            && (m.Origin == QueuedMessageOrigin.Delegation || m.Origin == QueuedMessageOrigin.Check)
            && m.SourceTaskId != null
            && m.CapacityWaitId == null
            && db.AgentTasks.Any(t => t.Id == m.SourceTaskId
                && (t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Failed
                    || t.Status == AgentTaskStatus.Canceled))
            && !db.AgentTaskLandNotifications.Any(n => (n.Id == m.SourceLandNotificationId || n.QueueMessageId == m.Id)
                && (n.Kind == LandNotificationKind.ProviderInputWait
                    || (n.State != LandNotificationState.Confirmed && n.State != LandNotificationState.NotRequired
                        && n.State != LandNotificationState.LegacyUnverified && n.State != LandNotificationState.Canceled))));

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private sealed record Row(Guid Id, Guid SessionId, QueuedMessageOrigin Origin, int DeliveryAttempts, DateTime CreatedAt);
}

/// <summary>
/// CARD-0747 aged queue rows. <c>Outcomes</c> counts every scanned row (<c>WouldCancel</c> on a dry run,
/// <c>Canceled</c> on apply, otherwise <c>TypedPending</c>, <c>Busy</c> or <c>Changed</c>);
/// <c>EligibleByKind</c> breaks the canceled (or would-cancel) rows down by queue origin, and
/// <c>Oldest</c>/<c>Newest</c> are their creation times.
/// </summary>
public sealed record AgedQueueRowExpiryDto(int Scanned, IReadOnlyDictionary<string, int> Outcomes,
    IReadOnlyDictionary<string, int> EligibleByKind, DateTime? Oldest, DateTime? Newest, int Failures);
