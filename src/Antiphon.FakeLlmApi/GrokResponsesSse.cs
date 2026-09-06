using System.Text.Json;

namespace Antiphon.FakeLlmApi;

/// <summary>Complete Responses SSE fields required by the measured Grok 1.0.13 parser.</summary>
public static class GrokResponsesSse
{
    public static async Task WriteAsync(HttpResponse response, StubResponse scripted, CancellationToken ct)
    {
        if (scripted is ScriptedError error) { await OpenAiResponsesSse.WriteErrorAsync(response, error, ct); return; }
        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        var id = $"resp_stub_{Guid.NewGuid():N}";
        var itemId = $"item_stub_{Guid.NewGuid():N}";
        var sequence = 0;
        object Part(string text) => new { type = "output_text", text, annotations = Array.Empty<object>(), logprobs = Array.Empty<object>() };
        object Message(string text, string status) => new { type = "message", id = itemId, status, role = "assistant", content = new[] { Part(text) } };
        object Call(ScriptedFunctionCall call, string arguments, string status) => new {
            type = "function_call", id = itemId, call_id = call.CallId, name = call.Name, arguments, status };
        object Envelope(string status, object[] output) => new { id, @object = "response",
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model = "grok-4.6", status, output,
            usage = new { input_tokens = 10, output_tokens = 5, total_tokens = 15,
                input_tokens_details = new { cached_tokens = 0 }, output_tokens_details = new { reasoning_tokens = 0 } } };
        async Task Emit(string type, object payload)
        {
            var node = JsonSerializer.SerializeToNode(payload)!.AsObject();
            node["type"] = type;
            node["sequence_number"] = sequence++;
            await response.WriteAsync($"event: {type}\ndata: {node.ToJsonString()}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
        await Emit("response.created", new { response = Envelope("in_progress", []) });
        object item;
        if (scripted is ScriptedFunctionCall call)
        {
            item = Call(call, call.Arguments, "completed");
            await Emit("response.output_item.added", new { output_index = 0, item = Call(call, "", "in_progress") });
            await Emit("response.function_call_arguments.delta", new { item_id = itemId, output_index = 0, delta = call.Arguments });
            await Emit("response.function_call_arguments.done", new { item_id = itemId, output_index = 0, arguments = call.Arguments });
        }
        else
        {
            var text = (scripted as ScriptedTextTurn)?.Text ?? "ok";
            item = Message(text, "completed");
            await Emit("response.output_item.added", new { output_index = 0,
                item = new { type = "message", id = itemId, status = "in_progress", role = "assistant", content = Array.Empty<object>() } });
            await Emit("response.content_part.added", new { item_id = itemId, output_index = 0, content_index = 0, part = Part("") });
            await Emit("response.output_text.delta", new { item_id = itemId, output_index = 0, content_index = 0, delta = text, logprobs = Array.Empty<object>() });
            await Emit("response.output_text.done", new { item_id = itemId, output_index = 0, content_index = 0, text, logprobs = Array.Empty<object>() });
            await Emit("response.content_part.done", new { item_id = itemId, output_index = 0, content_index = 0, part = Part(text) });
        }
        await Emit("response.output_item.done", new { output_index = 0, item });
        await Emit("response.completed", new { response = Envelope("completed", [item]) });
    }
}
