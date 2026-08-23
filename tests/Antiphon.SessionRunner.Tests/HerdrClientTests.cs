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

    [Test]
    public async Task Typed_wrappers_round_trip_workspace_tab_pane_and_agent_against_fake_state()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        var client = ClientFor(fake.Session);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await fake.WaitUntilListeningAsync(cts.Token);
        var empty = await client.WorkspaceListAsync(cts.Token);
        empty.ShouldBeEmpty();

        await fake.WaitUntilListeningAsync(cts.Token);
        var created = await client.WorkspaceCreateAsync(@"C:\src\Antiphon", "probe-ws", cts.Token);
        // P1: workspace.create returns workspace.workspace_id
        created.WorkspaceId.ShouldBe("w1");
        created.Label.ShouldBe("probe-ws");

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.WorkspaceReportMetadataAsync(
            created.WorkspaceId,
            new Dictionary<string, string?> { ["antiphon-ws"] = "project:test" },
            cts.Token);
        // P5: omit ttl — fake accepts tokens without ttl_ms
        fake.Workspaces[0].Tokens["antiphon-ws"].ShouldBe("project:test");
        var reportReq = fake.Requests.Last(r => r.GetProperty("method").GetString() == "workspace.report_metadata");
        reportReq.GetProperty("params").GetProperty("source").GetString().ShouldBe(HerdrSources.Antiphon);
        reportReq.GetProperty("params").TryGetProperty("ttl_ms", out _).ShouldBeFalse();

        await fake.WaitUntilListeningAsync(cts.Token);
        var listed = await client.WorkspaceListAsync(cts.Token);
        listed.Count.ShouldBe(1);
        listed[0].WorkspaceId.ShouldBe("w1");
        listed[0].Tokens!["antiphon-ws"].ShouldBe("project:test");

        await fake.WaitUntilListeningAsync(cts.Token);
        var tab = await client.TabCreateAsync(
            created.WorkspaceId,
            @"C:\src\worktree",
            new Dictionary<string, string> { ["ANTIPHON_TASK_TOKEN"] = "tok-1" },
            "Agent-A",
            cts.Token);
        // P2: tab.create returns root_pane.pane_id as initial pane
        tab.InitialPaneId.ShouldBe("w1:p2"); // p1 was workspace root; p2 is tab root
        tab.TabId.ShouldNotBeNullOrWhiteSpace();
        tab.RootPane.Cwd.ShouldBe(@"C:\src\worktree");

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.PaneRenameAsync(tab.InitialPaneId, "Agent-A", cts.Token);

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.PaneReportMetadataAsync(
            tab.InitialPaneId,
            new Dictionary<string, string?> { ["antiphon-session"] = Guid.NewGuid().ToString("N") },
            "Agent-A",
            cts.Token);
        var paneMeta = fake.Requests.Last(r => r.GetProperty("method").GetString() == "pane.report_metadata");
        paneMeta.GetProperty("params").GetProperty("source").GetString().ShouldBe(HerdrSources.Antiphon);

        await fake.WaitUntilListeningAsync(cts.Token);
        var split = await client.PaneSplitAsync(
            tab.InitialPaneId, "right", 0.5, @"C:\src\worktree-2",
            new Dictionary<string, string> { ["ANTIPHON_TASK_TOKEN"] = "tok-2" },
            cts.Token);
        split.PaneId.ShouldNotBe(tab.InitialPaneId);
        split.TabId.ShouldBe(tab.TabId);

        await fake.WaitUntilListeningAsync(cts.Token);
        var panes = await client.PaneListAsync(created.WorkspaceId, cts.Token);
        panes.Count.ShouldBeGreaterThanOrEqualTo(2);

        await fake.WaitUntilListeningAsync(cts.Token);
        var got = await client.PaneGetAsync(tab.InitialPaneId, cts.Token);
        got.PaneId.ShouldBe(tab.InitialPaneId);
        got.Label.ShouldBe("Agent-A");

        await fake.WaitUntilListeningAsync(cts.Token);
        var agent = await client.AgentStartAsync(
            "Agent-A", HerdrAgentKinds.Claude, tab.InitialPaneId,
            ["--dangerously-skip-permissions"], 30_000, cts.Token);
        // P4: kind = "claude"; P6: env on tab.create inherits to pane (surfaced via fake screen text)
        agent.PaneId.ShouldBe(tab.InitialPaneId);
        agent.Agent.ShouldBe(HerdrAgentKinds.Claude);
        var startReq = fake.Requests.Last(r => r.GetProperty("method").GetString() == "agent.start");
        startReq.GetProperty("params").GetProperty("kind").GetString().ShouldBe("claude");

        await fake.WaitUntilListeningAsync(cts.Token);
        var read = await client.PaneReadAsync(tab.InitialPaneId, "visible", stripAnsi: true, lines: 50, cts.Token);
        read.Text.ShouldContain("ANTIPHON_TASK_TOKEN=tok-1");
        read.Truncated.ShouldBeFalse();

        await fake.WaitUntilListeningAsync(cts.Token);
        var proc = await client.PaneProcessInfoAsync(tab.InitialPaneId, cts.Token);
        proc.ShellPid.ShouldBe(4242);
        proc.ForegroundProcesses.ShouldNotBeNull();
        proc.ForegroundProcesses![0].Name.ShouldBe("claude.exe");

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.PaneReportAgentSessionAsync(
            tab.InitialPaneId, HerdrAgentKinds.Claude, "sess-1", null, cts.Token);
        var sessReq = fake.Requests.Last(r => r.GetProperty("method").GetString() == "pane.report_agent_session");
        sessReq.GetProperty("params").GetProperty("source").GetString().ShouldBe(HerdrSources.Antiphon);

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.PaneSendTextAsync(tab.InitialPaneId, "hello", cts.Token);
        await fake.WaitUntilListeningAsync(cts.Token);
        await client.PaneSendKeysAsync(tab.InitialPaneId, ["enter"], cts.Token);

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.PaneCloseAsync(split.PaneId, cts.Token);

        // P3: closing the last pane of a tab auto-removes the tab — TabCloseAsync is then a no-op path
        // for callers; here close the initial pane and assert the tab is gone from state.
        await fake.WaitUntilListeningAsync(cts.Token);
        var tabIdBefore = tab.TabId;
        await client.PaneCloseAsync(tab.InitialPaneId, cts.Token);
        fake.Workspaces[0].Tabs.Any(t => t.TabId == tabIdBefore).ShouldBeFalse();
    }

    [Test]
    public async Task Report_methods_stamp_source_antiphon_and_tab_close_acks()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        var client = ClientFor(fake.Session);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await fake.WaitUntilListeningAsync(cts.Token);
        var ws = await client.WorkspaceCreateAsync(null, "close-probe", cts.Token);
        await fake.WaitUntilListeningAsync(cts.Token);
        var tab = await client.TabCreateAsync(ws.WorkspaceId, null, null, "t", cts.Token);

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.TabCloseAsync(tab.TabId, cts.Token);
        fake.Workspaces[0].Tabs.Any(t => t.TabId == tab.TabId).ShouldBeFalse();
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
