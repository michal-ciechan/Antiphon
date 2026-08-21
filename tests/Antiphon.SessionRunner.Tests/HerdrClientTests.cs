using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public class HerdrClientTests
{
    [Test]
    public async Task Ping_uses_one_ndjson_request_and_validates_the_protocol()
    {
        var pipeName = NewPipeName();
        var server = ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            request.GetProperty("method").GetString().ShouldBe("ping");
            request.GetProperty("params").ValueKind.ShouldBe(JsonValueKind.Object);
            await WriteLineAsync(writer,
                $"{{\"id\":\"{request.GetProperty("id").GetString()}\",\"result\":{{\"type\":\"pong\",\"version\":\"0.8.2\",\"protocol\":20}}}}",
                ct);
        });

        var client = ClientFor(pipeName);
        var info = await client.ConnectAndValidateAsync(CancellationToken.None);

        info.ShouldBe(new HerdrServerInfo("0.8.2", 20));
        await server;
    }

    [Test]
    public async Task Request_returns_the_result_and_surfaces_a_herdr_error()
    {
        var pipeName = NewPipeName();
        var server = ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            request.GetProperty("method").GetString().ShouldBe("pane.send_text");
            request.GetProperty("params").GetProperty("text").GetString().ShouldBe("hello\nworld");
            await WriteLineAsync(writer,
                $"{{\"id\":\"{request.GetProperty("id").GetString()}\",\"result\":{{\"type\":\"ok\"}}}}", ct);
        });

        var result = await ClientFor(pipeName).SendRequestAsync(
            "pane.send_text", new { pane_id = "w1:p1", text = "hello\nworld" }, CancellationToken.None);

        result.GetProperty("type").GetString().ShouldBe("ok");
        await server;
    }

    [Test]
    public async Task Incompatible_protocol_is_a_loud_failure()
    {
        var pipeName = NewPipeName();
        var server = ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            await WriteLineAsync(writer,
                $"{{\"id\":\"{request.GetProperty("id").GetString()}\",\"result\":{{\"type\":\"pong\",\"version\":\"9.9.9\",\"protocol\":99}}}}",
                ct);
        });

        await Should.ThrowAsync<HerdrProtocolMismatchException>(
            () => ClientFor(pipeName).ConnectAndValidateAsync(CancellationToken.None));
        await server;
    }

    [Test]
    public async Task Missing_pipe_is_a_loud_backend_unavailable_failure()
    {
        var client = new HerdrClient(new HerdrSettings
        {
            Enabled = true,
            Session = $"missing-{Guid.NewGuid():N}",
            ConnectTimeoutMs = 50
        });

        var error = await Should.ThrowAsync<HerdrBackendUnavailableException>(
            () => client.ConnectAndValidateAsync(CancellationToken.None));

        error.Message.ShouldContain("Herdr is unavailable");
    }

    [Test]
    public async Task Optional_backend_defaults_off_and_never_silently_falls_back()
    {
        var error = await Should.ThrowAsync<HerdrBackendUnavailableException>(
            () => new HerdrClient(new HerdrSettings()).ConnectAndValidateAsync(CancellationToken.None));

        error.Message.ShouldContain("disabled");
    }

    [Test]
    public async Task Event_subscription_consumes_the_acknowledgement_then_yields_push_events()
    {
        var pipeName = NewPipeName();
        var server = ServePingThenSubscriptionAsync(pipeName);
        var client = ClientFor(pipeName);
        var events = new List<HerdrEvent>();

        // The fake deliberately closes after two frames. A production disconnect is loud (the
        // unavailable exception below), while this still proves the initial ack is not mistaken
        // for an event and both NDJSON push frames are parsed before the close.
        await Should.ThrowAsync<HerdrBackendUnavailableException>(async () =>
        {
            await foreach (var item in client.SubscribeEventsAsync(
                               [new HerdrSubscription("pane.agent_status_changed")],
                               CancellationToken.None))
            {
                events.Add(item);
            }
        });

        events.Count.ShouldBe(2);
        events[0].Name.ShouldBe("pane.agent_status_changed");
        events[0].Data.GetProperty("agent_status").GetString().ShouldBe("working");
        events[1].Data.GetProperty("agent_status").GetString().ShouldBe("idle");
        await server;
    }

    private static HerdrClient ClientFor(string pipeName) => new(new HerdrSettings
    {
        Enabled = true,
        ConnectTimeoutMs = 2_000,
        // HERDR_SOCKET_PATH is deliberately avoided: tests can run concurrently without mutating
        // process-wide environment. The session string is itself the fake Windows pipe name.
        Session = pipeName
    });

    private static string NewPipeName() => $"antiphon-herdr-test-{Guid.NewGuid():N}";

    private static async Task ServeOnceAsync(
        string pipeName,
        Func<JsonElement, StreamWriter, CancellationToken, Task> handler)
    {
        await using var pipe = NewServer(SocketPathForFakeSession(pipeName));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.WaitForConnectionAsync(cts.Token);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cts.Token);
        line.ShouldNotBeNull();
        using var request = JsonDocument.Parse(line!);
        await handler(request.RootElement, writer, cts.Token);
    }

    private static async Task ServePingThenSubscriptionAsync(string pipeName)
    {
        await ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            request.GetProperty("method").GetString().ShouldBe("ping");
            await WriteLineAsync(writer,
                $"{{\"id\":\"{request.GetProperty("id").GetString()}\",\"result\":{{\"type\":\"pong\",\"version\":\"0.8.2\",\"protocol\":20}}}}",
                ct);
        });

        await ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            request.GetProperty("method").GetString().ShouldBe("events.subscribe");
            request.GetProperty("params").GetProperty("subscriptions")[0].GetProperty("type").GetString()
                .ShouldBe("pane.agent_status_changed");
            var id = request.GetProperty("id").GetString();
            await WriteLineAsync(writer, $"{{\"id\":\"{id}\",\"result\":{{\"type\":\"events_subscribed\"}}}}", ct);
            await WriteLineAsync(writer,
                "{\"event\":\"pane.agent_status_changed\",\"data\":{\"pane_id\":\"w1:p1\",\"workspace_id\":\"w1\",\"agent_status\":\"working\"}}",
                ct);
            await WriteLineAsync(writer,
                "{\"event\":\"pane.agent_status_changed\",\"data\":{\"pane_id\":\"w1:p1\",\"workspace_id\":\"w1\",\"agent_status\":\"idle\"}}",
                ct);
        });
    }

    private static NamedPipeServerStream NewServer(string pipeName) => new(
        pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static string SocketPathForFakeSession(string session) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "herdr", "sessions", session, "herdr.sock");

    private static Task WriteLineAsync(StreamWriter writer, string text, CancellationToken ct) =>
        writer.WriteLineAsync(text.AsMemory(), ct);
}
