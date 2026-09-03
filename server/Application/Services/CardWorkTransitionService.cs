using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Moves cards from the delegated work bound to them (CARD-0040): Backlog → In Progress when a task
/// dispatches, In Progress → Review when the last open task settles Succeeded.
/// </summary>
/// <remarks>
/// A SWEEP rather than a hook at the dispatch and settle sites, for three reasons. Every input is a
/// durable row (<c>AgentTasks</c>, <c>CardRevisions</c>, <c>Cards</c>) so a sweep is exact rather
/// than approximate — nothing here polls a runner or reads a transcript. The CARD-0040 backfill and
/// every server outage are then handled by the same code with no replay logic. And a second writer
/// beside the settle site is the shape that produced CARD-0056's flap counter.
///
/// <para><b>The edge trigger is load-bearing.</b> The sweep acts only when the evidence event is
/// NEWER than the card's last word — its latest Move/Reopen revision, or <c>UpdatedAt</c> for a card
/// with no history. That is what stops it fighting a human who moved a card back (the human's move
/// is the newer fact, and the next dispatch is newer still), and it is also what makes the sweep
/// idempotent: its own Move row becomes the new last word, so a second pass over unchanged rows
/// writes nothing.</para>
///
/// <para>It never reads session or transcript liveness. Every "strand" gotcha in AGENTS.md
/// (CARD-0041/0055/0056) is about working/idle read from a transcript; no rule here touches it.</para>
/// </remarks>
public sealed class CardWorkTransitionService
{
    /// <summary>A task is OPEN while somebody (or something) is still on it. Queued is not open —
    /// nothing has started, and dispatch is the signal the card's thesis rests on.</summary>
    private static readonly AgentTaskStatus[] OpenStatuses =
        [AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    private readonly AppDbContext _db;
    private readonly CardService _cards;
    private readonly CardWorkTransitionSettings _settings;
    private readonly ILogger<CardWorkTransitionService> _logger;

    public CardWorkTransitionService(
        AppDbContext db,
        CardService cards,
        IOptions<CardWorkTransitionSettings> settings,
        ILogger<CardWorkTransitionService> logger)
    {
        _db = db;
        _cards = cards;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>What the evidence says should happen to one card.</summary>
    private sealed record Decision(CardStatus Target, DateTime EvidenceAt, string Reason);

    /// <summary>
    /// One pass. Public and cancellable so tests drive it directly rather than waiting on a timer.
    /// A failure on one card is logged and the sweep continues — the same contract
    /// <see cref="SessionReconciliationService"/> keeps.
    /// </summary>
    /// <returns>How many cards were actually moved.</returns>
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        // Cards with no bound task are never loaded: the join IS the candidate set.
        var candidates = await _db.Cards.AsNoTracking()
            .Where(c => c.ArchivedAt == null
                && c.OwnerSessionId == null
                && (c.Status == CardStatus.Backlog
                    || c.Status == CardStatus.InProgress
                    || c.Status == CardStatus.Review)
                && _db.AgentTasks.Where(AgentTaskRoles.NotSpecialist).Any(t => t.CardId == c.Id))
            .Select(c => new { c.Id, c.Identifier, c.Status, c.UpdatedAt })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var cardIds = candidates.Select(c => c.Id).ToList();

        var tasks = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId != null && cardIds.Contains(t.CardId!.Value))
            .Where(AgentTaskRoles.NotSpecialist)
            .Select(t => new
            {
                t.Id,
                CardId = t.CardId!.Value,
                t.Role,
                t.ModelLevel,
                t.Status,
                t.DispatchedAt,
                t.CompletedAt,
            })
            .ToListAsync(ct);
        var tasksByCard = tasks.GroupBy(t => t.CardId).ToDictionary(g => g.Key, g => g.ToList());

        // "The human's last word": the newest Move or Reopen row. Content edits are deliberately
        // NOT counted here — the fallback for a card with no history is UpdatedAt, which a content
        // edit does bump, and that errs in the safe direction (a card someone touched after the
        // evidence keeps its column).
        var lastWords = (await _db.CardRevisions.AsNoTracking()
                .Where(r => cardIds.Contains(r.CardId)
                    && (r.Kind == CardRevisionKind.Move || r.Kind == CardRevisionKind.Reopen))
                .Select(r => new { r.CardId, r.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(r => r.CardId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.CreatedAt));

        var moved = 0;
        foreach (var card in candidates)
        {
            if (!tasksByCard.TryGetValue(card.Id, out var cardTasks))
                continue;

            var decision = Decide(
                cardTasks.Select(t => new Evidence(
                    t.Id, t.Role, t.ModelLevel, t.Status, t.DispatchedAt, t.CompletedAt)).ToList());
            if (decision is null || decision.Target == card.Status)
                continue;

            var lastWord = lastWords.TryGetValue(card.Id, out var stamp) ? stamp : card.UpdatedAt;
            if (decision.EvidenceAt <= lastWord)
            {
                _logger.LogDebug(
                    "Card {Identifier}: evidence at {EvidenceAt:o} is not newer than its last word at "
                    + "{LastWord:o}, so the automated move to {Target} was skipped.",
                    card.Identifier, decision.EvidenceAt, lastWord, decision.Target);
                continue;
            }

            try
            {
                if (!await _cards.ApplyAutomatedMoveAsync(
                        card.Id, decision.Target, decision.Reason, CardService.TransitionActor, ct))
                {
                    continue;
                }

                moved++;
                _logger.LogInformation(
                    "Card {Identifier} moved {From} -> {To} by {Actor}: {Reason}",
                    card.Identifier, card.Status, decision.Target, CardService.TransitionActor, decision.Reason);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Automated move of card {Identifier} to {Target} failed", card.Identifier, decision.Target);
            }
        }

        return moved;
    }

    private sealed record Evidence(
        Guid TaskId,
        AgentTaskRole Role,
        AgentModelLevel ModelLevel,
        AgentTaskStatus Status,
        DateTime? DispatchedAt,
        DateTime? CompletedAt);

    /// <summary>
    /// The rule, exactly. An OPEN bound task means the card is being worked. Otherwise the NEWEST
    /// event decides: a Succeeded settle sends it to Review, a Failed or Canceled one sends it
    /// nowhere — a failed attempt is sometimes simply wrong (CARD-0085), <c>RecentFailure</c>
    /// already covers the first 24 h, and the stale attention row is the long-run backstop.
    /// </summary>
    private static Decision? Decide(IReadOnlyList<Evidence> tasks)
    {
        // The NEWEST open dispatch, not the first. With one task they are the same; with two they
        // differ exactly when a human moved the card between them, and there the later dispatch is
        // the fact that should win.
        var newestOpen = tasks
            .Where(t => OpenStatuses.Contains(t.Status) && t.DispatchedAt is not null)
            .OrderByDescending(t => t.DispatchedAt!.Value)
            .FirstOrDefault();
        if (newestOpen is not null)
        {
            return new Decision(
                CardStatus.InProgress,
                newestOpen.DispatchedAt!.Value,
                $"Task {DelegationReportFormatter.Short(newestOpen.TaskId)} "
                + $"({newestOpen.Role}, {newestOpen.ModelLevel}) dispatched against this card.");
        }

        var newestSettle = tasks
            .Where(t => AgentTaskService.IsSettled(t.Status) && t.CompletedAt is not null)
            .OrderByDescending(t => t.CompletedAt!.Value)
            .FirstOrDefault();
        if (newestSettle is null || newestSettle.Status != AgentTaskStatus.Succeeded)
            return null;

        return new Decision(
            CardStatus.Review,
            newestSettle.CompletedAt!.Value,
            $"Task {DelegationReportFormatter.Short(newestSettle.TaskId)} ({newestSettle.Role}) settled "
            + "Succeeded; no other task is open against this card.");
    }
}
