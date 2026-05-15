using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Domain.StateMachine;

[Category("Unit")]
public class RunAttemptStateMachineTests
{
    [Test]
    public void RunAttemptPhaseMachine_illegal_transitions_throw()
    {
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var attempt = NewAttempt(RunPhase.PreparingWorkspace, now);

        Should.Throw<InvalidOperationException>(() =>
            RunAttemptStateMachine.Transition(attempt, RunPhase.StreamingTurn, now.AddSeconds(1)));
    }

    [Test]
    public void RunAttemptPhaseMachine_records_phase_durations()
    {
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var attempt = NewAttempt(RunPhase.PreparingWorkspace, now);

        RunAttemptStateMachine.Transition(attempt, RunPhase.BuildingPrompt, now.AddSeconds(2));
        RunAttemptStateMachine.Transition(attempt, RunPhase.LaunchingAgent, now.AddSeconds(5));
        RunAttemptStateMachine.Transition(attempt, RunPhase.InitializingSession, now.AddSeconds(7));
        RunAttemptStateMachine.Transition(attempt, RunPhase.StreamingTurn, now.AddSeconds(11));
        RunAttemptStateMachine.Transition(attempt, RunPhase.Finishing, now.AddSeconds(13));
        RunAttemptStateMachine.Transition(attempt, RunPhase.Succeeded, now.AddSeconds(17));

        var durations = JsonSerializer.Deserialize<Dictionary<string, double>>(attempt.PhaseDurationsJson)!;
        durations[nameof(RunPhase.PreparingWorkspace)].ShouldBe(2_000, tolerance: 1);
        durations[nameof(RunPhase.BuildingPrompt)].ShouldBe(3_000, tolerance: 1);
        durations[nameof(RunPhase.Finishing)].ShouldBe(4_000, tolerance: 1);
        attempt.CompletedAt.ShouldBe(now.AddSeconds(17));
    }

    private static RunAttempt NewAttempt(RunPhase phase, DateTime startedAt) => new()
    {
        Id = Guid.NewGuid(),
        CardId = Guid.NewGuid(),
        AttemptNumber = 1,
        Phase = phase,
        CreatedAt = startedAt,
        StartedAt = startedAt,
        LastEventAt = startedAt,
        PhaseStartedAt = startedAt,
        Prompt = "test"
    };
}
