using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Antiphon.Messaging.Tests.FakeSlack;

/// <summary>
/// In-process fake of Slack's Socket Mode + Web API, covering exactly the surface the Slack adapter
/// calls: <c>apps.connections.open</c> and a real local WebSocket endpoint it points at,
/// <c>auth.test</c>, <c>chat.postMessage</c>, <c>users.info</c>, <c>conversations.info</c>, the
/// external-upload pair (<c>files.getUploadURLExternal</c> + <c>files.completeUploadExternal</c>)
/// and the authenticated <c>url_private</c> file endpoint.
///
/// The sibling of <c>FakeTelegramServer</c>, and faithful in the same way: responses are hand-built
/// as <see cref="JsonObject"/> so the wire shape (snake_case keys, the <c>ok</c>/<c>error</c>
/// envelope, HTTP 200 carrying <c>ok:false</c>) matches real Slack. Tests push envelopes down the
/// socket, read recorded sends, and observe the envelope ACKs the adapter writes back.
/// </summary>
public sealed class FakeSlackServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly object _gate = new();

    private readonly Queue<Frame> _pending = new();          // frames waiting for a live socket
    private readonly List<string> _acks = [];
    private readonly List<SlackSentMessage> _sent = [];
    private readonly List<SlackUploadedFile> _uploaded = [];
    private readonly Dictionary<string, StoredFile> _files = [];
    private readonly Dictionary<string, JsonObject> _users = [];
    private readonly Dictionary<string, JsonObject> _conversations = [];
    private readonly Dictionary<string, byte[]> _uploadBuffers = [];

    private readonly Queue<Fault> _connectionsOpenFaults = new();
    private readonly Queue<Fault> _postMessageFaults = new();
    private readonly Queue<Fault> _uploadUrlFaults = new();

    private TaskCompletionSource<bool>? _downloadGate;
    private long _nextTs = 1_700_000_000;
    private int _nextFileId = 1;

    public string BotToken { get; }
    public string AppToken { get; }
    public string BotUserId { get; }
    public string BotId { get; }
    public string BaseUrl { get; private set; } = "";
    public string ApiBaseUrl => BaseUrl + "/api";

    /// <summary>How many times <c>apps.connections.open</c> was called (incl. faulted calls).</summary>
    public int ConnectionsOpenCalls { get; private set; }

    /// <summary>How many WebSocket connections have been accepted — the reconnect counter.</summary>
    public int SocketConnections { get; private set; }

    /// <summary>How many times <c>chat.postMessage</c> was called (incl. faulted calls).</summary>
    public int PostMessageCalls { get; private set; }

    public int AuthTestCalls { get; private set; }

    /// <summary>How many times <c>files.getUploadURLExternal</c> was called (incl. faulted calls).</summary>
    public int UploadUrlCalls { get; private set; }

    /// <summary>Envelope ids the adapter has acked, in order.</summary>
    public IReadOnlyList<string> Acks
    {
        get { lock (_gate) return _acks.ToList(); }
    }

    public IReadOnlyList<SlackSentMessage> SentMessages
    {
        get { lock (_gate) return _sent.ToList(); }
    }

    public IReadOnlyList<SlackUploadedFile> UploadedFiles
    {
        get { lock (_gate) return _uploaded.ToList(); }
    }

    public FakeSlackServer(string botToken = "xoxb-test-bot-token", string appToken = "xapp-test-app-token")
    {
        BotToken = botToken;
        AppToken = appToken;
        BotUserId = "U0BOTSELF";
        BotId = "B0BOTSELF";
        var builder = WebApplication.CreateSlimBuilder();
        KestrelLoopback.ListenEphemeral(builder.WebHost);
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.UseWebSockets();
        MapEndpoints(_app);
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        BaseUrl = KestrelLoopback.BoundUrl(_app);
    }

    // ------------------------------------------------------------ directory fixtures

    /// <summary>Register a user so <c>users.info</c> resolves it (display name / username).</summary>
    public void RegisterUser(string userId, string username, string? displayName = null, string? realName = null)
    {
        lock (_gate)
            _users[userId] = new JsonObject
            {
                ["id"] = userId,
                ["name"] = username,
                ["real_name"] = realName ?? displayName ?? username,
                ["profile"] = new JsonObject
                {
                    ["display_name"] = displayName ?? "",
                    ["real_name"] = realName ?? displayName ?? username,
                },
            };
    }

    /// <summary>Register a conversation so <c>conversations.info</c> resolves its name.</summary>
    public void RegisterConversation(string channelId, string? name, bool isIm = false)
    {
        lock (_gate)
        {
            var channel = new JsonObject { ["id"] = channelId, ["is_im"] = isIm };
            if (name is not null)
                channel["name"] = name;
            _conversations[channelId] = channel;
        }
    }

    /// <summary>Register downloadable bytes for a file id, served from the authenticated url_private.</summary>
    public void RegisterFile(string fileId, byte[] bytes, string contentType = "application/octet-stream")
    {
        lock (_gate) _files[fileId] = new StoredFile(bytes, contentType);
    }

    public string UrlPrivate(string fileId) => $"{BaseUrl}/files/{fileId}";

    // ------------------------------------------------------------ inbound (socket) fixtures

    /// <summary>
    /// Queue a <c>message</c> event exactly as Socket Mode delivers it: an <c>events_api</c>
    /// envelope wrapping an <c>event_callback</c> payload.
    /// </summary>
    public string EnqueueMessage(
        string channelId,
        string userId,
        string text,
        string channelType = "channel",
        string? threadTs = null,
        string? subtype = null,
        string? botId = null,
        JsonArray? files = null,
        string? ts = null)
    {
        var stamp = ts ?? NextTs();
        var ev = new JsonObject
        {
            ["type"] = "message",
            ["channel"] = channelId,
            ["user"] = userId,
            ["text"] = text,
            ["ts"] = stamp,
            ["channel_type"] = channelType,
            ["event_ts"] = stamp,
        };
        if (threadTs is not null) ev["thread_ts"] = threadTs;
        if (subtype is not null) ev["subtype"] = subtype;
        if (botId is not null) ev["bot_id"] = botId;
        if (files is not null) ev["files"] = files;

        return EnqueueEventCallback(ev);
    }

    /// <summary>A file entry as Slack embeds it in a <c>file_share</c> message event.</summary>
    public JsonObject FileEntry(string fileId, string name, string mimetype, long size) => new()
    {
        ["id"] = fileId,
        ["name"] = name,
        ["title"] = name,
        ["mimetype"] = mimetype,
        ["size"] = size,
        ["url_private"] = UrlPrivate(fileId),
        ["url_private_download"] = UrlPrivate(fileId),
    };

    /// <summary>Queue a raw event payload wrapped in an <c>events_api</c> envelope; returns the envelope id.</summary>
    public string EnqueueEventCallback(JsonObject slackEvent)
    {
        var envelopeId = Guid.NewGuid().ToString("n");
        EnqueueEnvelope(new JsonObject
        {
            ["envelope_id"] = envelopeId,
            ["type"] = "events_api",
            ["accepts_response_payload"] = false,
            ["payload"] = new JsonObject
            {
                ["type"] = "event_callback",
                ["team_id"] = "T0TESTTEAM",
                ["api_app_id"] = "A0TESTAPP",
                ["event_id"] = "Ev" + envelopeId[..8],
                ["event"] = slackEvent,
            },
        });
        return envelopeId;
    }

    /// <summary>Queue any raw envelope.</summary>
    public void EnqueueEnvelope(JsonObject envelope)
    {
        lock (_gate) _pending.Enqueue(new Frame(envelope, Abort: false));
    }

    /// <summary>
    /// Queue Slack's <c>disconnect</c> envelope — the routine connection recycle. Nothing queued
    /// behind it is written on this socket: the fake waits for the client to reconnect, which is
    /// what real Slack's grace period effectively buys.
    /// </summary>
    public void EnqueueDisconnect(string reason = "warning") =>
        EnqueueEnvelope(new JsonObject { ["type"] = "disconnect", ["reason"] = reason });

    /// <summary>Kill the live socket without a close handshake — a mid-stream connection death.</summary>
    public void AbortSocket()
    {
        lock (_gate) _pending.Enqueue(new Frame(null, Abort: true));
    }

    // ------------------------------------------------------------ fault injection

    /// <summary>An <c>ok:false</c> from <c>apps.connections.open</c> (HTTP 200, Slack's own shape).</summary>
    public void EnqueueConnectionsOpenError(string error = "invalid_auth") =>
        Enqueue(_connectionsOpenFaults, new Fault(200, error, null));

    /// <summary>A gateway-style 5xx with a non-JSON body from <c>apps.connections.open</c>.</summary>
    public void EnqueueConnectionsOpenServerError(int status = 502) =>
        Enqueue(_connectionsOpenFaults, new Fault(status, null, null, NonJson: true));

    /// <summary>
    /// Hold the next <c>apps.connections.open</c> response — a stalled handshake that trips the
    /// caller's <c>HttpClient.Timeout</c> as a TaskCanceledException while the ambient token is NOT
    /// cancelled. Treating that OCE as shutdown is how an ingress stream dies silently.
    /// </summary>
    public void EnqueueConnectionsOpenHang(TimeSpan holdFor) =>
        Enqueue(_connectionsOpenFaults, new Fault(200, null, null, Hang: holdFor));

    public void EnqueuePostMessageRateLimit(int retryAfterSeconds) =>
        Enqueue(_postMessageFaults, new Fault(429, "ratelimited", retryAfterSeconds));

    public void EnqueuePostMessageError(string error = "channel_not_found") =>
        Enqueue(_postMessageFaults, new Fault(200, error, null));

    public void EnqueuePostMessageServerError(int status = 500) =>
        Enqueue(_postMessageFaults, new Fault(status, null, null, NonJson: true));

    public void EnqueuePostMessageHang(TimeSpan holdFor) =>
        Enqueue(_postMessageFaults, new Fault(200, null, null, Hang: holdFor));

    /// <summary>A gateway-style 5xx from <c>files.getUploadURLExternal</c> — the first leg of the
    /// external-upload flow, which the whole flow must be retried past.</summary>
    public void EnqueueUploadUrlServerError(int status = 503) =>
        Enqueue(_uploadUrlFaults, new Fault(status, null, null, NonJson: true));

    /// <summary>Block every file download until <see cref="ResumeDownloads"/>. Used to prove the
    /// adapter acks an envelope BEFORE it hydrates that message's attachments.</summary>
    public void PauseDownloads()
    {
        lock (_gate) _downloadGate ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void ResumeDownloads()
    {
        TaskCompletionSource<bool>? gate;
        lock (_gate) { gate = _downloadGate; _downloadGate = null; }
        gate?.TrySetResult(true);
    }

    private void Enqueue(Queue<Fault> q, Fault f) { lock (_gate) q.Enqueue(f); }

    // ------------------------------------------------------------ endpoints

    private void MapEndpoints(WebApplication app)
    {
        app.MapPost("/api/apps.connections.open", async (HttpContext ctx) =>
        {
            if (!Authorized(ctx, AppToken)) return InvalidAuth();

            Fault? fault;
            lock (_gate)
            {
                ConnectionsOpenCalls++;
                fault = _connectionsOpenFaults.Count > 0 ? _connectionsOpenFaults.Dequeue() : null;
            }
            if (fault is not null)
            {
                if (fault.Hang is { } hang) await Task.Delay(hang);
                if (fault.NonJson)
                    return Results.Text("<html><body>Bad Gateway</body></html>", "text/html", statusCode: fault.StatusCode);
                if (fault.Error is not null)
                    return SlackError(fault.Error);
                // Hang-only fault: answer normally — the caller usually gave up long ago.
            }

            // Real Slack hands back a single-use wss:// URL; ours is a local ws:// endpoint.
            return Ok(new JsonObject { ["url"] = $"{BaseUrl.Replace("http://", "ws://", StringComparison.Ordinal)}/link?ticket={Guid.NewGuid():n}" });
        });

        app.MapPost("/api/auth.test", (HttpContext ctx) =>
        {
            if (!Authorized(ctx, BotToken)) return InvalidAuth();
            AuthTestCalls++;
            return Ok(new JsonObject
            {
                ["url"] = "https://testteam.slack.com/",
                ["team"] = "Test Team",
                ["user"] = "antiphon",
                ["team_id"] = "T0TESTTEAM",
                ["user_id"] = BotUserId,
                ["bot_id"] = BotId,
                ["is_enterprise_install"] = false,
            });
        });

        app.MapPost("/api/chat.postMessage", async (HttpContext ctx) =>
        {
            if (!Authorized(ctx, BotToken)) return InvalidAuth();

            Fault? fault;
            lock (_gate)
            {
                PostMessageCalls++;
                fault = _postMessageFaults.Count > 0 ? _postMessageFaults.Dequeue() : null;
            }
            if (fault is not null)
            {
                if (fault.Hang is { } hang) await Task.Delay(hang);
                if (fault.NonJson)
                    return Results.Text("<html><body>Internal Server Error</body></html>", "text/html", statusCode: fault.StatusCode);
                if (fault.Error is not null)
                    return SlackError(fault.Error, fault.StatusCode, fault.RetryAfter, ctx);
            }

            var body = await ReadJsonAsync(ctx);
            var channel = body?["channel"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(channel))
                return SlackError("invalid_arguments");

            var text = body?["text"]?.GetValue<string>();
            var threadTs = body?["thread_ts"]?.GetValue<string>();
            var ts = NextTs();
            lock (_gate)
                _sent.Add(new SlackSentMessage(channel, text, threadTs, body?.DeepClone() as JsonObject));

            return Ok(new JsonObject
            {
                ["channel"] = channel,
                ["ts"] = ts,
                ["message"] = new JsonObject
                {
                    ["type"] = "message",
                    ["text"] = text,
                    ["user"] = BotUserId,
                    ["bot_id"] = BotId,
                    ["ts"] = ts,
                },
            });
        });

        app.MapPost("/api/users.info", async (HttpContext ctx) =>
        {
            if (!Authorized(ctx, BotToken)) return InvalidAuth();
            var form = await ctx.Request.ReadFormAsync();
            var id = form["user"].FirstOrDefault();
            lock (_gate)
                return id is not null && _users.TryGetValue(id, out var user)
                    ? Ok(new JsonObject { ["user"] = user.DeepClone() })
                    : SlackError("user_not_found");
        });

        app.MapPost("/api/conversations.info", async (HttpContext ctx) =>
        {
            if (!Authorized(ctx, BotToken)) return InvalidAuth();
            var form = await ctx.Request.ReadFormAsync();
            var id = form["channel"].FirstOrDefault();
            lock (_gate)
                return id is not null && _conversations.TryGetValue(id, out var channel)
                    ? Ok(new JsonObject { ["channel"] = channel.DeepClone() })
                    : SlackError("channel_not_found");
        });

        // --- the external-upload trio (files.upload is deprecated and closed to new apps) ---

        app.MapPost("/api/files.getUploadURLExternal", async (HttpContext ctx) =>
        {
            if (!Authorized(ctx, BotToken)) return InvalidAuth();

            Fault? fault;
            lock (_gate)
            {
                UploadUrlCalls++;
                fault = _uploadUrlFaults.Count > 0 ? _uploadUrlFaults.Dequeue() : null;
            }
            if (fault is not null)
            {
                if (fault.Hang is { } hang) await Task.Delay(hang);
                if (fault.NonJson)
                    return Results.Text("<html><body>Service Unavailable</body></html>", "text/html", statusCode: fault.StatusCode);
                if (fault.Error is not null)
                    return SlackError(fault.Error, fault.StatusCode, fault.RetryAfter, ctx);
            }

            var form = await ctx.Request.ReadFormAsync();
            var filename = form["filename"].FirstOrDefault();
            if (string.IsNullOrEmpty(filename))
                return SlackError("invalid_arguments");

            string fileId;
            lock (_gate) fileId = $"F{_nextFileId++:D6}";
            return Ok(new JsonObject
            {
                ["upload_url"] = $"{BaseUrl}/_upload/{fileId}",
                ["file_id"] = fileId,
            });
        });

        // The one-shot upload URL. Real Slack accepts the bytes as multipart or as the raw body;
        // the fake takes either, like FakeTelegramServer's sendDocument does.
        app.MapPost("/_upload/{fileId}", async (string fileId, HttpContext ctx) =>
        {
            byte[] bytes;
            if (ctx.Request.HasFormContentType)
            {
                var form = await ctx.Request.ReadFormAsync();
                var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                if (file is null)
                    return Results.BadRequest("no file part");
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            else
            {
                using var ms = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            lock (_gate) _uploadBuffers[fileId] = bytes;
            return Results.Text("OK", "text/plain");
        });

        app.MapPost("/api/files.completeUploadExternal", async (HttpContext ctx) =>
        {
            if (!Authorized(ctx, BotToken)) return InvalidAuth();
            var body = await ReadJsonAsync(ctx);
            var files = body?["files"] as JsonArray;
            if (files is null || files.Count == 0)
                return SlackError("invalid_arguments");

            var channelId = body?["channel_id"]?.GetValue<string>();
            var threadTs = body?["thread_ts"]?.GetValue<string>();
            var comment = body?["initial_comment"]?.GetValue<string>();

            var completed = new JsonArray();
            foreach (var entry in files)
            {
                var id = entry?["id"]?.GetValue<string>() ?? "";
                var title = entry?["title"]?.GetValue<string>();
                byte[]? uploaded;
                lock (_gate) _uploadBuffers.TryGetValue(id, out uploaded);
                if (uploaded is null)
                    return SlackError("file_not_found");
                lock (_gate)
                    _uploaded.Add(new SlackUploadedFile(id, title, uploaded, channelId, threadTs, comment));
                completed.Add(new JsonObject { ["id"] = id, ["title"] = title });
            }

            return Ok(new JsonObject { ["files"] = completed });
        });

        // url_private / url_private_download: authenticated, and answers Slack's HTML sign-in page
        // (HTTP 200!) to an unauthenticated caller rather than a 401.
        app.MapGet("/files/{fileId}", async (string fileId, HttpContext ctx) =>
        {
            if (!Authorized(ctx, BotToken))
                return Results.Text("<html><body>Sign in to Slack</body></html>", "text/html");

            Task? gate;
            lock (_gate) gate = _downloadGate?.Task;
            if (gate is not null)
                await gate;

            lock (_gate)
                return _files.TryGetValue(fileId, out var file)
                    ? Results.Bytes(file.Bytes, file.ContentType)
                    : Results.NotFound();
        });

        // --- Socket Mode ---
        app.Map("/link", HandleSocketAsync);
    }

    private async Task HandleSocketAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            return;
        }

        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        lock (_gate) SocketConnections++;
        var ct = ctx.RequestAborted;

        // Slack greets every fresh Socket Mode connection with a hello envelope.
        await SendJsonAsync(socket, new JsonObject { ["type"] = "hello", ["num_connections"] = 1 }, ct);

        var acks = ReadAcksAsync(socket, ct);

        // Envelopes written on this connection that we have not yet seen acked. A connection is
        // only torn down once they are all acked: real Slack redelivers an unacked envelope on the
        // next connection, and modelling THAT is a different test — an abort that races the client's
        // very first read would just be testing the fake's own timing.
        var unacked = new List<string>();

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                Frame? frame;
                lock (_gate) frame = _pending.Count > 0 ? _pending.Dequeue() : null;

                if (frame is null)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                if (frame.Abort)
                {
                    await WaitForAcksAsync(unacked, ct);
                    socket.Abort();
                    break;
                }

                if (frame.Envelope!["envelope_id"]?.GetValue<string>() is { } id)
                    unacked.Add(id);
                await SendJsonAsync(socket, frame.Envelope!, ct);

                // Anything queued behind a disconnect belongs to the NEXT connection — writing it
                // here would race the client's reconnect and lose the message.
                if (frame.Envelope!["type"]?.GetValue<string>() == "disconnect")
                {
                    await WaitForAcksAsync(unacked, ct);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }

        try { await acks; } catch { /* the client closing mid-read is normal */ }
    }

    /// <summary>Block until every envelope written on this connection has been acked (or ~5s).</summary>
    private async Task WaitForAcksAsync(List<string> unacked, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (unacked.Count > 0 && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            lock (_gate) unacked.RemoveAll(_acks.Contains);
            if (unacked.Count == 0)
                return;
            await Task.Delay(10, ct);
        }
    }

    private async Task ReadAcksAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (JsonNode.Parse(text) is JsonObject ack && ack["envelope_id"]?.GetValue<string>() is { } id)
                    lock (_gate) _acks.Add(id);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (System.Text.Json.JsonException) { }
    }

    private static Task SendJsonAsync(WebSocket socket, JsonObject payload, CancellationToken ct) =>
        socket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload.ToJsonString())),
            WebSocketMessageType.Text, endOfMessage: true, ct);

    private static async Task<JsonObject?> ReadJsonAsync(HttpContext ctx)
    {
        try { return await JsonNode.ParseAsync(ctx.Request.Body) as JsonObject; }
        catch { return null; }
    }

    private static bool Authorized(HttpContext ctx, string expectedToken) =>
        ctx.Request.Headers.Authorization.ToString() == "Bearer " + expectedToken;

    private string NextTs()
    {
        long seconds;
        lock (_gate) seconds = _nextTs++;
        return $"{seconds}.000100";
    }

    // --- Slack-shaped envelopes: HTTP 200 with ok:false is the norm, not an HTTP error status ---

    private static IResult Ok(JsonObject result)
    {
        // "ok" leads the object like real Slack; rebuild rather than rely on insertion order.
        var ordered = new JsonObject { ["ok"] = true };
        foreach (var prop in result.ToList())
            ordered[prop.Key] = prop.Value?.DeepClone();
        return Results.Text(ordered.ToJsonString(), "application/json");
    }

    private static IResult SlackError(string error, int statusCode = 200, int? retryAfter = null, HttpContext? ctx = null)
    {
        if (retryAfter is { } ra && ctx is not null)
            ctx.Response.Headers.RetryAfter = ra.ToString();
        return Results.Text(
            new JsonObject { ["ok"] = false, ["error"] = error }.ToJsonString(),
            "application/json",
            statusCode: statusCode);
    }

    private static IResult InvalidAuth() => SlackError("invalid_auth");

    private sealed record Frame(JsonObject? Envelope, bool Abort);

    private sealed record Fault(int StatusCode, string? Error, int? RetryAfter, bool NonJson = false, TimeSpan? Hang = null);

    private sealed record StoredFile(byte[] Bytes, string ContentType);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>A recorded outbound <c>chat.postMessage</c> call.</summary>
public sealed record SlackSentMessage(string Channel, string? Text, string? ThreadTs, JsonObject? RawBody);

/// <summary>A recorded completed external upload — the bytes plus where they were shared.</summary>
public sealed record SlackUploadedFile(
    string FileId, string? Title, byte[] Bytes, string? ChannelId, string? ThreadTs, string? InitialComment);
