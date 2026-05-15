using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Domain.StateMachine;

[Category("Unit")]
public class CardStateMachineTests
{
    [Test]
    public void Card_state_machine_rejects_backlog_to_done_direct_transition()
    {
        CardStateMachine.CanTransition(CardStatus.Backlog, CardStatus.Done)
            .ShouldBeFalse();
    }

    [Test]
    public void CardStateMachine_legal_transitions_match_spec()
    {
        CardStateMachine.GetAvailableTransitions(CardStatus.Backlog)
            .ShouldBe([CardStatus.InProgress, CardStatus.Blocked, CardStatus.Canceled]);

        CardStateMachine.GetAvailableTransitions(CardStatus.InProgress)
            .ShouldBe([CardStatus.Review, CardStatus.Blocked, CardStatus.Canceled]);

        CardStateMachine.GetAvailableTransitions(CardStatus.Review)
            .ShouldBe([CardStatus.InProgress, CardStatus.Done, CardStatus.Blocked, CardStatus.Canceled]);
    }

    [Test]
    public void CardStateMachine_terminal_states_are_immutable()
    {
        foreach (var terminalStatus in new[] { CardStatus.Done, CardStatus.Canceled })
        {
            CardStateMachine.GetAvailableTransitions(terminalStatus).ShouldBeEmpty();
            foreach (var target in Enum.GetValues<CardStatus>())
            {
                CardStateMachine.CanTransition(terminalStatus, target)
                    .ShouldBeFalse($"{terminalStatus} should not transition to {target}");
            }
        }
    }
}
