using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Pure arithmetic for CARD-0072's retry ladder. A gap that is wrong by a rung is invisible in
/// an integration test that only asserts "a later resume was scheduled", so the curve is pinned
/// directly.
/// </summary>
[Category("Unit")]
public class ApiErrorRetryScheduleTests
{
    [Test]
    public void transient_rungs_are_1_3_5_10_30_60_then_hourly()
    {
        var expected = new[] { 1, 3, 5, 10, 30, 60, 60, 60 };
        for (var i = 0; i < expected.Length; i++)
        {
            ApiErrorRetrySchedule.Interval(i + 1, ApiErrorClassification.Transient)
                .ShouldBe(TimeSpan.FromMinutes(expected[i]), $"attempt {i + 1}");
        }
    }

    [Test]
    public void unknown_shares_the_transient_ladder()
    {
        ApiErrorRetrySchedule.Interval(1, ApiErrorClassification.Unknown)
            .ShouldBe(TimeSpan.FromMinutes(1));
        ApiErrorRetrySchedule.Interval(6, ApiErrorClassification.Unknown)
            .ShouldBe(TimeSpan.FromMinutes(60));
        ApiErrorRetrySchedule.Interval(20, ApiErrorClassification.Unknown)
            .ShouldBe(TimeSpan.FromMinutes(60));
    }

    [Test]
    public void wall_enters_at_the_30_minute_rung_then_hourly()
    {
        ApiErrorRetrySchedule.Interval(1, ApiErrorClassification.Wall)
            .ShouldBe(TimeSpan.FromMinutes(ApiErrorRetrySchedule.WallEntryRungMinutes));
        ApiErrorRetrySchedule.Interval(2, ApiErrorClassification.Wall)
            .ShouldBe(TimeSpan.FromMinutes(60));
        ApiErrorRetrySchedule.Interval(9, ApiErrorClassification.Wall)
            .ShouldBe(TimeSpan.FromMinutes(60));
    }

    [Test]
    public void needs_human_never_schedules()
    {
        ApiErrorRetrySchedule.Interval(1, ApiErrorClassification.NeedsHuman).ShouldBeNull();
        ApiErrorRetrySchedule.Interval(99, ApiErrorClassification.NeedsHuman).ShouldBeNull();
    }

    [Test]
    public void clamped_input_still_produces_a_rung()
    {
        ApiErrorRetrySchedule.Interval(0, ApiErrorClassification.Transient)
            .ShouldBe(TimeSpan.FromMinutes(1), "attempt 0 clamps to 1");
        ApiErrorRetrySchedule.Interval(-4, ApiErrorClassification.Transient)
            .ShouldBe(TimeSpan.FromMinutes(1));
        ApiErrorRetrySchedule.Interval(10_000, ApiErrorClassification.Transient)
            .ShouldBe(TimeSpan.FromMinutes(60), "a huge attempt number must clamp, not overflow");
    }

    [Test]
    public void unknown_give_up_is_the_attempt_cap()
    {
        ApiErrorRetrySchedule.UnknownIsExhausted(2, cap: 3).ShouldBeFalse();
        ApiErrorRetrySchedule.UnknownIsExhausted(3, cap: 3).ShouldBeTrue();
        ApiErrorRetrySchedule.UnknownIsExhausted(4, cap: 3).ShouldBeTrue();
        ApiErrorRetrySchedule.UnknownIsExhausted(1, cap: 0).ShouldBeTrue("cap 0 still parks rather than looping");
    }

    [Test]
    public void wall_parks_at_three_consecutive_deaths()
    {
        ApiErrorRetrySchedule.WallIsParked(2, cap: 3).ShouldBeFalse();
        ApiErrorRetrySchedule.WallIsParked(3, cap: 3).ShouldBeTrue();
        ApiErrorRetrySchedule.WallIsParked(5, cap: 3).ShouldBeTrue();
    }
}
