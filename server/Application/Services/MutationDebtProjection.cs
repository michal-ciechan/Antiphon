using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0552 D-4/D-5. The one rule that decides which confirmed publications still owe a Mutation
/// battery, shared by the pipeline glance (which renders the rows) and the auto-dispatch sweep
/// (which creates one task from the oldest). Keeping it in one place is the point: a glance and a
/// sweep that disagreed about what "ready" means would dispatch work nobody can see, or hide work
/// that is running.
/// </summary>
/// <remarks>
/// Pure and in-memory. The caller loads the rows and has already applied
/// <see cref="AgentTaskLandingState.HasPublication"/> to the landings it passes in.
/// </remarks>
internal static class MutationDebtProjection
{
    internal sealed record LandingRow(Guid Id, Guid TaskId, Guid VerificationCardId, DateTime RemoteConfirmedAt,
        string OriginalSourceSha, string VerifiedSourceSha, string ObservedRemoteTargetSha,
        LandPublicationOutcome Publication);

    internal sealed record SourcedRow(Guid Id, Guid SourceLandingOperationId, Guid? CardId, AgentTaskRole Role,
        AgentTaskStatus Status, DateTime CreatedAt, DateTime? DispatchedAt, DateTime? CompletedAt);

    internal sealed record CardRow(Guid Id, string Identifier, string Title, CardStatus Status, DateTime? ArchivedAt);

    /// <param name="AnyAttempt">
    /// True when ANY sourced task in any status names this operation. The glance still shows the
    /// row (D-5: a Failed or Canceled battery is debt the orchestrator must see), but the sweep
    /// never auto-retries one — CARD-0478 D-5 requires an evidence and restoration assessment
    /// before a new same-O task, and that is an explicit act.
    /// </param>
    internal sealed record Debt(CardRow Companion, LandingRow Source, bool AnyAttempt);

    /// <summary>
    /// A sourced attempt that consumes the row: open, or Succeeded. Failed and Canceled do not —
    /// an incomplete battery "is never clean" (CARD-0478 D-4) and the debt must return.
    /// </summary>
    private static bool Consumes(AgentTaskStatus status) => status
        is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched or AgentTaskStatus.Working
        or AgentTaskStatus.Blocked or AgentTaskStatus.Succeeded;

    private static bool IsOpen(AgentTaskStatus status) => status
        is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched or AgentTaskStatus.Working
        or AgentTaskStatus.Blocked;

    internal static IReadOnlyList<Debt> Build(
        IReadOnlyList<LandingRow> landings,
        IReadOnlyList<SourcedRow> sourced,
        IReadOnlyList<AgentTaskPipelineStatusService.TaskRow> boundStages,
        IReadOnlyDictionary<Guid, CardRow> cards)
    {
        var debts = new List<Debt>();
        foreach (var group in landings.GroupBy(l => l.VerificationCardId))
        {
            // Rule 1: the same card-state skip BuildReady applies, so a closed, canceled,
            // undecided or archived companion is never a row.
            if (!cards.TryGetValue(group.Key, out var companion)) continue;
            if (companion.ArchivedAt is not null) continue;
            if (companion.Status is CardStatus.Done or CardStatus.Canceled or CardStatus.NeedsDecision) continue;

            // Rule 2: the newest confirmed operation is the source; consumption is judged on it.
            var source = group
                .OrderByDescending(l => l.RemoteConfirmedAt).ThenByDescending(l => l.Id)
                .First();

            // Rule 3: an open or Succeeded sourced task on THIS operation consumes the row, and so
            // does an open unsourced Mutation task bound to the companion (an explicit dispatch
            // must not be doubled while it runs).
            var attempts = sourced.Where(s => s.SourceLandingOperationId == source.Id).ToList();
            if (attempts.Any(s => Consumes(s.Status))) continue;
            if (boundStages.Any(t => t.CardId == companion.Id
                && t.Role == AgentTaskRole.Mutation
                && IsOpen(t.Status)))
            {
                continue;
            }

            debts.Add(new Debt(companion, source, attempts.Count > 0));
        }

        return debts
            .OrderBy(d => d.Source.RemoteConfirmedAt)
            .ThenBy(d => d.Companion.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
