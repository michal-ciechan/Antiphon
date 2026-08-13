using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.StateMachine;

public static class CardStateMachine
{
    private static readonly Dictionary<CardStatus, CardStatus[]> Transitions = new()
    {
        // Backlog -> Done is deliberate, added 2026-08-13. It was previously forbidden to stop a
        // card skipping the work, but that reading assumed every Done card was done BY this card.
        // Two ordinary closes do not fit it: a card that is no longer wanted, and one already
        // fixed as part of another card's work. Forcing those through InProgress -> Review invents
        // a history that never happened, and the alternative — Canceled — is wrong for the second
        // case, because the work DID happen. Closing a card is not the same as doing it, and the
        // reason is what tells them apart: see MoveCardRequest.TerminalReason.
        [CardStatus.Backlog] = [CardStatus.InProgress, CardStatus.Blocked, CardStatus.Done, CardStatus.Canceled],
        [CardStatus.InProgress] = [CardStatus.Review, CardStatus.Blocked, CardStatus.Canceled],
        [CardStatus.Review] = [CardStatus.InProgress, CardStatus.Done, CardStatus.Blocked, CardStatus.Canceled],
        [CardStatus.Blocked] = [CardStatus.Backlog, CardStatus.InProgress, CardStatus.Canceled],
        [CardStatus.Done] = [],
        [CardStatus.Canceled] = [],
    };

    public static bool CanTransition(CardStatus from, CardStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<CardStatus> GetAvailableTransitions(CardStatus currentStatus) =>
        Transitions.TryGetValue(currentStatus, out var targets) ? targets : [];
}
