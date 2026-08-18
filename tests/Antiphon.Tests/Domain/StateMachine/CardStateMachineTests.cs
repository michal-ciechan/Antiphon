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

    /// <summary>
    /// Every live state reaches every other one DIRECTLY (widened 2026-08-13). The reason is not a
    /// preference for permissiveness: a transition has side effects — moving into an ACTIVE column
    /// spawns an agent — so forcing a path makes the caller pay for every state on the way. Moving
    /// six finished cards to Done for bookkeeping launched six agent sessions, six worktrees and
    /// six branches, purely because the only legal route ran through InProgress.
    /// </summary>
    [Test]
    [Arguments(CardStatus.Backlog)]
    [Arguments(CardStatus.InProgress)]
    [Arguments(CardStatus.Review)]
    [Arguments(CardStatus.Blocked)]
    public void Every_live_state_reaches_every_other_directly(CardStatus from)
    {
        foreach (var to in Enum.GetValues<CardStatus>())
        {
            if (to == from)
            {
                CardStateMachine.CanTransition(from, to)
                    .ShouldBeFalse($"{from} -> {from} is not a move");
                continue;
            }

            CardStateMachine.CanTransition(from, to)
                .ShouldBeTrue($"{from} -> {to} must not require passing through another state, "
                              + "because transiting a state has side effects");
        }
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

    [Test]
    [Arguments(CardStatus.Done, true)]
    [Arguments(CardStatus.Canceled, true)]
    [Arguments(CardStatus.Backlog, false)]
    [Arguments(CardStatus.InProgress, false)]
    [Arguments(CardStatus.Review, false)]
    [Arguments(CardStatus.Blocked, false)]
    public void CanReopenFrom_is_true_for_exactly_the_terminal_statuses(CardStatus from, bool expected)
    {
        CardStateMachine.CanReopenFrom(from).ShouldBe(expected);
    }
}
