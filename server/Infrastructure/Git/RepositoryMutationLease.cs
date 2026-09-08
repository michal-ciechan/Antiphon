using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class RepositoryMutationLease(ILandingGit git) : IRepositoryMutationLease
{
    public async Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
    {
        var common = await git.CommonDirectoryAsync(repository, ct);
        var directory = Path.Combine(common, "antiphon");
        Directory.CreateDirectory(directory);
        ct.ThrowIfCancellationRequested();
        try
        {
            return new OwnedLease(this, common, new FileStream(Path.Combine(directory, "landing.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException) { return null; }
    }

    public bool Owns(RepositoryLease lease, string commonDirectory) => lease is OwnedLease owned
        && ReferenceEquals(owned.Provider, this) && !owned.Disposed
        && LandingGit.PathsEqual(owned.CommonDirectory, commonDirectory);

    private sealed class OwnedLease(RepositoryMutationLease provider, string common, FileStream stream) : RepositoryLease
    {
        public RepositoryMutationLease Provider { get; } = provider;
        public bool Disposed { get; private set; }
        public override string CommonDirectory { get; } = common;
        public override async ValueTask DisposeAsync()
        {
            if (Disposed) return;
            Disposed = true;
            await stream.DisposeAsync();
            // File existence is never ownership; never unlink the lock file.
        }
    }
}
