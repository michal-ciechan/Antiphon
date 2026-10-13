using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>T18..T35 and T61/T62 bind scheduling, checked recovery and owned wakeups.</summary>
public sealed class StaleWorkingTranscriptRecovery(
    IServiceScopeFactory scopes, AgentSessionRuntime runtime, CompletionNoteFlushQueue flushes,
    IOptions<StaleWorkingTranscriptSettings> options, TimeProvider clock,
    ILogger<StaleWorkingTranscriptRecovery> logger) : IAsyncDisposable
{
    private const int DirectoryCapacity = 1024;
    private readonly StaleWorkingTranscriptSettings _settings = options.Value;
    private readonly Guid _epoch = Guid.NewGuid();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _ticks = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Guid, Entry> _directory = [];
    private readonly LinkedList<Entry> _pending = [];
    private readonly Dictionary<Guid, Operation> _owned = [];
    private Guid? _discoveryCursor, _reconcileCursor;
    private int _passes, _discovered, _reconciled;
    private bool _stopping, _disposed;
    private Task? _stopTask;

    private sealed class Entry(Guid sessionId, DateTime generation, long discoveredAt)
    {
        public Guid SessionId { get; } = sessionId;
        public DateTime Generation { get; } = generation;
        public long DiscoveredAt { get; } = discoveredAt;
        public long? LastSuccess, LastAttempt;
        public LinkedListNode<Entry>? Pending;
    }
    private sealed class Operation(Entry entry, long started)
    {
        public Entry Entry { get; } = entry;
        public long Started { get; } = started;
        public Task Task { get; set; } = Task.CompletedTask;
    }
    private sealed record Owner(Guid Id, DateTime StartedAt, SessionStatus Status, string? RunnerId, Guid? RunnerStoreId, string? RunnerCwd)
    {
        public bool LocalRunning => Status == SessionStatus.Running && StartedAt != default
            && (string.IsNullOrWhiteSpace(RunnerId) || RunnerId == PhoneHomeProtocol.LocalRunnerId)
            && RunnerStoreId is null && RunnerCwd is null;
    }

    public async Task TickAsync(CancellationToken ct)
    {
        await _ticks.WaitAsync(ct);
        try
        {
            lock (_gate)
            {
                if (_stopping) return;
                _discovered = _reconciled = 0;
                Reap();
                foreach (var operation in _owned.Values)
                    if (clock.GetElapsedTime(operation.Started).TotalSeconds >= _settings.ProbeTimeoutSeconds)
                        logger.LogWarning("Stale Working probe overdue session {SessionId}, generation {Generation}, epoch {ServerEpoch}, reason {Reason}, elapsed {ElapsedAgeSeconds}",
                            operation.Entry.SessionId, operation.Entry.Generation, _epoch, "owned_operation_overdue", Age(operation.Entry));
            }
            using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.ProbeTimeoutSeconds), clock);
            using var read = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token, timer.Token);
            try { await ReconcileAsync(read.Token); }
            catch (Exception ex) when (!ct.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            { logger.LogWarning(ex, "Stale Working reconciliation failed epoch {ServerEpoch}; retaining uncertain ownership and debt", _epoch); }
            try { await DiscoverAsync(read.Token); }
            catch (Exception ex) when (!ct.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            { logger.LogWarning(ex, "Stale Working discovery failed epoch {ServerEpoch}; retaining discovery cursor and debt", _epoch); }
        }
        finally
        {
            lock (_gate)
            {
                _passes++;
                if (!_stopping && !ct.IsCancellationRequested) Admit(ct);
            }
            _ticks.Release();
        }
    }

    private async Task DiscoverAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = db.AgentSessions.AsNoTracking().Where(s => s.Status == SessionStatus.Running && s.StartedAt != default
            && (s.RunnerId == null || s.RunnerId.Trim() == "" || s.RunnerId == PhoneHomeProtocol.LocalRunnerId)
            && s.RunnerStoreId == null && s.RunnerCwd == null);
        if (_discoveryCursor is Guid after) query = query.Where(s => s.Id.CompareTo(after) > 0);
        var rows = await query.OrderBy(s => s.Id).Take(_settings.PageSize).TagWith("stale-working.discovery")
            .Select(s => new Owner(s.Id, s.StartedAt, s.Status, s.RunnerId, s.RunnerStoreId, s.RunnerCwd)).ToListAsync(ct);
        var working = await TranscriptWorkingStateQuery.ReadAsync(db, rows.Select(s => s.Id).ToArray(), ct);
        lock (_gate)
        {
            _discoveryCursor = rows.Count < _settings.PageSize ? null : rows[^1].Id;
            _discovered = rows.Count;
            foreach (var row in rows)
                if (working.TryGetValue(row.Id, out var busy) && busy) Track(row);
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        Guid[] ids;
        lock (_gate)
            ids = _directory.Keys.Where(id => _reconcileCursor is null || id.CompareTo(_reconcileCursor.Value) > 0)
                .Order().Take(_settings.PageSize).ToArray();
        if (ids.Length == 0) { _reconcileCursor = null; return; }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AgentSessions.AsNoTracking().Where(s => ids.Contains(s.Id)).Take(_settings.PageSize)
            .TagWith("stale-working.reconcile")
            .Select(s => new Owner(s.Id, s.StartedAt, s.Status, s.RunnerId, s.RunnerStoreId, s.RunnerCwd)).ToListAsync(ct);
        var working = await TranscriptWorkingStateQuery.ReadAsync(db, rows.Select(s => s.Id).ToArray(), ct);
        var owners = rows.ToDictionary(s => s.Id);
        lock (_gate)
        {
            _reconciled = ids.Length;
            _reconcileCursor = ids.Length < _settings.PageSize ? null : ids[^1];
            foreach (var id in ids)
            {
                if (!owners.TryGetValue(id, out var row) || !row.LocalRunning || !working.TryGetValue(id, out var busy) || !busy)
                    Remove(id);
                else Track(row);
            }
        }
    }

    private void Track(Owner row)
    {
        if (_directory.TryGetValue(row.Id, out var existing))
        {
            if (SessionGeneration.Equal(existing.Generation, row.StartedAt)) return;
            Remove(row.Id);
        }
        if (_directory.Count >= DirectoryCapacity)
        {
            logger.LogWarning("Stale Working directory overflow session {SessionId}, generation {Generation}, epoch {ServerEpoch}, reason {Reason}; discovery advances for a later retry",
                row.Id, row.StartedAt, _epoch, "directory_capacity");
            return;
        }
        _directory.Add(row.Id, new Entry(row.Id, row.StartedAt, clock.GetTimestamp()));
    }
    private void Remove(Guid id)
    {
        if (!_directory.Remove(id, out var entry)) return;
        if (entry.Pending is not null) _pending.Remove(entry.Pending);
        entry.Pending = null;
    }
    private void Reap()
    {
        foreach (var id in _owned.Where(pair => pair.Value.Task.IsCompleted).Select(pair => pair.Key).ToArray()) _owned.Remove(id);
    }
    private double Age(Entry entry) => clock.GetElapsedTime(entry.LastSuccess ?? entry.DiscoveredAt).TotalSeconds;
    private bool Due(Entry entry) => entry.LastSuccess is null || Age(entry) >= _settings.IntervalSeconds;

    private void Admit(CancellationToken ct)
    {
        // Workers refill once per Tick, not on probe completion. T62 binds the host
        // poll cadence; backlog timing includes both probe budget and the next poll.
        Reap();
        foreach (var entry in _directory.Values)
        {
            if (!Due(entry) || entry.Pending is not null || _owned.TryGetValue(entry.SessionId, out var owned) && ReferenceEquals(owned.Entry, entry)) continue;
            if (entry.LastAttempt is long attempt && clock.GetElapsedTime(attempt).TotalSeconds < _settings.PollSeconds) continue;
            entry.Pending = _pending.AddLast(entry);
        }
        var remaining = _pending.Count;
        while (_owned.Count < _settings.MaxConcurrentProbes && remaining-- > 0 && _pending.First is { } first)
        {
            var entry = first.Value;
            _pending.RemoveFirst(); entry.Pending = null;
            if (!_directory.TryGetValue(entry.SessionId, out var current) || !ReferenceEquals(current, entry)) continue;
            if (_owned.ContainsKey(entry.SessionId)) { entry.Pending = _pending.AddLast(entry); continue; }
            entry.LastAttempt = clock.GetTimestamp();
            logger.LogInformation("Stale Working probe due session {SessionId}, generation {Generation}, epoch {ServerEpoch}, reason {Reason}, elapsed {ElapsedAgeSeconds}",
                entry.SessionId, entry.Generation, _epoch, entry.LastSuccess is null ? "unknown_freshness" : "interval_elapsed", Age(entry));
            var operation = new Operation(entry, clock.GetTimestamp());
            _owned.Add(entry.SessionId, operation);
            operation.Task = Task.Run(() => ProbeAsync(operation, ct), CancellationToken.None);
        }
        foreach (var entry in _pending)
            logger.LogWarning("Stale Working capacity deferred session {SessionId}, generation {Generation}, epoch {ServerEpoch}, reason {Reason}, elapsed {ElapsedAgeSeconds}",
                entry.SessionId, entry.Generation, _epoch, "worker_capacity", Age(entry));
    }

    private async Task ProbeAsync(Operation operation, CancellationToken ct)
    {
        var entry = operation.Entry;
        var age = Age(entry);
        TranscriptRecoveryResult result = new(TranscriptRecoveryOutcome.Unknown, "probe_not_completed", entry.Generation, 0, 0, 0, TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.ProbeTimeoutSeconds), clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token, timeout.Token);
        try
        {
            result = await runtime.ProbeWorkingTranscriptAsync(entry.SessionId, budget.Token);
            if (!SessionGeneration.Equal(result.AcceptedGeneration, entry.Generation))
                result = result with { Outcome = TranscriptRecoveryOutcome.Unknown, Reason = "scheduler_generation_changed" };
        }
        catch (Exception ex)
        {
            result = result with { Reason = "probe_fault", Duration = clock.GetElapsedTime(operation.Started) };
            logger.LogWarning(ex, "Stale Working probe fault session {SessionId}, generation {Generation}, epoch {ServerEpoch}", entry.SessionId, entry.Generation, _epoch);
        }
        finally
        {
            bool wake;
            lock (_gate)
            {
                var current = _directory.TryGetValue(entry.SessionId, out var tracked) && ReferenceEquals(tracked, entry);
                if (current && result.Outcome != TranscriptRecoveryOutcome.Unknown) entry.LastSuccess = clock.GetTimestamp();
                // Reconciliation may observe the committed idle verdict and remove
                // this entry before the checked probe returns. Directory membership
                // controls future scheduling, never this operation's owned wakeup.
                // The accepted-generation check above still gates success; the
                // ordinary coalesced flush rechecks current idle before delivery.
                wake = result.Outcome == TranscriptRecoveryOutcome.RecoveredIdle;
            }
            logger.LogInformation("Stale Working probe result session {SessionId}, generation {Generation}, epoch {ServerEpoch}, reason {Reason}, outcome {Outcome}, elapsed {ElapsedAgeSeconds}, pre {PreStoredSequence}, post {PostStoredSequence}, added {AddedCount}, duration {DurationMs}",
                entry.SessionId, entry.Generation, _epoch, result.Reason, result.Outcome.ToString(), age,
                result.PreStoredSequence, result.PostStoredSequence, result.AddedCount, result.Duration.TotalMilliseconds);
            if (result.Outcome == TranscriptRecoveryOutcome.Unknown)
                logger.LogWarning("Stale Working probe unknown session {SessionId}, generation {Generation}, epoch {ServerEpoch}, reason {Reason}; evidence debt retained",
                    entry.SessionId, entry.Generation, _epoch, result.Reason);
            if (wake)
            {
                try { flushes.TryEnqueue(entry.SessionId); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Stale Working completion wakeup failed session {SessionId}; requesting durable recovery sweep", entry.SessionId);
                    flushes.Recovery.RequestSweep();
                }
            }
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        TaskCompletionSource? start = null;
        Task stopped;
        lock (_gate)
        {
            if (_stopTask is null)
            {
                _stopping = true;
                start = new TaskCompletionSource();
                _stopTask = StopCoreAsync(start.Task);
            }
            stopped = _stopTask;
        }
        // Start outside the monitor. T25 holds a save at the owned join, and T26
        // calls shutdown through both host and provider disposal on two epochs.
        start?.TrySetResult();
        return stopped;
    }
    private async Task StopCoreAsync(Task admission)
    {
        await admission.ConfigureAwait(false);
        try { _lifetime.Cancel(); }
        finally
        {
            await _ticks.WaitAsync(CancellationToken.None);
            try { await JoinCurrentProbesAsync(); }
            finally { lock (_gate) Reap(); _ticks.Release(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync(CancellationToken.None);
        _disposed = true; _lifetime.Dispose(); _ticks.Dispose();
    }

    // H4 read-only observations and joins. T21..T25 inspect actual owned operations.
    internal sealed record ObservationEntry(Guid SessionId, DateTime Generation, long? LastSuccess, long? LastAttempt, long DiscoveredAt);
    internal sealed record Observation(Guid ServerEpoch, int Passes, int DiscoveredThisPass, int ReconciledThisPass,
        ObservationEntry[] Directory, ObservationEntry[] Pending, ObservationEntry[] Active);
    private static ObservationEntry Describe(Entry entry) => new(entry.SessionId, entry.Generation, entry.LastSuccess, entry.LastAttempt, entry.DiscoveredAt);
    internal Observation Observe()
    {
        lock (_gate) return new(_epoch, _passes, _discovered, _reconciled, _directory.Values.Select(Describe).ToArray(),
            _pending.Select(Describe).ToArray(), _owned.Values.Where(o => !o.Task.IsCompleted).Select(o => Describe(o.Entry)).ToArray());
    }
    internal Task JoinCurrentProbesAsync()
    { lock (_gate) return Task.WhenAll(_owned.Values.Select(o => o.Task).ToArray()); }
    internal Task JoinCurrentProbesExceptAsync(Guid except)
    { lock (_gate) return Task.WhenAll(_owned.Where(pair => pair.Key != except).Select(pair => pair.Value.Task).ToArray()); }
}
