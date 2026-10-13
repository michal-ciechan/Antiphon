using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Optional S3 boundary shared by runtime and mirror service. C1032_FenceStartOrders and
/// C1032_FenceCreationOrders exercise its lock before session/pane/repository admission.
/// No production composition installs it in S3.
/// </summary>
public sealed class RunnerWorkspaceUseGate(RunnerWorkspaceRemovalJournal journal)
{
    // One runner-local admission lane also serializes Git registration changes in a common repository.
    private readonly SemaphoreSlim _admission = new(1, 1);
    private readonly object _ownerSync = new();
    private readonly List<Owner> _owners = [];
    private volatile bool _censusReady;
    private volatile bool _censusUnknown;

    public async Task<IAsyncDisposable> AcquireUseAsync(string path, CancellationToken ct)
    {
        await _admission.WaitAsync(ct);
        try
        {
            var records = journal.Read();
            if (Normalize(path) is null || records is null
                || records.Any(i => Overlaps(i.Command.Binding.WorktreePath, path)))
                throw new PhoneHomeAdmissionException("workspace_retiring_or_unknown", "Workspace admission is held.", 409);
            return new Lease(_admission);
        }
        catch { _admission.Release(); throw; }
    }

    public async Task<IAsyncDisposable> AcquireCensusAsync(CancellationToken ct)
    {
        _censusReady = false;
        await _admission.WaitAsync(ct);
        _censusUnknown = false;
        return new Lease(_admission);
    }

    internal async Task<IAsyncDisposable> AcquireRetainedUseAsync(string? path, Guid id,
        Func<bool> stillOwned, CancellationToken ct)
    {
        await _admission.WaitAsync(ct);
        RecordOwner(path, id, stillOwned);
        return new Lease(_admission);
    }

    public void CompleteCensus() => _censusReady = !_censusUnknown;
    public void MarkCensusUnknown() => _censusUnknown = true;

    public void RecordOwner(string? path, Guid id, Func<bool> stillOwned)
    {
        var normalized = path is null ? null : Normalize(path);
        if (normalized is null) { MarkCensusUnknown(); return; }
        lock (_ownerSync)
        {
            // Retain older generations/paths when the same session id is reused. Prune only a
            // positively absent owner, keeping unreadable process observations as debt.
            _owners.RemoveAll(owner =>
            {
                try { return !owner.StillOwned(); }
                catch { return false; }
            });
            _owners.Add(new(normalized, stillOwned));
        }
    }

    public bool IsRetiring(string? path)
    {
        var records = journal.Read();
        return path is null || Normalize(path) is null || records is null
            || records.Any(i => Overlaps(i.Command.Binding.WorktreePath, path));
    }

    public bool HasOwnershipConflict(string path) => IsRetiring(path) && HasOwner(path);

    private bool HasOwner(string path)
    {
        lock (_ownerSync)
        {
            foreach (var owner in _owners)
            {
                if (!Overlaps(owner.Path, path)) continue;
                try { if (owner.StillOwned()) return true; }
                catch { return true; }
            }
            return false;
        }
    }

    public async Task<bool> TryRetireAsync(CompletedCardWorkspaceCommand command, Func<Task> admitted,
        CancellationToken ct)
    {
        // A census in progress returns a safety hold, rather than waiting behind its external I/O.
        if (!_censusReady || _censusUnknown) return false;
        await _admission.WaitAsync(ct);
        try
        {
            if (!_censusReady || _censusUnknown || HasOwner(command.Binding.WorktreePath)
                || !journal.Record(command)) return false;
            // C1032_FenceStartOrders observes the persisted intent from another reader here.
            await admitted();
            return true;
        }
        finally { _admission.Release(); }
    }

    internal static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var resolved = RunnerWorkspaceService.TryResolveFinal(path);
        return resolved is null ? null : Path.TrimEndingDirectorySeparator(resolved);
    }

    internal static bool Overlaps(string left, string right)
    {
        var a = Normalize(left); var b = Normalize(right);
        if (a is null || b is null) return true;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return a.Equals(b, comparison) || a.StartsWith(b + Path.DirectorySeparatorChar, comparison)
            || b.StartsWith(a + Path.DirectorySeparatorChar, comparison);
    }

    private sealed record Owner(string Path, Func<bool> StillOwned);
    private sealed class Lease(SemaphoreSlim admission) : IAsyncDisposable
    {
        private int _released;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) admission.Release();
            return ValueTask.CompletedTask;
        }
    }
}
