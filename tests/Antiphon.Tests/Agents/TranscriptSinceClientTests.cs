using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1764: the server holds the last whole transcript and asks for rows after it. A delta is
/// applied only when it is gap-free on the same stream and generation; anything else is read
/// again in full, so a missed row never reads as nothing having arrived.
/// </summary>
[Category("Unit")]
public class TranscriptSinceClientTests
{
    private static readonly Guid SessionId = Guid.Parse("8d1c7f0e-5e8a-4c0b-9a51-1764a0c0ffee");
    private static readonly DateTime Generation = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Http_second_read_sends_the_cursor_and_joins_the_delta()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body(Rows(1, 2), 2, "t1"));
        handler.Enqueue(Body(Rows(3, 3), 3, "t1"));
        var client = Client(handler);

        var first = await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var second = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        first.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        second.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L]);
        second.Entries.Select(e => e.Text).ShouldBe(["row 1", "row 2", "row 3"]);
        second.LastSequence.ShouldBe(3);
        handler.Queries.ShouldBe(["", "?since=2&stream=t1"]);
    }

    [Test]
    public async Task Http_empty_delta_at_the_cursor_returns_the_held_rows()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body(Rows(1, 2), 2, "t1"));
        handler.Enqueue(Body([], 2, "t1", terminalComplete: true));
        var client = Client(handler);

        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var second = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        second.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        second.TerminalComplete.ShouldBeTrue();
        handler.Queries.ShouldBe(["", "?since=2&stream=t1"]);
    }

    [Test]
    [Arguments("gap", 5, 5, 5L)]
    [Arguments("lower", 1, 2, 3L)]
    [Arguments("overlap", 2, 3, 3L)]
    [Arguments("short-of-last", 3, 3, 4L)]
    [Arguments("hole-inside", 3, 3, 5L)]
    [Arguments("empty-behind", 0, 0, 3L)]
    [Arguments("empty-ahead", 0, 0, 1L)]
    public async Task Http_delta_with_a_gap_is_read_again_in_full(string name, int from, int to, long last)
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body(Rows(1, 2), 2, "t1"));
        handler.Enqueue(Body(name == "hole-inside" ? [Row(3), Row(5)] : Rows(from, to), last, "t1"));
        handler.Enqueue(Body(Rows(1, 5), 5, "t1"));
        var client = Client(handler);

        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var second = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        second.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L, 4L, 5L], name);
        handler.Queries.ShouldBe(["", "?since=2&stream=t1", ""], name);
    }

    [Test]
    public async Task Http_changed_stream_or_generation_is_read_again_in_full()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body(Rows(1, 2), 2, "t1"));
        handler.Enqueue(Body(Rows(3, 3), 3, "t2"));
        handler.Enqueue(Body(Rows(1, 1), 1, "t2"));
        handler.Enqueue(Body(Rows(2, 2), 2, "t2", generation: Generation.AddSeconds(1)));
        handler.Enqueue(Body(Rows(1, 2), 2, "t2", generation: Generation.AddSeconds(1)));
        var client = Client(handler);

        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var restream = await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var relaunch = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        restream.Entries.Select(e => e.Sequence).ShouldBe([1L]);
        relaunch.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        relaunch.AcceptedStartedAt.ShouldBe(Generation.AddSeconds(1));
        handler.Queries.ShouldBe(["", "?since=2&stream=t1", "", "?since=1&stream=t2", ""]);
    }

    [Test]
    public async Task Http_runner_without_a_stream_is_never_sent_a_cursor()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body(Rows(1, 2), 2, null));
        handler.Enqueue(Body(Rows(1, 3), 3, null));
        var client = Client(handler);

        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var second = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        second.Entries.Count.ShouldBe(3);
        handler.Queries.ShouldBe(["", ""]);
    }

    [Test]
    public async Task Http_full_body_with_a_hole_is_not_held()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body([Row(1), Row(3)], 3, "t1"));
        handler.Enqueue(Body(Rows(1, 3), 3, "t1"));
        handler.Enqueue(Body([], 3, "t1"));
        var client = Client(handler);

        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var third = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        third.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L]);
        handler.Queries.ShouldBe(["", "", "?since=3&stream=t1"]);
    }

    [Test]
    public async Task Phone_home_sends_the_cursor_only_when_the_runner_advertises_it()
    {
        await using var advertised = PhoneHome.Start(advertise: true);
        advertised.Socket.Replies.Enqueue(Body(Rows(1, 2), 2, "t1"));
        advertised.Socket.Replies.Enqueue(Body(Rows(3, 4), 4, "t1"));
        await advertised.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var joined = await advertised.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        joined.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L, 4L]);
        advertised.Socket.Sent[0].ShouldNotContain("\"since\"");
        advertised.Socket.Sent[1].ShouldContain("\"since\":2");
        advertised.Socket.Sent[1].ShouldContain("\"stream\":\"t1\"");

        await using var silent = PhoneHome.Start(advertise: false);
        silent.Socket.Replies.Enqueue(Body(Rows(1, 2), 2, "t1"));
        silent.Socket.Replies.Enqueue(Body(Rows(1, 3), 3, "t1"));
        await silent.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var full = await silent.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        full.Entries.Count.ShouldBe(3);
        silent.Socket.Sent.ShouldAllBe(payload => !payload.Contains("\"since\"", StringComparison.Ordinal));
    }

    [Test]
    public async Task Phone_home_gap_is_read_again_and_an_ended_session_drops_the_cursor()
    {
        await using var phone = PhoneHome.Start(advertise: true);
        phone.Socket.Replies.Enqueue(Body(Rows(1, 2), 2, "t1"));
        phone.Socket.Replies.Enqueue(Body(Rows(4, 4), 4, "t1"));
        phone.Socket.Replies.Enqueue(Body(Rows(1, 4), 4, "t1"));
        await phone.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var repaired = await phone.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        repaired.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L, 4L]);
        phone.Socket.Sent[1].ShouldContain("\"since\":2");
        phone.Socket.Sent[2].ShouldNotContain("\"since\"");

        phone.Socket.Replies.Enqueue(Body([], 4, "t1"));
        await phone.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        phone.Socket.Sent[3].ShouldContain("\"since\":4");

        phone.Connection.NoteSessionGone(SessionId, Generation);
        phone.Socket.Replies.Enqueue(Body(Rows(1, 1), 1, "t9"));
        await phone.Client.GetTranscriptAsync(SessionId, CancellationToken.None);
        phone.Socket.Sent[4].ShouldNotContain("\"since\"");
    }

    [Test]
    public void Hold_drops_the_least_recently_used_session_past_its_cap()
    {
        var hold = new TranscriptReadHold();
        var body = new Antiphon.Server.Application.Dtos.SessionRunnerTranscriptDto(SessionId, [], 0);
        var ids = Enumerable.Range(0, TranscriptReadHold.MaxSessions + 1).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids.Take(TranscriptReadHold.MaxSessions))
            hold.Remember(id, "t", body);
        hold.TryGet(ids[0], out _, out _).ShouldBeTrue();

        hold.Remember(ids[^1], "t", body);

        hold.Count.ShouldBe(TranscriptReadHold.MaxSessions);
        hold.TryGet(ids[0], out _, out _).ShouldBeTrue();
        hold.TryGet(ids[1], out _, out _).ShouldBeFalse();
        hold.TryGet(ids[^1], out _, out _).ShouldBeTrue();
    }

    private static RunnerTranscriptEvent Row(long sequence) =>
        new(SessionId, sequence, TranscriptKinds.AssistantText, "u" + sequence, null,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence), "assistant", "row " + sequence, null, null, null, null, null);

    private static RunnerTranscriptEvent[] Rows(int from, int to) =>
        Enumerable.Range(from, Math.Max(0, to - from + 1)).Select(i => Row(i)).ToArray();

    private static RunnerTranscriptDto Body(
        RunnerTranscriptEvent[] rows, long last, string? stream, bool terminalComplete = false, DateTime? generation = null) =>
        new(SessionId, rows, last, terminalComplete, generation ?? Generation, Stream: stream);

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
        private readonly Queue<RunnerTranscriptDto> _bodies = new();
        public List<string> Queries { get; } = new();

        public void Enqueue(RunnerTranscriptDto body) => _bodies.Enqueue(body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsolutePath.ShouldBe($"/sessions/{SessionId:D}/transcript");
            Queries.Add(request.RequestUri.Query);
            var json = JsonSerializer.Serialize(_bodies.Dequeue(), RunnerReadJson.Use(RunnerReadJson.HttpTranscript));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class PhoneHome : IAsyncDisposable
    {
        public ScriptedSocket Socket { get; }
        public PhoneHomeRunnerClient Client { get; }
        public PhoneHomeLiveConnection Connection { get; }
        private readonly CancellationTokenSource _stop;
        private readonly Task _loop;

        private PhoneHome(ScriptedSocket socket, PhoneHomeLiveConnection connection, Task loop, CancellationTokenSource stop)
        {
            Socket = socket;
            Connection = connection;
            Client = new PhoneHomeRunnerClient(connection);
            _loop = loop;
            _stop = stop;
        }

        public static PhoneHome Start(bool advertise)
        {
            var socket = new ScriptedSocket();
            var capabilities = new RunnerCapabilitiesDto("ConPty", "ConPty", "test", false,
                Features: advertise ? [RunnerCapabilityFeatures.TranscriptSinceV1] : []);
            var connection = new PhoneHomeLiveConnection(
                "runner", Guid.NewGuid(), Guid.NewGuid(), 1, socket, new PhoneHomeLimits(), TimeProvider.System,
                capabilities: capabilities);
            var stop = new CancellationTokenSource();
            return new PhoneHome(socket, connection, connection.ReceiveLoopAsync(stop.Token), stop);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await Connection.DisposeAsync();
            try { await _loop; } catch (OperationCanceledException) { }
        }
    }

    private sealed class ScriptedSocket : WebSocket
    {
        private readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>();
        private readonly CancellationTokenSource _abort = new();
        private byte[]? _pending;
        private int _offset;
        private WebSocketState _state = WebSocketState.Open;
        public List<string> Sent { get; } = new();
        public Queue<RunnerTranscriptDto> Replies { get; } = new();

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
            request.Operation.ShouldBe(PhoneHomeOperation.Transcript);
            var reply = new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                JsonSerializer.SerializeToElement(Replies.Dequeue(), RunnerReadJson.Use(RunnerReadJson.PhoneTranscript)));
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
}
