using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Process-local committed projection. Every read/append/reset takes the same session gate.
/// The directory lock only rents gates; it never spans I/O or callbacks. No subscriber runs here.
/// </summary>
public sealed class SessionStateStore : IDisposable
{
    private readonly object _directoryGate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly ISessionStateLoader _loader;
    private readonly TimeProvider _clock;
    private readonly SessionStateSettings _settings;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Meter _meter = new("Antiphon.SessionState");
    private readonly Counter<long> _hits, _misses, _faults, _evictions, _pressure, _ingests, _rows;
    private long _revision;
    private long _hitCount, _loadCount, _faultCount, _evictionCount, _pressureCount, _ingestCount, _rowCount, _duplicateCount;
    public Guid ServerEpoch { get; } = Guid.NewGuid();
    public bool Enabled => _settings.Enabled;

    public SessionStateStore(ISessionStateLoader loader, TimeProvider clock, IOptions<SessionStateSettings> settings)
    {
        _loader = loader;
        _clock = clock;
        _settings = settings.Value;
        if (_settings.MaxSessions < 1 || _settings.WarmupBatchSize is < 1 or > 512
            || _settings.TerminalIdleMinutes < 0 || _settings.FailureRetrySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(settings));
        _hits = _meter.CreateCounter<long>("session_state.hits");
        _misses = _meter.CreateCounter<long>("session_state.loads");
        _faults = _meter.CreateCounter<long>("session_state.load_faults");
        _evictions = _meter.CreateCounter<long>("session_state.evictions");
        _pressure = _meter.CreateCounter<long>("session_state.capacity_fallbacks");
        _ingests = _meter.CreateCounter<long>("session_state.ingest_calls");
        _rows = _meter.CreateCounter<long>("session_state.committed_rows");
        _meter.CreateObservableGauge("session_state.entries", () => CachedCount);
    }

    internal int CachedCount { get { lock (_directoryGate) return _entries.Values.Count(e => e.Admitted); } }

    public async Task<SessionStateSnapshot> ReadAsync(Guid sessionId, CancellationToken ct) =>
        (await ReadBatchAsync([sessionId], ct))[sessionId];

    public async Task<bool> IsWorkingAsync(Guid sessionId, CancellationToken ct) => (await ReadAsync(sessionId, ct)).Working;

    public async Task<IReadOnlyDictionary<Guid, bool>> IsWorkingBatchAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        (await ReadBatchAsync(ids, ct)).ToDictionary(p => p.Key, p => p.Value.Working);

