using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0747. Operator sweep over open land and completion notes created at least
/// <c>olderThanDays</c> ago (never fewer than <see cref="MinimumDays"/>). A dry run (the default)
/// classifies and writes nothing. Apply settles each eligible note through
/// <see cref="AgentTaskLandNotificationService.ExpireAgedAsync"/>, the rule the reconcile itself uses:
/// LegacyUnverified with <c>receipt_expired_aged</c>, and a never-typed row canceled first. It never
/// deletes a row, never touches a younger note, a non-terminal one or a row the queue already typed.
/// Running it twice is safe: a settled note is no longer open.
/// </summary>
public sealed class LandNoteExpirySweepService(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock)
{
    public const int MinimumDays = 7;
    private const int PageSize = 256;

    public async Task<LandNoteExpirySweepDto> SweepAsync(bool apply, int? olderThanDays, CancellationToken ct)
    {
        var days = olderThanDays ?? MinimumDays;
        if (days < MinimumDays)
            throw new ValidationException("olderThanDays", $"must be at least {MinimumDays} days.");
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(-days);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var settledByKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var settledByState = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = 0;
        var scanned = 0;
        Guid? cursor = null;
        while (true)
        {
            var query = db.AgentTaskLandNotifications.AsNoTracking()
                .Where(n => (n.State == LandNotificationState.AwaitingReceipt || n.State == LandNotificationState.DestinationUnavailable)
                    && n.CreatedAt <= cutoff);
            if (cursor is Guid after) query = query.Where(n => n.Id.CompareTo(after) > 0);
            var ids = await query.OrderBy(n => n.Id).Select(n => n.Id).Take(PageSize).ToListAsync(ct);
            foreach (var id in ids)
            {
                scanned++;
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var (outcome, kind, state) = await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                        .ExpireAgedAsync(id, cutoff, apply, ct);
                    Increment(outcomes, outcome.ToString());
                    if (outcome is AgentTaskLandNotificationService.ExpiryOutcome.Settled
                        or AgentTaskLandNotificationService.ExpiryOutcome.WouldSettle)
                    {
                        Increment(settledByKind, kind.ToString());
                        Increment(settledByState, state.ToString());
                    }
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    failures++;
                }
            }
            if (ids.Count < PageSize) break;
            cursor = ids[^1];
        }
        return new LandNoteExpirySweepDto(apply, days, cutoff, scanned, outcomes, settledByKind, settledByState, failures);
    }

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}

/// <summary>
/// CARD-0747 sweep result. <c>Outcomes</c> counts every scanned note by what happened
/// (<c>WouldSettle</c> on a dry run, <c>Settled</c> on apply, otherwise why it was left);
/// <c>EligibleByKind</c> and <c>EligibleByState</c> break down the settled (or would-settle) ones.
/// </summary>
public sealed record LandNoteExpirySweepDto(bool Apply, int OlderThanDays, DateTime Cutoff, int Scanned,
    IReadOnlyDictionary<string, int> Outcomes, IReadOnlyDictionary<string, int> EligibleByKind,
    IReadOnlyDictionary<string, int> EligibleByState, int Failures);

public sealed record LandNoteExpirySweepRequest(bool? Apply, int? OlderThanDays);
