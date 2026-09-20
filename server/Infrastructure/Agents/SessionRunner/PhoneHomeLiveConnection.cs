using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class PhoneHomeLiveConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly PhoneHomeLimits _limits;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PhoneHomeFrame>> _waiters = new();
    private readonly Channel<PhoneHomeFrame> _events = Channel.CreateUnbounded<PhoneHomeFrame>();
    private int _inFlight;
    private int _pendingEvents;
    private int _pendingEventBytes;
    private int _liveBufferEvents;
    private int _liveBufferBytes;

    public PhoneHomeLiveConnection(
        string runnerId,
        Guid runnerStoreId,
        Guid processBootId,
        long epoch,
        WebSocket socket,
        PhoneHomeLimits limits,
        TimeProvider clock)
    {
        RunnerId = runnerId;
        RunnerStoreId = runnerStoreId;
        ProcessBootId = processBootId;
        Epoch = epoch;
        _socket = socket;
        _limits = limits;
        Clock = clock;
        LastHeartbeatUtc = clock.GetUtcNow();
    }

    public string RunnerId { get; }
    public Guid RunnerStoreId { get; }
    public Guid ProcessBootId { get; }
    public long Epoch { get; }
    public TimeProvider Clock { get; }
    public DateTimeOffset LastHeartbeatUtc { get; private set; }
    public bool DispatchEligible { get; set; }
    public bool SocketOpen => _socket.State == WebSocketState.Open;
    public ChannelReader<PhoneHomeFrame> Events => _events.Reader;
    public int InFlight => Volatile.Read(ref _inFlight);
    public int PendingEvents => Volatile.Read(ref _pendingEvents);
    internal int LiveBufferEvents => Volatile.Read(ref _liveBufferEvents);
    internal int LiveBufferBytes => Volatile.Read(ref _liveBufferBytes);

    public void NoteHeartbeat(DateTimeOffset at) => LastHeartbeatUtc = at;

    public bool IsLeaseExpired(TimeSpan lease) =>
        Clock.GetUtcNow() - LastHeartbeatUtc > lease;

    public async Task<PhoneHomeFrame> RequestAsync(PhoneHomeOperation operation, object? payload, CancellationToken ct)
    {
        if (!DispatchEligible && operation is PhoneHomeOperation.Launch or PhoneHomeOperation.Input
            or PhoneHomeOperation.ConditionalInput or PhoneHomeOperation.KillGeneration
            or PhoneHomeOperation.ClearBuffer or PhoneHomeOperation.Resize)
            throw new InvalidOperationException("Phone-home connection is not dispatch-eligible.");
        if (Volatile.Read(ref _inFlight) >= _limits.MaxInFlightRequests)
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.RequestLimit, "In-flight request limit reached.");

        var id = Guid.NewGuid();
        var waiter = new TaskCompletionSource<PhoneHomeFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[id] = waiter;
        Interlocked.Increment(ref _inFlight);
        var frame = new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, Epoch, id, operation,
            payload is null ? null : JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));
        try
        {
            await _send.WaitAsync(ct);
            try
            {
                await PhoneHomeFraming.WriteFrameAsync(_socket, frame, _limits.MaxMessageUtf8Bytes, ct);
            }
            finally
            {
                _send.Release();
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var completed = await Task.WhenAny(waiter.Task, Task.Delay(Timeout.Infinite, linked.Token));
            if (completed != waiter.Task)
            {
                _waiters.TryRemove(id, out _);
                throw new OperationCanceledException(ct);
            }

            var result = await waiter.Task;
            if (result.Epoch != Epoch)
                throw new PhoneHomeTransportException(PhoneHomeProblemTypes.StaleEpoch, "Stale epoch reply.");
            return result;
        }
        catch
        {
            _waiters.TryRemove(id, out _);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            var frame = await PhoneHomeFraming.ReadFrameAsync(_socket, _limits.MaxMessageUtf8Bytes, ct);
            if (frame is null)
                break;
            if (frame.Epoch != Epoch)
                continue;
            if (frame.Kind == PhoneHomeFrameKind.Heartbeat)
            {
                NoteHeartbeat(Clock.GetUtcNow());
                continue;
            }

            if (frame.Kind is PhoneHomeFrameKind.Result or PhoneHomeFrameKind.Error)
            {
                if (_waiters.TryRemove(frame.RequestId, out var waiter))
                    waiter.TrySetResult(frame);
                continue;
            }

            if (frame.Kind == PhoneHomeFrameKind.Event)
            {
                var size = frame.Payload?.GetRawText().Length ?? 0;
                if (Volatile.Read(ref _pendingEvents) + 1 > _limits.MaxPendingEvents
                    || Volatile.Read(ref _pendingEventBytes) + size > _limits.MaxPendingEventBytes)
                    throw new PhoneHomeTransportException(PhoneHomeProblemTypes.EventOverflow, "Pending live events overflowed.");
                Interlocked.Increment(ref _pendingEvents);
                Interlocked.Add(ref _pendingEventBytes, size);
                Interlocked.Increment(ref _liveBufferEvents);
                Interlocked.Add(ref _liveBufferBytes, size);
                await _events.Writer.WriteAsync(frame, ct);
            }
        }
    }

    public void ReleaseEvent(int bytes)
    {
        Interlocked.Decrement(ref _pendingEvents);
        Interlocked.Add(ref _pendingEventBytes, -bytes);
    }

    public async ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        foreach (var waiter in _waiters.Values)
            waiter.TrySetCanceled();
        _waiters.Clear();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None);
        }
        catch { /* already closed */ }
        _socket.Dispose();
        _send.Dispose();
    }
}
