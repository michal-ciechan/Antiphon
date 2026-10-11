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

    /// <summary>
    /// CARD-1186 V-2. Event text is built here, not by <see cref="Abandon"/> or
    /// <see cref="FormatAbandonedGuidance"/>.
    /// </summary>
    [Test]
    [Arguments("single")]
    [Arguments("multiple")]
    [Arguments("foreign-task")]
    [Arguments("wrong-start-id")]
    [Arguments("missing-start")]
    [Arguments("different-failure")]
    [Arguments("duplicate-abandonment")]
    [Arguments("truncated-abandonment")]
    public void C1186_Abandoned_facts_match_the_saved_failure(string shape)
    {
        const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string DigestC = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        var early = Guid.Parse("00000000-0000-4000-8000-000000000010");
        var tieLow = Guid.Parse("00000000-0000-4000-8000-000000000020");
        var tieHigh = Guid.Parse("00000000-0000-4000-8000-000000000030");
        var tiedAt = T0.AddMinutes(5);

        var facts = shape switch
        {
            "single" => Select(Pair(early, DigestA, T0)),
            "multiple" => Select(Shuffled(
                Pair(tieHigh, DigestC, tiedAt),
                Pair(early, DigestA, T0),
                Pair(tieLow, DigestB, tiedAt))),
            "foreign-task" => SelectForeign(early, DigestA, T0),
            "wrong-start-id" => Select(WrongStart(early, DigestA, T0)),
            "missing-start" => SelectMissing(early, DigestA, T0),
            "different-failure" => SelectDifferent(early, DigestA, T0),
            "duplicate-abandonment" => Select(DuplicateAbandonment(early, DigestA, T0)),
            "truncated-abandonment" => SelectTruncated(early, DigestA, T0),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "unknown CARD-1186 shape"),
        };

        switch (shape)
        {
            case "single":
            case "duplicate-abandonment":
                facts.Select(f => f.EventId).ShouldBe([early]);
                facts.Select(f => f.Settlement).ShouldBe([DigestA]);
                facts.Single().StartedAt.ShouldBe(T0);
                break;
            case "multiple":
                facts.Select(f => f.EventId).ShouldBe([early, tieLow, tieHigh]);
                facts.Select(f => f.Settlement).ShouldBe([DigestA, DigestB, DigestC]);
                facts.Select(f => f.StartedAt).ShouldBe([T0, tiedAt, tiedAt]);
                facts.Select(f => f.EventId).Distinct().Count().ShouldBe(facts.Count);
                break;
            case "truncated-abandonment":
                facts.Select(f => f.EventId).ShouldBe([early]);
                facts.Select(f => f.Settlement).ShouldBe([DigestA]);
                break;
            case "foreign-task":
            case "wrong-start-id":
            case "missing-start":
            case "different-failure":
                facts.ShouldBeEmpty(shape);
                break;
        }
    }

    private static IReadOnlyList<Pending> Select(IEnumerable<AgentTaskEvent> events, string? reason = null)
    {
        var rows = events.ToList();
        return SelectAbandonedForSavedFailure(rows, Task, reason ?? ReasonFor(rows, Task));
    }

    private static string ReasonFor(IEnumerable<AgentTaskEvent> events, Guid taskId)
    {
        var markers = events
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.CommitRecoveryStarted)
            .Select(e => $"{e.Id:D} (settlement {e.Detail})");
        return "the pty-host exited (code 1). The task held unresolved commit-recovery obligation(s) "
            + string.Join(", ", markers) + "; they were abandoned and any gated commit is local and unpushed.";
    }

    private static IEnumerable<AgentTaskEvent> Pair(Guid id, string settlement, DateTime at, Guid? taskId = null)
    {
        var start = StartedRow(id, settlement, at, taskId);
        var reason = ReasonFor([start], start.AgentTaskId);
        yield return start;
        yield return AbandonedRow(start.AgentTaskId, WriterDetail(id, reason), at.AddMinutes(1));
    }

    private static IEnumerable<AgentTaskEvent> Shuffled(params IEnumerable<AgentTaskEvent>[] pairs)
    {
        var rows = pairs.SelectMany(p => p).ToList();
        var reason = ReasonFor(rows, Task);
        foreach (var row in rows)
        {
            if (row.Type != AgentTaskEventType.CommitRecoveryAbandoned)
            {
                yield return row;
                continue;
            }

            var startId = Guid.Parse(row.Detail.Split(' ')[0]);
            yield return AbandonedRow(row.AgentTaskId, WriterDetail(startId, reason), row.At);
        }
    }

    private static IEnumerable<AgentTaskEvent> WrongStart(Guid id, string settlement, DateTime at)
    {
        var start = StartedRow(id, settlement, at, Task);
        var reason = ReasonFor([start], Task);
        var other = Guid.Parse("99999999-9999-4999-8999-999999999999");
        yield return start;
        yield return AbandonedRow(Task, WriterDetail(other, reason), at.AddMinutes(1));
    }

    private static IReadOnlyList<Pending> SelectForeign(Guid id, string settlement, DateTime at)
    {
        var foreign = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");
        var rows = Pair(id, settlement, at, foreign).ToList();
        return SelectAbandonedForSavedFailure(rows, Task, ReasonFor(rows, foreign));
    }

    private static IReadOnlyList<Pending> SelectMissing(Guid id, string settlement, DateTime at)
    {
        var ghost = StartedRow(id, settlement, at, Task);
        var reason = ReasonFor([ghost], Task);
        var rows = new[] { AbandonedRow(Task, WriterDetail(id, reason), at.AddMinutes(1)) };
        return SelectAbandonedForSavedFailure(rows, Task, reason);
    }

    private static IReadOnlyList<Pending> SelectDifferent(Guid id, string settlement, DateTime at)
    {
        var start = StartedRow(id, settlement, at, Task);
        var saved = ReasonFor([start], Task);
        var rows = new[] { start, AbandonedRow(Task, WriterDetail(id, saved), at.AddMinutes(1)) };
        return SelectAbandonedForSavedFailure(rows, Task, saved + " and then a later failure");
    }

    private static IEnumerable<AgentTaskEvent> DuplicateAbandonment(Guid id, string settlement, DateTime at)
    {
        foreach (var row in Pair(id, settlement, at))
            yield return row;
        var start = StartedRow(id, settlement, at, Task);
        var reason = ReasonFor([start], Task);
        yield return AbandonedRow(Task, WriterDetail(id, reason), at.AddMinutes(2));
    }

    private static IReadOnlyList<Pending> SelectTruncated(Guid id, string settlement, DateTime at)
    {
        var marker = $"{id:D} (settlement {settlement})";
        var reason = marker + new string('z', 5000);
        var markerAt = reason.IndexOf(marker, StringComparison.Ordinal);
        (markerAt >= 0 && markerAt + marker.Length < reason.Length).ShouldBeTrue("obligation sits before the end");
        var detail = WriterDetail(id, reason);
        detail.Length.ShouldBe(4000);
        var rows = new[]
        {
            StartedRow(id, settlement, at, Task),
            AbandonedRow(Task, detail, at.AddMinutes(1)),
        };
        return SelectAbandonedForSavedFailure(rows, Task, reason);
    }

    private static AgentTaskEvent StartedRow(Guid id, string settlement, DateTime at, Guid? taskId) => new()
    {
        Id = id,
        AgentTaskId = taskId ?? Task,
        Type = AgentTaskEventType.CommitRecoveryStarted,
        Detail = settlement,
        At = at,
    };

    private static AgentTaskEvent AbandonedRow(Guid taskId, string detail, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        AgentTaskId = taskId,
        Type = AgentTaskEventType.CommitRecoveryAbandoned,
        Detail = detail,
        At = at,
    };

    /// <summary>The dead-session writer's text, copied here so the fixture does not call <see cref="Abandon"/>.</summary>
    private static string WriterDetail(Guid startId, string failureReason)
    {
        var full = $"{startId:D} dead-session reconciler: {failureReason}";
        return full.Length <= 4000 ? full : full[..4000];
    }
}
