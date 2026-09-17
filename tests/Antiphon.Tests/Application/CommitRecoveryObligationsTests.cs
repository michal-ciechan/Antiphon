using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;
using static Antiphon.Server.Application.Services.CommitRecoveryObligations;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0547 D-1/D-3: the pure obligation rule, the shared hold predicate and the row shapes.</summary>
[Category("Unit")]
public sealed class CommitRecoveryObligationsTests
{
    private static readonly Guid Task = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static AgentTaskEvent Row(AgentTaskEventType type, string detail, DateTime at, Guid? task = null) => new()
    {
        Id = Guid.NewGuid(),
        AgentTaskId = task ?? Task,
        Type = type,
        Detail = detail,
        At = at,
    };

    private static AgentTaskEvent Started(DateTime? at = null) => Row(AgentTaskEventType.CommitRecoveryStarted, Digest, at ?? T0);

    [Test]
    public void Unresolved_lists_a_started_obligation_with_no_resolution()
    {
        var started = Started();
        var events = new[]
        {
            started,
            Row(AgentTaskEventType.Warning, $"{started.Id:D} unrelated", T0),
            Row(AgentTaskEventType.CommitRecoveryStarted, Digest, T0, Guid.NewGuid()),
        };

        var pending = Unresolved(events, Task).ShouldHaveSingleItem();

        pending.EventId.ShouldBe(started.Id);
        pending.TaskId.ShouldBe(Task);
        pending.Settlement.ShouldBe(Digest);
        pending.StartedAt.ShouldBe(T0);
    }

    [Test]
    public void NotNeeded_whose_detail_starts_with_the_started_id_and_a_space_resolves()
    {
        foreach (var outcome in new[] { "NothingToCommit", "CommitFailed" })
        {
            var started = Started();
            Unresolved([started, Row(AgentTaskEventType.CommitRecoveryNotNeeded, $"{started.Id:D} {outcome}", T0)], Task)
                .ShouldBeEmpty(outcome);
        }
    }

    [Test]
    public void Resolution_rows_naming_another_obligation_do_not_resolve()
    {
        foreach (var type in new[] { AgentTaskEventType.CommitRecoveryNotNeeded, AgentTaskEventType.CommitRecoveryAbandoned })
        {
            var started = Started();
            Unresolved([started, Row(type, $"{Guid.NewGuid():D} NothingToCommit", T0)], Task)
                .ShouldHaveSingleItem($"other D id ({type})");
            Unresolved([started, Row(type, $"{started.Id:N} NothingToCommit", T0)], Task)
                .ShouldHaveSingleItem($"N-format id ({type})");
            Unresolved([started, Row(type, $"{started.Id:D}NothingToCommit", T0)], Task)
                .ShouldHaveSingleItem($"no separating space ({type})");
        }
    }

    [Test]
    public void Abandoned_whose_detail_starts_with_the_started_id_resolves()
    {
        var started = Started();
        Unresolved([started, Row(AgentTaskEventType.CommitRecoveryAbandoned, $"{started.Id:D} overdue-task deadline: reason", T0)], Task)
            .ShouldBeEmpty();
    }

    [Test]
    public void Committed_at_or_after_the_start_resolves_and_before_does_not()
    {
        var started = Started();
        Unresolved([started, Row(AgentTaskEventType.Committed, "abc1234 a.md", T0)], Task)
            .ShouldBeEmpty("same instant");
        Unresolved([started, Row(AgentTaskEventType.Committed, "abc1234 a.md", T0.AddTicks(1))], Task)
            .ShouldBeEmpty("+1 tick");
        Unresolved([started, Row(AgentTaskEventType.Committed, "abc1234 a.md", T0.AddTicks(-1))], Task)
            .ShouldHaveSingleItem("-1 tick");
    }

    [Test]
    public void Abandon_builds_the_type_36_row_with_the_prefix_convention()
    {
        var started = Started();
        var p = Unresolved([started], Task).ShouldHaveSingleItem();
        var now = T0.AddMinutes(800);

        var row = Abandon(p, "overdue-task deadline", "why", now);

        row.Type.ShouldBe(AgentTaskEventType.CommitRecoveryAbandoned);
        ((int)row.Type).ShouldBe(36);
        row.AgentTaskId.ShouldBe(Task);
        row.Detail.ShouldBe($"{p.EventId:D} overdue-task deadline: why");
        row.At.ShouldBe(now);
        Unresolved([started, row], Task).ShouldBeEmpty();
    }

    [Test]
    public void Describe_names_the_event_the_digest_and_the_recipe()
    {
        var p = Unresolved([Started()], Task).ShouldHaveSingleItem();

        var text = Describe(Task, p);

        text.ShouldContain(p.EventId.ToString("D"));
        text.ShouldContain(p.Settlement);
        text.ShouldContain($"git log --all --reflog --fixed-strings --all-match --grep={Task:D} --grep={p.Settlement} --format=%H");
        text.ShouldContain("nothing was pushed", Case.Insensitive);
    }

    [Test]
    public void Default_hold_is_720_minutes()
    {
        new DelegationSettings().CommitRecoveryHoldMinutes.ShouldBe(720);
    }

    [Test]
    public void ShouldHold_rules()
    {
        var now = T0.AddDays(2);
        Pending Aged(int minutes) => new(Task, Guid.NewGuid(), Digest, now.AddMinutes(-minutes));
        var hold = TimeSpan.FromMinutes(720);

        ShouldHold([], hold, now).ShouldBeFalse("empty");
        ShouldHold([Aged(10)], TimeSpan.Zero, now).ShouldBeFalse("hold 0");
        ShouldHold([Aged(10)], TimeSpan.FromMinutes(-5), now).ShouldBeFalse("hold -5 min");
        ShouldHold([Aged(10)], hold, now).ShouldBeTrue("young (10 min, hold 720)");
        ShouldHold([Aged(800)], hold, now).ShouldBeFalse("expired (800 min)");
        ShouldHold([Aged(720)], hold, now).ShouldBeFalse("at the boundary (720 min exactly)");
        ShouldHold([Aged(10), Aged(800)], hold, now).ShouldBeFalse("oldest of two decides (10 min + 800 min)");
    }
}
