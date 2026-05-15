using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.StateMachine;

public static class CardStateMachine
{
    private static readonly Dictionary<CardStatus, CardStatus[]> Transitions = new()
    {
        [CardStatus.Backlog] = [CardStatus.InProgress, CardStatus.Blocked, CardStatus.Canceled],
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
