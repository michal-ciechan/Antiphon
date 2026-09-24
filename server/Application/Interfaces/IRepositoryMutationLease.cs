namespace Antiphon.Server.Application.Interfaces;

public interface IRepositoryMutationLease
{
    Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct);

    /// <summary>
    /// CARD-0641: record <paramref name="owner"/> only after the OS lock and child-journal
    /// checks succeed. The default keeps existing callers and fakes on the untagged acquire.
    /// </summary>
    Task<RepositoryLease?> TryAcquireAsync(string repository, RepositoryLeaseOwnerTag owner, CancellationToken ct) =>
        TryAcquireAsync(repository, ct);

    bool Owns(RepositoryLease lease, string commonDirectory);

    /// <summary>
    /// CARD-0535: journal-only probe for why a lease is unavailable. Must never open
    /// <c>landing.lock</c>. Default is unknown (null).
    /// </summary>
    Task<string?> DescribeUnavailableAsync(string repository, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// CARD-0641: read-only owner lookup. Must never open <c>landing.lock</c>, recover a child,
    /// or admit a mutation. Null means this provider does not track owners.
    /// </summary>
    Task<RepositoryLeaseOwner?> FindOwnerAsync(string repository, CancellationToken ct) =>
        Task.FromResult<RepositoryLeaseOwner?>(null);
}

/// <summary>Advisory identity passed into a tagged acquire. The OS lock stays the authority.</summary>
public sealed record RepositoryLeaseOwnerTag(Guid? TaskId, string Purpose);

/// <summary>Stable purpose strings for the production acquire sites (CARD-0641).</summary>
public static class RepositoryLeasePurposes
{
    public const string Land = "land";
    public const string Dispatch = "dispatch";
    public const string GatedCommit = "gated-commit";
    public const string WorktreeProvision = "worktree-provision";
    public const string WorktreeSettlement = "worktree-settlement";
}

public enum RepositoryLeaseOwnerState
{
    Known = 0,
    Untagged = 1,
    Unknown = 2,
}

/// <summary>One in-process acquisition. Unknown is explicit and carries no task.</summary>
public sealed record RepositoryLeaseOwner(
    RepositoryLeaseOwnerState State,
    Guid? TaskId,
    string? Purpose,
    Guid? AcquisitionId,
    DateTimeOffset? AcquiredAt)
{
    public static RepositoryLeaseOwner Unknown { get; } = new(RepositoryLeaseOwnerState.Unknown, null, null, null, null);
}

/// <summary>Opaque live ownership, minted by the lease provider and checked by that provider.</summary>
public abstract class RepositoryLease : IAsyncDisposable
{
    public abstract string CommonDirectory { get; }
    public abstract ValueTask DisposeAsync();
}
