using System.Text.Json;

namespace Antiphon.FakeLlmApi;

/// <summary>
/// Grok chat-proxy surface (paths WITHOUT /v1 when base URL has none): /models, /settings,
/// /api-key, POST /responses. The /api-key hit is the credential-injection oracle; /responses
/// carrying the nonce is the chat-redirect oracle.
/// </summary>
internal static class GrokStubEndpoints
{
    public static void Map(WebApplication app, StubScript script)
    {
        app.MapGet("/models", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.GrokModels);
            if (next is ScriptedError err)
            {
                await OpenAiResponsesSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                return;
            }

            await OpenAiResponsesSse.WriteModelsListAsync(ctx.Response, ctx.RequestAborted);
        });

        app.MapGet("/settings", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.GrokSettings);
            if (next is ScriptedError err)
            {
                await OpenAiResponsesSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"ok":true}""", ctx.RequestAborted);
        });

        // S4 interactive probe (2026-08-24): GET /billing?format=credits three times around the
        // turn. 404 did not stall the TUI, but a recorded unmatched path is a surface gap.
        app.MapGet("/billing", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.GrokBilling);
            if (next is ScriptedError err)
            {
                await OpenAiResponsesSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"credits":1,"format":"credits"}""", ctx.RequestAborted);
        });

        app.MapGet("/api-key", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.GrokApiKey);
            if (next is ScriptedError err)
            {
                await OpenAiResponsesSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                return;
            }

            // Echo a minimal "key accepted" shape; body content is not the oracle.
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(
                JsonSerializer.Serialize(new { ok = true, source = "stub" }),
                ctx.RequestAborted);
        });

        app.MapPost("/responses", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.GrokResponses);
            switch (next)
            {
                case ScriptedError err:
                    await OpenAiResponsesSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                    break;
                case ScriptedTextTurn turn:
                    await OpenAiResponsesSse.WriteTextTurnAsync(ctx.Response, turn.Text, ctx.RequestAborted);
                    break;
                default:
                    await OpenAiResponsesSse.WriteTextTurnAsync(ctx.Response, "ok", ctx.RequestAborted);
                    break;
            }
        });

        // Re-probed 2026-08-24: grok -p sends the user turn to Chat Completions; /responses is
        // session-title generation only. Without this route the CLI 404s after a successful title.
        app.MapPost("/chat/completions", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.GrokChatCompletions);
            switch (next)
            {
                case ScriptedError err:
                    await OpenAiChatCompletionsSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                    break;
                case ScriptedTextTurn turn:
                    await OpenAiChatCompletionsSse.WriteTextTurnAsync(ctx.Response, turn.Text, ctx.RequestAborted);
                    break;
                default:
                    await OpenAiChatCompletionsSse.WriteTextTurnAsync(ctx.Response, "ok", ctx.RequestAborted);
                    break;
            }
        });
    }
}
