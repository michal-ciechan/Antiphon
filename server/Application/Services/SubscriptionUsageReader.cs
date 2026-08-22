using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Read-only seam for CARD-0136. Latest <c>Parsed</c> sample per (Provider, SubscriptionKey).
/// This card builds no threshold, no alert, no switching, and no dispatch pause.
/// </summary>
public sealed class SubscriptionUsageReader
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    public SubscriptionUsageReader(AppDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<IReadOnlyList<SubscriptionUsageSnapshot>> GetLatestAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var parsed = await _db.SubscriptionUsageSamples.AsNoTracking()
            .Where(s => s.ParseStatus == SubscriptionUsageParseStatus.Parsed
                && s.RemainingPercent != null)
            .ToListAsync(ct);

        return parsed
            .GroupBy(s => (s.Provider, s.SubscriptionKey))
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .Select(s => ToSnapshot(s, now))
            .ToList();
    }

    public async Task<SubscriptionUsageSnapshot?> GetLatestAsync(
        AgentKind provider, string subscriptionKey, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var row = await _db.SubscriptionUsageSamples.AsNoTracking()
            .Where(s => s.Provider == provider
                && s.SubscriptionKey == subscriptionKey
                && s.ParseStatus == SubscriptionUsageParseStatus.Parsed
                && s.RemainingPercent != null)
            .OrderByDescending(s => s.ObservedAt)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToSnapshot(row, now);
    }

    private static SubscriptionUsageSnapshot ToSnapshot(
        Domain.Entities.SubscriptionUsageSample s, DateTime now) =>
        new(
            s.Provider,
            s.SubscriptionKey,
            s.PlanLabel,
            s.RemainingPercent!.Value,
            s.ResetsAt,
            s.ObservedAt,
            Age: now - s.ObservedAt);
}

public sealed record SubscriptionUsageSnapshot(
    AgentKind Provider,
    string SubscriptionKey,
    string? PlanLabel,
    double RemainingPercent,
    DateTime? ResetsAt,
    DateTime ObservedAt,
    TimeSpan Age);
