using System.Text;
using System.Text.Json;

namespace Antiphon.FakeLlmApi;

/// <summary>
/// Minimal OpenAI Responses API SSE (Grok chat-proxy + Codex wire_api=responses).
/// Sequence: response.created → output_item.added → output_text.delta(s) → output_item.done → response.completed.
/// </summary>
public static class OpenAiResponsesSse
{
    public static async Task WriteTextTurnAsync(HttpResponse response, string text, CancellationToken ct)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        var responseId = $"resp_stub_{Guid.NewGuid():N}";
        var itemId = $"msg_stub_{Guid.NewGuid():N}";
        var model = "stub-model";

        var created = new
        {
            id = responseId,
            @object = "response",
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            status = "in_progress",
            model,
            output = Array.Empty<object>(),
        };

        await WriteEventAsync(response, "response.created", new
        {
            type = "response.created",
            response = created,
        }, ct);

        await WriteEventAsync(response, "response.output_item.added", new
        {
            type = "response.output_item.added",
            output_index = 0,
            item = new
            {
                type = "message",
                id = itemId,
                status = "in_progress",
                role = "assistant",
                content = Array.Empty<object>(),
            },
        }, ct);

        await WriteEventAsync(response, "response.content_part.added", new
        {
            type = "response.content_part.added",
            item_id = itemId,
            output_index = 0,
            content_index = 0,
            part = new { type = "output_text", text = "" },
        }, ct);

        await WriteEventAsync(response, "response.output_text.delta", new
        {
            type = "response.output_text.delta",
            item_id = itemId,
            output_index = 0,
            content_index = 0,
            delta = text,
        }, ct);

        await WriteEventAsync(response, "response.output_text.done", new
        {
            type = "response.output_text.done",
            item_id = itemId,
            output_index = 0,
            content_index = 0,
            text,
        }, ct);

        await WriteEventAsync(response, "response.content_part.done", new
        {
            type = "response.content_part.done",
            item_id = itemId,
            output_index = 0,
            content_index = 0,
            part = new { type = "output_text", text },
        }, ct);

        await WriteEventAsync(response, "response.output_item.done", new
        {
            type = "response.output_item.done",
            output_index = 0,
            item = new
            {
                type = "message",
                id = itemId,
                status = "completed",
                role = "assistant",
                content = new object[]
                {
                    new { type = "output_text", text },
                },
            },
        }, ct);

        await WriteEventAsync(response, "response.completed", new
        {
            type = "response.completed",
            response = new
            {
                id = responseId,
                @object = "response",
                created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                status = "completed",
                model,
                output = new object[]
                {
                    new
                    {
                        type = "message",
                        id = itemId,
                        status = "completed",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text },
                        },
                    },
                },
                usage = new { input_tokens = 10, output_tokens = Math.Max(1, text.Length / 4), total_tokens = 10 + Math.Max(1, text.Length / 4) },
            },
        }, ct);

        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteErrorAsync(HttpResponse response, ScriptedError error, CancellationToken ct)
    {
        response.StatusCode = error.StatusCode;
        response.ContentType = "application/json";
        var body = error.JsonBody ?? JsonSerializer.Serialize(new
        {
            error = new
            {
                message = $"stub scripted error {error.StatusCode}",
                type = "invalid_request_error",
                code = "stub_error",
            },
        });
        await response.WriteAsync(body, ct);
    }

    /// <summary>
    /// Minimal models list used by Grok/Codex startup probes. Includes both OpenAI
    /// <c>data</c> and Codex's expected <c>models</c> field (Codex 0.147 errors without it).
    /// </summary>
    public static async Task WriteModelsListAsync(HttpResponse response, CancellationToken ct)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json";
        var model = new { id = "stub-model", @object = "model", created = 1_700_000_000, owned_by = "stub" };
        var body = JsonSerializer.Serialize(new
        {
            @object = "list",
            data = new object[] { model },
            models = new object[] { model },
        });
        await response.WriteAsync(body, ct);
    }

    private static async Task WriteEventAsync(HttpResponse response, string eventName, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);
        var payload = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await response.Body.WriteAsync(bytes, ct);
        await response.Body.FlushAsync(ct);
    }
}
