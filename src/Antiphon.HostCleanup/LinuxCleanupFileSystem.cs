namespace Antiphon.HostCleanup;

public sealed class LinuxCleanupFileSystem : ICleanupFileSystem
{
    public ValueTask<CleanupCandidateFacts?> ObserveAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Linux no-follow adapter is pending qualification.");

    public ValueTask<CleanupDeleteResult> DeleteNoFollowAsync(
        CleanupCandidateFacts current, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Linux no-follow adapter is pending qualification.");
}
