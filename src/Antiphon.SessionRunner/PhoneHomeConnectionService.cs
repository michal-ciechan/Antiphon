using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

public sealed class PhoneHomeConnectionService : BackgroundService
{
    private readonly PhoneHomeSettings _settings;
    private readonly IPhoneHomeAdoptionGate _adoption;
    private readonly PhoneHomeCommandDispatcher _dispatcher;
    private readonly SessionRunnerRuntime _runtime;
    private readonly IHttpClientFactory _httpFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<PhoneHomeConnectionService> _logger;
    private readonly Guid _bootId = Guid.NewGuid();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PhoneHomeFrame>> _waiters = new();
    private long _epoch;
    private int _inFlight;
    private readonly List<PhoneHomeFrame> _sentMutations = [];

    public PhoneHomeConnectionService(
        IOptions<PhoneHomeSettings> settings,
        IPhoneHomeAdoptionGate adoption,
        PhoneHomeCommandDispatcher dispatcher,
        SessionRunnerRuntime runtime,
        IHttpClientFactory httpFactory,
        TimeProvider clock,
        ILogger<PhoneHomeConnectionService> logger)
    {
        _settings = settings.Value;
        _adoption = adoption;
        _dispatcher = dispatcher;
        _runtime = runtime;
        _httpFactory = httpFactory;
        _clock = clock;
        _logger = logger;
    }

