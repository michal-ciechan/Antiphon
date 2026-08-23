using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0113: sequence 0 is a real first-turn floor, so a failed fetch is not synonymous with it.
/// </summary>
public class TranscriptTurnBaselineTests
{
    [Test]
    public async Task A_successful_read_wins_even_when_its_LastSequence_is_zero()
    {
        TranscriptTurnBaseline.Resolve(fetchedLastSequence: 0, lastKnownSequence: 12).ShouldBe(0);
        TranscriptTurnBaseline.PreservedLastKnown(0, 12).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task A_failed_read_preserves_the_last_known_sequence()
    {
        TranscriptTurnBaseline.Resolve(fetchedLastSequence: null, lastKnownSequence: 12).ShouldBe(12);
        TranscriptTurnBaseline.PreservedLastKnown(null, 12).ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task A_failed_read_with_no_prior_observation_is_the_first_turn_floor()
    {
        TranscriptTurnBaseline.Resolve(fetchedLastSequence: null, lastKnownSequence: null).ShouldBe(0);
        TranscriptTurnBaseline.PreservedLastKnown(null, null).ShouldBeFalse();
        await Task.CompletedTask;
    }
}
