namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1428 D-4: the per-task lifetime of same-card inspection entries, kept apart from
/// <see cref="CardSiblingInspectionQueue"/>'s workers, channel and cancellation. Pure in-memory state
/// on a <see cref="TimeProvider"/>; it has no claim or launch authority, and claim-time revalidation
/// under the lease stays the only authority over a result. A result is not consumed by being read:
/// it survives a lease refusal and the other pre-claim holds until its owner invalidates it, its
/// intent changes, the task leaves the queue, or <see cref="RetainedResultMaxAge"/> passes.
/// </summary>
public sealed class SiblingInspectionResults(TimeProvider clock)
{
    /// <summary>
    /// D-5: a completed result is valid for at most this long from completion. About two typical
    /// lands; it bounds how old the advisory sibling warnings on a cut can be. Correctness does not
    /// rest on it: the claim revalidates intent, inventory metadata and every frozen ref under the lease.
    /// </summary>
    public static readonly TimeSpan RetainedResultMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>D-6: entries the owned workers still have to inspect; <see cref="Begin"/> refuses past this.</summary>
    public const int InFlightCapacity = 32;

    /// <summary>D-6: completed results kept for reuse; past this the oldest completed entry is evicted first.</summary>
    public const int RetainedCapacity = 256;

    /// <summary>One task's entry: the intent it answers, the owned cancellation, and the result once complete.</summary>
    public sealed class Entry
    {
        internal Entry(CardSiblingInspectionIntent intent) => Intent = intent;
        public CardSiblingInspectionIntent Intent { get; }
        internal CancellationTokenSource Cancel { get; } = new();
        internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long CompletedAt { get; set; }
        public CancellationToken Cancellation => Cancel.Token;
        public SiblingBaseGuard? Result { get; internal set; }
        public bool IsCompleted => Done.Task.IsCompleted;
        public Task Completion => Done.Task;
    }

    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    /// <summary>Every entry, in flight or completed.</summary>
    public int Count { get { lock (_sync) return _entries.Count; } }

    /// <summary>Entries the workers have not completed.</summary>
    public int InFlightCount { get { lock (_sync) return InFlightUnlocked(); } }

    /// <summary>
    /// Admits an inspection for <paramref name="intent"/>. The same live intent is deduplicated
    /// (<paramref name="started"/> false, the existing entry returned); a different intent evicts the
    /// old entry, and null means refused: cancelled work for this task is still joining, or the
    /// in-flight capacity is reached. A started entry must reach a worker or be <see cref="Abandon"/>ed.
    /// </summary>
    public Entry? Begin(CardSiblingInspectionIntent intent, out bool started)
    {
        started = false;
        lock (_sync)
        {
            if (_entries.TryGetValue(intent.TaskId, out var existing))
            {
                if (existing.Intent == intent && !existing.Cancel.IsCancellationRequested) return existing;
                Evict(existing);
                if (_entries.ContainsKey(intent.TaskId)) return null;
            }
            if (InFlightUnlocked() >= InFlightCapacity) return null;
            TrimRetained();
            var entry = new Entry(intent);
            _entries.Add(intent.TaskId, entry);
            started = true;
            return entry;
        }
    }

    /// <summary>The caller could not hand a started entry to a worker.</summary>
    public void Abandon(Entry entry)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(entry.Intent.TaskId, out var current) || !ReferenceEquals(current, entry)) return;
            _entries.Remove(entry.Intent.TaskId);
            entry.Cancel.Dispose();
        }
    }

    /// <summary>The owned worker finished: a result, null when it skipped (the task changed), or cancelled.</summary>
    public void Complete(Entry entry, SiblingBaseGuard? result)
    {
        lock (_sync)
        {
            entry.Result = result;
            entry.CompletedAt = clock.GetTimestamp();
            entry.Done.TrySetResult();
            if (entry.Cancel.IsCancellationRequested && _entries.TryGetValue(entry.Intent.TaskId, out var current)
                && ReferenceEquals(current, entry))
                Evict(entry);
        }
    }

    /// <summary>
    /// D-4: the completed, uncancelled, unexpired result for an equal intent, left in place for the
    /// next attempt. A different intent evicts the entry; a skipped, cancelled or expired result is
    /// evicted and null, so the caller enqueues a fresh inspection.
    /// </summary>
    public SiblingBaseGuard? Peek(CardSiblingInspectionIntent intent)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(intent.TaskId, out var entry)) return null;
            if (entry.Intent != intent) { Evict(entry); return null; }
            if (!entry.IsCompleted) return null;
            if (entry.Cancel.IsCancellationRequested || entry.Result is null
                || clock.GetElapsedTime(entry.CompletedAt) >= RetainedResultMaxAge)
            {
                Evict(entry);
                return null;
            }
            return entry.Result;
        }
    }

    /// <summary>The task's entry is no longer valid: cancelled if in flight, removed if complete.</summary>
    public void Invalidate(Guid taskId)
    {
        lock (_sync)
            if (_entries.TryGetValue(taskId, out var entry)) Evict(entry);
    }

    /// <summary>Start of a tick: entries for tasks no longer queued, and expired results, are evicted.</summary>
    public void Reconcile(IReadOnlySet<Guid> queued)
    {
        lock (_sync)
        {
            foreach (var entry in _entries.Values.ToArray())
                if (!queued.Contains(entry.Intent.TaskId) || entry.IsCompleted
                    && clock.GetElapsedTime(entry.CompletedAt) >= RetainedResultMaxAge)
                    Evict(entry);
        }
    }

    /// <summary>The completion of every entry, for an idle wait.</summary>
    public Task[] Completions()
    {
        lock (_sync) return _entries.Values.Select(entry => entry.Completion).ToArray();
    }

    /// <summary>Shutdown, after the owned workers have joined: every entry is completed and released.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Done.TrySetResult();
                entry.Cancel.Dispose();
            }
            _entries.Clear();
        }
    }

    private int InFlightUnlocked() => _entries.Values.Count(entry => !entry.IsCompleted);

    // D-6: retained results have their own bound; the oldest completed entries go first.
    private void TrimRetained()
    {
        var completed = _entries.Values.Where(entry => entry.IsCompleted).ToList();
        if (completed.Count < RetainedCapacity) return;
        foreach (var oldest in completed.OrderBy(entry => entry.CompletedAt).Take(completed.Count - RetainedCapacity + 1))
            Evict(oldest);
    }

    // Only on an entry still in the dictionary: a removed entry's cancellation is disposed.
    private void Evict(Entry entry)
    {
        entry.Cancel.Cancel();
        // Keep cancelled work counted until the owned worker has joined it.
        if (entry.IsCompleted)
        {
            _entries.Remove(entry.Intent.TaskId);
            entry.Cancel.Dispose();
        }
    }
}
