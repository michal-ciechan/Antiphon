namespace Antiphon.Server.Application.Interfaces;

public interface IRepositoryMutationLease
{
    Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct);
    bool Owns(RepositoryLease lease, string commonDirectory);

    /// <summary>
    /// CARD-0535: journal-only probe for why a lease is unavailable. Must never open
    /// <c>landing.lock</c>. Default is unknown (null).
    /// </summary>
    Task<string?> DescribeUnavailableAsync(string repository, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}

/// <summary>Opaque live ownership, minted by the lease provider and checked by that provider.</summary>
public abstract class RepositoryLease : IAsyncDisposable
{
    public abstract string CommonDirectory { get; }
    public abstract ValueTask DisposeAsync();
}
