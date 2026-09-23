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
        var epoch = ticket.Epoch;
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

        var receive = ReceiveLoopAsync(ws, epoch, connectionCts.Token);
        var heartbeat = HeartbeatLoopAsync(ws, epoch, connectionCts.Token);
        var events = EventLoopAsync(ws, epoch, reader, connectionCts.Token);
        try
        {
            await Task.WhenAny(receive, heartbeat, events);
        }
        finally
        {
            connectionCts.Cancel();
            CancelWaiters(epoch);
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, overflow ? PhoneHomeProblemTypes.EventOverflow : "disconnect", CancellationToken.None); }
            catch { /* closing a dropped socket */ }
            ws.Dispose();
        }

        if (overflow)
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.EventOverflow, "Event hub overflow.");
    }

    private async Task ReceiveLoopAsync(WebSocket ws, long epoch, CancellationToken ct)
    {
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
                await PhoneHomeFraming.WriteFrameAsync(ws, new PhoneHomeFrame(
                    PhoneHomeFrameKind.Error, epoch, frame.RequestId, frame.Operation,
                    ErrorCode: PhoneHomeProblemTypes.RequestLimit,
                    ErrorDetail: "In-flight request limit reached.",
                    StatusCode: 429), _settings.Limits.MaxMessageUtf8Bytes, ct);
                continue;
            }

            Interlocked.Increment(ref _inFlight);
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _dispatcher.DispatchAsync(frame with { Epoch = epoch }, ct);
                    if (IsMutation(frame.Operation))
                        _sentMutations.Add(frame);
                    await PhoneHomeFraming.WriteFrameAsync(ws, result, _settings.Limits.MaxMessageUtf8Bytes, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Phone-home command {Operation} failed", frame.Operation);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }, ct);
        }
    }

    private async Task HeartbeatLoopAsync(WebSocket ws, long epoch, CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(_settings.HeartbeatSeconds);
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            await PhoneHomeFraming.WriteFrameAsync(ws, new PhoneHomeFrame(
                PhoneHomeFrameKind.Heartbeat, epoch, Guid.NewGuid()), _settings.Limits.MaxMessageUtf8Bytes, ct);
            await Task.Delay(period, _clock, ct);
        }
    }

    private async Task EventLoopAsync(
        WebSocket ws, long epoch, System.Threading.Channels.ChannelReader<RunnerServerSentEvent> reader, CancellationToken ct)
    {
        await foreach (var evt in reader.ReadAllAsync(ct))
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(evt.Json, PhoneHomeFraming.Json);
            await PhoneHomeFraming.WriteFrameAsync(ws, new PhoneHomeFrame(
                PhoneHomeFrameKind.Event, epoch, Guid.Empty, EventName: evt.EventName, Payload: payload),
                _settings.Limits.MaxMessageUtf8Bytes, ct);
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
