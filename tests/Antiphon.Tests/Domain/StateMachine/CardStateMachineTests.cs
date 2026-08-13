using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Domain.StateMachine;

[Category("Unit")]
public class CardStateMachineTests
{
    /// <summary>
    /// Backlog -> Done is ALLOWED (changed 2026-08-13). It was forbidden on the reading that a
    /// card must not skip its own work, but two ordinary closes do not fit that: a card nobody
    /// wants any more, and a card already fixed as part of another card. Routing those through
    /// InProgress -> Review invents a history that never happened — six cards were walked that way
    /// by hand on 2026-08-13 — and Canceled is wrong for the second, because the work DID happen.
    /// What separates a close from a completion is the reason, not the path.
    /// </summary>
    [Test]
    public void Card_can_be_closed_straight_from_backlog()
    {
        CardStateMachine.CanTransition(CardStatus.Backlog, CardStatus.Done)
            .ShouldBeTrue("a card can be closed without being worked — no longer wanted, or "
                          + "already fixed as part of another card");
    }

    [Test]
    public void CardStateMachine_legal_transitions_match_spec()
    {
        CardStateMachine.GetAvailableTransitions(CardStatus.Backlog)
            .ShouldBe([CardStatus.InProgress, CardStatus.Blocked, CardStatus.Done, CardStatus.Canceled]);

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