    internal int RegistrationAttempts { get; private set; }
    internal IReadOnlyList<PhoneHomeFrame> SentMutations => _sentMutations;
    public long Epoch => Interlocked.Read(ref _epoch);
    public int InFlight => Volatile.Read(ref _inFlight);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        _settings.Validate();
        await _adoption.WaitAsync(stoppingToken);
        var backoff = _settings.ReconnectBackoffMs;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken);
                backoff = _settings.ReconnectBackoffMs;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Phone-home connection ended; reconnecting with backoff {Backoff}ms", backoff);
                await Task.Delay(TimeSpan.FromMilliseconds(backoff), _clock, stoppingToken);
                backoff = Math.Min(backoff * 2, _settings.ReconnectBackoffMaxMs);
            }
        }
    }

    internal async Task RunConnectionAsync(CancellationToken ct)
    {
        var storeId = PhoneHomeStoreIdentity.LoadOrCreate(_settings.StoreIdPath);
        var secret = await File.ReadAllTextAsync(_settings.SecretPath, ct);
        secret = secret.Trim();
        RegistrationAttempts++;
        using var http = _httpFactory.CreateClient(nameof(PhoneHomeConnectionService));
        http.BaseAddress = new Uri(_settings.ServerOrigin.TrimEnd('/') + "/");
        using var register = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath.TrimStart('/'));
        register.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, secret);
        register.Content = JsonContent.Create(new PhoneHomeRegistrationRequest(
            PhoneHomeProtocol.Version,
            _settings.RunnerId,
            _bootId,
            storeId,
            OperatingSystem.IsLinux() ? "linux" : "windows",
            _settings.Capacity,
            // CARD-0604: the registration carries the capabilities DTO, so the server can report
            // the runner's platform, build and custody backend from its own registration rather
            // than from a separate Capabilities round trip the deploy row cannot make.
            _dispatcher.Capabilities()), options: PhoneHomeFraming.Json);
        using var registered = await http.SendAsync(register, ct);
        registered.EnsureSuccessStatusCode();
        var ticket = await registered.Content.ReadFromJsonAsync<PhoneHomeRegistrationResponse>(PhoneHomeFraming.Json, ct)
            ?? throw new InvalidOperationException("Empty registration response.");

        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
        var origin = new Uri(_settings.ServerOrigin);
        var wsScheme = origin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var connect = new Uri($"{wsScheme}://{origin.Authority}/api/session-runners/{Uri.EscapeDataString(_settings.RunnerId)}/connect");
        await ws.ConnectAsync(connect, ct);
        // CARD-0604 CP-6a: the SERVER owns the epoch and handed it to us in the registration
        // response. Numbering our own connections here desynchronised the two counters the moment
        // either process restarted alone, and both receive loops silently dropped every frame that
        // did not match - the socket stayed open while heartbeats, requests and replies vanished.
        await RunConnectedAsync(ws, ticket.Epoch, ct);
    }

    /// <summary>
    /// One connected socket, from the server-owned epoch until the first of its receive,
    /// heartbeat and event loops ends. Split from <see cref="RunConnectionAsync"/> so a scripted
    /// socket can drive a connection's end without a registration round trip.
    /// </summary>
    internal async Task RunConnectedAsync(WebSocket ws, long epoch, CancellationToken ct)
    {
        Interlocked.Exchange(ref _epoch, epoch);
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var overflow = false;
        var reader = _runtime.SubscribeBounded(
            _settings.Limits.MaxPendingEvents,
            _settings.Limits.MaxPendingEventBytes,
            () =>
            {
                overflow = true;
                connectionCts.Cancel();
            },
            connectionCts.Token);

        var writer = new PhoneHomeConnectionWriter(ws, _settings.Limits.MaxMessageUtf8Bytes);
        var receive = ReceiveLoopAsync(writer, epoch, connectionCts.Token);
        var heartbeat = HeartbeatLoopAsync(writer, epoch, connectionCts.Token);
        var events = EventLoopAsync(writer, epoch, reader, connectionCts.Token);
        try
        {
            await Task.WhenAny(receive, heartbeat, events);
        }
        finally
        {
            connectionCts.Cancel();
            CancelWaiters(epoch);
            // CARD-0631 D-4: the close handshake goes through the writer's gate, so it never
            // overlaps a reply or heartbeat that is still being written.
            try { await writer.CloseAsync(WebSocketCloseStatus.NormalClosure, overflow ? PhoneHomeProblemTypes.EventOverflow : "disconnect"); }
            catch { /* closing a dropped socket */ }
            ws.Dispose();
        }

        if (overflow)
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.EventOverflow, "Event hub overflow.");
    }

    /// <summary>
    /// CARD-0631: the receive pump for one connection. Reads frames from the writer's socket and
    /// hands each accepted request to <see cref="DispatchAndReplyAsync"/> off the read loop.
    /// </summary>
    internal async Task ReceiveLoopAsync(PhoneHomeConnectionWriter writer, long epoch, CancellationToken ct)
    {
        var ws = writer.Socket;
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var frame = await PhoneHomeFraming.ReadFrameAsync(ws, _settings.Limits.MaxMessageUtf8Bytes, ct);
            if (frame is null)
                break;
            if (frame.Epoch != epoch)
                continue;
            if (frame.Kind is PhoneHomeFrameKind.Result or PhoneHomeFrameKind.Error)
            {
                if (_waiters.TryRemove(frame.RequestId, out var waiter))
                    waiter.TrySetResult(frame);
                Interlocked.Decrement(ref _inFlight);
                continue;
            }

            if (frame.Kind != PhoneHomeFrameKind.Request)
                continue;

            if (Volatile.Read(ref _inFlight) >= _settings.Limits.MaxInFlightRequests
                && frame.Operation is PhoneHomeOperation.Launch or PhoneHomeOperation.Input
                    or PhoneHomeOperation.ConditionalInput or PhoneHomeOperation.ClearBuffer
                    or PhoneHomeOperation.Resize or PhoneHomeOperation.KillGeneration)
            {
                await SendRequestLimitAsync(writer, epoch, frame, ct);
                continue;
            }

            Interlocked.Increment(ref _inFlight);
            // CARD-0631 D-3: no scheduling token. A task cancelled before it starts would never
            // run its finally, and the increment above would leak for the life of the process.
            _ = Task.Run(async () =>
            {
                try
                {
                    await DispatchAndReplyAsync(writer, frame, epoch, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Phone-home request {RequestId} ({Operation}) ended without a reply", frame.RequestId, frame.Operation);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// CARD-0631 D-2/D-3: dispatch one request and send exactly one reply through the
    /// connection's writer. A dispatch that throws - including a cancellation the connection did
    /// not ask for - is answered with the shared internal Error. Cancellation of the connection
    /// itself sends nothing: there is no one left to answer. A reply that cannot be sent is never
    /// followed by a second Error on the same transport; the connection is aborted instead, so
    /// the server observes the failure rather than a healthy socket that lost a reply.
    /// </summary>
    internal async Task DispatchAndReplyAsync(PhoneHomeConnectionWriter writer, PhoneHomeFrame frame, long epoch, CancellationToken ct)
    {
        var request = frame with { Epoch = epoch };
        PhoneHomeFrame reply;
        try
        {
            reply = await _dispatcher.DispatchAsync(request, ct);
            if (IsMutation(frame.Operation))
                _sentMutations.Add(frame);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Phone-home request {RequestId} ({Operation}) cancelled with its connection", frame.RequestId, frame.Operation);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phone-home request {RequestId} ({Operation}) failed in dispatch; replying with an error", frame.RequestId, frame.Operation);
            reply = PhoneHomeErrorFrames.Internal(request, ex, writer.MaxUtf8Bytes);
        }

        try
        {
            await writer.SendAsync(reply, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Phone-home reply for {RequestId} ({Operation}) dropped: its connection ended", frame.RequestId, frame.Operation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phone-home reply for {RequestId} ({Operation}) could not be sent; aborting the connection", frame.RequestId, frame.Operation);
            writer.Abort();
        }
    }

    internal Task SendRequestLimitAsync(PhoneHomeConnectionWriter writer, long epoch, PhoneHomeFrame request, CancellationToken ct) =>
        writer.SendAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Error, epoch, request.RequestId, request.Operation,
            ErrorCode: PhoneHomeProblemTypes.RequestLimit,
            ErrorDetail: "In-flight request limit reached.",
            StatusCode: 429), ct);

    internal Task SendHeartbeatAsync(PhoneHomeConnectionWriter writer, long epoch, CancellationToken ct) =>
        writer.SendAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, epoch, Guid.NewGuid()), ct);

    internal Task SendEventAsync(PhoneHomeConnectionWriter writer, long epoch, RunnerServerSentEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.Json, PhoneHomeFraming.Json);
        return writer.SendAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Event, epoch, Guid.Empty, EventName: evt.EventName, Payload: payload), ct);
    }

    private async Task HeartbeatLoopAsync(PhoneHomeConnectionWriter writer, long epoch, CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(_settings.HeartbeatSeconds);
        while (!ct.IsCancellationRequested && writer.Socket.State == WebSocketState.Open)
        {
            await SendHeartbeatAsync(writer, epoch, ct);
            await Task.Delay(period, _clock, ct);
        }
    }

    internal async Task EventLoopAsync(
        PhoneHomeConnectionWriter writer, long epoch, System.Threading.Channels.ChannelReader<RunnerServerSentEvent> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                try
                {
                    await SendEventAsync(writer, epoch, evt, ct);
                }
                finally
                {
                    // After the send, so a socket held inside SendAsync still occupies queue depth.
                    if (reader is ISessionRunnerEventLease lease)
                        lease.Release(evt);
                }
            }
        }
        finally
        {
            // Cancellation stops ReadAllAsync before it sees the rest of the queue. Releasing
            // those reservations is idempotent with the subscription close, which drains the same channel.
            if (reader is ISessionRunnerEventLease unread)
                unread.ReleaseUnread();
        }
    }

    private void CancelWaiters(long epoch)
    {
        foreach (var (id, waiter) in _waiters)
        {
            waiter.TrySetCanceled();
            _waiters.TryRemove(id, out _);
        }
    }

    private static bool IsMutation(PhoneHomeOperation? operation) =>
        operation is PhoneHomeOperation.Launch or PhoneHomeOperation.Input
            or PhoneHomeOperation.ConditionalInput or PhoneHomeOperation.ClearBuffer
            or PhoneHomeOperation.Resize or PhoneHomeOperation.KillGeneration;
}
