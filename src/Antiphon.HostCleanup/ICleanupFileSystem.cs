namespace Antiphon.HostCleanup;

public sealed record CleanupDeleteResult(CleanupOutcomeKind Kind, long ReclaimedBytes, string Reason);

public interface ICleanupFileSystem
{
    ValueTask<CleanupCandidateFacts?> ObserveAsync(string path, CancellationToken cancellationToken);
    ValueTask<CleanupDeleteResult> DeleteNoFollowAsync(
        CleanupCandidateFacts current, CancellationToken cancellationToken);
}

public interface ICleanupPlanStore
{
    // Must return only after the entire immutable plan is durably committed.
    ValueTask PersistAsync(CleanupPlan plan, CancellationToken cancellationToken);
}

public interface ICleanupClaimStore
{
    ValueTask<CleanupOutcome?> ExistingAsync(CleanupIdentity identity,
        CancellationToken cancellationToken);
    ValueTask<bool> TryClaimAsync(CleanupIdentity identity,
        long reservedBytes, CancellationToken cancellationToken);
    ValueTask CompleteAsync(CleanupOutcome outcome,
        CancellationToken cancellationToken);
}