    public Task<IReadOnlyDictionary<Guid, SessionStateSnapshot>> ReadBatchAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count != ids.Distinct().Count()) throw new ArgumentException("Duplicate session IDs.", nameof(ids));
        ct.ThrowIfCancellationRequested();
        // The caller only cancels its wait. A shared seed finishes under the host lifetime token.
        return ReadCoreAsync(ids.Order().ToArray()).WaitAsync(ct);
    }

    private async Task<IReadOnlyDictionary<Guid, SessionStateSnapshot>> ReadCoreAsync(Guid[] ids)
    {
        var result = new Dictionary<Guid, SessionStateSnapshot>();
        foreach (var batch in ids.Chunk(_settings.WarmupBatchSize))
        {
            var held = new List<Lease>();
            try
            {
                foreach (var id in batch) held.Add(await AcquireAsync(id, _shutdown.Token));
                await EnsureLoadedAsync(held.Select(l => l._entry).ToArray(), _shutdown.Token);
                foreach (var lease in held) result.Add(lease.SessionId, lease.Snapshot);
            }
            finally { for (var i = held.Count - 1; i >= 0; i--) held[i].Dispose(); }
        }
        return result;
    }

    // Ingestion owns the lease through its transaction commit AND snapshot publication.
    internal async Task<Lease> BeginWriteAsync(Guid id, CancellationToken ct)
    {
        var lease = await AcquireAsync(id, ct);
        try
        {
            if (lease._entry.State?.Readiness == SessionStateReadiness.Missing) lease._entry.State = null;
            await EnsureLoadedAsync([lease._entry], ct);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private async Task<Lease> AcquireAsync(Guid id, CancellationToken ct)
    {
        Entry entry;
        lock (_directoryGate)
        {
            EvictIdleLocked();
            if (!_entries.TryGetValue(id, out entry!))
            {
                var admitted = _entries.Values.Count(e => e.Admitted) < _settings.MaxSessions;
                entry = new Entry(id, admitted, _clock.GetUtcNow());
                _entries.Add(id, entry);
                if (!admitted) { _pressure.Add(1); Interlocked.Increment(ref _pressureCount); }
            }
            entry.Users++;
        }
        try { await entry.Gate.WaitAsync(ct); return new Lease(this, entry); }
        catch { Return(entry, releaseGate: false); throw; }
    }

    private async Task EnsureLoadedAsync(Entry[] entries, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        foreach (var entry in entries)
            if (entry.Fault is not null && now < entry.RetryAt) entry.Fault.Throw();
        var cold = entries.Where(e => e.State is null || e.Fault is not null
            || (e.State.Readiness == SessionStateReadiness.Missing && now >= e.RetryAt)).ToArray();
        _hits.Add(entries.Length - cold.Length);
        Interlocked.Add(ref _hitCount, entries.Length - cold.Length);
        if (cold.Length == 0) return;
        await LoadAsync(cold, reset: false, ct);
    }

    private async Task LoadAsync(Entry[] entries, bool reset, CancellationToken ct)
    {
        _misses.Add(1);
        Interlocked.Increment(ref _loadCount);
        try
        {
            var loaded = await _loader.LoadAsync(entries.Select(e => e.Id).ToArray(), ct);
            foreach (var entry in entries)
            {
                var previous = entry.State;
                var next = loaded[entry.Id] with { ServerEpoch = ServerEpoch,
                    Revision = previous?.Revision ?? 0,
                    ResetEpoch = (previous?.ResetEpoch ?? 0) + (reset ? 1 : 0) };
                if (next != previous) next = next with { Revision = Interlocked.Increment(ref _revision) };
                entry.State = next;
                entry.Fault = null;
                entry.RetryAt = _clock.GetUtcNow().AddSeconds(_settings.FailureRetrySeconds);
                lock (_directoryGate) entry.Pinned = next.Pinned;
            }
        }
        catch (Exception ex)
        {
            _faults.Add(1);
            Interlocked.Increment(ref _faultCount);
            foreach (var entry in entries)
            {
                entry.Fault = ExceptionDispatchInfo.Capture(ex);
                entry.RetryAt = _clock.GetUtcNow().AddSeconds(_settings.FailureRetrySeconds);
            }
            throw;
        }
    }

    public async Task WarmAsync(CancellationToken ct)
    {
        var pinned = await _loader.LoadPinnedIdsAsync(ct);
        lock (_directoryGate)
        {
            foreach (var entry in _entries.Values) entry.Pinned = pinned.Contains(entry.Id);
            EvictIdleLocked();
        }
        await ReadBatchAsync(pinned.ToArray(), ct);
    }

    private void EvictIdleLocked()
    {
        var cutoff = _clock.GetUtcNow().AddMinutes(-_settings.TerminalIdleMinutes);
        foreach (var entry in _entries.Values.Where(e => e.Users == 0 && !e.Pinned && e.LastUse <= cutoff).ToArray())
        {
            _entries.Remove(entry.Id);
            entry.Gate.Dispose();
            _evictions.Add(1);
            Interlocked.Increment(ref _evictionCount);
        }
    }

    private void Return(Entry entry, bool releaseGate)
    {
        lock (_directoryGate)
        {
            if (releaseGate) entry.Gate.Release();
            entry.Users--;
            entry.LastUse = _clock.GetUtcNow();
            if (entry.Users == 0 && !entry.Admitted)
            {
                _entries.Remove(entry.Id);
                entry.Gate.Dispose();
            }
        }
    }

    internal sealed class Entry(Guid id, bool admitted, DateTimeOffset now)
    {
        public Guid Id { get; } = id;
        public bool Admitted { get; } = admitted;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Users;
        public bool Pinned;
        public DateTimeOffset LastUse = now, RetryAt;
        public SessionStateSnapshot? State;
        public ExceptionDispatchInfo? Fault;
    }

    internal sealed class Lease(SessionStateStore owner, Entry entry) : IDisposable
    {
        internal readonly Entry _entry = entry;
        private bool _disposed;
        public Guid SessionId => entry.Id;
        public SessionStateSnapshot Snapshot => entry.Fault is null && entry.State is not null
            ? entry.State : throw new InvalidOperationException("Session state is unavailable.");

        public async Task PublishAsync(IReadOnlyList<TranscriptEntry> rows, DateTime? acceptedGeneration, CancellationToken ct)
        {
            try
            {
                var next = Snapshot.Append(rows) with { AcceptedGeneration = acceptedGeneration ?? Snapshot.AcceptedGeneration };
                if (next != entry.State) entry.State = next with { Revision = Interlocked.Increment(ref owner._revision) };
            }
            catch
            {
                // A committed non-append/failed publication must never leave Ready stale state.
                await ReconcileAsync(false, ct);
            }
        }

        public Task ReconcileAsync(bool reset, CancellationToken ct) => owner.LoadAsync([entry], reset, ct);
        public void RecordIngest(long previousCount, bool duplicate) =>
            owner.RecordIngest(Math.Max(0, Snapshot.Count - previousCount), duplicate);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Return(entry, releaseGate: true);
        }
    }

    internal void RecordIngest(long committedRows, bool duplicate)
    {
        _ingests.Add(1); Interlocked.Increment(ref _ingestCount);
        _rows.Add(committedRows); Interlocked.Add(ref _rowCount, committedRows);
        if (duplicate) Interlocked.Increment(ref _duplicateCount);
    }

    public object GetMetrics() => new
    {
        serverEpoch = ServerEpoch, enabled = Enabled, cachedSessions = CachedCount,
        hits = Interlocked.Read(ref _hitCount), loads = Interlocked.Read(ref _loadCount),
        loadFaults = Interlocked.Read(ref _faultCount), evictions = Interlocked.Read(ref _evictionCount),
        capacityFallbacks = Interlocked.Read(ref _pressureCount), ingestCalls = Interlocked.Read(ref _ingestCount),
        committedRows = Interlocked.Read(ref _rowCount), duplicateBatches = Interlocked.Read(ref _duplicateCount)
    };

    public void Dispose() { _shutdown.Cancel(); _shutdown.Dispose(); _meter.Dispose(); }
}
