using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.StateMachine;

public static class CardStateMachine
{
    // Any live state reaches any other DIRECTLY. Widened 2026-08-13, and the reason is concrete
    // rather than a preference for permissiveness.
    //
    // A transition is not free: moving a card into an ACTIVE column spawns an agent
    // (CardService.ApplyColumnMove). So a state machine that forces a path makes the caller pay
    // for every state on the way, including ones it never wanted to be in. Six already-finished
    // cards were moved to Done for bookkeeping on 2026-08-13; the only legal route ran through
    // InProgress, and it launched six agent sessions, six worktrees and six branches. They failed,
    // so nothing ran away — but the operation's whole intent was tidying a board.
    //
    // The old table encoded a PROCESS ("work flows Backlog -> InProgress -> Review -> Done").
    // That is a good description of how work usually happens and a bad rule for how a record may
    // be corrected. Cards get filed in the wrong state, finished as part of another card,
    // abandoned, un-abandoned, or pulled straight into review because the work already existed.
    // Forcing those through intermediate states does not make the history truer — it invents a
    // history that never happened AND triggers side effects for it.
    //
    // What separates a legitimate move from a mistake is the REASON, not the path: see
    // MoveCardRequest.Reason.
    private static readonly CardStatus[] AnyLiveState =
    [
        CardStatus.Backlog, CardStatus.InProgress, CardStatus.Review,
        CardStatus.Blocked, CardStatus.Done, CardStatus.Canceled,
    ];

    private static readonly Dictionary<CardStatus, CardStatus[]> Transitions = new()
    {
        [CardStatus.Backlog] = Without(CardStatus.Backlog),
        [CardStatus.InProgress] = Without(CardStatus.InProgress),
        [CardStatus.Review] = Without(CardStatus.Review),
        [CardStatus.Blocked] = Without(CardStatus.Blocked),
        // Terminal states stay terminal. Reopening a closed card is a different question from
        // reaching a state directly, and it is not answered here: CompletedAt, TerminalReason and
        // the review checkpoint captured on completion all mean something once set. Reopen belongs
        // with CARD-0019's card history, where undoing a close can be recorded as a correction
        // rather than by quietly rewriting the row.
        [CardStatus.Done] = [],
        [CardStatus.Canceled] = [],
    };

    private static CardStatus[] Without(CardStatus self) =>
        AnyLiveState.Where(s => s != self).ToArray();

    public static bool CanTransition(CardStatus from, CardStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<CardStatus> GetAvailableTransitions(CardStatus currentStatus) =>
        Transitions.TryGetValue(currentStatus, out var targets) ? targets : [];
}
