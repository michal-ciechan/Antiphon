using System.Threading.Channels;
using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>Bounded wakeups only. Persisted Pending completion notes are the recovery source.</summary>
public class CompletionNoteFlushQueue
{
    internal CompletionNoteRecoveryQueue Recovery { get; } = new();
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(128)
    { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    public virtual bool TryEnqueue(Guid sessionId)
    {
        // Preserve a publication that arrives while this session is already being flushed.
        if (!_pending.TryAdd(sessionId, 0))
        {
            if (_pending.TryUpdate(sessionId, 1, 0) || _pending.ContainsKey(sessionId)) return true;
            return TryEnqueue(sessionId);
        }
        if (_channel.Writer.TryWrite(sessionId)) return true;
        _pending.TryRemove(sessionId, out _);
        Recovery.RequestSweep();
        return false;
    }
    public void Complete(Guid sessionId)
    {
        if (_pending.TryRemove(sessionId, out var again) && again != 0) TryEnqueue(sessionId);
    }
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Coalesced hints, never delivery evidence. Overflow requests a durable sweep. The single
/// consumer takes IDs before doing I/O, so a concurrent event cannot be erased on completion.
/// Held sessions retain one deadline each; they wake without polling the database.
/// </summary>
internal sealed class CompletionNoteRecoveryQueue
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _tasks = [];
    private readonly Dictionary<Guid, DateTime> _sessions = [];
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(1);
    private bool _sweep;

    public void Check(Guid taskId)
    {
        lock (_gate)
        {
            if (_tasks.Count < 128) _tasks.Add(taskId);
            else _sweep = true;
            _wake.Writer.TryWrite(true);
        }
    }

    public void ScheduleFlush(Guid sessionId, DateTime notBefore)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var previous) || notBefore < previous)
                _sessions[sessionId] = notBefore;
            _wake.Writer.TryWrite(true);
        }
    }

    public void RequestSweep()
    {
        lock (_gate) { _sweep = true; _wake.Writer.TryWrite(true); }
    }

    public (bool Sweep, Guid[] Tasks, Guid[] Sessions) Take(DateTime now)
    {
        lock (_gate)
        {
            _wake.Reader.TryRead(out _);
            var tasks = _tasks.ToArray();
            _tasks.Clear();
            var sessions = _sessions.Where(s => s.Value <= now).Select(s => s.Key).ToArray();
            foreach (var session in sessions) _sessions.Remove(session);
            var sweep = _sweep;
            _sweep = false;
            return (sweep, tasks, sessions);
        }
    }

    public async Task WaitAsync(DateTime backstop, TimeProvider clock, CancellationToken ct)
    {
        DateTime due;
        lock (_gate) due = _sessions.Values.Append(backstop).Min();
        var delay = due - clock.GetUtcNow().UtcDateTime;
        if (delay <= TimeSpan.Zero) return;
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var signal = _wake.Reader.WaitToReadAsync(wait.Token).AsTask();
        var timer = Task.Delay(delay, clock, wait.Token);
        try { await await Task.WhenAny(signal, timer); }
        finally { await wait.CancelAsync(); }
    }
}

public sealed record SpecialistFailure(SpecialistSpec Spec, string Reason);

/// <summary>Optional incident delivery cannot occupy the serial model-request worker.</summary>
public sealed class SpecialistFailureQueue
{
    private readonly Channel<SpecialistFailure> _channel = Channel.CreateBounded<SpecialistFailure>(32);
    public bool TryEnqueue(SpecialistFailure failure) => _channel.Writer.TryWrite(failure);
    public IAsyncEnumerable<SpecialistFailure> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
