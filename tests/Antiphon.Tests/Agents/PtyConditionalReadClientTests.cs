using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1763: the server keeps the last pty body for its validator. A 304 or phone-home
/// notModified with no held body is read again; it is never an empty screen.
/// </summary>
[Category("Unit")]
public class PtyConditionalReadClientTests
{
    private static readonly Guid SessionId = Guid.Parse("4142c507-241f-4234-aa07-c03ebcc7f7d2");

    [Test]
    public async Task Http_buffer_reuses_the_held_body_and_drops_it_without_a_validator()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"sessionId":"4142c507-241f-4234-aa07-c03ebcc7f7d2","buffer":"one","lastSequence":1}""", "\"b1.one\"");
        handler.Enqueue(HttpStatusCode.NotModified, "", "\"b1.one\"");
        handler.Enqueue(HttpStatusCode.OK, """{"sessionId":"4142c507-241f-4234-aa07-c03ebcc7f7d2","buffer":"two","lastSequence":1}""");
        handler.Enqueue(HttpStatusCode.OK, """{"sessionId":"4142c507-241f-4234-aa07-c03ebcc7f7d2","buffer":"three","lastSequence":2}""", "\"b1.three\"");
        var client = Client(handler);

        var first = await client.GetBufferAsync(SessionId, CancellationToken.None);
        var second = await client.GetBufferAsync(SessionId, CancellationToken.None);
        var third = await client.GetBufferAsync(SessionId, CancellationToken.None);
        var fourth = await client.GetBufferAsync(SessionId, CancellationToken.None);

        first.Buffer.ShouldBe("one");
        second.Buffer.ShouldBe("one");
        third.Buffer.ShouldBe("two");
        fourth.Buffer.ShouldBe("three");
        handler.Matches.ShouldBe(new string?[] { null, "\"b1.one\"", "\"b1.one\"", null });
    }

    [Test]
    public async Task Http_304_without_a_held_body_is_read_again()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.NotModified, "", "\"b1.gone\"");
        handler.Enqueue(HttpStatusCode.OK, """{"sessionId":"4142c507-241f-4234-aa07-c03ebcc7f7d2","buffer":"fresh","lastSequence":1}""", "\"b1.fresh\"");
        var client = Client(handler);

        var body = await client.GetBufferAsync(SessionId, CancellationToken.None);

        body.Buffer.ShouldBe("fresh");
        handler.Matches.ShouldBe(new string?[] { null, null });
    }

    [Test]
    public async Task Http_snapshot_304_returns_the_held_screen()
    {
        const string snapshot = """{"sessionId":"4142c507-241f-4234-aa07-c03ebcc7f7d2","rawOutput":"screen","renderedScreen":"screen","lastSequence":1,"startedAt":"2026-11-08T00:00:00Z"}""";
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, snapshot, "\"s1.screen\"");
        handler.Enqueue(HttpStatusCode.NotModified, "", "\"s1.screen\"");
        var client = Client(handler);

        var first = await client.GetSnapshotAsync(SessionId, CancellationToken.None);
        var second = await client.GetSnapshotAsync(SessionId, CancellationToken.None);

        first.RawOutput.ShouldBe("screen");
        second.RawOutput.ShouldBe("screen");
        handler.Matches.ShouldBe(new string?[] { null, "\"s1.screen\"" });
    }

    [Test]
    public async Task Phone_home_reuses_the_held_body_only_when_the_runner_advertises_it()
    {
        await using var advertised = PhoneHome.Start(advertise: true);
        var first = await advertised.Client.GetBufferAsync(SessionId, CancellationToken.None);
        var second = await advertised.Client.GetBufferAsync(SessionId, CancellationToken.None);
        first.Buffer.ShouldBe("held-text");
        second.Buffer.ShouldBe("held-text");
        advertised.Socket.Sent[0].ShouldNotContain("ifNoneMatch");
        advertised.Socket.Sent[1].ShouldContain("ifNoneMatch");
        advertised.Socket.Sent[1].ShouldContain("s1.held");

        await using var silent = PhoneHome.Start(advertise: false);
        await silent.Client.GetBufferAsync(SessionId, CancellationToken.None);
        await silent.Client.GetBufferAsync(SessionId, CancellationToken.None);
        silent.Socket.Sent.ShouldAllBe(payload => !payload.Contains("ifNoneMatch", StringComparison.Ordinal));
    }

    [Test]
    public async Task Phone_home_not_modified_without_a_held_body_is_read_again()
    {
        await using var phone = PhoneHome.Start(advertise: true, notModifiedFirst: true);
        var body = await phone.Client.GetBufferAsync(SessionId, CancellationToken.None);
        body.Buffer.ShouldBe("fresh-text");
        phone.Socket.Sent.Count.ShouldBe(2);
        phone.Socket.Sent.ShouldAllBe(payload => !payload.Contains("ifNoneMatch", StringComparison.Ordinal));
    }

    [Test]
    public async Task Two_resolve_calls_on_one_live_connection_send_if_none_match_on_the_second_read()
    {
        await using var lane = ResolvedLane.Open();
        var first = lane.Directory.Resolve(ResolvedLane.RunnerId);
        var second = lane.Directory.Resolve(ResolvedLane.RunnerId);
        await first.GetBufferAsync(SessionId, CancellationToken.None);
        await second.GetBufferAsync(SessionId, CancellationToken.None);
        var buffers = Frames(lane.Socket.Sent, PhoneHomeOperation.Buffer);
        buffers.Count.ShouldBe(2);
        buffers[0].ShouldNotContain("ifNoneMatch");
        buffers[1].ShouldContain("ifNoneMatch");
        buffers[1].ShouldContain("b1.held");

        var third = lane.Directory.Resolve(ResolvedLane.RunnerId);
        var fourth = lane.Directory.Resolve(ResolvedLane.RunnerId);
        await third.GetSnapshotAsync(SessionId, CancellationToken.None);
        await fourth.GetSnapshotAsync(SessionId, CancellationToken.None);
        var snapshots = Frames(lane.Socket.Sent, PhoneHomeOperation.Snapshot);
        snapshots.Count.ShouldBe(2);
        snapshots[0].ShouldNotContain("ifNoneMatch");
        snapshots[1].ShouldContain("ifNoneMatch");
        snapshots[1].ShouldContain("s1.screen");
    }

    [Test]
    public async Task A_new_connection_does_not_reuse_another_connections_hold()
    {
        await using var lane = ResolvedLane.Open();
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetBufferAsync(SessionId, CancellationToken.None);
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetBufferAsync(SessionId, CancellationToken.None);
        var held = Frames(lane.Socket.Sent, PhoneHomeOperation.Buffer);
        held.Count.ShouldBe(2);
        held[1].ShouldContain("ifNoneMatch");

        var fresh = lane.ReplaceConnection();
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetBufferAsync(SessionId, CancellationToken.None);
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetSnapshotAsync(SessionId, CancellationToken.None);
        Frames(fresh.Sent, PhoneHomeOperation.Buffer).Single().ShouldNotContain("ifNoneMatch");
        Frames(fresh.Sent, PhoneHomeOperation.Snapshot).Single().ShouldNotContain("ifNoneMatch");
    }

    [Test]
    public async Task A_killed_session_drops_its_phone_home_hold()
    {
        await using var lane = ResolvedLane.Open();
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetBufferAsync(SessionId, CancellationToken.None);
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetBufferAsync(SessionId, CancellationToken.None);
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetSnapshotAsync(SessionId, CancellationToken.None);
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetSnapshotAsync(SessionId, CancellationToken.None);
        Frames(lane.Socket.Sent, PhoneHomeOperation.Buffer)[1].ShouldContain("ifNoneMatch");
        Frames(lane.Socket.Sent, PhoneHomeOperation.Snapshot)[1].ShouldContain("ifNoneMatch");

        var killed = await lane.Directory.Resolve(ResolvedLane.RunnerId).KillAsync(SessionId, CancellationToken.None);
        killed.Status.ShouldBe("Exited");

        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetBufferAsync(SessionId, CancellationToken.None);
        await lane.Directory.Resolve(ResolvedLane.RunnerId).GetSnapshotAsync(SessionId, CancellationToken.None);
        Frames(lane.Socket.Sent, PhoneHomeOperation.Buffer)[2].ShouldNotContain("ifNoneMatch");
        Frames(lane.Socket.Sent, PhoneHomeOperation.Snapshot)[2].ShouldNotContain("ifNoneMatch");
    }

    private static List<string> Frames(IEnumerable<string> sent, PhoneHomeOperation operation) =>
        sent.Where(json =>
            JsonSerializer.Deserialize<PhoneHomeFrame>(json, PhoneHomeFraming.Json)?.Operation == operation).ToList();

    private static SessionRunnerHttpClient Client(ScriptedHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body, string? ETag)> _scripts = new();
        public List<string?> Matches { get; } = new();

        public void Enqueue(HttpStatusCode status, string body, string? etag = null) =>
            _scripts.Enqueue((status, body, etag));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Matches.Add(request.Headers.TryGetValues("If-None-Match", out var values) ? string.Join(",", values) : null);
            var script = _scripts.Dequeue();
            var response = new HttpResponseMessage(script.Status)
            {
                Content = new StringContent(script.Body, Encoding.UTF8, "application/json"),
            };
            if (script.ETag is not null)
                response.Headers.ETag = EntityTagHeaderValue.Parse(script.ETag);
            return Task.FromResult(response);
        }
    }

    private sealed class PhoneHome : IAsyncDisposable
    {
        public ScriptedSocket Socket { get; }
        public PhoneHomeRunnerClient Client { get; }
        private readonly CancellationTokenSource _stop;
        private readonly Task _loop;
        private readonly PhoneHomeLiveConnection _connection;

        private PhoneHome(ScriptedSocket socket, PhoneHomeRunnerClient client, PhoneHomeLiveConnection connection, Task loop, CancellationTokenSource stop)
        {
            Socket = socket;
            Client = client;
            _connection = connection;
            _loop = loop;
            _stop = stop;
        }

        public static PhoneHome Start(bool advertise, bool notModifiedFirst = false)
        {
            var socket = new ScriptedSocket(notModifiedFirst);
            RunnerCapabilitiesDto? capabilities = advertise
                ? new RunnerCapabilitiesDto("ConPty", "ConPty", "test", false,
                    Features: [RunnerCapabilityFeatures.PtyConditionalReadV1])
                : null;
            var connection = new PhoneHomeLiveConnection(
                "runner", Guid.NewGuid(), Guid.NewGuid(), 1, socket, new PhoneHomeLimits(), TimeProvider.System,
                capabilities: capabilities);
            var stop = new CancellationTokenSource();
            var loop = connection.ReceiveLoopAsync(stop.Token);
            return new PhoneHome(socket, new PhoneHomeRunnerClient(connection), connection, loop, stop);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _connection.DisposeAsync();
            try { await _loop; } catch (OperationCanceledException) { }
        }
    }

    private sealed class ScriptedSocket(bool notModifiedFirst) : WebSocket
    {
        private readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>();
        private readonly CancellationTokenSource _abort = new();
        private byte[]? _pending;
        private int _offset;
        private int _step;
        private WebSocketState _state = WebSocketState.Open;
        public List<string> Sent { get; } = new();
        public Func<PhoneHomeFrame, PhoneHomeFrame>? Reply { get; set; }

        private PhoneHomeFrame BufferReply(PhoneHomeFrame request, string json)
        {
            _step++;
            var modified = notModifiedFirst && _step == 1
                || !notModifiedFirst && json.Contains("ifNoneMatch", StringComparison.Ordinal);
            var payload = modified
                ? JsonSerializer.SerializeToElement(new { notModified = true }, PhoneHomeFraming.Json)
                : JsonSerializer.SerializeToElement(
                    new RunnerBufferDto(SessionId, notModifiedFirst ? "fresh-text" : "held-text", 4),
                    RunnerReadJson.Use(RunnerReadJson.PhoneBuffer));
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation, payload,
                Match: "s1.held");
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => CloseNow();
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            CloseNow();
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            CloseNow();
            return Task.CompletedTask;
        }
        public override void Dispose() => CloseNow();

        private void CloseNow()
        {
            _state = WebSocketState.Aborted;
            _abort.Cancel();
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            var json = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            Sent.Add(json);
            var request = JsonSerializer.Deserialize<PhoneHomeFrame>(json, PhoneHomeFraming.Json)!;
            var reply = Reply?.Invoke(request) ?? BufferReply(request, json);
            _inbound.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(reply, PhoneHomeFraming.Json));
            return Task.CompletedTask;
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_abort.IsCancellationRequested)
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token);
            try
            {
                _pending ??= await _inbound.Reader.ReadAsync(linked.Token);
            }
            catch (OperationCanceledException) when (_abort.IsCancellationRequested)
            {
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            }
            var count = Math.Min(buffer.Count, _pending.Length - _offset);
            _pending.AsSpan(_offset, count).CopyTo(buffer.AsSpan());
            _offset += count;
            var end = _offset >= _pending.Length;
            if (end)
            {
                _pending = null;
                _offset = 0;
            }
            return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
        }
    }

    /// <summary>
    /// CARD-1763 D-1. Production polls call <see cref="PhoneHomeRunnerDirectory.Resolve"/>
    /// on every read. The hold has to survive that, stay on one connection, and leave when the session does.
    /// </summary>
    private sealed class ResolvedLane : IAsyncDisposable
    {
        public const string RunnerId = "grok-linux";
        public PhoneHomeRunnerDirectory Directory { get; }
        public ScriptedSocket Socket { get; private set; } = null!;
        private PhoneHomeLiveConnection _connection = null!;
        private CancellationTokenSource _stop = null!;
        private readonly List<Task> _loops = new();
        private readonly Guid _boot = Guid.NewGuid();
        private readonly Guid _store = Guid.NewGuid();

        private ResolvedLane(PhoneHomeRunnerDirectory directory) => Directory = directory;

        public static ResolvedLane Open()
        {
            var settings = Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true,
                AllowedRunnerId = RunnerId,
                LeaseSeconds = 90,
                TicketTtlSeconds = 30,
                Limits = new PhoneHomeLimits(),
            });
            var lane = new ResolvedLane(new PhoneHomeRunnerDirectory(
                new PhoneHomeTestHost.RecordingLocalClient(),
                settings,
                new IdleScopes(),
                TimeProvider.System));
            lane.Attach(new ScriptedSocket(notModifiedFirst: false));
            return lane;
        }

        public ScriptedSocket ReplaceConnection()
        {
            var socket = new ScriptedSocket(notModifiedFirst: false);
            Attach(socket);
            return socket;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _connection.DisposeAsync();
            try { await Task.WhenAll(_loops); }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or PhoneHomeTransportException)
            {
            }
        }

        private void Attach(ScriptedSocket socket)
        {
            _stop?.Cancel();
            socket.Reply = DirectoryReply;
            var registration = Directory.Register(new PhoneHomeRegistrationRequest(
                PhoneHomeProtocol.Version, RunnerId, _boot, _store, "linux", 1, Capabilities()));
            var connection = Directory.AcceptConnect(RunnerId, registration.Ticket, socket);
            Directory.MarkRecovered(connection);
            var stop = new CancellationTokenSource();
            Socket = socket;
            _connection = connection;
            _stop = stop;
            _loops.Add(connection.ReceiveLoopAsync(stop.Token));
        }

        private static RunnerCapabilitiesDto Capabilities() =>
            new("ConPty", "ConPty", "test", false,
                Features: [RunnerCapabilityFeatures.PtyConditionalReadV1]);

        private static PhoneHomeFrame DirectoryReply(PhoneHomeFrame request)
        {
            if (request.Operation == PhoneHomeOperation.ReleaseSlot)
            {
                var session = new RunnerSessionDto(
                    SessionId, 1, DateTime.UtcNow, "Exited", 0, "KilledByRequest", 4,
                    AcceptedStartedAt: DateTime.UtcNow);
                return new PhoneHomeFrame(
                    PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                    JsonSerializer.SerializeToElement(session, PhoneHomeFraming.Json));
            }

            if (request.Operation == PhoneHomeOperation.Snapshot)
            {
                var snapshot = new RunnerSnapshotDto(SessionId, "screen", "screen", 4, DateTime.UtcNow);
                return new PhoneHomeFrame(
                    PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                    JsonSerializer.SerializeToElement(snapshot, RunnerReadJson.Use(RunnerReadJson.PhoneSnapshot)),
                    Match: "s1.screen");
            }

            var buffer = new RunnerBufferDto(SessionId, "held-text", 4);
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                JsonSerializer.SerializeToElement(buffer, RunnerReadJson.Use(RunnerReadJson.PhoneBuffer)),
                Match: "b1.held");
        }

        private sealed class IdleScopes : IServiceScopeFactory
        {
            public IServiceScope CreateScope() => throw new InvalidOperationException("unused");
        }
    }
}
