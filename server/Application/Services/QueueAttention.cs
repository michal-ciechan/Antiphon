using System.Linq.Expressions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0501 re-review R2: ONE definition of "this queue row is not at rest yet", shared by every
/// caller that asks "what needs recovery / discovery / reconciliation".
///
/// <para>The defect this replaces was not one bad clause, it was a shape: each caller wrote its own
/// partial filter, and the partial filters disagreed. The automatic sweep
/// (<c>FlushStrandedQueuesAsync</c>) excluded rows at the attempts cap on EVERY arm, because parking
/// means "no automatic retry". But the flush path's own interrupted-run selector
/// (<c>LoadInterruptedSentRunAsync</c>) reads Status/verdict/age ONLY, with no cap at all — and that
/// is deliberate, because a crashed <c>Sent</c> row at the cap still has work to do before it rests:
/// it must be late-confirmed if its body landed, and reverted to the visible parked shape if it did
/// not. So a session whose ONLY row was a capped crashed <c>Sent</c> row was never brought into the
/// sweep's scope, and the row sat <c>Sent</c> forever — invisible to
/// <see cref="ParkedMessageSweepService"/> too, which only discards <c>Pending</c> rows at the cap.
/// Nothing but a direct flush from some unrelated event could ever settle it.</para>
///
/// <para>The invariant these expressions state, once: <b>DISCOVERY MUST BE A SUPERSET OF ACTION.</b>
/// If <see cref="NeedsAttention"/> excludes a row that <see cref="InterruptedSent"/> or
/// <see cref="DeliverablePending"/> would act on, that row is stranded by construction. "Parked"
/// is a RESTING state a row must first be brought TO; it is not a licence to stop looking at rows
/// that have not reached it. Hence the asymmetry that looks wrong and is not: the cap gates the
/// Pending arms (a parked row is at rest — a human owns it) and does NOT gate the interrupted-Sent
/// arm (a crashed row is mid-flight — nobody owns it).</para>
///
/// <para>Whether a discovered row is actually typed is decided downstream, by the flush path's own
/// gates (the attempts cap, the F1 composer hold, the generation gate, the working/Starting
/// guards). Widening discovery therefore charges no attempt and types nothing on its own — a capped
/// row that the sweep now reaches is reverted to <c>Pending</c> at the cap and then, being at rest,
/// stops being discovered at all. One extra pass, then silence.</para>
///
/// <para>All members are <see cref="Expression"/>s so the same predicate runs in the database on a
/// discovery query and in memory on a loaded run — the two cannot drift into disagreement, which is
/// the whole point.</para>
/// </summary>
internal static class QueueAttention
{
    /// <summary>
    /// A run that was typed and whose fate this process never learned: <c>Sent</c>, no verdict, old
    /// enough that the confirm loop would have concluded by now, and inside the bounded window past
    /// which re-pressing Enter is no longer safe. Mirrors <c>LoadInterruptedSentRunAsync</c>'s
    /// selector exactly, INCLUDING its deliberate absence of an attempts-cap clause.
    /// </summary>
    public static Expression<Func<SessionQueuedMessage, bool>> InterruptedSent(
        DateTime ageFloor, DateTime windowFloor) =>
        m => m.Status == QueuedMessageStatus.Sent
            && m.DeliveryVerdict == null
            && m.LastDeliveryStartedAt != null
            && m.LastDeliveryStartedAt <= ageFloor
            && m.LastDeliveryStartedAt >= windowFloor;

    /// <summary>
    /// A <c>Pending</c> row an automatic path may still type: below the durable attempts cap, and
    /// either aged past the stranded cutoff or carrying the CARD-0342 <c>NoSubmitOutput</c> verdict
    /// inside the interrupted window (which does not wait for <c>CreatedAt</c> to age).
    /// </summary>
    public static Expression<Func<SessionQueuedMessage, bool>> DeliverablePending(
        int maxAttempts, DateTime strandedCutoff, DateTime windowFloor) =>
        m => m.Status == QueuedMessageStatus.Pending
            && m.DeliveryAttempts < maxAttempts
            && (m.CreatedAt <= strandedCutoff
                || (m.DeliveryVerdict == DeliveryVerdict.NoSubmitOutput
                    && m.LastDeliveryStartedAt != null
                    && m.LastDeliveryStartedAt >= windowFloor));

    /// <summary>
    /// The canonical union: every row shape that still has a state transition owed to it. This is
    /// what a discovery query must select on; anything narrower strands the difference.
    /// </summary>
    public static Expression<Func<SessionQueuedMessage, bool>> NeedsAttention(
        int maxAttempts, DateTime strandedCutoff, DateTime ageFloor, DateTime windowFloor) =>
        Or(DeliverablePending(maxAttempts, strandedCutoff, windowFloor),
            InterruptedSent(ageFloor, windowFloor));

    /// <summary>
    /// <see cref="NeedsAttention"/> narrowed to rows no human is watching — delegation and
    /// supervision briefs. A human-origin row on a non-always-on session stays visible in the queue
    /// for its owner to resend rather than being re-typed under them (CARD-0003).
    /// </summary>
    public static Expression<Func<SessionQueuedMessage, bool>> MachineOriginNeedsAttention(
        int maxAttempts, DateTime strandedCutoff, DateTime ageFloor, DateTime windowFloor) =>
        And(m => m.Origin == QueuedMessageOrigin.Delegation
                || m.Origin == QueuedMessageOrigin.Supervision
                || m.Origin == QueuedMessageOrigin.Mention,
            NeedsAttention(maxAttempts, strandedCutoff, ageFloor, windowFloor));

    private static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    private static Expression<Func<T, bool>> And<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> join)
    {
        var parameter = Expression.Parameter(typeof(T), "m");
        var body = join(
            new Rebind(left.Parameters[0], parameter).Visit(left.Body)!,
            new Rebind(right.Parameters[0], parameter).Visit(right.Body)!);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
