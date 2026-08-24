namespace Antiphon.FakeLlmApi;

internal static class ClaudeStubEndpoints
{
    public static void Map(WebApplication app, StubScript script)
    {
        // Probe-confirmed: print-mode stalls forever unless HEAD /api/hello returns 200.
        app.MapMethods("/api/hello", ["HEAD", "GET"], async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.ClaudeHello);
            if (next is ScriptedError err)
            {
                ctx.Response.StatusCode = err.StatusCode;
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await Task.CompletedTask;
        });

        // Any query string (e.g. ?beta=true).
        app.MapPost("/v1/messages", async (HttpContext ctx) =>
        {
            var next = script.Next(StubEndpointKeys.ClaudeMessages);
            switch (next)
            {
                case ScriptedError err:
                    await AnthropicSse.WriteErrorAsync(ctx.Response, err, ctx.RequestAborted);
                    break;
                case ScriptedTextTurn turn:
                    await AnthropicSse.WriteTextTurnAsync(ctx.Response, turn.Text, ctx.RequestAborted);
                    break;
                default:
                    await AnthropicSse.WriteTextTurnAsync(ctx.Response, "ok", ctx.RequestAborted);
                    break;
            }
        });
    }
}
