namespace Antiphon.HostCleanup;

public sealed class WindowsCleanupFileSystem : ICleanupFileSystem
{
    public ValueTask<CleanupCandidateFacts?> ObserveAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Windows no-follow adapter is pending qualification.");

    public ValueTask<CleanupDeleteResult> DeleteNoFollowAsync(
        CleanupCandidateFacts current, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Windows no-follow adapter is pending qualification.");
}
