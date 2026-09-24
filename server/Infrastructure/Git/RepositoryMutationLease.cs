using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class RepositoryMutationLease(ILandingGit git) : IRepositoryMutationLease
{
    private static readonly StringComparer PathKeyComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly object _ownersGate = new();
    private readonly Dictionary<string, OwnerSlot> _owners = new(PathKeyComparer);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
        AcquireAsync(repository, null, ct);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, RepositoryLeaseOwnerTag owner, CancellationToken ct) =>
        AcquireAsync(repository, owner, ct);

    private async Task<RepositoryLease?> AcquireAsync(string repository, RepositoryLeaseOwnerTag? tag, CancellationToken ct)
    {
        var common = await git.CommonDirectoryAsync(repository, ct);
        var directory = Path.Combine(common, "antiphon");
        Directory.CreateDirectory(directory);
        ct.ThrowIfCancellationRequested();
        try
        {
            var stream = new FileStream(Path.Combine(directory, "landing.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            OwnerSlot? registered = null;
            try
            {
                if (await RepositoryChildJournal.HasUnfinishedAsync(common, git, ct))
                {
                    await stream.DisposeAsync();
                    return null;
                }

                var slot = new OwnerSlot(Guid.NewGuid(), tag, DateTimeOffset.UtcNow);
                lock (_ownersGate)
                    _owners[PathKey(common)] = slot;
                registered = slot;
                return new OwnedLease(this, common, stream, slot.AcquisitionId);
            }
            catch
            {
                if (registered is not null)
                    ReleaseOwner(common, registered.AcquisitionId);
                await stream.DisposeAsync();
                throw;
            }
        }
        catch (IOException) { return null; }
    }

    public bool Owns(RepositoryLease lease, string commonDirectory) => lease is OwnedLease owned
        && ReferenceEquals(owned.Provider, this) && !owned.Disposed
        && LandingGit.PathsEqual(owned.CommonDirectory, commonDirectory);

    public async Task<RepositoryLeaseOwner?> FindOwnerAsync(string repository, CancellationToken ct)
    {
        // Resolve the common directory only. Opening landing.lock here would become a second authority.
        var common = await git.CommonDirectoryAsync(repository, ct);
        lock (_ownersGate)
        {
            return _owners.TryGetValue(PathKey(common), out var slot)
                ? slot.ToOwner()
                : RepositoryLeaseOwner.Unknown;
        }
    }

    public async Task<string?> DescribeUnavailableAsync(string repository, CancellationToken ct)
    {
        var common = await git.CommonDirectoryAsync(repository, ct);
        if (!await RepositoryChildJournal.HasUnfinishedAsync(common, git, ct))
            return null;
        return "unfinished repository child journal under "
            + Path.Combine(common, "antiphon", "children")
            + "; run scripts/recover-repository-children.ps1";
    }

    private void ReleaseOwner(string common, Guid acquisitionId)
    {
        lock (_ownersGate)
        {
            var key = PathKey(common);
            if (_owners.TryGetValue(key, out var slot) && slot.AcquisitionId == acquisitionId)
                _owners.Remove(key);
        }
    }

    private static string PathKey(string common) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(common));

    private sealed record OwnerSlot(Guid AcquisitionId, RepositoryLeaseOwnerTag? Tag, DateTimeOffset AcquiredAt)
    {
        public RepositoryLeaseOwner ToOwner() => Tag is null
            ? new RepositoryLeaseOwner(RepositoryLeaseOwnerState.Untagged, null, null, AcquisitionId, AcquiredAt)
            : new RepositoryLeaseOwner(
                RepositoryLeaseOwnerState.Known, Tag.TaskId, Tag.Purpose, AcquisitionId, AcquiredAt);
    }

    private sealed class OwnedLease(RepositoryMutationLease provider, string common, FileStream stream, Guid acquisitionId)
        : RepositoryLease
    {
        public RepositoryMutationLease Provider { get; } = provider;
        public bool Disposed { get; private set; }
        public override string CommonDirectory { get; } = common;

        public override async ValueTask DisposeAsync()
        {
            lock (Provider._ownersGate)
            {
                if (Disposed) return;
                Disposed = true;
            }

            Provider.ReleaseOwner(CommonDirectory, acquisitionId);
            await stream.DisposeAsync();
        }
    }
}
