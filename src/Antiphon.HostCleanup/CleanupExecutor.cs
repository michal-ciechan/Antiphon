namespace Antiphon.HostCleanup;

public sealed class CleanupExecutor
{
    private readonly CleanupPlanner planner;
    private readonly ICleanupFileSystem fileSystem;
    private readonly ICleanupPlanStore planStore;
    private readonly ICleanupClaimStore claimStore;
    private readonly CleanupLimits limits;

    public CleanupExecutor(CleanupPlanner planner, ICleanupFileSystem fileSystem,
        ICleanupPlanStore planStore, ICleanupClaimStore claimStore, CleanupLimits limits)
    {
        this.planner = planner;
        this.fileSystem = fileSystem;
        this.planStore = planStore;
        this.claimStore = claimStore;
        this.limits = limits;
    }

    public async ValueTask<CleanupReceipt> ExecuteAsync(CleanupPlan plan, bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (dryRun) return new CleanupReceipt(plan.RunId, []);
        // A failed persist aborts the entire run before any candidate claim or delete.
        await planStore.PersistAsync(plan, cancellationToken);
        var budget = new CleanupBudget(limits);
        var outcomes = new List<CleanupOutcome>(plan.Decisions.Count);
        foreach (var decision in plan.Decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decision.Disposition != CleanupDisposition.Eligible || decision.Identity is null ||
                decision.ReservedBytes is null ||
                !StringComparer.Ordinal.Equals(decision.Identity.StorageId, plan.StorageId))
            {
                outcomes.Add(new CleanupOutcome(decision, CleanupOutcomeKind.Kept,
                    decision.Reason, 0, Guid.NewGuid()));
                continue;
            }
            var prior = await claimStore.ExistingAsync(decision.Identity, cancellationToken);
            if (prior is not null)
            {
                outcomes.Add(prior);
                continue;
            }
            if (!budget.TryReserve(decision.ReservedBytes.Value, out var budgetReason))
            {
                outcomes.Add(new CleanupOutcome(decision, CleanupOutcomeKind.Kept,
                    budgetReason, 0, Guid.NewGuid()));
                continue;
            }
            if (!await claimStore.TryClaimAsync(decision.Identity,
                    decision.ReservedBytes.Value, cancellationToken))
            {
                outcomes.Add(new CleanupOutcome(decision, CleanupOutcomeKind.Deferred,
                    "claim_busy", 0, Guid.NewGuid()));
                continue;
            }

            var current = await fileSystem.ObserveAsync(decision.Path, cancellationToken);
            CleanupDeleteResult deletion;
            if (current is null)
            {
                deletion = new CleanupDeleteResult(CleanupOutcomeKind.AlreadyAbsent, 0, "already_absent");
            }
            else if (current.Identity != decision.Identity)
            {
                deletion = new CleanupDeleteResult(CleanupOutcomeKind.Kept, 0, "identity_changed");
            }
            else
            {
                var refreshed = planner.Decide(current);
                if (refreshed.Disposition != CleanupDisposition.Eligible ||
                    refreshed.NewestWriteUtc != decision.NewestWriteUtc ||
                    refreshed.ReservedBytes != decision.ReservedBytes)
                {
                    deletion = new CleanupDeleteResult(CleanupOutcomeKind.Kept, 0,
                        refreshed.Disposition == CleanupDisposition.Eligible
                            ? "inventory_changed" : refreshed.Reason);
                }
                else
                {
                    deletion = await fileSystem.DeleteNoFollowAsync(current, cancellationToken);
                }
            }
            var outcome = new CleanupOutcome(decision, deletion.Kind, deletion.Reason,
                deletion.Kind == CleanupOutcomeKind.Removed ? Math.Max(0, deletion.ReclaimedBytes) : 0,
                Guid.NewGuid());
            await claimStore.CompleteAsync(outcome, cancellationToken);
            outcomes.Add(outcome);
        }
        return new CleanupReceipt(plan.RunId, outcomes);
    }
}
