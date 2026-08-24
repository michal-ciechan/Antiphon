using System.Text;
using System.Text.Json;

namespace Antiphon.FakeLlmApi;

/// <summary>
/// Minimal OpenAI Chat Completions SSE (Grok's actual -p turn path, re-probed 2026-08-24:
/// /responses is session-title only; the user turn hits POST /chat/completions).
/// </summary>
public static class OpenAiChatCompletionsSse
{
    public static async Task WriteTextTurnAsync(HttpResponse response, string text, CancellationToken ct)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";

        var id = $"chatcmpl-stub-{Guid.NewGuid():N}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await WriteDataAsync(response, new
        {
            id,
            @object = "chat.completion.chunk",
            created,
            model = "stub-model",
            choices = new object[]
            {
                new
                {
                    index = 0,
                    delta = new { role = "assistant", content = "" },
                    finish_reason = (string?)null,
                },
            },
        }, ct);

        await WriteDataAsync(response, new
        {
            id,
            @object = "chat.completion.chunk",
            created,
            model = "stub-model",
            choices = new object[]
            {
                new
                {
                    index = 0,
                    delta = new { content = text },
                    finish_reason = (string?)null,
                },
            },
        }, ct);

        await WriteDataAsync(response, new
        {
            id,
            @object = "chat.completion.chunk",
            created,
            model = "stub-model",
            choices = new object[]
            {
                new
                {
                    index = 0,
                    delta = new { },
                    finish_reason = "stop",
                },
            },
        }, ct);

        var done = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await response.Body.WriteAsync(done, ct);
        await response.Body.FlushAsync(ct);
    }

    public static Task WriteErrorAsync(HttpResponse response, ScriptedError error, CancellationToken ct)
        => OpenAiResponsesSse.WriteErrorAsync(response, error, ct);

    private static async Task WriteDataAsync(HttpResponse response, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);
        var payload = $"data: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await response.Body.WriteAsync(bytes, ct);
        await response.Body.FlushAsync(ct);
    }
}
