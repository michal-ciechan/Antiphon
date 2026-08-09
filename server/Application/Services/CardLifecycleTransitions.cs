using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>A card's removal from its agent's queue, with the cards whose positions shifted.</summary>
internal sealed record AgentQueueRemoval(Guid AgentId, Guid CardId, Guid BoardId, IReadOnlyList<Card> ShiftedCards);

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

    /// <summary>
    /// The agent queue reflects work REMAINING: a card that reaches Review or a terminal status
    /// (Done/Canceled) is finished from its agent's perspective and must leave the queue, with the
    /// same clearing + compaction semantics as the explicit queue-remove endpoint
    /// (AgentService.RemoveCardAsync). Every status-transition path must call this after moving a
    /// card — a finished card left at queue head makes AgentControlService.StartAsync re-spawn a
    /// session onto it on every agent restart (the CARD-0001 respawn loop). Mutates tracked
    /// entities only; the caller owns SaveChanges and publishes the returned removal afterwards
    /// via <see cref="PublishQueueRemovalAsync"/>. Returns null when the card isn't finished or
    /// isn't assigned to an agent.
    /// </summary>
    public static async Task<AgentQueueRemoval?> DequeueFinishedCardAsync(
        AppDbContext db, Card card, DateTime utcNow, CancellationToken ct)
    {
        if (card.Status is not (CardStatus.Review or CardStatus.Done or CardStatus.Canceled))
            return null;
        if (card.AssignedAgentId is not Guid agentId)
            return null;

        card.AssignedAgentId = null;
        card.AgentQueuePosition = null;
        card.ActiveWorkflowRunId = null;
        card.ActiveWorkflowRun = null;
        card.UpdatedAt = utcNow;
        card.ConcurrencyToken = Guid.NewGuid();

        // The SQL filter sees pre-save values: a card dequeued earlier in the same unit of work
        // still matches AssignedAgentId in the database, comes back as its tracked (already
        // unassigned) instance, and would be handed a queue position back. Re-check the tracked
        // values before compacting.
        var remaining = (await db.Cards
                .Where(c => c.AssignedAgentId == agentId && c.Id != card.Id)
                .OrderBy(c => c.AgentQueuePosition)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync(ct))
            .Where(c => c.AssignedAgentId == agentId)
            .ToList();
        var shifted = new List<Card>();
        for (var index = 0; index < remaining.Count; index++)
        {
            var queued = remaining[index];
            var position = index + 1;
            if (queued.AgentQueuePosition == position)
                continue;

            queued.AgentQueuePosition = position;
            queued.UpdatedAt = utcNow;
            queued.ConcurrencyToken = Guid.NewGuid();
            shifted.Add(queued);
        }

        return new AgentQueueRemoval(agentId, card.Id, card.BoardId, shifted);
    }

    /// <summary>Publishes the queue-change events for a dequeue, after the caller has saved.</summary>
    public static async Task PublishQueueRemovalAsync(
        IEventBus eventBus, AgentQueueRemoval removal, CancellationToken ct)
    {
        await eventBus.PublishToAllAsync(
            "AgentQueueChanged",
            new AgentQueueChangedEventDto(
                removal.AgentId,
                CardId: removal.CardId,
                CardIds: removal.ShiftedCards.Select(c => c.Id).ToList(),
                BoardId: removal.BoardId),
            ct);
        foreach (var card in removal.ShiftedCards)
            await eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
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
