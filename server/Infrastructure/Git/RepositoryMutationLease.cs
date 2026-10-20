using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class RepositoryMutationLease(ILandingGit git, IRepositoryFenceObserver? fences = null,
    RepositoryLeaseWaiters? leaseWaiters = null)
    : IRepositoryMutationLease
{
    private readonly IRepositoryFenceObserver? _fences = fences;

    private void NotifyFenced(string common) => _fences?.Fenced(common);
    private void NotifyReleased() => leaseWaiters?.SignalReady();
    private static readonly StringComparer PathKeyComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly object _ownersGate = new();
    private readonly Dictionary<string, OwnerSlot> _owners = new(PathKeyComparer);
    private readonly RepositoryVerificationWindows _windows = new(git);
    public bool SupportsVerificationWindows => true;

    public Task<RepositoryLease?> TryAcquirePreflightAsync(string repository, RepositoryPreflight preflight,
        RepositoryLeaseOwnerTag owner, CancellationToken ct) => AcquireAsync(repository, owner, ct, preflight);

    public async Task<RepositoryVerificationWindow?> OpenVerificationWindowAsync(RepositoryLease lease,
        IAsyncDisposable executionLease, RepositoryVerificationIdentity identity, CancellationToken ct)
    {
        if (!Owns(lease, lease.CommonDirectory))
            throw new InvalidOperationException("verification_window_repository_lease_required");
        return await _windows.OpenAsync(lease.CommonDirectory, executionLease, identity, ct);
    }

    public bool AllowsWorktree(RepositoryLease lease, string path, string branch) =>
        lease is OwnedLease owned && ReferenceEquals(owned.Provider, this) && !owned.Disposed
        && (owned.Preflight is null || owned.Preflight.WorktreePath is { } expected
            && LandingGit.PathsEqual(expected, path) && owned.Preflight.Branch == branch);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
        AcquireAsync(repository, null, ct);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, RepositoryLeaseOwnerTag owner, CancellationToken ct) =>
        AcquireAsync(repository, owner, ct);

    private async Task<RepositoryLease?> AcquireAsync(string repository, RepositoryLeaseOwnerTag? tag, CancellationToken ct,
        RepositoryPreflight? preflight = null)
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
                var admission = await _windows.InspectAsync(common, preflight, ct);
                if (!admission.Allowed
                    || admission.WindowId is not null && preflight is not null
                        && leaseWaiters?.AllowsPreflightTurn(common, preflight.Identity) == false
                    || await RepositoryChildJournal.FirstFencingAsync(common, git, ct,
                        admission.WindowId, admission.TaskId) is not null)
                {
                    await stream.DisposeAsync();
                    NotifyFenced(common);
                    return null;
                }

                var slot = new OwnerSlot(Guid.NewGuid(), tag, DateTimeOffset.UtcNow);
                lock (_ownersGate)
                    _owners[PathKey(common)] = slot;
                registered = slot;
                return new OwnedLease(this, common, stream, slot.AcquisitionId,
                    admission.WindowId is null ? null : preflight);
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
        if (!(await _windows.InspectAsync(common, null, ct)).Allowed)
            return "verification_window_active_or_recovery_required; inspect " + Path.Combine(common, "antiphon", "verification-window.json");
        var fence = await RepositoryChildJournal.FirstFencingAsync(common, git, ct);
        if (fence is null)
            return null;
        NotifyFenced(common);
        var sentence = "unfinished repository child journal under "
            + Path.Combine(common, "antiphon", "children");
        if (fence.Purpose is not null || fence.TaskId is not null)
            sentence += " (purpose=" + (fence.Purpose ?? "") + " task=" + (fence.TaskId?.ToString("N") ?? "") + ")";
        return sentence + "; run scripts/recover-repository-children.ps1";
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

    private sealed class OwnedLease(RepositoryMutationLease provider, string common, FileStream stream, Guid acquisitionId,
        RepositoryPreflight? preflight)
        : RepositoryLease
    {
        public RepositoryMutationLease Provider { get; } = provider;
        public bool Disposed { get; private set; }
        public override string CommonDirectory { get; } = common;
        public RepositoryPreflight? Preflight { get; } = preflight;

        public override async ValueTask DisposeAsync()
        {
            lock (Provider._ownersGate)
            {
                if (Disposed) return;
                Disposed = true;
            }

            Provider.ReleaseOwner(CommonDirectory, acquisitionId);
            await stream.DisposeAsync();
            Provider.NotifyReleased();
        }
    }
}
