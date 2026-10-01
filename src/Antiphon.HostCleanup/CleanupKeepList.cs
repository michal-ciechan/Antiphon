namespace Antiphon.HostCleanup;

public sealed record CleanupHold(
    string StorageId, string Path, string? OwnerGeneration, string Reason,
    string Creator, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, long Revision);

public sealed class CleanupKeepList
{
    private readonly IReadOnlyList<CleanupHold> holds;

    public CleanupKeepList(IReadOnlyList<CleanupHold> holds) => this.holds = holds;

    public string? Veto(string storageId, string path, string? ownerGeneration, DateTimeOffset now)
    {
        foreach (var hold in holds)
        {
            if (!StringComparer.Ordinal.Equals(hold.StorageId, storageId) ||
                (hold.OwnerGeneration is not null &&
                 !StringComparer.Ordinal.Equals(hold.OwnerGeneration, ownerGeneration)))
                continue;
            if (CleanupPath.IsSameOrChild(path, hold.Path) || CleanupPath.IsSameOrChild(hold.Path, path))
                return hold.ExpiresUtc <= now ? "hold_expired_review_required" : "hold_active";
        }
        return null;
    }
}
