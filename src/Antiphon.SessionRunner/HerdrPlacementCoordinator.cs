using System.Collections.Concurrent;

namespace Antiphon.SessionRunner;

internal readonly record struct PaneBinding(Guid SessionId, string Origin, bool Live);

/// <summary>
/// CARD-0384: instance-scoped placement coordination. Serializes workspace find/create by
/// WorkspaceKey, then the short select/claim phase by resolved workspace ID. Claims cover tab
/// and pane IDs until a sidecar is published or the attempt fails. No static state.
/// </summary>
internal sealed class HerdrPlacementCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceKeyLocks =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceIdLocks =
        new(StringComparer.Ordinal);
    private readonly object _claimsGate = new();
    private readonly List<PlacementClaim> _claims = [];

    public async Task<IAsyncDisposable> LockWorkspaceKeyAsync(string workspaceKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceKey);
        var sem = _workspaceKeyLocks.GetOrAdd(workspaceKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct).ConfigureAwait(false);
        return new SemaphoreReleaser(sem);
    }

    public async Task<IAsyncDisposable> LockWorkspaceIdAsync(string workspaceId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var sem = _workspaceIdLocks.GetOrAdd(workspaceId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct).ConfigureAwait(false);
        return new SemaphoreReleaser(sem);
    }

    public PlacementClaim Claim(
        Guid sessionId, string workspaceId, string tabId, string paneId, bool named)
    {
        var claim = new PlacementClaim(this, sessionId, workspaceId, tabId, paneId, named);
        lock (_claimsGate)
            _claims.Add(claim);
        return claim;
    }

    public PlacementClaim? FindPaneClaim(string paneId, Guid? exceptSessionId)
    {
        lock (_claimsGate)
        {
            return _claims.FirstOrDefault(c =>
                !c.Released
                && string.Equals(c.PaneId, paneId, StringComparison.Ordinal)
                && exceptSessionId != c.SessionId);
        }
    }

    public bool IsNamedTabReserved(string tabId)
    {
        lock (_claimsGate)
        {
            return _claims.Any(c =>
                !c.Released
                && c.Named
                && string.Equals(c.TabId, tabId, StringComparison.Ordinal));
        }
    }

    internal IReadOnlyList<Guid> InspectPaneClaims(string paneId)
    {
        lock (_claimsGate)
            return _claims.Where(c => !c.Released && c.PaneId == paneId)
                .Select(c => c.SessionId).ToArray();
    }

    internal void Release(PlacementClaim claim)
    {
        lock (_claimsGate)
            _claims.Remove(claim);
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class PlacementClaim : IAsyncDisposable
{
    private readonly HerdrPlacementCoordinator _owner;
    private int _released;

    public PlacementClaim(
        HerdrPlacementCoordinator owner,
        Guid sessionId,
        string workspaceId,
        string tabId,
        string paneId,
        bool named)
    {
        _owner = owner;
        SessionId = sessionId;
        WorkspaceId = workspaceId;
        TabId = tabId;
        PaneId = paneId;
        Named = named;
    }

    public Guid SessionId { get; }
    public string WorkspaceId { get; }
    public string TabId { get; }
    public string PaneId { get; }
    public bool Named { get; }
    public bool Released => _released != 0;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _owner.Release(this);
        return ValueTask.CompletedTask;
    }
}
