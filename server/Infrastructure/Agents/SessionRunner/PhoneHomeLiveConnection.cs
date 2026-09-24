using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class PhoneHomeLiveConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly PhoneHomeLimits _limits;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Waiter> _waiters = new();
    private readonly Channel<PhoneHomeFrame> _events = Channel.CreateUnbounded<PhoneHomeFrame>();
    private int _inFlight;
    private int _pendingEvents;
    private int _pendingEventBytes;
    private int _liveBufferEvents;
    private int _liveBufferBytes;
    private string? _disconnectReason;
    private DateTimeOffset? _disconnectAtUtc;
    private readonly object _disconnectGate = new();
    private bool _warnedHalf;
    private bool _warnedHigh;
    private string? _closedReason;

    // CARD-0679 D-1: released events per wall-clock second, the last ten seconds, for the
    // high-water line's pump rate.
    private readonly object _rateGate = new();
    private readonly long[] _releaseSecond = new long[RateWindowSeconds];
    private readonly int[] _releaseCount = new int[RateWindowSeconds];
    private const int RateWindowSeconds = 10;

    public PhoneHomeLiveConnection(
        string runnerId,
        Guid runnerStoreId,
        Guid processBootId,
        long epoch,
        WebSocket socket,
        PhoneHomeLimits limits,
        TimeProvider clock,
        int capacity = 1,
        string? platform = null,
        RunnerCapabilitiesDto? capabilities = null)
    {
        RunnerId = runnerId;
        RunnerStoreId = runnerStoreId;
        ProcessBootId = processBootId;
        Epoch = epoch;
        _socket = socket;
        _limits = limits;
        Clock = clock;
        Capacity = capacity;
        Platform = platform;
        Capabilities = capabilities;
        LastHeartbeatUtc = clock.GetUtcNow();
        StartedAtUtc = LastHeartbeatUtc;
    }

    /// <summary>CARD-0679 D-1: when the directory accepted this connection.</summary>
    public DateTimeOffset StartedAtUtc { get; }

    public string RunnerId { get; }
    public Guid RunnerStoreId { get; }
    public Guid ProcessBootId { get; }
    public long Epoch { get; }

    /// <summary>CARD-0604 D-14: how many concurrent sessions this runner declared it can hold.</summary>
    public int Capacity { get; }

    /// <summary>What the runner reported at registration ("linux"/"windows"), for diagnostics.</summary>
    public string? Platform { get; }

    /// <summary>The capabilities DTO the runner sent with its registration, or null.</summary>
    public RunnerCapabilitiesDto? Capabilities { get; }
    public TimeProvider Clock { get; }
    public DateTimeOffset LastHeartbeatUtc { get; private set; }
    public bool DispatchEligible { get; set; }
    public bool SocketOpen => _socket.State == WebSocketState.Open;
    internal WebSocketState SocketState => _socket.State;
    public ChannelReader<PhoneHomeFrame> Events => _events.Reader;
    public int InFlight => Volatile.Read(ref _inFlight);
    public int PendingEvents => Volatile.Read(ref _pendingEvents);
    public int PendingEventBytes => Volatile.Read(ref _pendingEventBytes);
    internal int LiveBufferEvents => Volatile.Read(ref _liveBufferEvents);
    internal int LiveBufferBytes => Volatile.Read(ref _liveBufferBytes);

    /// <summary>Requests still waiting for a reply; a dispose fails each of them.</summary>
    internal int PendingWaiters => _waiters.Count;

    /// <summary>CARD-0679 D-1: the first reason recorded for this connection's end, or null while it lives.</summary>
    public string? LastDisconnectReason => Volatile.Read(ref _disconnectReason);

    /// <summary>CARD-0679 D-1: when <see cref="LastDisconnectReason"/> was recorded.</summary>
    public DateTimeOffset? LastDisconnectAtUtc
    {
        get
        {
            lock (_disconnectGate)
                return _disconnectAtUtc;
        }
    }

    /// <summary>
    /// CARD-0679 D-1/D-10: how long the last recovery List took, in milliseconds; null until the
    /// recovery pump records one.
    /// </summary>
    public long? LastCatchUpMs { get; set; }

    /// <summary>
    /// CARD-0679 D-1: records why this connection ended. The first writer wins, so the route's
    /// classified reason is not overwritten by a later generic one. Returns false when a reason
    /// was already recorded.
    /// </summary>
    internal bool TryRecordDisconnect(string reason)
    {
        lock (_disconnectGate)
        {
            if (_disconnectReason is not null)
                return false;
            _disconnectAtUtc = Clock.GetUtcNow();
            Volatile.Write(ref _disconnectReason, reason);
            return true;
        }
    }

    public void NoteHeartbeat(DateTimeOffset at) => LastHeartbeatUtc = at;

    public bool IsLeaseExpired(TimeSpan lease) =>
        Clock.GetUtcNow() - LastHeartbeatUtc > lease;

    // A mirror fetches a branch from origin and adds a worktree on the runner, so it gets far more
    // room than a control request; everything else is an in-memory runner operation.
    internal static TimeSpan RequestTimeoutFor(PhoneHomeOperation operation) => operation switch
    {
        PhoneHomeOperation.WorkspaceMirror or PhoneHomeOperation.WorkspaceRemove => TimeSpan.FromMinutes(5),
        PhoneHomeOperation.Launch => TimeSpan.FromMinutes(2),
        _ => TimeSpan.FromSeconds(60),
    };

    public async Task<PhoneHomeFrame> RequestAsync(PhoneHomeOperation operation, object? payload, CancellationToken ct)
    {
        // CARD-0679 D-5: a request on a connection that already closed is the same typed transport
        // loss as one the close caught in flight, not a raw WebSocketException from the send.
        if (Volatile.Read(ref _closedReason) is not null || _socket.State != WebSocketState.Open)
            throw Closed(operation, Volatile.Read(ref _closedReason) ?? $"socket {_socket.State}");
        if (!DispatchEligible && operation is PhoneHomeOperation.Launch or PhoneHomeOperation.Input
            or PhoneHomeOperation.ConditionalInput or PhoneHomeOperation.KillGeneration
            or PhoneHomeOperation.ClearBuffer or PhoneHomeOperation.Resize)
            throw new InvalidOperationException(Antiphon.Server.Application.Services.PhoneHomeTransportLoss.NotDispatchEligibleMessage);
        if (Volatile.Read(ref _inFlight) >= _limits.MaxInFlightRequests)
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.RequestLimit, "In-flight request limit reached.");

        var id = Guid.NewGuid();
        var waiter = new Waiter(operation,
            new TaskCompletionSource<PhoneHomeFrame>(TaskCreationOptions.RunContinuationsAsynchronously));
        _waiters[id] = waiter;
        // A dispose that ran between the check above and the registration never saw this waiter.
        if (Volatile.Read(ref _closedReason) is { } raced && _waiters.TryRemove(id, out _))
            throw Closed(operation, raced);
        Interlocked.Increment(ref _inFlight);
        var frame = new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, Epoch, id, operation,
            payload is null ? null : JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));
        try
        {
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
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                throw Closed(operation, Volatile.Read(ref _closedReason) ?? $"send failed: {ex.GetType().Name}");
            }

            // CARD-0629: this used to wait on Timeout.Infinite. A runner that never answers one
            // request (seen live on 2026-09-23: a WorkspaceMirror to server2) then froze the caller
            // forever — and the dispatcher's tick is serial, so one silent reply stopped dispatch
            // fleet-wide for hours while holding a claim transaction open. Bound every request.
            var timeout = RequestTimeoutFor(operation);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var completed = await Task.WhenAny(waiter.Reply.Task, Task.Delay(timeout, Clock, linked.Token));
            linked.Cancel(); // release the delay timer on the success path
            if (completed != waiter.Reply.Task)
            {
                _waiters.TryRemove(id, out _);
                ct.ThrowIfCancellationRequested();
                throw new PhoneHomeTransportException(
                    PhoneHomeProblemTypes.RequestTimeout,
                    $"Runner did not reply to {operation} within {timeout.TotalSeconds:0}s.");
            }

            var result = await waiter.Reply.Task;
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

    public async Task ReceiveLoopAsync(CancellationToken ct, ILogger? logger = null)
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
                    waiter.Reply.TrySetResult(frame);
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
                if (logger is not null)
                    WarnHighWater(logger);
                await _events.Writer.WriteAsync(frame, ct);
            }
        }
    }

    /// <summary>
    /// CARD-0679 D-1: the backlog is visible before the overflow closes the socket. Warned once
    /// per connection at 50% and once at 90% of the event or byte cap, whichever is fuller.
    /// </summary>
    private void WarnHighWater(ILogger logger)
    {
        var events = Volatile.Read(ref _pendingEvents);
        var bytes = Volatile.Read(ref _pendingEventBytes);
        var fill = Math.Max(
            (double)events / _limits.MaxPendingEvents,
            (double)bytes / _limits.MaxPendingEventBytes);
        int percent;
        if (fill >= 0.9 && !_warnedHigh)
        {
            _warnedHigh = _warnedHalf = true;
            percent = 90;
        }
        else if (fill >= 0.5 && !_warnedHalf)
        {
            _warnedHalf = true;
            percent = 50;
        }
        else
        {
            return;
        }

        logger.LogWarning(
            "Phone-home connection {RunnerId} epoch {Epoch} pending live events passed {Percent}% of the cap: "
            + "{PendingEvents}/{MaxPendingEvents} events, {PendingEventBytes}/{MaxPendingEventBytes} bytes; "
            + "pump released {EventsPerSecond:0.0} events/s over the last 10s",
            RunnerId, Epoch, percent, events, _limits.MaxPendingEvents, bytes, _limits.MaxPendingEventBytes,
            ReleasedEventsPerSecond());
    }

    /// <summary>CARD-0679 D-1: the pump's release rate over the last ten seconds.</summary>
    internal double ReleasedEventsPerSecond()
    {
        var now = Clock.GetUtcNow().ToUnixTimeSeconds();
        var total = 0;
        lock (_rateGate)
        {
            for (var i = 0; i < RateWindowSeconds; i++)
            {
                if (now - _releaseSecond[i] < RateWindowSeconds)
                    total += _releaseCount[i];
            }
        }

        return total / (double)RateWindowSeconds;
    }

    public void ReleaseEvent(int bytes)
    {
        Interlocked.Decrement(ref _pendingEvents);
        Interlocked.Add(ref _pendingEventBytes, -bytes);
        var second = Clock.GetUtcNow().ToUnixTimeSeconds();
        var slot = (int)(second % RateWindowSeconds);
        lock (_rateGate)
        {
            if (_releaseSecond[slot] != second)
            {
                _releaseSecond[slot] = second;
                _releaseCount[slot] = 0;
            }

            _releaseCount[slot]++;
        }
    }

    public ValueTask DisposeAsync() => DisposeAsync("dispose");

    /// <summary>
    /// CARD-0679 D-5: closing fails every request still waiting with a typed
    /// <see cref="PhoneHomeProblemTypes.ConnectionClosed"/> naming the runner, epoch, operation and
    /// <paramref name="reason"/>. It used to cancel them, and a cancellation reads as the caller's
    /// own token to every <c>catch (OperationCanceledException)</c> on the way up.
    /// </summary>
    public async ValueTask DisposeAsync(string reason)
    {
        Interlocked.CompareExchange(ref _closedReason, reason, null);
        var closedReason = Volatile.Read(ref _closedReason) ?? reason;
        _events.Writer.TryComplete();
        foreach (var (id, waiter) in _waiters)
        {
            if (_waiters.TryRemove(id, out _))
                waiter.Reply.TrySetException(Closed(waiter.Operation, closedReason));
        }
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None);
        }
        catch { /* already closed */ }
        _socket.Dispose();
        _send.Dispose();
    }

    private PhoneHomeTransportException Closed(PhoneHomeOperation operation, string reason) =>
        new(PhoneHomeProblemTypes.ConnectionClosed,
            $"Phone-home connection to {RunnerId} (epoch {Epoch}) closed while {operation} was in flight: {reason}");

    private sealed record Waiter(PhoneHomeOperation Operation, TaskCompletionSource<PhoneHomeFrame> Reply);
}
