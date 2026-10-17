using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1320 S2. The Investigate backlog candidate rules, shared by the pipeline snapshot
/// and keep-six. Callers take their own slice: the snapshot keeps five, keep-six filters
/// to one board before it takes five. This read is uncapped.
/// </summary>
public static class PipelineBacklogSelection
{
    public const string PostLandVerificationLabel = "post-land-verification";

    public sealed record Row(
        Guid Id,
        Guid BoardId,
        string Identifier,
        string Title,
        CardImportance Importance,
        CardUrgency Urgency,
        DateTime? DueAt,
        int? Position,
        DateTime CreatedAt,
        string LabelsJson);

    public sealed record Selection(
        IReadOnlyList<Row> Candidates,
        IReadOnlyList<Row> Held,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> OpenBlockers,
        IReadOnlyCollection<Guid> BoardIds);

    public static async Task<Selection> SelectAsync(
        AppDbContext db, PipelineScope scope, DateTime asOf, CancellationToken ct)
    {
        // This is a narrow, uncapped fleet read. The card-list route caps by UpdatedAt and can
        // omit the highest-ranked work. Stage history is already materialized for this snapshot.
        var backlogCards = await db.Cards.AsNoTracking()
            .Where(c => c.Status == CardStatus.Backlog && c.ArchivedAt == null
                && c.Board.ArchivedAt == null && !c.BoardColumn.IsTerminal
                && c.OwnerSessionId == null)
            .Select(c => new Row(c.Id, c.BoardId, c.Identifier, c.Title,
                c.Importance, c.Urgency, c.DueAt, c.Position, c.CreatedAt, c.LabelsJson))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var boardIds = backlogCards.Select(c => c.BoardId).Distinct().ToList();
        var boardProjects = boardIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Boards.AsNoTracking()
                .Where(b => boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.ProjectId, ct)
                .ConfigureAwait(false);
        var startedCardIds = await LoadStartedCardIdsAsync(db, ct).ConfigureAwait(false);
        var unstarted = backlogCards
            .Where(c => CardInScope(scope, c.BoardId, boardProjects)
                && !startedCardIds.Contains(c.Id)
                && !BoardService.ParseLabels(c.LabelsJson).Contains(
                    PostLandVerificationLabel, StringComparer.Ordinal))
            .OrderBy(c => CardRanking.OrderKey(c.Importance, c.Urgency, c.DueAt,
                c.Position, c.CreatedAt, asOf))
            .ThenBy(c => c.BoardId)
            .ThenBy(c => c.Id)
            .ToList();
        // CARD-0100: a card with an open blocker is held, not a candidate; the snapshot lists it.
        var openBlockers = await LoadOpenBlockersAsync(db, unstarted.Select(c => c.Id).ToList(), ct)
            .ConfigureAwait(false);
        return new Selection(
            unstarted.Where(c => !openBlockers.ContainsKey(c.Id)).ToList(),
            unstarted.Where(c => openBlockers.ContainsKey(c.Id)).ToList(),
            openBlockers,
            boardIds);
    }

    /// <summary>
    /// A card already bound to a Queued, Dispatched, Working, Blocked, or Succeeded stage task.
    /// Failed and Canceled stage tasks, and non-stage roles, leave the card dispatchable.
    /// </summary>
    private static async Task<HashSet<Guid>> LoadStartedCardIdsAsync(AppDbContext db, CancellationToken ct)
    {
        var ids = await db.AgentTasks.AsNoTracking()
            .Where(task => task.CardId != null)
            .Where(AgentTaskRoles.Stage)
            .Where(task => task.Status == AgentTaskStatus.Queued
                || task.Status == AgentTaskStatus.Dispatched
                || task.Status == AgentTaskStatus.Working
                || task.Status == AgentTaskStatus.Blocked
                || task.Status == AgentTaskStatus.Succeeded)
            .Select(task => task.CardId!.Value)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ids.ToHashSet();
    }

    private static bool CardInScope(
        PipelineScope scope, Guid boardId, IReadOnlyDictionary<Guid, Guid> boardProjects)
    {
        if (scope.Kind == PipelineScopeKind.Fleet)
            return true;
        return boardProjects.TryGetValue(boardId, out var project)
            && (scope.Kind == PipelineScopeKind.Project
                ? project == scope.ProjectId
                : false);
    }

    /// <summary>
    /// CARD-0100. The open blockers of each card, by identifier (ordinal order), through the shared
    /// <see cref="CardBlockers.IsOpenBlocker"/> predicate. Cards with none are absent.
    /// </summary>
    private static async Task<Dictionary<Guid, IReadOnlyList<string>>> LoadOpenBlockersAsync(
        AppDbContext db, IReadOnlyCollection<Guid> cardIds, CancellationToken ct)
    {
        if (cardIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<string>>();
        var rows = await db.CardRelationships.AsNoTracking()
            .Where(r => cardIds.Contains(r.CardId))
            .Where(CardBlockers.IsOpenBlocker)
            .Select(r => new { r.CardId, r.RelatedCard.Identifier })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.GroupBy(r => r.CardId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<string>)g.Select(r => r.Identifier).Order(StringComparer.Ordinal).ToList());
    }
}
