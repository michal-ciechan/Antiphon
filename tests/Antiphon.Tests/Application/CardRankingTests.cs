using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0039: one function owns the derived triple. The 12-cell rank table, due-date windows,
/// quadrant, tracker-scale mapping and the ignored blockedDependants extension point.
/// </summary>
[Category("Unit")]
public class CardRankingTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    private static readonly (CardImportance Importance, CardUrgency Urgency, int Rank)[] RankTable =
    [
        (CardImportance.Critical, CardUrgency.Now, 0),
        (CardImportance.Critical, CardUrgency.Soon, 2),
        (CardImportance.Critical, CardUrgency.Normal, 4),
        (CardImportance.High, CardUrgency.Now, 3),
        (CardImportance.High, CardUrgency.Soon, 5),
        (CardImportance.High, CardUrgency.Normal, 7),
        (CardImportance.Normal, CardUrgency.Now, 6),
        (CardImportance.Normal, CardUrgency.Soon, 8),
        (CardImportance.Normal, CardUrgency.Normal, 10),
        (CardImportance.Low, CardUrgency.Now, 9),
        (CardImportance.Low, CardUrgency.Soon, 11),
        (CardImportance.Low, CardUrgency.Normal, 13),
    ];

    [Test]
    public void Rank_table_matches_the_twelve_cells()
    {
        foreach (var (importance, urgency, rank) in RankTable)
            CardRanking.Rank(importance, urgency, dueAt: null, Now).ShouldBe(rank);
    }

    [Test]
    public void Rank_prefers_critical_normal_over_normal_now_and_low_now_over_the_default()
    {
        var criticalNormal = CardRanking.Rank(CardImportance.Critical, CardUrgency.Normal, null, Now);
        var normalNow = CardRanking.Rank(CardImportance.Normal, CardUrgency.Now, null, Now);
        var lowNow = CardRanking.Rank(CardImportance.Low, CardUrgency.Now, null, Now);
        var defaultRank = CardRanking.Rank(CardImportance.Normal, CardUrgency.Normal, null, Now);

        criticalNormal.ShouldBeLessThan(normalNow);
        lowNow.ShouldBeLessThan(defaultRank);
    }

    [Test]
    public void EffectiveUrgency_due_within_three_days_or_passed_implies_Now()
    {
        CardRanking.EffectiveUrgency(CardUrgency.Normal, Now.AddDays(3), Now).ShouldBe(CardUrgency.Now);
        CardRanking.EffectiveUrgency(CardUrgency.Normal, Now, Now).ShouldBe(CardUrgency.Now);
        CardRanking.EffectiveUrgency(CardUrgency.Normal, Now.AddDays(-1), Now).ShouldBe(CardUrgency.Now);
        CardRanking.EffectiveUrgency(CardUrgency.Soon, Now.AddDays(1), Now).ShouldBe(CardUrgency.Now);
    }

    [Test]
    public void EffectiveUrgency_due_just_past_three_days_and_within_fourteen_implies_Soon()
    {
        CardRanking.EffectiveUrgency(CardUrgency.Normal, Now.AddDays(3).AddTicks(1), Now)
            .ShouldBe(CardUrgency.Soon);
        CardRanking.EffectiveUrgency(CardUrgency.Normal, Now.AddDays(14), Now).ShouldBe(CardUrgency.Soon);
    }

    [Test]
    public void EffectiveUrgency_due_just_past_fourteen_days_does_not_escalate()
    {
        CardRanking.EffectiveUrgency(CardUrgency.Normal, Now.AddDays(14).AddTicks(1), Now)
            .ShouldBe(CardUrgency.Normal);
        CardRanking.EffectiveUrgency(CardUrgency.Normal, dueAt: null, Now).ShouldBe(CardUrgency.Normal);
    }

    [Test]
    public void EffectiveUrgency_stored_Now_is_not_lowered_by_a_distant_due_date()
    {
        CardRanking.EffectiveUrgency(CardUrgency.Now, Now.AddDays(30), Now).ShouldBe(CardUrgency.Now);
        CardRanking.EffectiveUrgency(CardUrgency.Soon, Now.AddDays(30), Now).ShouldBe(CardUrgency.Soon);
    }

    [Test]
    public void Rank_due_date_escalation_moves_a_normal_card_into_the_Now_cell()
    {
        CardRanking.Rank(CardImportance.Normal, CardUrgency.Normal, Now.AddDays(2), Now)
            .ShouldBe(6);
    }

    [Test]
    public void DueAtSortKey_puts_null_after_any_date()
    {
        CardRanking.DueAtSortKey(Now).ShouldBe(Now);
        CardRanking.DueAtSortKey(null).ShouldBe(DateTime.MaxValue);
        CardRanking.DueAtSortKey(Now).ShouldBeLessThan(CardRanking.DueAtSortKey(null));
    }

    [Test]
    public void Quadrant_maps_the_four_eisenhower_cells()
    {
        CardRanking.Quadrant(CardImportance.Critical, CardUrgency.Now).ShouldBe(CardQuadrant.DoFirst);
        CardRanking.Quadrant(CardImportance.High, CardUrgency.Soon).ShouldBe(CardQuadrant.DoFirst);
        CardRanking.Quadrant(CardImportance.High, CardUrgency.Normal).ShouldBe(CardQuadrant.Schedule);
        CardRanking.Quadrant(CardImportance.Critical, CardUrgency.Normal).ShouldBe(CardQuadrant.Schedule);
        CardRanking.Quadrant(CardImportance.Normal, CardUrgency.Now).ShouldBe(CardQuadrant.Clear);
        CardRanking.Quadrant(CardImportance.Low, CardUrgency.Soon).ShouldBe(CardQuadrant.Clear);
        CardRanking.Quadrant(CardImportance.Normal, CardUrgency.Normal).ShouldBe(CardQuadrant.Someday);
        CardRanking.Quadrant(CardImportance.Low, CardUrgency.Normal).ShouldBe(CardQuadrant.Someday);
    }

    [Test]
    public void FromTrackerScale_maps_the_zero_to_five_adapter_scale()
    {
        CardRanking.FromTrackerScale(5).ShouldBe(CardImportance.Critical);
        CardRanking.FromTrackerScale(6).ShouldBe(CardImportance.Critical);
        CardRanking.FromTrackerScale(4).ShouldBe(CardImportance.High);
        CardRanking.FromTrackerScale(3).ShouldBe(CardImportance.Normal);
        CardRanking.FromTrackerScale(2).ShouldBe(CardImportance.Normal);
        CardRanking.FromTrackerScale(1).ShouldBe(CardImportance.Low);
        CardRanking.FromTrackerScale(0).ShouldBe(CardImportance.Normal);
        CardRanking.FromTrackerScale(-1).ShouldBe(CardImportance.Normal);
    }

    [Test]
    public void Rank_ignores_blockedDependants_until_CARD_0100()
    {
        var without = CardRanking.Rank(CardImportance.High, CardUrgency.Now, null, Now);
        var with = CardRanking.Rank(CardImportance.High, CardUrgency.Now, null, Now, blockedDependants: 12);
        with.ShouldBe(without);
    }

    [Test]
    public void Due_windows_are_the_documented_constants()
    {
        CardRanking.NowDueWindow.ShouldBe(TimeSpan.FromDays(3));
        CardRanking.SoonDueWindow.ShouldBe(TimeSpan.FromDays(14));
    }
}
