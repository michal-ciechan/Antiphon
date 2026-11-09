using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Server.Application.Dtos;
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

        var first = await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var second = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        second.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L]);
        second.TerminalComplete.ShouldBeTrue();
        // CARD-1798 A-3: nothing arrived, so the held rows are answered as they are, not copied.
        second.Entries.ShouldBeSameAs(first.Entries);
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

    // CARD-1798 A-1: a refused cursor is answered with the whole transcript on the runner's new
    // stream; that answer is used and held, not read a second time.
    [Test]
    public async Task Http_refused_cursor_keeps_the_runners_full_answer()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body(Rows(1, 2), 2, "t1"));
        handler.Enqueue(Body(Rows(1, 3), 3, "t2"));
        handler.Enqueue(Body(Rows(4, 4), 4, "t2"));
        // A spare whole body: a second full read shows in Queries, not as an empty queue.
        handler.Enqueue(Body(Rows(1, 4), 4, "t2"));
        var client = Client(handler);

        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var rebound = await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        var next = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        rebound.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L]);
        next.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L, 4L]);
        handler.Queries.ShouldBe(["", "?since=2&stream=t1", "?since=3&stream=t2"]);
    }

    // CARD-1798 A-4: a gap-free delta on the held stream from another generation is not joined.
    [Test]
    public void Join_refuses_a_gap_free_delta_from_another_generation()
    {
        var held = ServerBody(1, 2, Generation);

        TranscriptSinceRead.Join(SessionId, "t1", held, "t1", ServerBody(3, 3, Generation))!
            .Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L]);
        TranscriptSinceRead.Join(SessionId, "t1", held, "t1", ServerBody(3, 3, Generation.AddSeconds(1))).ShouldBeNull();
        TranscriptSinceRead.Join(SessionId, "t1", held, "t1", ServerBody(3, 3, null)).ShouldBeNull();
    }

    // CARD-1798 A-2: a session that ended or was killed is not held until LRU eviction.
    [Test]
    [Arguments("kill")]
    [Arguments("kill-generation")]
    [Arguments("exited")]
    public async Task Http_session_end_drops_the_held_transcript(string end)
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(Body(Rows(1, 2), 2, "t1"));
        handler.Enqueue(Body(Rows(1, 3), 3, "t1"));
        // A spare whole body: a cursor still sent shows in Queries, not as an empty queue.
        handler.Enqueue(Body(Rows(1, 3), 3, "t1"));
        var client = Client(handler);

        await client.GetTranscriptAsync(SessionId, CancellationToken.None);
        switch (end)
        {
            case "kill":
                await client.KillAsync(SessionId, CancellationToken.None);
                break;
            case "kill-generation":
                (await client.KillGenerationAsync(SessionId, Generation, CancellationToken.None)).Killed.ShouldBeTrue();
                break;
            default:
                var events = new List<string>();
                await foreach (var item in client.StreamEventsAsync(CancellationToken.None))
                    events.Add(item.EventName);
                events.ShouldContain(SessionRunnerEventNames.SessionExited);
                break;
        }
        var after = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        after.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L], end);
        handler.Queries.ShouldBe(["", ""], end);
        handler.Other.ShouldHaveSingleItem(end);
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
        var body = new SessionRunnerTranscriptDto(SessionId, [], 0);
        var ids = Enumerable.Range(0, TranscriptReadHold.MaxSessions + 1).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids.Take(TranscriptReadHold.MaxSessions))
            hold.Remember(id, "t", body, 0);
        hold.TryGet(ids[0], out _, out _, out _).ShouldBeTrue();

        hold.Remember(ids[^1], "t", body, 0);

        hold.Count.ShouldBe(TranscriptReadHold.MaxSessions);
        hold.TryGet(ids[0], out _, out _, out _).ShouldBeTrue();
        hold.TryGet(ids[1], out _, out _, out _).ShouldBeFalse();
        hold.TryGet(ids[^1], out _, out _, out _).ShouldBeTrue();
    }

    // CARD-1798 A-2: the held bodies stay under a byte cap; the least recently read go first.
    [Test]
    public void Hold_drops_the_least_recently_used_sessions_past_its_byte_cap()
    {
        var body = ServerBody(1, 1, Generation);
        var bytes = TranscriptReadHold.Weigh(body.Entries);
        var hold = new TranscriptReadHold(maxBytes: 3 * bytes);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        hold.Remember(a, "t", body, bytes);
        hold.Remember(b, "t", body, bytes);
        hold.Remember(c, "t", body, bytes);
        hold.TryGet(a, out _, out _, out _).ShouldBeTrue();

        hold.Remember(d, "t", body, bytes);

        hold.Count.ShouldBe(3);
        hold.Bytes.ShouldBe(3 * bytes);
        hold.TryGet(b, out _, out _, out _).ShouldBeFalse();
        hold.TryGet(a, out _, out _, out var held).ShouldBeTrue();
        held.ShouldBe(bytes);
        hold.TryGet(c, out _, out _, out _).ShouldBeTrue();
        hold.TryGet(d, out _, out _, out _).ShouldBeTrue();

        hold.Forget(a);
        hold.Bytes.ShouldBe(2 * bytes);
    }

    [Test]
    public void Hold_keeps_no_body_larger_than_its_byte_cap()
    {
        var small = ServerBody(1, 1, Generation);
        var large = ServerBody(1, 4, Generation);
        var hold = new TranscriptReadHold(maxBytes: TranscriptReadHold.Weigh(large.Entries) - 1);
        var other = Guid.NewGuid();
        hold.Remember(SessionId, "t", small, TranscriptReadHold.Weigh(small.Entries));
        hold.Remember(other, "t", small, TranscriptReadHold.Weigh(small.Entries));

        hold.Remember(SessionId, "t", large, TranscriptReadHold.Weigh(large.Entries));

        hold.TryGet(SessionId, out _, out _, out _).ShouldBeFalse();
        hold.TryGet(other, out _, out _, out _).ShouldBeTrue();
        hold.Count.ShouldBe(1);
        hold.Bytes.ShouldBe(TranscriptReadHold.Weigh(small.Entries));
    }

    [Test]
    public void Weigh_counts_the_text_each_row_carries()
    {
        var empty = TranscriptReadHold.Weigh([ServerRow(1, "")]);
        var text = TranscriptReadHold.Weigh([ServerRow(1, new string('x', 10_000))]);
        var tool = TranscriptReadHold.Weigh([ServerRow(1, "") with { ToolInput = new string('y', 10_000) }]);

        empty.ShouldBeGreaterThan(0);
        (text - empty).ShouldBeGreaterThanOrEqualTo(20_000);
        (tool - empty).ShouldBeGreaterThanOrEqualTo(20_000);
        TranscriptReadHold.Weigh([ServerRow(1, "a"), ServerRow(2, "b")]).ShouldBe(2 * TranscriptReadHold.Weigh([ServerRow(1, "a")]));
        TranscriptReadHold.DefaultMaxBytes.ShouldBe(TranscriptReadHold.MaxSessions * 256L * 1024);
    }

    // CARD-1798 A-2: a join that grows past the cap is still answered whole; it is just not held.
    [Test]
    public async Task Read_past_the_byte_cap_answers_the_whole_transcript_and_holds_nothing()
    {
        var hold = new TranscriptReadHold(maxBytes: TranscriptReadHold.Weigh(ServerBody(1, 2, Generation).Entries));
        var answers = new Queue<RunnerTranscriptDto>([Body(Rows(1, 2), 2, "t1"), Body(Rows(3, 3), 3, "t1"), Body(Rows(1, 3), 3, "t1")]);
        var queries = new List<string>();
        Task<SessionRunnerTranscriptDto> Read() => TranscriptSinceRead.ReadAsync(SessionId, hold, cursorEnabled: true,
            (since, stream, _) =>
            {
                queries.Add(stream is null ? "" : TranscriptSince.Query(since, stream));
                return Task.FromResult(answers.Dequeue());
            },
            Map, CancellationToken.None);

        (await Read()).Entries.Count.ShouldBe(2);
        hold.Count.ShouldBe(1);
        (await Read()).Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L]);
        hold.Count.ShouldBe(0);
        hold.Bytes.ShouldBe(0);
        (await Read()).Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L]);
        queries.ShouldBe(["", "?since=2&stream=t1", ""]);
    }

    private static RunnerTranscriptEvent Row(long sequence) =>
        new(SessionId, sequence, TranscriptKinds.AssistantText, "u" + sequence, null,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence), "assistant", "row " + sequence, null, null, null, null, null);

    private static RunnerTranscriptEvent[] Rows(int from, int to) =>
        Enumerable.Range(from, Math.Max(0, to - from + 1)).Select(i => Row(i)).ToArray();

    private static RunnerTranscriptDto Body(
        RunnerTranscriptEvent[] rows, long last, string? stream, bool terminalComplete = false, DateTime? generation = null) =>
        new(SessionId, rows, last, terminalComplete, generation ?? Generation, Stream: stream);

    private static SessionRunnerTranscriptEvent ServerRow(long sequence, string text) =>
        new(SessionId, sequence, TranscriptKinds.AssistantText, "u" + sequence, null,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence), "assistant", text, null, null, null, null, null);

    private static SessionRunnerTranscriptDto ServerBody(int from, int to, DateTime? generation) =>
        new(SessionId, Enumerable.Range(from, to - from + 1).Select(i => ServerRow(i, "row " + i)).ToArray(), to,
            AcceptedStartedAt: generation);

    private static SessionRunnerTranscriptDto Map(RunnerTranscriptDto body) =>
        new(body.SessionId,
            body.Entries.Select(e => ServerRow(e.Sequence, e.Text ?? "")).ToArray(),
            body.LastSequence, body.TerminalComplete, body.AcceptedStartedAt);

    private static SessionRunnerHttpClient Client(ScriptedHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(handler),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
        private readonly Queue<RunnerTranscriptDto> _bodies = new();
        public List<string> Queries { get; } = new();

        /// <summary>Every request that was not a transcript read: the kill, kill-generation and events routes.</summary>
        public List<string> Other { get; } = new();

        public void Enqueue(RunnerTranscriptDto body) => _bodies.Enqueue(body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path != $"/sessions/{SessionId:D}/transcript")
            {
                Other.Add(path);
                return Task.FromResult(Answer(path));
            }

            Queries.Add(request.RequestUri.Query);
            var json = JsonSerializer.Serialize(_bodies.Dequeue(), RunnerReadJson.Use(RunnerReadJson.HttpTranscript));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        private static HttpResponseMessage Answer(string path)
        {
            if (path == $"/sessions/{SessionId:D}/kill")
                return Json(new RunnerSessionDto(SessionId, null, Generation, "Exited", null, "Killed", 2));
            if (path == $"/sessions/{SessionId:D}/kill-generation")
                return Json(new RunnerKillGenerationResult(SessionId, true, "killed", Generation));
            path.ShouldBe("/events");
            var exited = JsonSerializer.Serialize(
                new RunnerSessionExitedEvent(SessionId, 0, "Exited", 2, Generation), Web);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"event: {SessionRunnerEventNames.SessionExited}\ndata: {exited}\n\n", Encoding.UTF8, "text/event-stream"),
            };
        }

        private static HttpResponseMessage Json<T>(T body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, Web), Encoding.UTF8, "application/json"),
            };
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
