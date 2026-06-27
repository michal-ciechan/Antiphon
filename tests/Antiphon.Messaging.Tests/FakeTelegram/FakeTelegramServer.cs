using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Antiphon.Messaging.Tests.FakeTelegram;

/// <summary>
/// In-process fake of the Telegram Bot API, covering exactly the endpoints the messaging service
/// calls: <c>getUpdates</c>, <c>sendMessage</c>, <c>deleteWebhook</c> (+ <c>getMe</c> for parity).
/// Responses are hand-built as <see cref="JsonObject"/> so the wire shape (snake_case keys like
/// <c>error_code</c>/<c>is_bot</c>, the <c>ok</c>/<c>result</c> envelope) matches real Telegram —
/// which the conformance tests verify. Tests enqueue updates and read recorded sends.
/// </summary>
public sealed class FakeTelegramServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly object _gate = new();
    private readonly List<JsonObject> _updates = [];        // pending updates (the long-poll queue)
    private readonly List<SentMessage> _sent = [];
    private readonly Queue<Fault> _getUpdatesFaults = new();   // one-shot faults, consumed in order
    private readonly Queue<Fault> _sendFaults = new();
    private long _nextUpdateId = 1;
    private long _nextMessageId = 1000;

    public string BotToken { get; }
    public string BaseUrl { get; private set; } = "";
    public bool WebhookDeleted { get; private set; }

    /// <summary>How many times getUpdates / sendMessage have been called (incl. faulted calls).</summary>
    public int GetUpdatesCalls { get; private set; }
    public int SendCalls { get; private set; }

    public IReadOnlyList<SentMessage> SentMessages
    {
        get { lock (_gate) return _sent.ToList(); }
    }

    public FakeTelegramServer(string botToken = "test-bot-token")
    {
        BotToken = botToken;
        BaseUrl = $"http://127.0.0.1:{GetFreePort()}";   // pin a known free port up front
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(BaseUrl);
        builder.Logging.ClearProviders();
        _app = builder.Build();
        MapEndpoints(_app);
    }

    public Task StartAsync() => _app.StartAsync();

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Enqueue a plain text direct-message update, as Telegram would deliver via getUpdates.</summary>
    public void EnqueueTextMessage(long chatId, long fromId, string text, string? username = null, string? firstName = "Tester")
    {
        var msg = new JsonObject
        {
            ["message_id"] = Next(ref _nextMessageId),
            ["from"] = new JsonObject { ["id"] = fromId, ["is_bot"] = false, ["first_name"] = firstName, ["username"] = username },
            ["chat"] = new JsonObject { ["id"] = chatId, ["type"] = "private", ["first_name"] = firstName },
            ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["text"] = text,
        };
        EnqueueUpdate(new JsonObject { ["message"] = msg });
    }

    /// <summary>Enqueue a raw update; <c>update_id</c> is assigned if absent. Use for mentions/attachments/replies.</summary>
    public void EnqueueUpdate(JsonObject update)
    {
        lock (_gate)
        {
            if (update["update_id"] is null)
                update["update_id"] = _nextUpdateId++;
            _updates.Add(update);
        }
    }

    // --- Fault injection (each enqueued fault is returned once, in order, then normal behavior resumes) ---

    /// <summary>409 Conflict — Telegram's response when another consumer is already long-polling this token.</summary>
    public void EnqueueGetUpdatesConflict() =>
        Enqueue(_getUpdatesFaults, new Fault(409, 409, "Conflict: terminated by other getUpdates request; make sure that only one bot instance is running", null));

    /// <summary>429 Too Many Requests with a <c>retry_after</c> hint.</summary>
    public void EnqueueGetUpdatesRateLimit(int retryAfterSeconds) =>
        Enqueue(_getUpdatesFaults, new Fault(429, 429, $"Too Many Requests: retry after {retryAfterSeconds}", retryAfterSeconds));

    /// <summary>A gateway-style 5xx with a non-JSON body (e.g. an HTML error page from a proxy).</summary>
    public void EnqueueGetUpdatesServerError(int status = 502) =>
        Enqueue(_getUpdatesFaults, new Fault(status, null, "Bad Gateway", null, NonJson: true));

    public void EnqueueSendRateLimit(int retryAfterSeconds) =>
        Enqueue(_sendFaults, new Fault(429, 429, $"Too Many Requests: retry after {retryAfterSeconds}", retryAfterSeconds));

    public void EnqueueSendBadRequest(string description = "Bad Request: chat not found") =>
        Enqueue(_sendFaults, new Fault(400, 400, description, null));

    public void EnqueueSendServerError(int status = 500) =>
        Enqueue(_sendFaults, new Fault(status, null, "Internal Server Error", null, NonJson: true));

    private void Enqueue(Queue<Fault> q, Fault f) { lock (_gate) q.Enqueue(f); }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/{bot}/getUpdates", (string bot, long? offset, int? timeout) =>
        {
            if (!TokenOk(bot)) return Unauthorized();
            lock (_gate)
            {
                GetUpdatesCalls++;
                if (_getUpdatesFaults.Count > 0)
                    return FaultResult(_getUpdatesFaults.Dequeue());

                var off = offset ?? 0;
                if (off > 0) _updates.RemoveAll(u => UpdateId(u) < off);   // offset confirms earlier updates
                var result = new JsonArray(_updates
                    .Where(u => UpdateId(u) >= off)
                    .Select(u => (JsonNode)u.DeepClone())
                    .ToArray());
                return Ok(result);
            }
        });

        app.MapPost("/{bot}/sendMessage", async (string bot, HttpContext ctx) =>
        {
            if (!TokenOk(bot)) return Unauthorized();

            lock (_gate)
            {
                SendCalls++;
                if (_sendFaults.Count > 0)
                    return FaultResult(_sendFaults.Dequeue());
            }

            JsonObject? body;
            try { body = (await JsonNode.ParseAsync(ctx.Request.Body)) as JsonObject; }
            catch { body = null; }

            var chatNode = body?["chat_id"];
            // Telegram rejects a missing/empty chat_id with 400, and an unknown chat (e.g. 0) with 400.
            if (chatNode is null || IsEmptyOrZeroChat(chatNode))
                return Error(400, "Bad Request: chat_id is empty");

            var text = body?["text"]?.GetValue<string>();
            var id = Next(ref _nextMessageId);
            lock (_gate)
                _sent.Add(new SentMessage(chatNode.ToJsonString().Trim('"'), text, body?.DeepClone() as JsonObject));

            var result = new JsonObject
            {
                ["message_id"] = id,
                ["from"] = new JsonObject { ["id"] = 42, ["is_bot"] = true, ["username"] = "fake_bot" },
                ["chat"] = new JsonObject { ["id"] = chatNode.DeepClone() },
                ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["text"] = text,
            };
            return Ok(result);
        });

        app.MapGet("/{bot}/deleteWebhook", (string bot) =>
        {
            if (!TokenOk(bot)) return Unauthorized();
            WebhookDeleted = true;
            return Ok(JsonValue.Create(true)!);
        });

        app.MapGet("/{bot}/getMe", (string bot) =>
        {
            if (!TokenOk(bot)) return Unauthorized();
            return Ok(new JsonObject
            {
                ["id"] = 42,
                ["is_bot"] = true,
                ["username"] = "fake_bot",
                ["first_name"] = "Fake",
            });
        });
    }

    // The {bot} route segment is the whole "bot<token>" prefix Telegram uses, e.g. /bot123:ABC/getMe.
    private bool TokenOk(string bot) => bot == "bot" + BotToken;

    private static long UpdateId(JsonObject u) => u["update_id"]?.GetValue<long>() ?? 0;

    private static bool IsEmptyOrZeroChat(JsonNode chat)
    {
        if (chat.GetValueKind() == JsonValueKind.String) return string.IsNullOrWhiteSpace(chat.GetValue<string>());
        if (chat.GetValueKind() == JsonValueKind.Number) return chat.GetValue<long>() == 0;
        return false;
    }

    private static long Next(ref long counter) => counter++;

    // --- Telegram-shaped envelopes (snake_case keys preserved by JsonObject) ---
    private static IResult Ok(JsonNode result) =>
        Results.Text(new JsonObject { ["ok"] = true, ["result"] = result }.ToJsonString(), "application/json");

    private static IResult Error(int code, string description) =>
        Results.Text(new JsonObject { ["ok"] = false, ["error_code"] = code, ["description"] = description }.ToJsonString(),
            "application/json", statusCode: code);

    private static IResult Unauthorized() => Error(401, "Unauthorized");

    private static IResult FaultResult(Fault f)
    {
        if (f.NonJson)   // e.g. a proxy HTML error page — exercises the adapter's non-JSON handling
            return Results.Text($"<html><body>{f.Description}</body></html>", "text/html", statusCode: f.StatusCode);

        var obj = new JsonObject { ["ok"] = false };
        if (f.ErrorCode is { } ec) obj["error_code"] = ec;
        obj["description"] = f.Description;
        if (f.RetryAfter is { } ra) obj["parameters"] = new JsonObject { ["retry_after"] = ra };   // Telegram's 429 shape
        return Results.Text(obj.ToJsonString(), "application/json", statusCode: f.StatusCode);
    }

    private sealed record Fault(int StatusCode, int? ErrorCode, string Description, int? RetryAfter, bool NonJson = false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>A recorded outbound sendMessage call.</summary>
public sealed record SentMessage(string ChatId, string? Text, JsonObject? RawBody);
