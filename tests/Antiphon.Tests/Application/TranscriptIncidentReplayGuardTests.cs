using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1219 D-2: the guard refuses a record equal to one it admitted while that key is held and
/// its window has not elapsed, and admits everything else: a record differing in any field (a
/// runner repeat with a higher Repeat/UnboundSeconds included), the same record once the window has
/// elapsed, and a record whose key was evicted at capacity. It answers admission only; whether an
/// incident is then written is the incident service's concern, not tested here.
/// </summary>
[Category("Unit")]
public class TranscriptIncidentReplayGuardTests
{
    private static readonly Guid Sid = Guid.Parse("11111111-2222-4333-8444-555555551219");
    private static readonly DateTimeOffset Epoch0 = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(900);

    private static readonly SessionRunnerTranscriptFaultEvent F = new(
        Sid, "AdoptionRefused", "no prompt in it matches input delivered to this session",
        "/home/a/.claude/projects/x/37512455.jsonl", 0, 1);

    private static readonly SessionRunnerTranscriptBoundEvent B = new(
        Sid, "/home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl", "discovery");

    [Test]
    public void First_fault_is_admitted_and_identical_replay_is_refused()
    {
        var guard = new TranscriptIncidentReplayGuard(new FakeTimeProvider(Epoch0), Window);

        guard.Admit(F).ShouldBeTrue("step 1: first fault");
        guard.Admit(F with { }).ShouldBeFalse("step 2: equal-valued distinct instance");
        guard.Admit(F).ShouldBeFalse("step 3: same instance again");
        guard.Admit(B).ShouldBeTrue("step 4: first bound");
        guard.Admit(B with { }).ShouldBeFalse("step 5: equal-valued bound");
    }

    [Test]
    public void A_genuine_repeat_with_higher_repeat_count_is_admitted()
    {
        var guard = new TranscriptIncidentReplayGuard(new FakeTimeProvider(Epoch0), Window);

        guard.Admit(F).ShouldBeTrue("step 1: first fault");
        guard.Admit(F with { Repeat = 2, UnboundSeconds = 300 }).ShouldBeTrue("step 2: five-minute repeat");
        guard.Admit(F with { Kind = "TranscriptMissing" }).ShouldBeTrue("step 3: other kind");
        guard.Admit(F with { Detail = "other detail" }).ShouldBeTrue("step 4: other detail");
        guard.Admit(F with { CandidatePath = null }).ShouldBeTrue("step 5: no candidate");
        guard.Admit(F with { SessionId = Guid.Parse("11111111-2222-4333-8444-555555559999") })
            .ShouldBeTrue("step 6: other session");
        guard.Admit(F).ShouldBeFalse("step 7: the first fault again");
    }

    [Test]
    public void Replay_after_the_window_is_admitted_again()
    {
        var clock = new FakeTimeProvider(Epoch0);
        var guard = new TranscriptIncidentReplayGuard(clock, Window);

        guard.Admit(F).ShouldBeTrue("step 1: first fault");
        clock.Advance(TimeSpan.FromSeconds(899));
        guard.Admit(F).ShouldBeFalse("step 2: 899 s is inside the window");
        clock.Advance(TimeSpan.FromSeconds(1));
        guard.Admit(F).ShouldBeTrue("step 3: 900 s elapsed, window over");
        guard.Admit(F).ShouldBeFalse("step 4: re-stamped at step 3");
    }

    [Test]
    public void Bound_identical_replay_is_refused_and_a_different_path_is_admitted()
    {
        var guard = new TranscriptIncidentReplayGuard(new FakeTimeProvider(Epoch0), Window);

        guard.Admit(B).ShouldBeTrue("step 1: first bound");
        guard.Admit(B).ShouldBeFalse("step 2: identical bound");
        guard.Admit(B with { TranscriptPath = "/home/a/.codex/sessions/2026/10/13/rollout-def.jsonl" })
            .ShouldBeTrue("step 3: other path");
        guard.Admit(B with { How = "fork" }).ShouldBeTrue("step 4: other method");
        guard.Admit(F).ShouldBeTrue("step 5: same session, different record type");
        guard.Admit(B).ShouldBeFalse("step 6: the first bound again");
    }

    [Test]
    public void Guard_evicts_the_oldest_key_past_capacity()
    {
        var guard = new TranscriptIncidentReplayGuard(new FakeTimeProvider(Epoch0), Window, capacity: 3);
        var f1 = F with { Repeat = 1 };
        var f2 = F with { Repeat = 2 };
        var f3 = F with { Repeat = 3 };
        var f4 = F with { Repeat = 4 };

        guard.Admit(f1).ShouldBeTrue("step 1: F1");
        guard.Admit(f2).ShouldBeTrue("step 2: F2");
        guard.Admit(f3).ShouldBeTrue("step 3: F3");
        guard.Admit(f4).ShouldBeTrue("step 4: F4 evicts F1");
        guard.Admit(f1).ShouldBeTrue("step 5: F1 was evicted; evicts F2");
        guard.Admit(f4).ShouldBeFalse("step 6: F4 still held");
        guard.Admit(f2).ShouldBeTrue("step 7: F2 was evicted; evicts F3");
        guard.Admit(f3).ShouldBeTrue("step 8: F3 was evicted; evicts F4");
        guard.Admit(f1).ShouldBeFalse("step 9: F1 still held");
    }
}
