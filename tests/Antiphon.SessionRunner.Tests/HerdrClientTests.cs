using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
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
        var createEnv = new Dictionary<string, string> { ["ANTIPHON_LAUNCH_SECRET"] = "tok-ws" };
        var created = await client.WorkspaceCreateAsync(@"C:\src\Antiphon", createEnv, "probe-ws", cts.Token);
        // P1: workspace.create returns workspace, tab, and root_pane
        created.WorkspaceId.ShouldBe("w1");
        created.Workspace.Label.ShouldBe("probe-ws");
        created.Tab.TabId.ShouldBe("w1:t1");
        created.RootPane.PaneId.ShouldBe("w1:p1");
        created.RootPane.Cwd.ShouldBe(@"C:\src\Antiphon");
        fake.Workspaces[0].Tabs[0].Panes[0].Env!["ANTIPHON_LAUNCH_SECRET"].ShouldBe("tok-ws");
        var createReq = fake.Requests.Last(r => r.GetProperty("method").GetString() == "workspace.create");
        createReq.GetProperty("params").GetProperty("cwd").GetString().ShouldBe(@"C:\src\Antiphon");
        createReq.GetProperty("params").GetProperty("env").GetProperty("ANTIPHON_LAUNCH_SECRET")
            .GetString().ShouldBe("tok-ws");

        await fake.WaitUntilListeningAsync(cts.Token);
        await client.TabRenameAsync(created.Tab.TabId, "Agent-A", cts.Token);
        fake.Workspaces[0].Tabs[0].Label.ShouldBe("Agent-A");
        var tabRename = fake.Requests.Last(r => r.GetProperty("method").GetString() == "tab.rename");
        tabRename.GetProperty("params").GetProperty("tab_id").GetString().ShouldBe(created.Tab.TabId);
        tabRename.GetProperty("params").GetProperty("label").GetString().ShouldBe("Agent-A");

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

    [Test]
    public async Task Workspace_create_without_tab_and_root_pane_is_a_protocol_failure()
    {
        var pipeName = NewPipeName();
        var server = ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            request.GetProperty("method").GetString().ShouldBe("workspace.create");
            await WriteLineAsync(writer,
                $"{{\"id\":\"{request.GetProperty("id").GetString()}\",\"result\":{{\"workspace\":{{\"workspace_id\":\"w1\",\"label\":\"x\",\"number\":1,\"active_tab_id\":\"\",\"pane_count\":0,\"tab_count\":0}}}}}}",
                ct);
        });

        var error = await Should.ThrowAsync<HerdrProtocolException>(
            () => ClientFor(pipeName).WorkspaceCreateAsync(@"C:\src", null, "x", CancellationToken.None));
        error.Message.ShouldContain("tab and root_pane");
        await server;
    }

    [Test]
    public async Task Agent_rename_sends_target_and_allows_null_name()
    {
        JsonElement? captured = null;
        var pipeName = NewPipeName();
        var server = ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            request.GetProperty("method").GetString().ShouldBe("agent.rename");
            captured = request.GetProperty("params").Clone();
            await WriteLineAsync(writer,
                $"{{\"id\":\"{request.GetProperty("id").GetString()}\",\"result\":{{\"type\":\"ok\"}}}}", ct);
        });

        await ClientFor(pipeName).AgentRenameAsync("w1:p1", name: null, CancellationToken.None);
        await server;

        captured.ShouldNotBeNull();
        captured.Value.GetProperty("target").GetString().ShouldBe("w1:p1");
        captured.Value.GetProperty("name").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Agent_list_deserialises_nameless_K5_agents()
    {
        var pipeName = NewPipeName();
        var server = ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            request.GetProperty("method").GetString().ShouldBe("agent.list");
            await WriteLineAsync(writer,
                $"{{\"id\":\"{request.GetProperty("id").GetString()}\",\"result\":{{\"type\":\"agent_list\",\"agents\":[{{\"pane_id\":\"w2:p9\",\"tab_id\":\"w2:t4\",\"workspace_id\":\"w2\",\"agent\":\"codex\",\"agent_status\":\"idle\"}}]}}}}",
                ct);
        });

        var agents = await ClientFor(pipeName).AgentListAsync(CancellationToken.None);
        await server;

        agents.ShouldHaveSingleItem();
        agents[0].PaneId.ShouldBe("w2:p9");
        agents[0].Agent.ShouldBe("codex");
        agents[0].Name.ShouldBeNull();
    }

    [Test]
    public void HerdrSubscription_serializes_pane_id_and_omits_it_when_null()
    {
        var withPane = new HerdrSubscription(HerdrEventTypes.PaneAgentStatusChangedSubscribe, "w1:p1");
        var json = JsonSerializer.Serialize(withPane, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var doc = JsonDocument.Parse(json))
        {
            doc.RootElement.GetProperty("type").GetString().ShouldBe("pane.agent_status_changed");
            doc.RootElement.GetProperty("pane_id").GetString().ShouldBe("w1:p1");
        }

        var typeOnly = new HerdrSubscription(HerdrEventTypes.PaneClosedSubscribe);
        var typeOnlyJson = JsonSerializer.Serialize(typeOnly, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var doc = JsonDocument.Parse(typeOnlyJson))
        {
            doc.RootElement.GetProperty("type").GetString().ShouldBe("pane.closed");
            doc.RootElement.TryGetProperty("pane_id", out _).ShouldBeFalse();
        }
    }

    [Test]
    public async Task Subscribe_ack_accepts_suffixed_error_id_and_surfaces_pane_not_found()
    {
        // CARD-0162 E2: herdr returns error id "<requestId>:sub:1:probe" — must be HerdrApiException,
        // not HerdrProtocolException("mismatched request id").
        var pipeName = NewPipeName();
        var server = ServePingThenSubscribeErrorAsync(pipeName);
        var client = ClientFor(pipeName);

        var error = await Should.ThrowAsync<HerdrApiException>(async () =>
        {
            await foreach (var _ in client.SubscribeEventsAsync(
                               [new HerdrSubscription(HerdrEventTypes.PaneAgentStatusChangedSubscribe, "w9:p9")],
                               CancellationToken.None))
            {
            }
        });

        error.Code.ShouldBe("pane_not_found");
        await server;
    }

    [Test]
    public async Task Normal_request_still_requires_strict_id_equality()
    {
        var pipeName = NewPipeName();
        var server = ServeOnceAsync(pipeName, async (request, writer, ct) =>
        {
            // Deliberately wrong id — strict path must still reject.
            await WriteLineAsync(writer,
                "{\"id\":\"not-the-request-id\",\"result\":{\"type\":\"ok\"}}", ct);
        });

        await Should.ThrowAsync<HerdrProtocolException>(
            () => ClientFor(pipeName).SendRequestAsync("pane.send_text", new { pane_id = "w1:p1", text = "x" },
                CancellationToken.None));
        await server;
    }

    [Test]
    public async Task Fake_replays_historical_pane_closed_to_every_new_subscriber()
    {
        await using var fake = new FakeHerdrServer();
        fake.AddReplayPaneClosed("w2:p3", "w2");
        fake.Start();
        var client = ClientFor(fake.Session);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await fake.WaitUntilListeningAsync(cts.Token);
        // ping validation connection
        _ = await client.ConnectAndValidateAsync(cts.Token);

        await fake.WaitUntilListeningAsync(cts.Token);
        using var subCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        subCts.CancelAfter(TimeSpan.FromSeconds(3));
        var events = new List<HerdrEvent>();
        try
        {
            await foreach (var item in client.SubscribeEventsAsync(
                               [new HerdrSubscription(HerdrEventTypes.PaneClosedSubscribe)],
                               subCts.Token))
            {
                events.Add(item);
                if (events.Count >= 1)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // expected if we break via cancel after first event
        }

        events.Count.ShouldBeGreaterThanOrEqualTo(1);
        events[0].Name.ShouldBe(HerdrEventTypes.PaneClosedWire);
        var data = JsonSerializer.Deserialize<HerdrPaneClosedEventData>(events[0].Data.GetRawText())!;
        data.PaneId.ShouldBe("w2:p3");
        data.WorkspaceId.ShouldBe("w2");
        fake.SubscriptionRecords.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    private static async Task ServePingThenSubscribeErrorAsync(string pipeName)
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
            var id = request.GetProperty("id").GetString();
            // Measured E2 wire shape: error id is the request id SUFFIXED.
            await WriteLineAsync(writer,
                $"{{\"id\":\"{id}:sub:1:probe\",\"error\":{{\"code\":\"pane_not_found\",\"message\":\"pane 'w9:p9' not found\"}}}}",
                ct);
        });
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
