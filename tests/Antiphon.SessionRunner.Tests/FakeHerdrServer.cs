using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// Named-pipe fake for <see cref="Antiphon.SessionRunner.HerdrClient"/> — one NDJSON request per
/// normal connection (herdr's normal-request contract); <c>events.subscribe</c> keeps the pipe
/// open and pushes events (CARD-0162). Holds a small scripted state model so the typed wrappers
/// can round-trip without a live herdr. Emulates herdr's historical <c>pane_closed</c> REPLAY
/// buffer (measured E5): every new subscription receives <see cref="ReplayBuffer"/> first.
/// </summary>
internal sealed class FakeHerdrServer : IAsyncDisposable
{
    private readonly string _session;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<JsonElement> _requests = new();
    private readonly ConcurrentQueue<string> _liveEvents = new();
    private readonly List<string> _replayBuffer = [];
    private readonly List<IReadOnlyList<JsonElement>> _subscriptionRecords = [];
    private readonly object _listenGate = new();
    private readonly object _eventGate = new();
    private TaskCompletionSource _listening = NewListeningTcs();
    private TaskCompletionSource _eventAvailable = NewListeningTcs();
    private Task? _loop;
    private int _workspaceSeq;
    private int _tabSeq;
    private int _paneSeq;
    private int _termSeq;
    private int _maxInstances = 4;

    /// <summary>
    /// When set, the next <c>events.subscribe</c> that names this pane_id on a status entry fails
    /// the WHOLE call with <c>pane_not_found</c> and a SUFFIXED error id (CARD-0162 E2 shape).
    /// Cleared after one rejection.
    /// </summary>
    public string? RejectSubscribePaneId { get; set; }

    /// <summary>
    /// CARD-0186 S4: when true, <c>pane.send_text</c> updates <see cref="PaneState.ScreenText"/> so
    /// a composer-evidence poll (ready probe, queue delivery) can see what was typed. Ctrl+U
    /// (<c>\\x15</c>) clears it. Default off — existing tests script the screen themselves.
    /// </summary>
    public bool EchoSendTextToScreen { get; set; }

    /// <summary>
    /// CARD-0187: kind <c>pane.send_text</c> of <c>&amp; '&lt;x&gt;.launch.ps1'</c> stamps on the pane
    /// after <see cref="LaunchScriptDetectDelayMs"/>. Null = never detect (timeout seam). Default
    /// claude so existing CARD-0160/0164/0186 tests keep passing.
    /// </summary>
    public string? LaunchScriptAgentKind { get; set; } = HerdrAgentKinds.Claude;

    /// <summary>CARD-0187: delay before the launch-script send_text is reflected in pane.agent.</summary>
    public int LaunchScriptDetectDelayMs { get; set; }

    /// <summary>
    /// CARD-0211: when set, <c>agent.rename</c> fails with this code (measured live collision is
    /// <c>agent_name_taken</c>). The launch itself must still succeed.
    /// </summary>
    public string? RejectAgentRename { get; set; }

    public FakeHerdrServer(string? session = null)
    {
        _session = session ?? $"antiphon-herdr-test-{Guid.NewGuid():N}";
    }

    public string Session => _session;

