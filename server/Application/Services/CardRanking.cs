using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Single owner of effective urgency, quadrant and rank. Every sort site calls
/// <see cref="Rank(CardImportance, CardUrgency, DateTime?, DateTime, int)"/>; there is no persisted
/// Rank column because it would go stale with the clock.
/// </summary>
/// <remarks>
/// <para><c>rank = 13 − (3·importance + 2·effectiveUrgency)</c>, lower sorts first. The weights
/// prefer a card that changes how everything else gets done over one more feature
/// (<c>Critical/Normal</c> beats <c>Normal/Now</c>), while a standing-red <c>Low/Now</c> still
/// beats the undifferentiated default <c>Normal/Normal</c>.</para>
///
/// <para>The <c>blockedDependants</c> argument on Rank is the CARD-0100 extension point: accepted
/// and ignored until relationships land and that card decides whether a blocker feeds rank or
/// stays a separate column in the reading order.</para>
/// </remarks>
public static class CardRanking
{
    public static readonly TimeSpan NowDueWindow = TimeSpan.FromDays(3);
    public static readonly TimeSpan SoonDueWindow = TimeSpan.FromDays(14);

    public static CardUrgency EffectiveUrgency(CardUrgency stored, DateTime? dueAt, DateTime now)
    {
        var implied = ImpliedByDueAt(dueAt, now);
        return stored >= implied ? stored : implied;
    }

    public static CardUrgency EffectiveUrgency(Card card, DateTime now) =>
        EffectiveUrgency(card.Urgency, card.DueAt, now);

    public static CardQuadrant Quadrant(CardImportance importance, CardUrgency effectiveUrgency)
    {
        var important = importance is CardImportance.High or CardImportance.Critical;
        var urgent = effectiveUrgency != CardUrgency.Normal;
        return (important, urgent) switch
        {
            (true, true) => CardQuadrant.DoFirst,
            (true, false) => CardQuadrant.Schedule,
            (false, true) => CardQuadrant.Clear,
            (false, false) => CardQuadrant.Someday
        };
    }

    public static CardQuadrant Quadrant(Card card, DateTime now) =>
        Quadrant(card.Importance, EffectiveUrgency(card, now));

    /// <summary>
    /// Lower sorts first. <paramref name="blockedDependants"/> is accepted and ignored until
    /// CARD-0100 wires relationships.
    /// </summary>
    public static int Rank(
        CardImportance importance,
        CardUrgency urgency,
        DateTime? dueAt,
        DateTime now,
        int blockedDependants = 0)
    {
        _ = blockedDependants;
        var effective = EffectiveUrgency(urgency, dueAt, now);
        return 13 - (3 * (int)importance + 2 * (int)effective);
    }

    public static int Rank(Card card, DateTime now, int blockedDependants = 0) =>
        Rank(card.Importance, card.Urgency, card.DueAt, now, blockedDependants);

    /// <summary>
    /// Maps the 0–5 tracker adapter scale onto <see cref="CardImportance"/>:
    /// 5 → Critical, 4 → High, 2–3 → Normal, 1 → Low, 0 → Normal.
    /// </summary>
    public static CardImportance FromTrackerScale(int trackerScale) => trackerScale switch
    {
        >= 5 => CardImportance.Critical,
        4 => CardImportance.High,
        3 or 2 => CardImportance.Normal,
        1 => CardImportance.Low,
        _ => CardImportance.Normal
    };

    /// <summary>Earliest due date first; null sorts last.</summary>
    public static DateTime DueAtSortKey(DateTime? dueAt) => dueAt ?? DateTime.MaxValue;

    private static CardUrgency ImpliedByDueAt(DateTime? dueAt, DateTime now)
    {
        if (dueAt is null)
            return CardUrgency.Normal;

        var remaining = dueAt.Value - now;
        if (remaining <= NowDueWindow)
            return CardUrgency.Now;
        if (remaining <= SoonDueWindow)
            return CardUrgency.Soon;
        return CardUrgency.Normal;
    }
}
