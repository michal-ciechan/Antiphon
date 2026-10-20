using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// In-process hand-off for explicit branch landings (CARD-0331). The durable fact that a land
/// is wanted lives on <c>AgentTaskLandRequests</c>; wakeups only request reconciliation.
/// There is one global single reader. <see cref="Capture"/> is the ordered
/// snapshot of the entry now executing and the entries still waiting.
/// </summary>
public sealed class AgentTaskLandQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, LandRequest> _active = new();
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly List<Tracked> _waiting = new();
    private readonly HashSet<Guid> _attempted = new();
    private Tracked? _executing;
    private long _nextEntryId;
    private bool _durableReader;
    private bool _reconciled;

    // Serializes complete database snapshots, not execution. A failed snapshot never opens
    // the reader or replaces the last complete ready set.
    internal SemaphoreSlim ReconciliationGate { get; } = new(1, 1);
    public void Wake() => _wake.Writer.TryWrite(true);
    internal void RequireReconciliation()
    {
        lock (_gate) { _durableReader = true; _reconciled = false; }
    }
    internal void InvalidateSnapshot()
    {
        lock (_gate) _reconciled = false;
    }

    internal bool IsExecuting(Guid taskId)
    {
        lock (_gate) return _executing?.Request.TaskId == taskId;
    }

    internal void Reconcile(IReadOnlyList<LandRequest> pending, bool nextGeneration)
    {
        lock (_gate)
        {
            if (nextGeneration) _attempted.Clear();
            var identities = pending.Where(r => r.RequestId.HasValue).Select(r => r.RequestId!.Value).ToHashSet();
            _attempted.RemoveWhere(id => !identities.Contains(id)
                && _executing?.Request.RequestId != id);
            foreach (var waiting in _waiting)
                if (_active.TryGetValue(waiting.Request.TaskId, out var active)
                    && ReferenceEquals(active, waiting.Request))
                    _active.Remove(waiting.Request.TaskId);
            _waiting.Clear();
            foreach (var request in pending.OrderBy(r => r.RequestedAt)
                         .ThenBy(r => r.RequestId?.ToString("N"), StringComparer.Ordinal))
            {
                if (_executing?.Request.TaskId == request.TaskId
                    || request.RequestId is Guid id && _attempted.Contains(id)) continue;
                _active[request.TaskId] = request;
                _waiting.Add(new Tracked(++_nextEntryId, request));
            }
            _reconciled = true;
        }
        if (nextGeneration) Wake();
    }

    /// <summary>
    /// Record a volatile hint. The production reader must reconcile the complete durable
    /// snapshot before consuming it. Returns false when this task is already queued or running.
    /// </summary>
    public bool TryEnqueue(Guid taskId, string? verifyFilter, Guid? requestId = null)
    {
        var item = new LandRequest(taskId, verifyFilter, requestId);
        lock (_gate)
        {
            if (_active.ContainsKey(taskId))
                return false;
            _active.Add(taskId, item);
            _waiting.Add(new Tracked(++_nextEntryId, item));
            _reconciled = false;
            Wake();
            return true;
        }
    }

    /// <summary>Queued or running in this process, now.</summary>
    public bool IsActive(Guid taskId)
    {
        lock (_gate)
            return _active.ContainsKey(taskId);
    }

    /// <summary>
    /// Drop the dedup claim and the executing entry. An item still unread in the channel stays
    /// in the snapshot: early release followed by requeue is a new entry, not a rewrite of the old one.
    /// </summary>
    public void Release(Guid taskId)
    {
        lock (_gate)
        {
            _active.Remove(taskId);
            if (_executing is not null && _executing.Request.TaskId == taskId)
                _executing = null;
        }
    }

    /// <summary>Finish only this channel entry; a later requeue keeps its own claim.</summary>
    public void Release(LandRequest item)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(item.TaskId, out var claimed) && ReferenceEquals(claimed, item))
                _active.Remove(item.TaskId);
            if (_executing is not null && ReferenceEquals(_executing.Request, item))
                _executing = null;
        }
    }

    public async IAsyncEnumerable<LandRequest> ReadAllAsync([EnumeratorCancellation] CancellationToken ct,
        Func<CancellationToken, Task<bool>>? reconcile = null)
    {
        while (!ct.IsCancellationRequested)
        {
            // The first selection is behind the same full reconciliation as every later
            // selection. A wake is only a hint, including during startup.
            if ((reconcile is null || await reconcile(ct)) && TryDequeue(out var request))
            {
                yield return request;
                continue;
            }
            await _wake.Reader.ReadAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ids queued or running in this process. Unbounded SingleReader channels do not
    /// implement <c>Reader.Count</c>; the active set is the durable in-process fact.
    /// </summary>
    public int PendingCount
    {
        get { lock (_gate) return _active.Count; }
    }

    /// <summary>Take one claim without waiting; false when the queue is empty (tests).</summary>
    public bool TryDequeue(out LandRequest request)
    {
        lock (_gate)
        {
            if (_waiting.Count == 0 || _durableReader && !_reconciled)
            {
                request = null!;
                return false;
            }
            request = _waiting[0].Request;
            if (request.RequestId is Guid id) _attempted.Add(id);
            Promote(request);
            return true;
        }
    }

    /// <summary>Ordered snapshot. A new instance has no executing owner and no waiting entries.</summary>
    public LandQueueSnapshot Capture(DateTime observedAt)
    {
        lock (_gate)
        {
            if (_durableReader && !_reconciled)
                return new LandQueueSnapshot(observedAt, _executing?.ToEntry(), []);
            var waiting = new LandQueueEntry[_waiting.Count];
            for (var i = 0; i < _waiting.Count; i++)
                waiting[i] = _waiting[i].ToEntry();
            return new LandQueueSnapshot(observedAt, _executing?.ToEntry(), waiting);
        }
    }

    private void Promote(LandRequest item)
    {
        var index = _waiting.FindIndex(entry => ReferenceEquals(entry.Request, item));
        if (index < 0)
            index = _waiting.FindIndex(entry => entry.Request.TaskId == item.TaskId && entry.Request.RequestId == item.RequestId);
        if (index < 0)
        {
            _executing = new Tracked(++_nextEntryId, item);
            return;
        }

        var tracked = _waiting[index];
        _waiting.RemoveAt(index);
        _executing = tracked;
    }

    private sealed class Tracked(long entryId, LandRequest request)
    {
        public LandRequest Request { get; } = request;
        public LandQueueEntry ToEntry() => new(entryId, Request.TaskId, Request.RequestId);
    }

    public sealed record LandRequest(Guid TaskId, string? VerifyFilter, Guid? RequestId = null,
        DateTime? RequestedAt = null);
}

/// <summary>One accepted channel entry. EntryId distinguishes a requeue from an unread older item.</summary>
public sealed record LandQueueEntry(long EntryId, Guid TaskId, Guid? RequestId);

/// <summary>Executing entry plus waiting entries in channel order. Position is one-based among waiting entries only.</summary>
public sealed record LandQueueSnapshot(DateTime ObservedAt, LandQueueEntry? Executing, IReadOnlyList<LandQueueEntry> Waiting)
{
    public int WaitingCount => Waiting.Count;

    public int? WaitingPosition(Guid taskId, Guid? requestId)
    {
        for (var i = 0; i < Waiting.Count; i++)
        {
            if (Matches(Waiting[i], taskId, requestId))
                return i + 1;
        }

        return null;
    }

    public bool IsExecuting(Guid taskId, Guid? requestId) =>
        Executing is not null && Matches(Executing, taskId, requestId);

    private static bool Matches(LandQueueEntry entry, Guid taskId, Guid? requestId)
    {
        if (requestId is Guid id)
            return entry.RequestId == id;
        return entry.TaskId == taskId && entry.RequestId is null;
    }
}
