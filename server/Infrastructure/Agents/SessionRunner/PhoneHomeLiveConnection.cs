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

    // CARD-0679 D-10: what this connection knows the runner holds live. Every change takes a stamp,
    // so a List sent before a launch ack or an exit does not undo them. An exit leaves a tombstone
    // for its generation, so neither a late ack nor a List brings that generation back (review
    // 57fa2e6a: the exit event can be pumped before the ack's continuation runs). Tombstones last
    // as long as this connection, the only inventory a reply on it can write, so no continuation is
    // late enough to outlive one (review 87af1bf6: a ten-minute tombstone could). Each live entry
    // keeps when the runner last confirmed it, so a cache no List refreshes ages out of "live" into
    // "unknown" instead of vouching for a lost session for as long as heartbeats hold the lease.
    private readonly object _inventoryGate = new();
    private readonly Dictionary<Guid, InventoryEntry> _inventory = new();
    private readonly Dictionary<Guid, Tombstone> _tombstones = new();
    private long _inventoryStamp;

    /// <summary>
    /// CARD-0679 D-10: the sessions the runner last reported Running/Starting, kept current by the
    /// recovery pump's List, launch acks, events, exits and kills. An entry no List, ack or event
    /// confirmed within <paramref name="maxAge"/> is left out; null is no age bound. Read without
    /// an RPC.
    /// </summary>
    public IReadOnlyCollection<Guid> KnownLiveSessions(TimeSpan? maxAge = null)
    {
        var now = Clock.GetUtcNow();
        lock (_inventoryGate)
        {
            return _inventory
                .Where(entry => maxAge is not { } age || now - entry.Value.ConfirmedAt <= age)
                .Select(entry => entry.Key)
                .ToArray();
        }
    }

    /// <summary>
    /// CARD-0679 (review 87af1bf6): the entries <see cref="KnownLiveSessions"/> leaves out for age
    /// (none when <paramref name="maxAge"/> is null). Not confirmed live, and not seen to end
    /// either: the directory reports them as unknown.
    /// </summary>
    public IReadOnlyCollection<Guid> UnconfirmedSessions(TimeSpan? maxAge)
    {
        var now = Clock.GetUtcNow();
        lock (_inventoryGate)
        {
            return _inventory
                .Where(entry => maxAge is { } age && now - entry.Value.ConfirmedAt > age)
                .Select(entry => entry.Key)
                .ToArray();
        }
    }

    /// <summary>
    /// CARD-0679 D-10: call before sending a request whose answer updates the inventory (the List,
    /// a Launch); pass the result with that answer, so changes seen after it outrank the answer.
    /// </summary>
    internal long BeginInventoryRead() => Interlocked.Increment(ref _inventoryStamp);

    /// <summary>
    /// CARD-0679 D-10: the runner's List is the inventory, except for sessions whose launch ack,
    /// exit or kill arrived after <paramref name="readStamp"/> (those are newer than the List) and
    /// generations an exit already ended. A listed session counts as confirmed now.
    /// </summary>
    internal void ReplaceKnownLiveSessions(IEnumerable<(Guid SessionId, DateTime? Generation)> live, long readStamp)
    {
        var now = Clock.GetUtcNow();
        lock (_inventoryGate)
        {
            var listed = new HashSet<Guid>();
            foreach (var (id, generation) in live)
            {
                if (Ended(id, generation, readStamp))
                    continue;
                listed.Add(id);
                if (_inventory.TryGetValue(id, out var current) && current.Stamp > readStamp)
                    continue;
                _inventory[id] = new InventoryEntry(generation, readStamp, now);
            }

            foreach (var (id, entry) in _inventory.ToArray())
            {
                // Absent from a List sent after this entry last changed: the List is the truth for it.
                if (entry.Stamp <= readStamp && !listed.Contains(id))
                    _inventory.Remove(id);
            }
        }
    }

    /// <summary>
    /// CARD-0679 D-10: the runner acknowledged a launch of <paramref name="generation"/>;
    /// <paramref name="requestStamp"/> was taken before the Launch was sent. An ack for a
    /// generation whose exit was already seen changes nothing. Returns whether it was recorded.
    /// </summary>
    internal bool NoteSessionLive(Guid sessionId, DateTime? generation, long requestStamp)
    {
        lock (_inventoryGate)
        {
            if (Ended(sessionId, generation, requestStamp))
                return false;
            _inventory[sessionId] = new InventoryEntry(
                generation, Interlocked.Increment(ref _inventoryStamp), Clock.GetUtcNow());
            return true;
        }
    }

    /// <summary>
    /// CARD-0679 D-10: the runner reported <paramref name="sessionId"/>'s <paramref name="generation"/>
    /// exited or killed (null: an older runner that does not say which). A live entry of a newer
    /// generation stays; the tombstone is left either way.
    /// </summary>
    internal void NoteSessionGone(Guid sessionId, DateTime? generation)
    {
        lock (_inventoryGate)
        {
            var stamp = Interlocked.Increment(ref _inventoryStamp);
            var ended = generation;
            if (_tombstones.TryGetValue(sessionId, out var prior)
                && prior.Generation is { } priorGeneration && generation is { } exitGeneration
                && SessionGeneration.Compare(priorGeneration, exitGeneration) > 0)
                ended = priorGeneration;
            _tombstones[sessionId] = new Tombstone(ended, stamp);

            if (_inventory.TryGetValue(sessionId, out var entry)
                && !(entry.Generation is { } liveGeneration && generation is { } goneGeneration
                    && SessionGeneration.Compare(liveGeneration, goneGeneration) > 0))
                _inventory.Remove(sessionId);
        }
    }

    /// <summary>CARD-0679 D-10: an event from <paramref name="sessionId"/> confirms it is still live.</summary>
    internal void NoteSessionConfirmed(Guid sessionId)
    {
        lock (_inventoryGate)
        {
            if (_inventory.TryGetValue(sessionId, out var entry))
                _inventory[sessionId] = entry with { ConfirmedAt = Clock.GetUtcNow() };
        }
    }

    /// <summary>
    /// Whether an exit already ended this <paramref name="generation"/>. With both generations
    /// known it is the older-or-equal test; otherwise an exit seen after <paramref name="stamp"/>
    /// (after the request was sent) outranks the answer. Callers hold the gate.
    /// </summary>
    private bool Ended(Guid sessionId, DateTime? generation, long stamp)
    {
        if (!_tombstones.TryGetValue(sessionId, out var tombstone))
            return false;
        if (tombstone.Generation is { } ended && generation is { } answered)
            return SessionGeneration.Compare(answered, ended) <= 0;
        return tombstone.Stamp > stamp;
    }

    private readonly record struct InventoryEntry(DateTime? Generation, long Stamp, DateTimeOffset ConfirmedAt);

    private readonly record struct Tombstone(DateTime? Generation, long Stamp);

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
        // CARD-0679 D-5: a request on a connection that already closed is a typed transport loss,
        // not a raw WebSocketException from the send. Every throw before the frame is written is
        // the never-sent code; only a waiter the close finds after its write is in flight.
        if (Volatile.Read(ref _closedReason) is not null || _socket.State != WebSocketState.Open)
            throw ClosedBeforeSend(operation, Volatile.Read(ref _closedReason) ?? $"socket {_socket.State}");
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
            throw ClosedBeforeSend(operation, raced);
        Interlocked.Increment(ref _inFlight);
        var frame = new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, Epoch, id, operation,
            payload is null ? null : JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));
        try
        {
            var written = false;
            try
            {
                await _send.WaitAsync(ct);
                try
                {
                    await PhoneHomeFraming.WriteFrameAsync(_socket, frame, _limits.MaxMessageUtf8Bytes, ct);
                    written = true;
                }
                finally
                {
                    _send.Release();
                }
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                // A close can dispose the send gate between a completed write and its Release: that
                // frame left, so it is in flight, never the never-sent code the queue refunds.
                var closedReason = Volatile.Read(ref _closedReason) ?? $"send failed: {ex.GetType().Name}";
                throw written ? ClosedInFlight(operation, closedReason) : ClosedBeforeSend(operation, closedReason);
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
    /// <see cref="PhoneHomeProblemTypes.ConnectionClosedInFlight"/> naming the runner, epoch, operation
    /// and <paramref name="reason"/>. It used to cancel them, and a cancellation reads as the caller's
    /// own token to every <c>catch (OperationCanceledException)</c> on the way up. A waiter is only
    /// ever awaited after its frame was written; a request whose write never happened throws
    /// <see cref="PhoneHomeProblemTypes.ConnectionClosedBeforeSend"/> itself, so the in-flight code
    /// is the conservative one (review 914a96fd D1: the runner may have acted).
    /// </summary>
    public async ValueTask DisposeAsync(string reason)
    {
        Interlocked.CompareExchange(ref _closedReason, reason, null);
        var closedReason = Volatile.Read(ref _closedReason) ?? reason;
        _events.Writer.TryComplete();
        foreach (var (id, waiter) in _waiters)
        {
            if (_waiters.TryRemove(id, out _))
                waiter.Reply.TrySetException(ClosedInFlight(waiter.Operation, closedReason));
        }
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                // CARD-0716 D-1: a planned host stop is 1001 server_stopping. Anything else stays
                // the ordinary dispose close. CloseOutputAsync is a send, so it can run beside the
                // receive that is still blocked; CloseAsync would start a second receive. A peer
                // that does not answer within the handshake bound is aborted.
                var stopping = string.Equals(reason, "request_aborted", StringComparison.Ordinal);
                using var handshake = new CancellationTokenSource(
                    TimeSpan.FromSeconds(PhoneHomeProtocol.CloseHandshakeSeconds));
                try
                {
                    await _socket.CloseOutputAsync(
                        stopping ? WebSocketCloseStatus.EndpointUnavailable : WebSocketCloseStatus.NormalClosure,
                        stopping ? PhoneHomeCloseReasons.ServerStopping : "dispose",
                        handshake.Token);
                    var deadline = DateTime.UtcNow.AddSeconds(PhoneHomeProtocol.CloseHandshakeSeconds);
                    while (DateTime.UtcNow < deadline
                        && _socket.State is WebSocketState.Open or WebSocketState.CloseSent
                        && !handshake.IsCancellationRequested)
                    {
                        await Task.Delay(50, handshake.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // handshake bound elapsed
                }
                catch
                {
                    // already closing
                }

                if (_socket.State is WebSocketState.Open or WebSocketState.CloseSent)
                {
                    try { _socket.Abort(); } catch { /* already closed */ }
                }
            }
        }
        catch { /* already closed */ }
        _socket.Dispose();
        _send.Dispose();
    }

    private PhoneHomeTransportException ClosedBeforeSend(PhoneHomeOperation operation, string reason) =>
        new(PhoneHomeProblemTypes.ConnectionClosedBeforeSend,
            $"Phone-home connection to {RunnerId} (epoch {Epoch}) closed before {operation} was sent: {reason}");

    private PhoneHomeTransportException ClosedInFlight(PhoneHomeOperation operation, string reason) =>
        new(PhoneHomeProblemTypes.ConnectionClosedInFlight,
            $"Phone-home connection to {RunnerId} (epoch {Epoch}) closed while {operation} was in flight: {reason}");

    private sealed record Waiter(PhoneHomeOperation Operation, TaskCompletionSource<PhoneHomeFrame> Reply);
}
