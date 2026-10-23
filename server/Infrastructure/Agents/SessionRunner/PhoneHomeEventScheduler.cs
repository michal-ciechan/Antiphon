using System.Threading.Channels;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>A fixed worker set; one ready key and at most one active observer per session.
/// Moving an item never changes its connection reservation.</summary>
internal sealed class PhoneHomeEventScheduler(
    PhoneHomeLiveConnection live, int concurrency,
    Func<int, PhoneHomeQueuedEvent, SessionRunnerEvent, CancellationToken, Task> observe,
    Action<PhoneHomeQueuedEvent, Exception> failed)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SessionQueue> _sessions = new();
    private readonly Channel<Guid> _ready = Channel.CreateUnbounded<Guid>();
    private bool _inputComplete;

    public async Task RunAsync(CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, live.Closed);
        var workers = Enumerable.Range(0, concurrency)
            .Select(worker => Task.Run(() => WorkerAsync(worker, stop.Token), CancellationToken.None)).ToArray();
        var dispatch = DispatchAsync(stop.Token);
        var work = Task.WhenAll(workers);
        try
        {
            if (await Task.WhenAny(dispatch, work) == work && !dispatch.IsCompleted) stop.Cancel();
            await dispatch;
            await work;
        }
        finally
        {
            stop.Cancel();
            _ready.Writer.TryComplete();
            try { await Task.WhenAll(dispatch, work); } catch { /* preserve the primary outcome */ }
            // Closed/cancelled groups discard only their own unread reservations. They do not
            // manufacture observation credit, and all active observers have already joined.
            foreach (var queue in _sessions.Values)
                while (queue.Items.TryDequeue(out var remaining)) remaining.Item.Abandon();
            _sessions.Clear();
            while (live.Events.TryRead(out var unread)) unread.Abandon();
        }
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var item in live.Events.ReadAllAsync(ct))
            {
                var transferred = false;
                var started = live.Clock.GetTimestamp();
                try
                {
                    var frame = item.Frame;
                    if (frame.EventName is null || frame.Payload is null) continue;
                    var parsed = RunnerContractMapper.ParseEvent(frame.EventName, frame.Payload.Value.GetRawText());
                    if (parsed is null) continue;
                    live.Diagnostics.Producer(parsed.SessionId, item.PayloadBytes);
                    lock (_gate)
                    {
                        if (!_sessions.TryGetValue(parsed.SessionId, out var queue))
                            _sessions.Add(parsed.SessionId, queue = new());
                        queue.Items.Enqueue((item, parsed));
                        transferred = true;
                        if (!queue.Active && !queue.Ready)
                        {
                            queue.Ready = true;
                            _ready.Writer.TryWrite(parsed.SessionId);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { failed(item, ex); }
                finally
                {
                    live.Diagnostics.Parsed(started);
                    if (!transferred) item.Release();
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _inputComplete = true;
                if (_sessions.Count == 0) _ready.Writer.TryComplete();
            }
        }
    }

    private async Task WorkerAsync(int worker, CancellationToken ct)
    {
        await foreach (var id in _ready.Reader.ReadAllAsync(ct))
        {
            SessionQueue queue;
            (PhoneHomeQueuedEvent Item, SessionRunnerEvent Parsed) next;
            lock (_gate)
            {
                queue = _sessions[id];
                queue.Ready = false;
                queue.Active = true;
                next = queue.Items.Dequeue();
            }
            try { await observe(worker, next.Item, next.Parsed, ct); }
            finally
            {
                next.Item.Release();
                live.Diagnostics.End(worker);
                lock (_gate)
                {
                    queue.Active = false;
                    if (queue.Items.Count == 0) _sessions.Remove(id);
                    else
                    {
                        queue.Ready = true;
                        _ready.Writer.TryWrite(id);
                    }
                    if (_inputComplete && _sessions.Count == 0) _ready.Writer.TryComplete();
                }
            }
        }
    }

    private sealed class SessionQueue
    {
        public Queue<(PhoneHomeQueuedEvent Item, SessionRunnerEvent Parsed)> Items { get; } = new();
        public bool Active;
        public bool Ready;
    }
}