    public string PipeName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "herdr", "sessions", _session, "herdr.sock");

    public IReadOnlyList<JsonElement> Requests => _requests.ToArray();

    /// <summary>Each subscribe call's subscriptions array, in order (CARD-0162).</summary>
    public IReadOnlyList<IReadOnlyList<JsonElement>> SubscriptionRecords => _subscriptionRecords.ToArray();

    /// <summary>
    /// Historical events re-delivered to EVERY new subscription (emulates measured E5 replay).
    /// Each entry is a full NDJSON event line (<c>{"event":"...","data":{...}}</c>).
    /// </summary>
    public IList<string> ReplayBuffer => _replayBuffer;

    public List<WorkspaceState> Workspaces { get; } = [];

    /// <summary>Enqueue a live push event for the current (or next) open subscription stream.</summary>
    public void EnqueueEvent(string eventName, object data)
    {
        var json = JsonSerializer.Serialize(new { @event = eventName, data });
        lock (_eventGate)
        {
            _liveEvents.Enqueue(json);
            _eventAvailable.TrySetResult();
        }
    }

    /// <summary>Emit the dotted live status event measured from herdr 0.8.2 (CARD-0163 R9).</summary>
    public void EnqueuePaneAgentStatusChanged(string paneId, string workspaceId, string agentStatus) =>
        EnqueueEvent(HerdrEventTypes.PaneAgentStatusChangedWireDotted,
            new { pane_id = paneId, workspace_id = workspaceId, agent_status = agentStatus });

    /// <summary>Record a historical <c>pane_closed</c> that every future subscriber will replay.</summary>
    public void AddReplayPaneClosed(string paneId, string workspaceId)
    {
        var json = JsonSerializer.Serialize(new
        {
            @event = HerdrEventTypes.PaneClosedWire,
            data = new { pane_id = paneId, workspace_id = workspaceId }
        });
        _replayBuffer.Add(json);
    }

    public void Start()
    {
        if (_loop is not null)
            throw new InvalidOperationException("FakeHerdrServer already started.");
        _loop = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>Waits until the fake is blocked in <c>WaitForConnection</c> (safe to dial).</summary>
    public Task WaitUntilListeningAsync(CancellationToken cancellationToken = default)
    {
        Task listening;
        lock (_listenGate)
            listening = _listening.Task;
        return listening.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, _maxInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                SignalListening();
                await pipe.WaitForConnectionAsync(ct);
                // Next Accept will re-signal; clear so WaitUntilListeningAsync after a call waits for
                // the subsequent listen rather than the one we just consumed.
                ResetListening();
                var reader = new StreamReader(
                    pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
                { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                line.ShouldNotBeNull();
                using var doc = JsonDocument.Parse(line!);
                var request = doc.RootElement.Clone();
                _requests.Enqueue(request);

                var method = request.GetProperty("method").GetString()!;
                if (method == "events.subscribe")
                {
                    // Long-lived on a detached task so AcceptLoop can keep serving pane.get / etc.
                    // while the subscription stream is open (CARD-0162 pump needs both).
                    var subPipe = pipe;
                    pipe = null; // ownership transferred — do not dispose in finally
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleSubscribeAsync(request, writer, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                        catch (IOException) { }
                        finally
                        {
                            try { writer.Dispose(); } catch (IOException) { }
                            try { reader.Dispose(); } catch (IOException) { }
                            await subPipe.DisposeAsync();
                        }
                    }, ct);
                    continue;
                }

                try
                {
                    await WriteLineAsync(writer, Handle(request), ct);
                }
                finally
                {
                    writer.Dispose();
                    reader.Dispose();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // Client disconnected mid-write — same as herdr closing the pipe after respond.
            }
            finally
            {
                if (pipe is not null)
                    await pipe.DisposeAsync();
            }
        }
    }

    private async Task HandleSubscribeAsync(JsonElement request, StreamWriter writer, CancellationToken ct)
    {
        var id = request.GetProperty("id").GetString()!;
        var parameters = request.TryGetProperty("params", out var p) ? p : default;
        var subs = new List<JsonElement>();
        if (parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("subscriptions", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in arr.EnumerateArray())
                subs.Add(entry.Clone());
        }

        _subscriptionRecords.Add(subs);

        // E2: naming an unknown / rejected pane fails the WHOLE call with a SUFFIXED error id.
        var rejectPane = RejectSubscribePaneId;
        if (rejectPane is not null)
        {
            var hit = subs.Any(s =>
                s.TryGetProperty("pane_id", out var pid)
                && string.Equals(pid.GetString(), rejectPane, StringComparison.Ordinal));
            if (hit)
            {
                RejectSubscribePaneId = null;
                var suffixedId = $"{id}:sub:1:probe";
                var err = $"{{\"id\":\"{suffixedId}\",\"error\":{{\"code\":\"pane_not_found\",\"message\":{JsonSerializer.Serialize($"pane '{rejectPane}' not found")}}}}}";
                await WriteLineAsync(writer, err, ct);
                return;
            }
        }

        await WriteLineAsync(writer,
            $"{{\"id\":\"{id}\",\"result\":{{\"type\":\"subscription_started\",\"subscriptions\":{subs.Count}}}}}",
            ct);

        // E5: replay historical pane_closed (and any other buffered events) to every new subscriber.
        foreach (var replay in _replayBuffer.ToArray())
            await WriteLineAsync(writer, replay, ct);

        while (!ct.IsCancellationRequested)
        {
            string? next;
            Task? wait = null;
            lock (_eventGate)
            {
                if (!_liveEvents.TryDequeue(out next))
                {
                    _eventAvailable = NewListeningTcs();
                    wait = _eventAvailable.Task;
                }
            }

            if (next is not null)
            {
                await WriteLineAsync(writer, next, ct);
                continue;
            }

            try
            {
                await wait!.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void SignalListening()
    {
        lock (_listenGate)
            _listening.TrySetResult();
    }

    private void ResetListening()
    {
        lock (_listenGate)
            _listening = NewListeningTcs();
    }

    private static TaskCompletionSource NewListeningTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string Handle(JsonElement request)
    {
        var id = request.GetProperty("id").GetString()!;
        var method = request.GetProperty("method").GetString()!;
        var parameters = request.TryGetProperty("params", out var p) ? p : default;

        try
        {
            var resultJson = method switch
            {
                "ping" => """{"type":"pong","version":"0.8.2","protocol":20}""",
                "workspace.list" => WorkspaceListJson(),
                "workspace.create" => WorkspaceCreateJson(parameters),
                "workspace.report_metadata" => ReportWorkspaceMetadata(parameters),
                "tab.create" => TabCreateJson(parameters),
                "tab.close" => TabCloseJson(parameters),
                "pane.split" => PaneSplitJson(parameters),
                "pane.rename" => PaneRenameJson(parameters),
                "pane.report_metadata" => ReportPaneMetadata(parameters),
                // CARD-0163 safety pin: no production route may claim lifecycle authority.
                "pane.report_agent" => throw new FakeHerdrApiException("forbidden_in_tests", "pane.report_agent is forbidden in tests"),
                "pane.report_agent_session" => ReportPaneAgentSession(parameters),
                "pane.get" => PaneGetJson(parameters),
                "pane.list" => PaneListJson(parameters),
                "pane.process_info" => PaneProcessInfoJson(parameters),
                "pane.read" => PaneReadJson(parameters),
                "pane.send_text" => PaneSendTextJson(parameters),
                "pane.send_keys" => OkJson(),
                "pane.close" => PaneCloseJson(parameters),
                "agent.start" => AgentStartJson(parameters),
                "agent.list" => AgentListJson(),
                "agent.rename" => AgentRenameJson(parameters),
                _ => throw new InvalidOperationException($"FakeHerdrServer has no handler for '{method}'.")
            };
            return $"{{\"id\":\"{id}\",\"result\":{resultJson}}}";
        }
        catch (FakeHerdrApiException ex)
        {
            return $"{{\"id\":\"{id}\",\"error\":{{\"code\":\"{ex.Code}\",\"message\":{JsonSerializer.Serialize(ex.Message)}}}}}";
        }
    }

    private string WorkspaceListJson()
    {
        var items = Workspaces.Select(w => WorkspaceJson(w));
        return $"{{\"type\":\"workspace_list\",\"workspaces\":[{string.Join(",", items)}]}}";
    }

    private string WorkspaceCreateJson(JsonElement parameters)
    {
        var id = $"w{++_workspaceSeq}";
        var tabId = $"{id}:t{++_tabSeq}";
        var paneId = $"{id}:p{++_paneSeq}";
        var termId = $"term_{++_termSeq:x12}";
        var label = OptString(parameters, "label") ?? id;
        var cwd = OptString(parameters, "cwd");
        var pane = new PaneState(paneId, tabId, id, termId, cwd, null, null, null, null);
        var tab = new TabState(tabId, id, "1", 1, [pane]);
        var ws = new WorkspaceState(id, label, _workspaceSeq, tabId, [tab], new Dictionary<string, string>());
        Workspaces.Add(ws);
        return
            $"{{\"type\":\"workspace_created\",\"workspace\":{WorkspaceJson(ws)},\"tab\":{TabJson(tab)},\"root_pane\":{PaneJson(pane)}}}";
    }

    private string ReportWorkspaceMetadata(JsonElement parameters)
    {
        var ws = RequireWorkspace(parameters.GetProperty("workspace_id").GetString()!);
        RequireSourceAntiphon(parameters);
        if (parameters.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in tokens.EnumerateObject())
                ws.Tokens[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? "" : prop.Value.GetString() ?? "";
        }

        return OkJson();
    }

    private string TabCreateJson(JsonElement parameters)
    {
        var ws = RequireWorkspace(parameters.GetProperty("workspace_id").GetString()!);
        var tabId = $"{ws.WorkspaceId}:t{++_tabSeq}";
        var paneId = $"{ws.WorkspaceId}:p{++_paneSeq}";
        var termId = $"term_{++_termSeq:x12}";
        var label = OptString(parameters, "label") ?? $"tab-{_tabSeq}";
        var cwd = OptString(parameters, "cwd");
        var env = ReadEnv(parameters);
        var pane = new PaneState(paneId, tabId, ws.WorkspaceId, termId, cwd, null, null, null, env);
        var tab = new TabState(tabId, ws.WorkspaceId, label, ws.Tabs.Count + 1, [pane]);
        ws.Tabs.Add(tab);
        ws.ActiveTabId = tabId;
        return $"{{\"type\":\"tab_created\",\"tab\":{TabJson(tab)},\"root_pane\":{PaneJson(pane)}}}";
    }

    private string TabCloseJson(JsonElement parameters)
    {
        var tabId = parameters.GetProperty("tab_id").GetString()!;
        foreach (var ws in Workspaces)
        {
            var idx = ws.Tabs.FindIndex(t => t.TabId == tabId);
            if (idx < 0)
                continue;
            ws.Tabs.RemoveAt(idx);
            if (ws.ActiveTabId == tabId)
                ws.ActiveTabId = ws.Tabs.FirstOrDefault()?.TabId ?? "";
            return OkJson();
        }

        throw new FakeHerdrApiException("not_found", $"tab '{tabId}' not found");
    }

    private string PaneSplitJson(JsonElement parameters)
    {
        var targetId = parameters.GetProperty("target_pane_id").GetString()!;
        var (ws, tab, _) = RequirePane(targetId);
        var paneId = $"{ws.WorkspaceId}:p{++_paneSeq}";
        var termId = $"term_{++_termSeq:x12}";
        var cwd = OptString(parameters, "cwd");
        var env = ReadEnv(parameters);
        var pane = new PaneState(paneId, tab.TabId, ws.WorkspaceId, termId, cwd, null, null, null, env);
        tab.Panes.Add(pane);
        return $"{{\"type\":\"pane_info\",\"pane\":{PaneJson(pane)}}}";
    }

    private string PaneRenameJson(JsonElement parameters)
    {
        var (_, _, pane) = RequirePane(parameters.GetProperty("pane_id").GetString()!);
        pane.Label = OptString(parameters, "label");
        return OkJson();
    }

    private string ReportPaneMetadata(JsonElement parameters)
    {
        var (_, _, pane) = RequirePane(parameters.GetProperty("pane_id").GetString()!);
        RequireSourceAntiphon(parameters);
        var seq = parameters.TryGetProperty("seq", out var suppliedSeq) && suppliedSeq.ValueKind == JsonValueKind.Number
            ? suppliedSeq.GetUInt64() : (ulong?)null;
        if (seq is ulong stale && stale <= pane.MetadataSeq)
            return OkJson();
        if (seq is ulong next)
            pane.MetadataSeq = next;
        pane.Title = OptString(parameters, "title");
        if (parameters.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
        {
            pane.Tokens ??= new Dictionary<string, string>();
            foreach (var prop in tokens.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Null)
                    pane.Tokens.Remove(prop.Name);
                else
                    pane.Tokens[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
        if (parameters.TryGetProperty("clear_state_labels", out var clear) && clear.GetBoolean())
            pane.StateLabels = null;
        if (parameters.TryGetProperty("state_labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
            pane.StateLabels = labels.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");

        // R6: metadata reports emit a dotted status event but do not change the effective status.
        EnqueuePaneAgentStatusChanged(pane.PaneId, pane.WorkspaceId, "unknown");

        return OkJson();
    }

    private string ReportPaneAgentSession(JsonElement parameters)
    {
        var (_, _, pane) = RequirePane(parameters.GetProperty("pane_id").GetString()!);
        RequireSourceAntiphon(parameters);
        var agent = parameters.GetProperty("agent").GetString()!;
        var sessionId = OptString(parameters, "agent_session_id");
        var sessionPath = OptString(parameters, "agent_session_path");
        pane.Agent = agent;
        if (sessionId is not null)
            pane.AgentSession = new AgentSessionState("antiphon", agent, "id", sessionId);
        else if (sessionPath is not null)
            pane.AgentSession = new AgentSessionState("antiphon", agent, "path", sessionPath);
        return OkJson();
    }

    private string PaneGetJson(JsonElement parameters)
    {
        var (_, _, pane) = RequirePane(parameters.GetProperty("pane_id").GetString()!);
        ApplyLaunchDetection(pane);
        return $"{{\"type\":\"pane_info\",\"pane\":{PaneJson(pane)}}}";
    }

    private string PaneListJson(JsonElement parameters)
    {
        IEnumerable<PaneState> panes;
        if (parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("workspace_id", out var wid)
            && wid.ValueKind == JsonValueKind.String)
        {
            var ws = RequireWorkspace(wid.GetString()!);
            panes = ws.Tabs.SelectMany(t => t.Panes);
        }
        else
        {
            panes = Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes));
        }

        return $"{{\"type\":\"pane_list\",\"panes\":[{string.Join(",", panes.Select(PaneJson))}]}}";
    }

    /// <summary>
    /// CARD-0186: script pane.process_info. Default (null override) is the historical
    /// shell 4242 / claude 4243 pair that <see cref="HerdrClientTests"/> pins.
    /// argv on this overload is <c>[name]</c> (historical).
    /// </summary>
    public void SetPaneProcessInfo(string paneId, int? shellPid, params (int Pid, string Name)[] foreground)
    {
        SetPaneProcessInfo(
            paneId,
            shellPid,
            (IReadOnlyList<(int Pid, string Name, string[] Argv, string? Cwd)>)
                foreground.Select(p => (p.Pid, p.Name, new[] { p.Name }, (string?)"C:\\src")).ToArray());
    }

    /// <summary>
    /// CARD-0213: stamp herdr's own <c>agent_session</c> (source != antiphon) so attach can
    /// take the native id from the pane when argv is silent.
    /// </summary>
    public void SetPaneAgentSession(string paneId, string source, string kind, string value, string agent = "grok")
    {
        var (_, _, pane) = RequirePane(paneId);
        pane.AgentSession = new AgentSessionState(source, agent, kind, value);
    }

    /// <summary>CARD-0224: script argv + cwd on each foreground process for identity checks.</summary>
    public void SetPaneProcessInfo(
        string paneId,
        int? shellPid,
        IReadOnlyList<(int Pid, string Name, string[] Argv, string? Cwd)> foreground)
    {
        var (_, _, pane) = RequirePane(paneId);
        pane.ShellPidOverride = shellPid;
        pane.ForegroundOverride = foreground
            .Select(p => new FakeForegroundProcess(p.Pid, p.Name, p.Argv, p.Cwd))
            .ToList();
    }

    /// <summary>
    /// CARD-0224: drop herdr's detected agent so an emptied pane no longer reads as occupied.
    /// Also clears launch-script detection so <see cref="ApplyLaunchDetection"/> cannot restamp it.
    /// </summary>
    public void ClearDetectedAgent(string paneId)
    {
        var (_, _, pane) = RequirePane(paneId);
        pane.Agent = null;
        pane.AgentName = null;
        pane.LaunchDetectKind = null;
        pane.LaunchDetectAtUtc = null;
        pane.AgentSession = null;
    }

    /// <summary>CARD-0224: remove a pane from the fake (herdr restart without layout restore).</summary>
    public void RemovePane(string paneId)
    {
        foreach (var ws in Workspaces)
        {
            foreach (var tab in ws.Tabs.ToList())
            {
                var idx = tab.Panes.FindIndex(p => p.PaneId == paneId);
                if (idx < 0)
                    continue;
                tab.Panes.RemoveAt(idx);
                if (tab.Panes.Count == 0)
                    ws.Tabs.Remove(tab);
                return;
            }
        }
    }

    private string PaneProcessInfoJson(JsonElement parameters)
    {
        var paneId = parameters.GetProperty("pane_id").GetString()!;
        var (_, _, pane) = RequirePane(paneId);
        ApplyLaunchDetection(pane);
        int? shellPid;
        string foregroundJson;
        if (pane.ForegroundOverride is { } fg)
        {
            shellPid = pane.ShellPidOverride;
            foregroundJson = string.Join(",", fg.Select(p =>
            {
                var argv = p.Argv is { Length: > 0 }
                    ? string.Join(",", p.Argv.Select(a => JsonSerializer.Serialize(a)))
                    : JsonSerializer.Serialize(p.Name);
                var cwd = JsonSerializer.Serialize(p.Cwd ?? "C:\\src");
                return $"{{\"pid\":{p.Pid},\"name\":{JsonSerializer.Serialize(p.Name)},\"argv\":[{argv}],\"cwd\":{cwd}}}";
            }));
        }
        else if (pane.Agent is not null)
        {
            // Probe K5/K6/K7: after detection the foreground list is the child (leaf under a
            // wrapper, cmd.exe for a .cmd launcher). Empty before detection (K8).
            shellPid = pane.ShellPidOverride ?? 4242;
            var childName = pane.Agent + ".exe";
            foregroundJson =
                $"{{\"pid\":4243,\"name\":{JsonSerializer.Serialize(childName)},\"argv\":[{JsonSerializer.Serialize(pane.Agent)}],\"cwd\":\"C:\\\\src\"}}";
        }
        else
        {
            shellPid = pane.ShellPidOverride ?? 4242;
            foregroundJson = "";
        }

        var shellJson = shellPid is int s ? s.ToString() : "null";
        return
            $"{{\"type\":\"pane_process_info\",\"process_info\":{{\"pane_id\":\"{paneId}\",\"shell_pid\":{shellJson},\"foreground_processes\":[{foregroundJson}],\"tty\":null}}}}";
    }

    private string PaneSendTextJson(JsonElement parameters)
    {
        var paneId = parameters.GetProperty("pane_id").GetString()!;
        var text = parameters.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var (_, _, pane) = RequirePane(paneId);
        if (IsLaunchScriptInvocation(text))
        {
            pane.LaunchDetectKind = LaunchScriptAgentKind;
            pane.LaunchDetectAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(0, LaunchScriptDetectDelayMs));
            ApplyLaunchDetection(pane);
        }

        if (EchoSendTextToScreen)
        {
            // Ctrl+U kill-line — ComposerInputProbe.KillLine. Anything else replaces the visible
            // composer the way a typed body does.
            pane.ScreenText = text == "\u0015" ? "" : text;
        }

        return OkJson();
    }

    /// <summary>
    /// CARD-0187: detect the typed launch line without referencing internal
    /// <c>HerdrLaunchScript</c> (this file is also compiled into Antiphon.Tests).
    /// </summary>
    private static bool IsLaunchScriptInvocation(string text)
    {
        const string prefix = "& '";
        if (text.Length < prefix.Length + 1
            || !text.StartsWith(prefix, StringComparison.Ordinal)
            || text[^1] != '\'')
            return false;
        var inner = text[prefix.Length..^1].Replace("''", "'", StringComparison.Ordinal);
        return inner.EndsWith(".launch.ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyLaunchDetection(PaneState pane)
    {
        if (pane.Agent is not null)
            return;
        if (pane.LaunchDetectAtUtc is not DateTime at)
            return;
        if (DateTime.UtcNow < at)
            return;
        if (pane.LaunchDetectKind is not { } kind)
            return;

        pane.Agent = kind;
        pane.ScreenText ??= $"agent:{kind} env={FormatEnv(pane.Env)}";
    }

    private string PaneReadJson(JsonElement parameters)
    {
        var paneId = parameters.GetProperty("pane_id").GetString()!;
        var (_, tab, pane) = RequirePane(paneId);
        var source = parameters.GetProperty("source").GetString()!;
        // CARD-0164: scripted text queue advances one step per read when present; otherwise the
        // sticky ScreenText. Identical consecutive reads must stay byte-identical (M1 pin).
        string text;
        if (pane.ScreenTextQueue is { Count: > 0 } queue)
        {
            text = queue[0];
            if (queue.Count > 1)
                queue.RemoveAt(0);
            pane.ScreenText = text;
        }
        else
        {
            text = pane.ScreenText ?? "";
        }

        return
            $"{{\"type\":\"pane_read\",\"read\":{{\"pane_id\":\"{paneId}\",\"workspace_id\":\"{pane.WorkspaceId}\",\"tab_id\":\"{tab.TabId}\",\"source\":\"{source}\",\"format\":\"text\",\"text\":{JsonSerializer.Serialize(text)},\"revision\":{pane.Revision},\"truncated\":false}}}}";
    }

    /// <summary>
    /// CARD-0164: set the sticky visible text for a pane (revision untouched — measured 0.8.2 truth).
    /// </summary>
    public void SetPaneScreenText(string paneId, string text)
    {
        var (_, _, pane) = RequirePane(paneId);
        pane.ScreenText = text;
        pane.ScreenTextQueue = null;
    }

    /// <summary>
    /// CARD-0164: script a per-read text sequence. Each <c>pane.read</c> consumes the next entry;
    /// the final entry sticks (repeated identical reads do not fabricate a delta).
    /// </summary>
    public void SetPaneScreenTextSequence(string paneId, params string[] texts)
    {
        if (texts is null || texts.Length == 0)
            throw new ArgumentException("At least one text entry is required.", nameof(texts));
        var (_, _, pane) = RequirePane(paneId);
        pane.ScreenTextQueue = texts.ToList();
        pane.ScreenText = texts[0];
    }

    /// <summary>
    /// CARD-0164: pin herdr revision explicitly. Default is sticky (never auto-bumps) — tests that
    /// need revision to move must call this. Fold-only path for "revision moves, text does not".
    /// </summary>
    public void SetPaneRevision(string paneId, long revision)
    {
        var (_, _, pane) = RequirePane(paneId);
        pane.Revision = revision;
    }

    /// <summary>
    /// CARD-0211: stamp a live detected agent (and optional name) onto an existing pane, or
    /// create a stub workspace/tab/pane with <paramref name="paneId"/>. Tests that seed a
    /// colliding holder must not call <see cref="RequireAgentPaneId"/>.
    /// </summary>
    public PaneState SeedDetectedAgent(string paneId, string kind, string? name = null)
    {
        foreach (var ws in Workspaces)
        {
            foreach (var tab in ws.Tabs)
            {
                var existing = tab.Panes.FirstOrDefault(p => p.PaneId == paneId);
                if (existing is not null)
                {
                    existing.Agent = kind;
                    existing.AgentName = name;
                    return existing;
                }
            }
        }

        var workspaceId = paneId.Contains(':') ? paneId.Split(':')[0] : paneId;
        var wsState = Workspaces.FirstOrDefault(w => w.WorkspaceId == workspaceId);
        if (wsState is null)
        {
            wsState = new WorkspaceState(
                workspaceId, "seeded", 99, $"{workspaceId}:t-seed", [], new Dictionary<string, string>());
            Workspaces.Add(wsState);
        }

        var tabId = $"{workspaceId}:t-seed";
        var tabState = wsState.Tabs.FirstOrDefault(t => t.TabId == tabId);
        if (tabState is null)
        {
            tabState = new TabState(tabId, workspaceId, "seed", wsState.Tabs.Count + 1, []);
            wsState.Tabs.Add(tabState);
            if (string.IsNullOrEmpty(wsState.ActiveTabId))
                wsState.ActiveTabId = tabId;
        }

        var pane = new PaneState(
            paneId, tabState.TabId, workspaceId, "term_seed", cwd: null, label: name, title: null,
            agent: kind, env: null)
        {
            AgentName = name,
        };
        tabState.Panes.Add(pane);
        return pane;
    }

    /// <summary>
    /// Resolve the pane that <c>agent.start</c> bound (workspace.create also leaves a root pane).
    /// </summary>
    public string RequireAgentPaneId()
    {
        var panes = Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes))
            .Where(p => p.Agent is not null)
            .ToList();
        panes.Count.ShouldBe(1, "expected exactly one agent-started pane");
        return panes[0].PaneId;
    }

    private string PaneCloseJson(JsonElement parameters)
    {
        var paneId = parameters.GetProperty("pane_id").GetString()!;
        foreach (var ws in Workspaces)
        {
            foreach (var tab in ws.Tabs)
            {
                var idx = tab.Panes.FindIndex(p => p.PaneId == paneId);
                if (idx < 0)
                    continue;
                tab.Panes.RemoveAt(idx);
                // P3: tabs auto-remove when last pane closes.
                if (tab.Panes.Count == 0)
                    ws.Tabs.Remove(tab);
                return OkJson();
            }
        }

        throw new FakeHerdrApiException("not_found", $"pane '{paneId}' not found");
    }

    private string AgentStartJson(JsonElement parameters)
    {
        var paneId = parameters.GetProperty("pane_id").GetString()!;
        var name = parameters.GetProperty("name").GetString()!;
        var kind = parameters.GetProperty("kind").GetString()!;
        var (_, _, pane) = RequirePane(paneId);
        pane.Agent = kind;
        pane.Label = name;
        pane.AgentName = name;
        // CARD-0164: sticky revision is the fake's default (measured 0.8.2). Screen text may change
        // on agent.start; revision does NOT auto-bump — tests that need it call SetPaneRevision.
        pane.ScreenText = $"agent:{kind} env={FormatEnv(pane.Env)}";
        return
            $"{{\"type\":\"agent_started\",\"agent\":{{\"pane_id\":\"{pane.PaneId}\",\"tab_id\":\"{pane.TabId}\",\"workspace_id\":\"{pane.WorkspaceId}\",\"terminal_id\":\"{pane.TerminalId}\",\"name\":{JsonSerializer.Serialize(name)},\"agent\":{JsonSerializer.Serialize(kind)},\"agent_status\":\"idle\",\"cwd\":{JsonSerializer.Serialize(pane.Cwd)},\"revision\":{pane.Revision},\"focused\":false,\"interactive_ready\":true,\"launch_pending\":false}},\"argv\":[]}}";
    }

    /// <summary>
    /// CARD-0211: live agents = panes with <see cref="PaneState.Agent"/> set. Name is omitted when
    /// null so the K5 nameless shape deserialises as <c>HerdrAgentInfo.Name == null</c>.
    /// </summary>
    private string AgentListJson()
    {
        var agents = Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes)).ToList();
        foreach (var pane in agents)
            ApplyLaunchDetection(pane);

        var items = agents
            .Where(p => p.Agent is not null)
            .Select(AgentListItemJson);
        return $"{{\"type\":\"agent_list\",\"agents\":[{string.Join(",", items)}]}}";
    }

    private static string AgentListItemJson(PaneState p)
    {
        var nameJson = p.AgentName is null ? "" : $",\"name\":{JsonSerializer.Serialize(p.AgentName)}";
        return
            $"{{\"pane_id\":\"{p.PaneId}\",\"tab_id\":\"{p.TabId}\",\"workspace_id\":\"{p.WorkspaceId}\",\"terminal_id\":\"{p.TerminalId}\"{nameJson},\"agent\":{JsonSerializer.Serialize(p.Agent)},\"agent_status\":\"idle\",\"cwd\":{JsonSerializer.Serialize(p.Cwd)},\"revision\":{p.Revision},\"focused\":false,\"interactive_ready\":true,\"launch_pending\":false}}";
    }

    /// <summary>
    /// CARD-0211: target is pane id or unique live name. Collision code is the measured
    /// <c>agent_name_taken</c> (2026-08-28 scratch-pane probe). <c>name: null</c> clears.
    /// </summary>
    private string AgentRenameJson(JsonElement parameters)
    {
        var target = parameters.GetProperty("target").GetString()
                     ?? throw new FakeHerdrApiException("invalid_params", "agent.rename requires target");
        if (RejectAgentRename is { } rejectCode)
            throw new FakeHerdrApiException(rejectCode, $"agent.rename rejected ({rejectCode})");

        string? name;
        if (!parameters.TryGetProperty("name", out var nameEl) || nameEl.ValueKind == JsonValueKind.Null)
            name = null;
        else if (nameEl.ValueKind == JsonValueKind.String)
            name = nameEl.GetString();
        else
            throw new FakeHerdrApiException("invalid_params", "agent.rename name must be a string or null");

        var pane = ResolveAgentTarget(target);
        if (name is null)
        {
            pane.AgentName = null;
            return $"{{\"type\":\"agent_info\",\"agent\":{AgentListItemJson(pane)}}}";
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9_-]{0,31}$"))
        {
            throw new FakeHerdrApiException(
                "invalid_agent_name",
                "agent name must start with a lowercase letter and contain only lowercase letters, digits, '-' or '_' (1-32 characters)");
        }

        var taken = Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes))
            .FirstOrDefault(p =>
                p.PaneId != pane.PaneId
                && p.Agent is not null
                && string.Equals(p.AgentName, name, StringComparison.Ordinal));
        if (taken is not null)
        {
            throw new FakeHerdrApiException(
                "agent_name_taken",
                $"agent name {name} is already used; candidates: pane_id={taken.PaneId}");
        }

        pane.AgentName = name;
        return $"{{\"type\":\"agent_info\",\"agent\":{AgentListItemJson(pane)}}}";
    }

    private PaneState ResolveAgentTarget(string target)
    {
        foreach (var ws in Workspaces)
        {
            foreach (var tab in ws.Tabs)
            {
                var byId = tab.Panes.FirstOrDefault(p => p.PaneId == target);
                if (byId is not null)
                {
                    if (byId.Agent is null)
                        throw new FakeHerdrApiException("not_found", $"pane '{target}' has no live agent");
                    return byId;
                }
            }
        }

        var byName = Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes))
            .Where(p => p.Agent is not null && string.Equals(p.AgentName, target, StringComparison.Ordinal))
            .ToList();
        if (byName.Count == 1)
            return byName[0];
        if (byName.Count > 1)
            throw new FakeHerdrApiException("agent_name_taken", $"agent name {target} is not unique");
        throw new FakeHerdrApiException("not_found", $"agent target '{target}' not found");
    }

    private static string OkJson() => """{"type":"ok"}""";

    private static void RequireSourceAntiphon(JsonElement parameters)
    {
        var source = parameters.GetProperty("source").GetString();
        if (!string.Equals(source, "antiphon", StringComparison.Ordinal))
            throw new FakeHerdrApiException("invalid_params", $"expected source 'antiphon', got '{source}'");
    }

    private WorkspaceState RequireWorkspace(string workspaceId)
    {
        var ws = Workspaces.FirstOrDefault(w => w.WorkspaceId == workspaceId);
        if (ws is null)
            throw new FakeHerdrApiException("not_found", $"workspace '{workspaceId}' not found");
        return ws;
    }

    private (WorkspaceState Ws, TabState Tab, PaneState Pane) RequirePane(string paneId)
    {
        foreach (var ws in Workspaces)
        {
            foreach (var tab in ws.Tabs)
            {
                var pane = tab.Panes.FirstOrDefault(p => p.PaneId == paneId);
                if (pane is not null)
                    return (ws, tab, pane);
            }
        }

        throw new FakeHerdrApiException("not_found", $"pane '{paneId}' not found");
    }

    private static string WorkspaceJson(WorkspaceState w)
    {
        var tokens = w.Tokens.Count == 0
            ? "null"
            : $"{{{string.Join(",", w.Tokens.Select(kv => $"{JsonSerializer.Serialize(kv.Key)}:{JsonSerializer.Serialize(kv.Value)}"))}}}";
        return
            $"{{\"workspace_id\":\"{w.WorkspaceId}\",\"label\":{JsonSerializer.Serialize(w.Label)},\"number\":{w.Number},\"active_tab_id\":\"{w.ActiveTabId}\",\"pane_count\":{w.Tabs.Sum(t => t.Panes.Count)},\"tab_count\":{w.Tabs.Count},\"focused\":false,\"agent_status\":\"unknown\",\"tokens\":{tokens}}}";
    }

    private static string TabJson(TabState t) =>
        $"{{\"tab_id\":\"{t.TabId}\",\"workspace_id\":\"{t.WorkspaceId}\",\"label\":{JsonSerializer.Serialize(t.Label)},\"number\":{t.Number},\"pane_count\":{t.Panes.Count},\"focused\":false,\"agent_status\":\"unknown\"}}";

    private static string PaneJson(PaneState p)
    {
        var tokens = p.Tokens is null || p.Tokens.Count == 0
            ? "null"
            : $"{{{string.Join(",", p.Tokens.Select(kv => $"{JsonSerializer.Serialize(kv.Key)}:{JsonSerializer.Serialize(kv.Value)}"))}}}";
        var agentSession = p.AgentSession is null
            ? "null"
            : $"{{\"source\":{JsonSerializer.Serialize(p.AgentSession.Source)},\"agent\":{JsonSerializer.Serialize(p.AgentSession.Agent)},\"kind\":{JsonSerializer.Serialize(p.AgentSession.Kind)},\"value\":{JsonSerializer.Serialize(p.AgentSession.Value)}}}";
        var labels = p.StateLabels is null || p.StateLabels.Count == 0
            ? "null"
            : $"{{{string.Join(",", p.StateLabels.Select(kv => $"{JsonSerializer.Serialize(kv.Key)}:{JsonSerializer.Serialize(kv.Value)}"))}}}";
        return
            $"{{\"pane_id\":\"{p.PaneId}\",\"tab_id\":\"{p.TabId}\",\"workspace_id\":\"{p.WorkspaceId}\",\"terminal_id\":\"{p.TerminalId}\",\"cwd\":{JsonSerializer.Serialize(p.Cwd)},\"revision\":{p.Revision},\"focused\":false,\"agent_status\":\"unknown\",\"label\":{JsonSerializer.Serialize(p.Label)},\"title\":{JsonSerializer.Serialize(p.Title)},\"agent\":{JsonSerializer.Serialize(p.Agent)},\"tokens\":{tokens},\"state_labels\":{labels},\"agent_session\":{agentSession}}}";
    }

    private static string? OptString(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null => null,
            _ => v.ToString()
        };
    }

    private static Dictionary<string, string>? ReadEnv(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("env", out var env)
            || env.ValueKind != JsonValueKind.Object)
            return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in env.EnumerateObject())
            map[prop.Name] = prop.Value.GetString() ?? "";
        return map;
    }

    private static string FormatEnv(Dictionary<string, string>? env) =>
        env is null || env.Count == 0
            ? ""
            : string.Join(";", env.Select(kv => $"{kv.Key}={kv.Value}"));

    private static Task WriteLineAsync(StreamWriter writer, string text, CancellationToken ct) =>
        writer.WriteLineAsync(text.AsMemory(), ct);

    internal sealed class WorkspaceState(
        string workspaceId,
        string label,
        int number,
        string activeTabId,
        List<TabState> tabs,
        Dictionary<string, string> tokens)
    {
        public string WorkspaceId { get; } = workspaceId;
        public string Label { get; set; } = label;
        public int Number { get; } = number;
        public string ActiveTabId { get; set; } = activeTabId;
        public List<TabState> Tabs { get; } = tabs;
        public Dictionary<string, string> Tokens { get; } = tokens;
    }

    internal sealed class TabState(
        string tabId,
        string workspaceId,
        string label,
        int number,
        List<PaneState> panes)
    {
        public string TabId { get; } = tabId;
        public string WorkspaceId { get; } = workspaceId;
        public string Label { get; set; } = label;
        public int Number { get; } = number;
        public List<PaneState> Panes { get; } = panes;
    }

    internal sealed class PaneState(
        string paneId,
        string tabId,
        string workspaceId,
        string terminalId,
        string? cwd,
        string? label,
        string? title,
        string? agent,
        Dictionary<string, string>? env)
    {
        public string PaneId { get; } = paneId;
        public string TabId { get; } = tabId;
        public string WorkspaceId { get; } = workspaceId;
        public string TerminalId { get; } = terminalId;
        public string? Cwd { get; set; } = cwd;
        public string? Label { get; set; } = label;
        public string? Title { get; set; } = title;
        public string? Agent { get; set; } = agent;
        /// <summary>CARD-0211: herdr live agent name (null = unnamed, the K5 passively-detected shape).</summary>
        public string? AgentName { get; set; }
        public Dictionary<string, string>? Env { get; } = env;
        public Dictionary<string, string>? Tokens { get; set; }
        public Dictionary<string, string>? StateLabels { get; set; }
        public ulong MetadataSeq { get; set; }
        public AgentSessionState? AgentSession { get; set; }
        public string? ScreenText { get; set; }
        /// <summary>CARD-0164: optional per-read text script; last entry sticks.</summary>
        public List<string>? ScreenTextQueue { get; set; }
        /// <summary>Sticky by default (CARD-0164) — only moves via <c>SetPaneRevision</c>.</summary>
        public long Revision { get; set; }
        /// <summary>CARD-0186: null = default 4242 (+ child after agent detect); set via <c>SetPaneProcessInfo</c>.</summary>
        public int? ShellPidOverride { get; set; }
        public List<FakeForegroundProcess>? ForegroundOverride { get; set; }
        /// <summary>CARD-0187: kind the launch-script send_text will stamp, once due.</summary>
        public string? LaunchDetectKind { get; set; }
        public DateTime? LaunchDetectAtUtc { get; set; }
    }

    internal sealed record FakeForegroundProcess(int Pid, string Name, string[]? Argv = null, string? Cwd = null);

    internal sealed record AgentSessionState(string Source, string Agent, string Kind, string Value);

    private sealed class FakeHerdrApiException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
