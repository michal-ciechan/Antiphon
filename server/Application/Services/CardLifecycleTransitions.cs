using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;

namespace Antiphon.Server.Application.Services;

internal static class CardLifecycleTransitions
{
    public static bool TryMoveToReview(Card card, DateTime utcNow)
    {
        if (card.BoardColumn.IsTerminal)
            return false;

        var reviewColumn = card.Board.Columns
            .OrderBy(c => c.ColumnOrder)
            .FirstOrDefault(c => c.CardStatus == CardStatus.Review);
        if (reviewColumn is null || card.BoardColumnId == reviewColumn.Id)
            return false;

        if (card.Status != reviewColumn.CardStatus
            && !CardStateMachine.CanTransition(card.Status, reviewColumn.CardStatus))
        {
            return false;
        }

        card.BoardColumnId = reviewColumn.Id;
        card.BoardColumn = reviewColumn;
        card.Status = reviewColumn.CardStatus;
        card.StartedAt ??= utcNow;
        card.CompletedAt = null;
        card.TerminalReason = null;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = utcNow;

        return true;
    }

    public static bool LatestAttemptSucceeded(Card card)
    {
        var latest = card.RunAttempts
            .OrderByDescending(a => a.AttemptNumber)
            .ThenByDescending(a => a.CreatedAt)
            .FirstOrDefault();

        return latest?.Phase == RunPhase.Succeeded;
    }
}
