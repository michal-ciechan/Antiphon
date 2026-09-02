using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0057 S1 — the calendar is a pure function, table-tested, no clock and no database.</summary>
public class ScheduleRecurrenceTests
{
    [Test]
    public void once_in_the_past_is_due_now_and_never_again()
    {
        var fireAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = fireAt.AddHours(2);
        var schedule = Once("late once", fireAt);

        ScheduleRecurrence.InitialNextFireAt(schedule, now).ShouldBe(fireAt);
        ScheduleRecurrence.NextAfter(schedule, fireAt).ShouldBe(fireAt);
        ScheduleRecurrence.NextAfter(schedule, now).ShouldBeNull();
        ScheduleRecurrence.NextOccurrences(schedule, now, 3).ShouldBe([fireAt]);
    }

    [Test]
    public void interval_is_anchored_and_does_not_drift_after_a_slow_tick()
    {
        var anchor = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var schedule = Interval("every 10", every: 10, anchor);

        ScheduleRecurrence.NextAfter(schedule, anchor).ShouldBe(anchor.AddMinutes(10));
        ScheduleRecurrence.NextAfter(schedule, anchor.AddMinutes(10)).ShouldBe(anchor.AddMinutes(20));

        // A slow tick that runs 3 minutes late still lands on the grid, not last+10 from the late now.
        var lateTick = anchor.AddMinutes(13);
        ScheduleRecurrence.NextAfter(schedule, lateTick).ShouldBe(anchor.AddMinutes(20));
        ScheduleRecurrence.NextAfter(schedule, lateTick).ShouldNotBe(lateTick.AddMinutes(10));
    }

    [Test]
    public void daily_skips_days_not_in_the_mask_and_wraps_the_week()
    {
        // 2026-09-01 is a Tuesday. Mask = Mon only.
        var schedule = Daily("mondays", "09:00", "Europe/London", ScheduleRecurrence.MondayBit);
        var tuesday = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        var next = ScheduleRecurrence.NextAfter(schedule, tuesday);
        next.ShouldNotBeNull();
        var local = TimeZoneInfo.ConvertTimeFromUtc(next.Value, London());
        local.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        local.Hour.ShouldBe(9);
        local.Minute.ShouldBe(0);
        local.Date.ShouldBe(new DateTime(2026, 9, 7));
    }

    [Test]
    public void daily_in_the_spring_forward_gap_rolls_to_the_first_valid_minute()
    {
        // Europe/London 2026-03-29 01:00 GMT -> 02:00 BST. 01:30 does not exist.
        var schedule = Daily("gap", "01:30", "Europe/London", ScheduleRecurrence.AllDaysMask);
        var before = new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc);

        var next = ScheduleRecurrence.NextAfter(schedule, before);
        next.ShouldNotBeNull();
        var local = TimeZoneInfo.ConvertTimeFromUtc(next.Value, London());
        local.Date.ShouldBe(new DateTime(2026, 3, 29));
        local.Hour.ShouldBe(2);
        local.Minute.ShouldBe(0);
    }

    [Test]
    public void daily_in_the_fall_back_overlap_takes_the_first_occurrence()
    {
        // Europe/London 2026-10-25 02:00 BST -> 01:00 GMT. 01:30 happens twice; take the first (BST).
        var schedule = Daily("overlap", "01:30", "Europe/London", ScheduleRecurrence.AllDaysMask);
        var before = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

        var next = ScheduleRecurrence.NextAfter(schedule, before);
        next.ShouldNotBeNull();
        next.Value.ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Test]
    public void an_unknown_zone_is_refused_at_create()
    {
        var ex = Should.Throw<ArgumentException>(() => ScheduleRecurrence.RequireTimeZone("Not/AZone"));
        ex.Message.ShouldContain("Unknown time zone");
        ScheduleRecurrence.TryGetTimeZone("Not/AZone", out _).ShouldBeFalse();
    }

    [Test]
    public void describe_names_weekdays_and_the_zone()
    {
        var weekdays = Daily(
            "triage",
            "09:00",
            "Europe/London",
            ScheduleRecurrence.MondayBit | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4));
        ScheduleRecurrence.Describe(weekdays).ShouldBe("daily 09:00 Europe/London, Mon-Fri");

        var once = Once("one shot", new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc));
        ScheduleRecurrence.Describe(once).ShouldBe("once 2026-09-04 08:00 UTC");

        var interval = Interval("heartbeat", 30, DateTime.UtcNow);
        ScheduleRecurrence.Describe(interval).ShouldBe("every 30 min");
    }

    private static TimeZoneInfo London() => TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static Schedule Once(string name, DateTime fireAt) => new()
    {
        Name = name,
        Kind = ScheduleKind.Prompt,
        Repeat = ScheduleRepeat.Once,
        TimeZoneId = "UTC",
        FireAt = fireAt,
    };

    private static Schedule Interval(string name, int every, DateTime anchor) => new()
    {
        Name = name,
        Kind = ScheduleKind.Prompt,
        Repeat = ScheduleRepeat.Interval,
        TimeZoneId = "UTC",
        EveryMinutes = every,
        AnchorAt = anchor,
    };

    private static Schedule Daily(string name, string atLocal, string zone, int days) => new()
    {
        Name = name,
        Kind = ScheduleKind.Prompt,
        Repeat = ScheduleRepeat.Daily,
        TimeZoneId = zone,
        AtLocal = atLocal,
        DaysOfWeek = days,
    };
}
