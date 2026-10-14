using System.Linq.Expressions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0100 D-5 / A-3. Whether a blocker still holds its card. Resolved is a whitelist (archived,
/// Done, Canceled); any other status, including one this code does not know, keeps the blocker open
/// (CardRelationshipApiTests.Resolved_follows_the_blockers_Done_Canceled_archived_state_and_reopen).
/// <see cref="IsResolved"/>, <see cref="IsOpenBlocker"/> and <see cref="NotBlocked"/> are built from
/// the one <see cref="Resolved"/> expression, so the API's <c>resolved</c> flag and the orchestrator
/// pickup rule (OrchestratorService, AgentTaskPipelineStatusService, ExpectationSnapshotReader) read
/// the same definition.
/// </summary>
public static class CardBlockers
{
    private static readonly Expression<Func<CardStatus, DateTime?, bool>> Resolved =
        (status, archivedAt) => archivedAt != null || status == CardStatus.Done || status == CardStatus.Canceled;

    private static readonly Func<CardStatus, DateTime?, bool> ResolvedCompiled = Resolved.Compile();

    public static bool IsResolved(CardStatus status, DateTime? archivedAt) => ResolvedCompiled(status, archivedAt);

    /// <summary>A <c>BlockedBy</c> row whose blocker is not resolved.</summary>
    public static readonly Expression<Func<CardRelationship, bool>> IsOpenBlocker = BuildIsOpenBlocker();

    /// <summary>
    /// A card with no open <c>BlockedBy</c> row: the pickup filter for the tick, the pipeline backlog
    /// and the watchdog backlog. A card with no relationship rows passes.
    /// </summary>
    public static readonly Expression<Func<Card, bool>> NotBlocked = BuildNotBlocked();

    private static Expression<Func<CardRelationship, bool>> BuildIsOpenBlocker()
    {
        Expression<Func<CardRelationship, bool>> isBlockedBy = r => r.Kind == CardRelationshipKind.BlockedBy;
        var row = isBlockedBy.Parameters[0];
        var blocker = Expression.Property(row, nameof(CardRelationship.RelatedCard));
        var resolved = new ParameterReplacer(new Dictionary<ParameterExpression, Expression>
        {
            [Resolved.Parameters[0]] = Expression.Property(blocker, nameof(Card.Status)),
            [Resolved.Parameters[1]] = Expression.Property(blocker, nameof(Card.ArchivedAt)),
        }).Visit(Resolved.Body);
        return Expression.Lambda<Func<CardRelationship, bool>>(
            Expression.AndAlso(isBlockedBy.Body, Expression.Not(resolved)), row);
    }

    private static Expression<Func<Card, bool>> BuildNotBlocked()
    {
        var card = Expression.Parameter(typeof(Card), "c");
        var any = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Any), [typeof(CardRelationship)],
            Expression.Property(card, nameof(Card.BlockedBy)), IsOpenBlocker);
        return Expression.Lambda<Func<Card, bool>>(Expression.Not(any), card);
    }

    private sealed class ParameterReplacer(IReadOnlyDictionary<ParameterExpression, Expression> map)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            map.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
    }
}
