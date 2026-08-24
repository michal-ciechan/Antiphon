using System.Text;
using System.Text.Json;

namespace Antiphon.FakeLlmApi;

/// <summary>
/// Minimal Anthropic Messages SSE: message_start → content_block_* → message_delta → message_stop.
/// Fidelity bar: real <c>claude -p</c> must accept this as a completed turn.
/// </summary>
public static class AnthropicSse
{
    public static async Task WriteTextTurnAsync(HttpResponse response, string text, CancellationToken ct)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        var msgId = $"msg_stub_{Guid.NewGuid():N}";
        var model = "claude-stub";

        await WriteEventAsync(response, "message_start", new
        {
            type = "message_start",
            message = new
            {
                id = msgId,
                type = "message",
                role = "assistant",
                content = Array.Empty<object>(),
                model,
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = new { input_tokens = 10, output_tokens = 1 },
            },
        }, ct);

        await WriteEventAsync(response, "content_block_start", new
        {
            type = "content_block_start",
            index = 0,
            content_block = new { type = "text", text = "" },
        }, ct);

        // One delta is enough; chunking is not required for print-mode acceptance.
        await WriteEventAsync(response, "content_block_delta", new
        {
            type = "content_block_delta",
            index = 0,
            delta = new { type = "text_delta", text },
        }, ct);

        await WriteEventAsync(response, "content_block_stop", new
        {
            type = "content_block_stop",
            index = 0,
        }, ct);

        await WriteEventAsync(response, "message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = new { output_tokens = Math.Max(1, text.Length / 4) },
        }, ct);

        await WriteEventAsync(response, "message_stop", new { type = "message_stop" }, ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteErrorAsync(HttpResponse response, ScriptedError error, CancellationToken ct)
    {
        response.StatusCode = error.StatusCode;
        response.ContentType = "application/json";
        var body = error.JsonBody ?? JsonSerializer.Serialize(new
        {
            type = "error",
            error = new
            {
                type = "invalid_request_error",
                message = $"stub scripted error {error.StatusCode}",
            },
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
