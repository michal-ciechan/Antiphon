using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CompactionContinuationPolicyTests
{
    private static readonly DateTime Accepted = new(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc);
    private static readonly Guid SessionId = Guid.Parse("cea73d57-3072-4cc2-8cb0-ab859e7d3415");

    [Test]
    public void Auto_boundary_with_silent_continuation_is_overdue_at_ten_minutes()
    {
        var (scope, entries, waitStart) = EligibleEpisode();
        var atTen = CompactionContinuationPolicy.Evaluate(scope, entries, 10, waitStart.AddMinutes(10));
        atTen.Overdue.ShouldBeTrue();
        atTen.Eligible.ShouldBeTrue();
        atTen.BoundarySequence.ShouldBe(1256);
        atTen.ContinuationSequence.ShouldBe(1257);
        atTen.OrdinaryPromptSequence.ShouldBe(1255);

        var early = CompactionContinuationPolicy.Evaluate(
            scope, entries, 10, waitStart.AddMinutes(10).AddMilliseconds(-1));
        early.Overdue.ShouldBeFalse();
    }

    [Test]
    public void Manual_and_unknown_compaction_never_arm_recovery()
    {
        foreach (var text in new[] { "Context compacted (manual)", "Context compacted" })
        {
            var (scope, entries, waitStart) = EligibleEpisode(boundaryText: text);
            var verdict = CompactionContinuationPolicy.Evaluate(scope, entries, 10, waitStart.AddHours(24));
            verdict.Eligible.ShouldBeFalse();
        }
    }

    [Test]
    public void Post_boundary_assistant_work_excludes_silent_continuation()
    {
        var (scope, entries, _) = EligibleEpisode();
        var withWork = entries.Append(Line(1258, TranscriptKinds.AssistantText, "still working", Accepted.AddMinutes(30))).ToArray();
        var verdict = CompactionContinuationPolicy.Evaluate(scope, withWork, 10, Accepted.AddHours(24));
        verdict.Eligible.ShouldBeFalse();
    }

    [Test]
    public void Ten_minutes_without_a_turn_end_is_insufficient_without_boundary()
    {
        var scope = CompactionScopeSnapshot.EligibleSeat(SessionId, Accepted);
        CompactionTranscriptFact[] entries =
        [
            Line(1254, TranscriptKinds.TurnEnd, null, Accepted),
            Line(1255, TranscriptKinds.UserPrompt, "check the board", Accepted.AddMinutes(1), check: true),
        ];
        var verdict = CompactionContinuationPolicy.Evaluate(scope, entries, 10, Accepted.AddHours(24));
        verdict.Eligible.ShouldBeFalse();
    }

    [Test]
    public void Unmatched_tool_before_boundary_is_not_silent()
    {
        var scope = CompactionScopeSnapshot.EligibleSeat(SessionId, Accepted);
        var promptAt = Accepted.AddMinutes(1);
        var blockedEntries = new[]
        {
            Line(12540, TranscriptKinds.TurnEnd, null, Accepted),
            Line(12550, TranscriptKinds.UserPrompt, "check the board", promptAt, check: true),
            Line(12560, TranscriptKinds.ToolCall, "read", promptAt.AddSeconds(1), tool: "tool-1"),
            Line(12570, TranscriptKinds.CompactBoundary, "Context compacted (auto)", promptAt.AddSeconds(3)),
            Line(12580, TranscriptKinds.UserPrompt, Continuation(), promptAt.AddSeconds(4)),
        };
        var blocked = CompactionContinuationPolicy.Evaluate(scope, blockedEntries, 10, promptAt.AddSeconds(4).AddMinutes(10));
        blocked.Eligible.ShouldBeFalse();

        var matchedEntries = new[]
        {
            blockedEntries[0],
            blockedEntries[1],
            blockedEntries[2],
            Line(12561, TranscriptKinds.ToolResult, "ok", promptAt.AddSeconds(2), tool: "tool-1"),
            blockedEntries[3],
            blockedEntries[4],
        };
        var matched = CompactionContinuationPolicy.Evaluate(scope, matchedEntries, 10, promptAt.AddSeconds(4).AddMinutes(10));
        matched.Eligible.ShouldBeTrue();
    }

    [Test]
    public void Missing_continuation_never_arms_recovery()
    {
        var (scope, entries, waitStart) = EligibleEpisode();
        var without = entries.Where(e => e.Sequence != 1257).ToArray();
        var verdict = CompactionContinuationPolicy.Evaluate(scope, without, 10, waitStart.AddHours(1));
        verdict.Eligible.ShouldBeFalse();
    }

    [Test]
    [Arguments("Starting")]
    [Arguments("Stopped")]
    [Arguments("Failed")]
    public void Non_running_session_never_arms_recovery(string status)
    {
        var (scope, entries, waitStart) = EligibleEpisode();
        scope = scope with { Running = false };
        _ = status;
        var verdict = CompactionContinuationPolicy.Evaluate(scope, entries, 10, waitStart.AddMinutes(10));
        verdict.Eligible.ShouldBeFalse();
    }

    [Test]
    public void Idle_compaction_never_arms_recovery()
    {
        var (scope, entries, waitStart) = EligibleEpisode();
        scope = scope with { Working = false };
        var verdict = CompactionContinuationPolicy.Evaluate(scope, entries, 10, waitStart.AddMinutes(10));
        verdict.Eligible.ShouldBeFalse();
    }

    [Test]
    public void Thinking_after_boundary_permanently_disqualifies_it() =>
        Disqualified(Line(1258, TranscriptKinds.Thinking, "hmm", Accepted.AddMinutes(20)));

    [Test]
    public void Tool_call_after_boundary_disqualifies_it() =>
        Disqualified(Line(1258, TranscriptKinds.ToolCall, "bash", Accepted.AddMinutes(20), tool: "tool-9"));

    [Test]
    public void Tool_result_after_boundary_disqualifies_it() =>
        Disqualified(Line(1258, TranscriptKinds.ToolResult, "done", Accepted.AddMinutes(20), tool: "tool-9"));

    [Test]
    public void Ordinary_prompt_after_boundary_disqualifies_it() =>
        Disqualified(Line(1258, TranscriptKinds.UserPrompt, "a real follow-up", Accepted.AddMinutes(20)));

    [Test]
    public void Unknown_activity_after_boundary_is_not_silence()
    {
        Disqualified(Line(1258, "MysteryRecord", "nope", Accepted.AddMinutes(20)));
        Disqualified(Line(1258, "SessionError", "wall", Accepted.AddMinutes(20)));
        Disqualified(Line(1258, "Wall", "timeout", Accepted.AddMinutes(20)));
    }

    [Test]
    public void Turn_end_after_boundary_disqualifies_it()
    {
        Disqualified(Line(1258, TranscriptKinds.TurnEnd, null, Accepted.AddMinutes(20)));
        Disqualified(Line(1258, TranscriptKinds.UserPrompt, "[Request interrupted by user]", Accepted.AddMinutes(20)));
        Disqualified(Line(1258, TranscriptKinds.CompactBoundary, "Context compacted (manual)", Accepted.AddMinutes(20)));
        Disqualified(Line(1258, TranscriptKinds.SessionRestartBoundary, null, Accepted.AddMinutes(20)));
    }

    [Test]
    public void Repeated_boundaries_postpone_the_grace()
    {
        var scope = CompactionScopeSnapshot.EligibleSeat(SessionId, Accepted);
        var old = Accepted.AddMinutes(1);
        var newest = Accepted.AddMinutes(11);
        CompactionTranscriptFact[] entries =
        [
            Line(1255, TranscriptKinds.UserPrompt, "check the board", old, check: true),
            Line(1256, TranscriptKinds.CompactBoundary, "Context compacted (auto)", old.AddSeconds(1)),
            Line(1257, TranscriptKinds.UserPrompt, Continuation(), old.AddSeconds(2)),
            Line(1258, TranscriptKinds.CompactBoundary, "Context compacted (auto)", newest),
            Line(1259, TranscriptKinds.UserPrompt, Continuation(), newest.AddSeconds(1)),
        ];
        var verdict = CompactionContinuationPolicy.Evaluate(scope, entries, 10, newest.AddSeconds(1).AddMinutes(1));
        verdict.Overdue.ShouldBeFalse();
        verdict.BoundarySequence.ShouldBe(1258);
    }

    [Test]
    public void Historical_backfill_cannot_arm_current_generation()
    {
        var scope = CompactionScopeSnapshot.EligibleSeat(SessionId, Accepted);
        var historical = Accepted.AddDays(-1);
        CompactionTranscriptFact[] entries =
        [
            Line(10, TranscriptKinds.UserPrompt, "check the board", Accepted.AddSeconds(1), check: true),
            Line(11, TranscriptKinds.CompactBoundary, "Context compacted (auto)", historical),
            Line(12, TranscriptKinds.UserPrompt, Continuation(), historical.AddSeconds(1)),
        ];
        var verdict = CompactionContinuationPolicy.Evaluate(scope, entries, 10, Accepted.AddMinutes(10));
        verdict.Eligible.ShouldBeFalse();
    }

    [Test]
    public void Latest_arrival_controls_the_ten_minute_boundary()
    {
        var now = Accepted.AddHours(1);
        AssertNotOverdueWhenLatest(now, static (prompt, boundary, continuation, latest) => boundary with { EventTime = latest });
        AssertNotOverdueWhenLatest(now, static (prompt, boundary, continuation, latest) => boundary with { CreatedAt = latest });
        AssertNotOverdueWhenLatest(now, static (prompt, boundary, continuation, latest) => continuation with { EventTime = latest });
        AssertNotOverdueWhenLatest(now, static (prompt, boundary, continuation, latest) => continuation with { CreatedAt = latest });

        var scope = CompactionScopeSnapshot.EligibleSeat(SessionId, now.AddMinutes(-1));
        var old = now.AddMinutes(-30);
        CompactionTranscriptFact[] freshArrival =
        [
            Line(1254, TranscriptKinds.TurnEnd, null, old),
            Line(1255, TranscriptKinds.UserPrompt, "check the board", old.AddSeconds(1), check: true),
            new(1256, TranscriptKinds.CompactBoundary, "Context compacted (auto)", old, old, "b", null, false, false, null),
            new(1257, TranscriptKinds.UserPrompt, Continuation(), old, now.AddMinutes(-1), "c", null, false, false, null),
        ];
        var verdict = CompactionContinuationPolicy.Evaluate(scope, freshArrival, 10, now);
        verdict.Overdue.ShouldBeFalse();
    }

    private static void AssertNotOverdueWhenLatest(
        DateTime now,
        Func<CompactionTranscriptFact, CompactionTranscriptFact, CompactionTranscriptFact, DateTime, CompactionTranscriptFact> lift)
    {
        var old = now.AddMinutes(-30);
        var latest = now.AddMinutes(-1);
        var prompt = Line(1255, TranscriptKinds.UserPrompt, "check the board", old, check: true);
        var boundary = Line(1256, TranscriptKinds.CompactBoundary, "Context compacted (auto)", old);
        var continuation = Line(1257, TranscriptKinds.UserPrompt, Continuation(), old);
        var moved = lift(prompt, boundary, continuation, latest);
        CompactionTranscriptFact[] entries =
        [
            Line(1254, TranscriptKinds.TurnEnd, null, old.AddSeconds(-1)),
            moved.Sequence == prompt.Sequence ? moved : prompt,
            moved.Sequence == boundary.Sequence ? moved : boundary,
            moved.Sequence == continuation.Sequence ? moved : continuation,
        ];
        var accepted = moved.Sequence == prompt.Sequence ? latest : old;
        if (moved.Sequence != prompt.Sequence)
            accepted = old;
        var scope = CompactionScopeSnapshot.EligibleSeat(SessionId, accepted);
        // When the prompt itself is not the lifted member, accepted stays old so the
        // lifted boundary or continuation time is the unique maximum.
        if (moved.Sequence == prompt.Sequence)
            scope = scope with { AcceptedStartedAt = latest };
        var verdict = CompactionContinuationPolicy.Evaluate(scope, entries, 10, now);
        verdict.Overdue.ShouldBeFalse();
    }

    private static void Disqualified(CompactionTranscriptFact extra)
    {
        var (scope, entries, _) = EligibleEpisode();
        var verdict = CompactionContinuationPolicy.Evaluate(
            scope, entries.Append(extra).ToArray(), 10, Accepted.AddHours(24));
        verdict.Eligible.ShouldBeFalse();
    }

    private static (CompactionScopeSnapshot Scope, CompactionTranscriptFact[] Entries, DateTime WaitStart) EligibleEpisode(
        string boundaryText = "Context compacted (auto)")
    {
        var promptAt = Accepted.AddMinutes(1);
        var boundaryAt = promptAt.AddSeconds(1);
        var continuationAt = boundaryAt.AddSeconds(1);
        var scope = CompactionScopeSnapshot.EligibleSeat(SessionId, Accepted);
        CompactionTranscriptFact[] entries =
        [
            Line(1254, TranscriptKinds.TurnEnd, null, Accepted),
            Line(1255, TranscriptKinds.UserPrompt, "check the board", promptAt, check: true),
            Line(1256, TranscriptKinds.CompactBoundary, boundaryText, boundaryAt),
            Line(1257, TranscriptKinds.UserPrompt, Continuation(), continuationAt),
        ];
        return (scope, entries, continuationAt);
    }

    private static CompactionTranscriptFact Line(
        long sequence,
        string kind,
        string? text,
        DateTime when,
        bool check = false,
        string? tool = null) =>
        new(sequence, kind, text, when, when, $"{kind}-{sequence}", tool, check);

    private static string Continuation() =>
        TranscriptKinds.CompactionContinuationPromptPrefix + ". The summary below covers the check.";
}
