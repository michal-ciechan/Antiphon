namespace Antiphon.HostCleanup;

// Native deletion is deliberately unavailable until the platform-specific no-follow
// implementations and their qualification are installed. A virtual implementation can
// exercise the policy and executor without granting a host filesystem capability.
public sealed class CleanupFileSystem : ICleanupFileSystem
{
    public ValueTask<CleanupCandidateFacts?> ObserveAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A qualified native cleanup adapter is required.");

    public ValueTask<CleanupDeleteResult> DeleteNoFollowAsync(
        CleanupCandidateFacts current, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A qualified native cleanup adapter is required.");
}
