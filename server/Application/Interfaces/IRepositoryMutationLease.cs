namespace Antiphon.Server.Application.Interfaces;

public interface IRepositoryMutationLease
{
    Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct);
    bool Owns(RepositoryLease lease, string commonDirectory);
}

/// <summary>Opaque live ownership, minted by the lease provider and checked by that provider.</summary>
public abstract class RepositoryLease : IAsyncDisposable
{
    public abstract string CommonDirectory { get; }
    public abstract ValueTask DisposeAsync();
}
