using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Deleting a board is really deleting a subtree — columns, cards, and everything hanging off the
/// cards (sessions, run attempts, worktrees, workflow runs). The database will not do it for us:
/// <c>Card -&gt; BoardColumn</c> is Restrict, so a DB-level cascade from <c>Boards</c> can delete a
/// column while a card still points at it, and <c>Board -&gt; Project</c> is Restrict too, which is
/// what made <c>DELETE /api/projects/{id}</c> answer 500 (GitHub issue #2).
///
/// So the order is explicit here, once, and both <see cref="ProjectService"/> and
/// <see cref="BoardService"/> go through it.
/// </summary>
public static class ProjectCascade
{
    /// <summary>Card states that no longer represent outstanding work.</summary>
    private static readonly CardStatus[] SettledStatuses = [CardStatus.Done, CardStatus.Canceled];

    /// <summary>
    /// Counts what deleting <paramref name="projectId"/> would destroy or detach. Read-only —
    /// this is what the confirmation dialog shows before anything happens.
    /// </summary>
    public static async Task<ProjectImpactCounts> MeasureAsync(
        AppDbContext db, Guid projectId, CancellationToken ct)
    {
        var boardIds = await db.Boards
            .Where(b => b.ProjectId == projectId)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var cards = db.Cards.Where(c => boardIds.Contains(c.BoardId));
        var cardCount = await cards.CountAsync(ct);
        var openCardCount = await cards.CountAsync(c => !SettledStatuses.Contains(c.Status), ct);

        var cardIds = await cards.Select(c => c.Id).ToListAsync(ct);
        var runningSessionCount = await db.AgentSessions
            .CountAsync(
                s => s.CardId != null
                    && cardIds.Contains(s.CardId.Value)
                    && (s.Status == SessionStatus.Running || s.Status == SessionStatus.Starting),
                ct);

        // Agents survive the delete — they are only unpinned from the board/card. Counted so the
        // dialog can say so rather than leaving the user to guess.
        var agentCount = await db.Agents
            .CountAsync(
                a => (a.BoardId != null && boardIds.Contains(a.BoardId.Value))
                    || (a.CurrentCardId != null && cardIds.Contains(a.CurrentCardId.Value)),
                ct);

        var workflowCount = await db.Workflows.CountAsync(w => w.ProjectId == projectId, ct);

        return new ProjectImpactCounts(
            boardIds.Count, cardCount, openCardCount, runningSessionCount, agentCount, workflowCount);
    }

    /// <summary>
    /// Deletes the given boards and everything beneath them, leaf-first. Agents are detached, not
    /// deleted. The caller owns the transaction — every step here is a separate statement.
    /// </summary>
    public static async Task DeleteBoardsAsync(
        AppDbContext db, IReadOnlyList<Guid> boardIds, CancellationToken ct)
    {
        if (boardIds.Count == 0)
            return;

        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync(ct);
        var runIds = await db.CardWorkflowRuns
            .Where(r => cardIds.Contains(r.CardId))
            .Select(r => r.Id)
            .ToListAsync(ct);
        var attemptIds = await db.RunAttempts
            .Where(a => cardIds.Contains(a.CardId))
            .Select(a => a.Id)
            .ToListAsync(ct);

        // Break the cycles first: a card points at the session/worktree/run that points back at it,
        // so nothing can be deleted until these are nulled.
        if (cardIds.Count > 0)
        {
            await db.Cards
                .Where(c => cardIds.Contains(c.Id))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                    .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                    .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                    .SetProperty(c => c.AgentQueuePosition, (int?)null)
                    .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null), ct);
        }

        if (runIds.Count > 0)
        {
            await db.CardWorkflowRuns
                .Where(r => runIds.Contains(r.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.CurrentStageId, (Guid?)null), ct);
        }

        // Agents outlive their board — unpin, never delete.
        await db.Agents
            .Where(a => (a.BoardId != null && boardIds.Contains(a.BoardId.Value))
                || (a.CurrentCardId != null && cardIds.Contains(a.CurrentCardId.Value)))
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.BoardId, (Guid?)null)
                .SetProperty(a => a.CurrentCardId, (Guid?)null), ct);

        if (cardIds.Count > 0)
        {
            await db.CardWorkflowStages.Where(s => runIds.Contains(s.CardWorkflowRunId)).ExecuteDeleteAsync(ct);
            await db.CardWorkflowRuns.Where(r => runIds.Contains(r.Id)).ExecuteDeleteAsync(ct);
            await db.TokenUsages.Where(t => attemptIds.Contains(t.RunAttemptId)).ExecuteDeleteAsync(ct);
            await db.RunAttempts.Where(a => attemptIds.Contains(a.Id)).ExecuteDeleteAsync(ct);
            await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync(ct);
            await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync(ct);
            // Transcript entries and queued messages have a real ON DELETE CASCADE from the
            // session, so the database clears them as these rows go.
            await db.AgentSessions
                .Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value))
                .ExecuteDeleteAsync(ct);
            await db.Worktrees.Where(w => cardIds.Contains(w.CardId)).ExecuteDeleteAsync(ct);
            await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync(ct);
        }

        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync(ct);
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync(ct);
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Raw counts behind a deletion-impact report.</summary>
public record ProjectImpactCounts(
    int BoardCount,
    int CardCount,
    int OpenCardCount,
    int RunningSessionCount,
    int AgentCount,
    int WorkflowCount);
