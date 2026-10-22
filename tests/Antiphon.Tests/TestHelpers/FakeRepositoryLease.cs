using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1427 V-3: an in-memory single-holder repository mutation lease. One common directory;
/// an acquire returns null while another holder has it, and disposing the holder frees it.
/// </summary>
internal sealed class FakeRepositoryLease : IRepositoryMutationLease
{
    private readonly object _gate = new();
    private Holder? _holder;

    /// <summary>The tag of the current holder, null when free.</summary>
    public RepositoryLeaseOwnerTag? HeldBy { get { lock (_gate) return _holder?.Owner; } }

    public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
        TryAcquireAsync(repository, new RepositoryLeaseOwnerTag(null, "untagged"), ct);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, RepositoryLeaseOwnerTag owner, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_holder is not null) return Task.FromResult<RepositoryLease?>(null);
            _holder = new Holder(this, repository, owner);
            return Task.FromResult<RepositoryLease?>(_holder);
        }
    }

    public bool Owns(RepositoryLease lease, string commonDirectory)
    {
        lock (_gate) return ReferenceEquals(lease, _holder);
    }

    private sealed class Holder(FakeRepositoryLease provider, string common, RepositoryLeaseOwnerTag owner) : RepositoryLease
    {
        public RepositoryLeaseOwnerTag Owner => owner;
        public override string CommonDirectory => common;

        public override ValueTask DisposeAsync()
        {
            lock (provider._gate)
                if (ReferenceEquals(provider._holder, this)) provider._holder = null;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// CARD-1427 V-4 / CARD-1428 V-7: refuses the first <paramref name="refusals"/> acquisitions
/// (the lease is "occupied by a land"), then delegates to the real lease so a claim can cut.
/// </summary>
internal sealed class RefusingRepositoryLease(IRepositoryMutationLease inner, int refusals) : IRepositoryMutationLease
{
    private int _refused;

    /// <summary>How many acquisitions were refused so far.</summary>
    public int Refused => Volatile.Read(ref _refused);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
        TryAcquireAsync(repository, new RepositoryLeaseOwnerTag(null, "untagged"), ct);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, RepositoryLeaseOwnerTag owner, CancellationToken ct)
    {
        if (Volatile.Read(ref _refused) < refusals)
        {
            Interlocked.Increment(ref _refused);
            return Task.FromResult<RepositoryLease?>(null);
        }
        return inner.TryAcquireAsync(repository, owner, ct);
    }

    public bool Owns(RepositoryLease lease, string commonDirectory) => inner.Owns(lease, commonDirectory);
}
